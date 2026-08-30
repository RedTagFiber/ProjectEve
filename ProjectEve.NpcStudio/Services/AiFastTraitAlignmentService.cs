using System.Net.Http.Json;
using System.Text.Json;
using ProjectEve.Characters.Traits;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Third-pass personality alignment:
/// Mid + Slow are the deeper authored personality.
/// Fast20 is then aligned to that personality as the NPC's starting emotional set-point.
/// </summary>
public sealed class AiFastTraitAlignmentService
{
    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;

    public AiFastTraitAlignmentService(HttpClient http, NpcStudioOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<AiFastTraitAlignmentPreview> BuildPreviewAsync(
        int npcId,
        string npcContext,
        CancellationToken cancellationToken = default)
    {
        var fast = FastTraitDossierService.Load(npcId);
        var mid = MidTraitDossierService.Load(npcId);
        var slow = SlowTraitDossierService.Load(npcId);

        var preview = new AiFastTraitAlignmentPreview
        {
            NpcId = npcId,
            MidCount = mid.Active.Count,
            SlowCount = slow.Active.Count
        };

        if (fast.Count != 20)
        {
            preview.Warnings.Add($"BLOCK: Fast20 dossier contains {fast.Count}/20 traits.");
            return preview;
        }

        if (mid.Active.Count == 0 && slow.Active.Count == 0)
        {
            preview.Warnings.Add("BLOCK: No Mid or Slow personality traits are available to align Fast20 against.");
            return preview;
        }

        var prompt = BuildPrompt(npcContext, fast, mid, slow);
        var raw = await GenerateAsync(prompt, cancellationToken);

        FastResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<FastResponse>(
                ExtractJson(raw),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            preview.Warnings.Add("BLOCK: Fast20 AI response was not valid JSON: " + ex.Message);
            return preview;
        }

        var allowed = fast.ToDictionary(x => x.TraitId, StringComparer.OrdinalIgnoreCase);

        foreach (var proposal in response?.Traits ?? new List<FastChoice>())
        {
            var id = proposal.TraitId?.Trim() ?? "";
            if (!allowed.TryGetValue(id, out var current))
                continue;

            if (preview.Selections.Any(x => x.TraitId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                continue;

            var value = Math.Clamp(proposal.Value, 10, 90);
            var style = CleanStyle(proposal.Style, current.StyleOptions);

            preview.Selections.Add(new AiFastTraitAlignmentSelection
            {
                TraitId = current.TraitId,
                TraitName = current.TraitName,
                BeforeValue = current.CurrentValue,
                BeforeBaseline = current.Baseline,
                Value = value,
                Baseline = value,
                Style = style,
                Rationale = proposal.Rationale?.Trim() ?? ""
            });
        }

        foreach (var current in fast)
        {
            if (preview.Selections.Any(x => x.TraitId.Equals(current.TraitId, StringComparison.OrdinalIgnoreCase)))
                continue;

            preview.Selections.Add(new AiFastTraitAlignmentSelection
            {
                TraitId = current.TraitId,
                TraitName = current.TraitName,
                BeforeValue = current.CurrentValue,
                BeforeBaseline = current.Baseline,
                Value = (int)Math.Round(current.CurrentValue),
                Baseline = (int)Math.Round(current.Baseline),
                Style = current.Style,
                Rationale = "AI omitted this trait; existing value preserved."
            });

            preview.Warnings.Add($"REVIEW: AI omitted {current.TraitId}; existing value will be preserved.");
        }

        preview.Selections.Sort((a, b) => string.Compare(a.TraitName, b.TraitName, StringComparison.OrdinalIgnoreCase));

        var neutralCount = preview.Selections.Count(x => x.Value == 50);
        if (neutralCount > 6)
        {
            preview.Warnings.Add($"REVIEW: {neutralCount} Fast20 values remain exactly neutral at 50.");
        }

        return preview;
    }

    public Task ApplyApprovedPreviewAsync(AiFastTraitAlignmentPreview preview)
    {
        if (!preview.IsValid)
            throw new InvalidOperationException("Fast20 alignment preview is not valid.");

        foreach (var item in preview.Selections)
        {
            NpcTraitRepository.SaveFastDossierState(
                preview.NpcId,
                item.TraitId,
                item.Value,
                item.Baseline,
                item.Style);
        }

        return Task.CompletedTask;
    }

    private static string BuildPrompt(
        string npcContext,
        IReadOnlyList<FastTraitDossierItem> fast,
        MidTraitDossier mid,
        SlowTraitDossier slow)
    {
        var midText = string.Join("\n", mid.Active
            .OrderByDescending(x => Math.Abs(x.CurrentValue - 50f))
            .Select(x => $"- {x.TraitId} | {x.TraitName} | value={x.CurrentValue:0} | style={x.Style}"));

        var slowText = string.Join("\n", slow.Active
            .OrderByDescending(x => Math.Abs(x.CurrentValue - 50f))
            .Select(x => $"- {x.TraitId} | {x.TraitName} | value={x.CurrentValue:0} | style={x.Style}"));

        var fastText = string.Join("\n", fast.Select(x =>
            $"- {x.TraitId} | {x.TraitName} | current={x.CurrentValue:0} | baseline={x.Baseline:0} | " +
            $"styles={(x.StyleOptions.Count == 0 ? "(none)" : string.Join(", ", x.StyleOptions))}"));

        return $$"""
        You are Project Eve's FAST20 PERSONALITY ALIGNER.

        The NPC's Mid and Slow traits are already selected and are the deeper personality truth.
        Your job is to set the NPC's STARTING Fast20 emotional set-points so they naturally reflect
        that deeper personality. Fast20 will later move dynamically during gameplay.

        NPC CONTEXT:
        {{npcContext}}

        MID PERSONALITY:
        {{midText}}

        SLOW PREFERENCES / IDENTITY:
        {{slowText}}

        FAST20 FIXED CATALOG:
        {{fastText}}

        HARD RULES:
        - Return all 20 Fast20 trait ids exactly once.
        - Never invent or rename a Fast20 trait.
        - Values must be integers 10-90.
        - Mid + Slow are authoritative context. Fast values must make sense with them.
        - Do not set everything to 50. Use a believable spread.
        - Usually no more than 4 traits should be exactly 50.
        - Avoid cartoon extremes. Values above 80 or below 20 should be rare and strongly justified.
        - Related Fast traits should make sense together.
        - A person may have nuanced tensions, but do not create obvious psychological nonsense.
        - Baseline/start state should represent who this person normally is before current events push them around.
        - style must be an allowed style from that Fast trait when options exist; otherwise blank is allowed.
        - rationale is one short sentence explaining how Mid/Slow supports the value.
        - Return JSON only.

        RETURN:
        {
          "traits": [
            {
              "traitId": "trait.anger",
              "value": 42,
              "style": "",
              "rationale": "Short explanation."
            }
          ]
        }
        """;
    }

    private async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _options.OllamaModel,
            prompt,
            stream = false,
            format = "json",
            keep_alive = "30m",
            options = new
            {
                temperature = 0.35,
                num_ctx = 8192,
                num_predict = 2400,
                repeat_penalty = 1.05
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(150));

        using var response = await _http.PostAsJsonAsync(
            new Uri(new Uri(_options.OllamaBaseUrl), "/api/generate"),
            request,
            cts.Token);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

        return doc.RootElement.TryGetProperty("response", out var value)
            ? value.GetString() ?? "{}"
            : "{}";
    }

    private static string CleanStyle(string? proposed, IReadOnlyList<string> allowed)
    {
        var value = proposed?.Trim() ?? "";
        if (value.Length == 0 || allowed.Count == 0)
            return value;

        return allowed.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private static string ExtractJson(string raw)
    {
        var text = raw?.Trim() ?? "{}";
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        return first >= 0 && last >= first ? text[first..(last + 1)] : "{}";
    }

    private sealed class FastResponse
    {
        public List<FastChoice> Traits { get; set; } = new();
    }

    private sealed class FastChoice
    {
        public string TraitId { get; set; } = "";
        public int Value { get; set; } = 50;
        public string Style { get; set; } = "";
        public string Rationale { get; set; } = "";
    }
}

public sealed class AiFastTraitAlignmentPreview
{
    public int NpcId { get; init; }
    public int MidCount { get; init; }
    public int SlowCount { get; init; }
    public List<AiFastTraitAlignmentSelection> Selections { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool IsValid =>
        Selections.Count == 20 &&
        Warnings.All(x => !x.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase));
}

public sealed class AiFastTraitAlignmentSelection
{
    public string TraitId { get; init; } = "";
    public string TraitName { get; init; } = "";
    public float BeforeValue { get; init; }
    public float BeforeBaseline { get; init; }
    public int Value { get; init; }
    public int Baseline { get; init; }
    public string Style { get; init; } = "";
    public string Rationale { get; init; } = "";
}