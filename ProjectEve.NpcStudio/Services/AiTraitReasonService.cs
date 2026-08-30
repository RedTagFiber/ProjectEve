using System.Net.Http.Json;
using System.Text.Json;
using ProjectEve.Characters.Traits;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Generates subjective causal reasons AFTER trait values have been selected.
/// Reasons explain the authored/current personality; they do not change trait values.
/// Canonical persistence is NpcTraitRepository.SaveTraitReason in RELATIONSHIPS.
/// </summary>
public sealed class AiTraitReasonService
{
    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;

    public AiTraitReasonService(HttpClient http, NpcStudioOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<int> AddReasonsAsync(
        int npcId,
        string traitId,
        string traitName,
        float traitValue,
        string npcContext,
        int maxTotalReasons = 4,
        CancellationToken cancellationToken = default)
    {
        if (npcId <= 0 || string.IsNullOrWhiteSpace(traitId))
            return 0;

        maxTotalReasons = Math.Clamp(maxTotalReasons, 1, 4);

        var existing = NpcTraitRepository.LoadActiveReasons(
            npcId,
            traitId,
            maxRows: 12);

        var desired = DesiredReasonCount(traitValue);
        desired = Math.Min(desired, maxTotalReasons);

        var needed = Math.Max(0, desired - existing.Count);
        if (needed == 0)
            return 0;

        var existingText = existing.Count == 0
            ? "(none)"
            : string.Join("\n", existing.Select(x => $"- {x.Reason}"));

        var prompt = $$"""
        You are Project Eve's NPC TRAIT REASON WRITER.

        NPC CONTEXT:
        {{npcContext}}

        TRAIT:
        id={{traitId}}
        name={{traitName}}
        current value={{traitValue:0}}

        EXISTING REASONS:
        {{existingText}}

        Write exactly {{needed}} NEW causal reason(s) explaining why this NPC plausibly has
        this trait at roughly this strength.

        HARD RULES:
        - Reasons are generated AFTER the trait is chosen. Do not change or question the trait.
        - Do not invent specific deaths, crimes, diagnoses, marriages, children, jobs, or major
          historical events unless they are explicitly present in NPC CONTEXT.
        - Prefer grounded character-design causes: upbringing, habits, temperament, social style,
          repeated everyday experiences, values, routines, preferences, or learned coping style.
        - Each reason must add something different.
        - impact must be -30 to 30 and should indicate whether this reason pushes the trait down or up.
        - confidence must be 60 to 95.
        - reasonType should be one of: temperament, upbringing, habit, values, social, preference, experience.
        - sourceType must be "ai-author".
        - No prose outside JSON.

        RETURN:
        {
          "reasons": [
            {
              "reasonType": "temperament",
              "reason": "Short grounded explanation.",
              "impact": 12,
              "confidence": 85
            }
          ]
        }
        """;

        var raw = await GenerateAsync(prompt, cancellationToken);
        AiReasonResponse? result;

        try
        {
            result = JsonSerializer.Deserialize<AiReasonResponse>(
                ExtractJson(raw),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return 0;
        }

        var saved = 0;
        var existingReasonText = existing
            .Select(x => x.Reason)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in result?.Reasons ?? new List<AiReasonChoice>())
        {
            if (saved >= needed)
                break;

            var reason = item.Reason?.Trim() ?? "";
            if (reason.Length < 8 || existingReasonText.Contains(reason))
                continue;

            NpcTraitRepository.SaveTraitReason(
                id: null,
                npcId: npcId,
                traitId: traitId,
                targetCharacterId: -1,
                reasonType: CleanReasonType(item.ReasonType),
                reason: reason,
                impact: Math.Clamp(item.Impact, -30f, 30f),
                sourceType: "ai-author",
                confidence: Math.Clamp(item.Confidence, 60, 95));

            existingReasonText.Add(reason);
            saved++;
        }

        return saved;
    }

    private static int DesiredReasonCount(float value)
    {
        var deviation = Math.Abs(value - 50f);
        if (deviation >= 30f) return 4;
        if (deviation >= 20f) return 3;
        if (deviation >= 10f) return 2;
        return 1;
    }

    private static string CleanReasonType(string? value)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "temperament", "upbringing", "habit", "values",
            "social", "preference", "experience"
        };

        var clean = value?.Trim() ?? "";
        return allowed.Contains(clean) ? clean.ToLowerInvariant() : "temperament";
    }

    private async Task<string> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken)
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
                temperature = 0.5,
                num_ctx = 4096,
                num_predict = 900,
                repeat_penalty = 1.05
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(90));

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

    private static string ExtractJson(string raw)
    {
        var text = raw?.Trim() ?? "{}";
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        return first >= 0 && last >= first
            ? text[first..(last + 1)]
            : "{}";
    }

    private sealed class AiReasonResponse
    {
        public List<AiReasonChoice> Reasons { get; set; } = new();
    }

    private sealed class AiReasonChoice
    {
        public string ReasonType { get; set; } = "";
        public string Reason { get; set; } = "";
        public float Impact { get; set; }
        public int Confidence { get; set; } = 80;
    }
}