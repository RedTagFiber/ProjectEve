using System.Text;
using System.Text.Json;

namespace ProjectEve.AI.Training;

public static class ThoughtDatasetBuilder
{
    private static readonly string SystemPrompt =
        "You are the Thought engine for Project Eve. Short inner line, then TAGS: trait.id±delta@intensity; ... or TAGS: none. Fast ids only. Intensity 1-10.";

    // Heat batch first
    private static readonly string[] HeatTraits =
    {
        "anger", "tension", "pride", "guard", "resentment"
    };

    public static string BuildHeatJsonl(
        string fastTraitFolder,
        string outJsonlPath,
        int minRows = 200)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outJsonlPath)!);

        var rows = new List<string>();

        foreach (var name in HeatTraits)
        {
            var path = Path.Combine(fastTraitFolder, $"{name}.json");
            if (!File.Exists(path))
            {
                Console.WriteLine($"skip missing: {path}");
                continue;
            }

            rows.AddRange(ExtractRowsFromTraitFile(path, name));
        }

        // Pad by cycling if under minRows (smoke / first train)
        if (rows.Count == 0)
            throw new InvalidOperationException("No training rows extracted. Check Fast JSON paths and examples.");

        var final = new List<string>(minRows);
        for (int i = 0; i < minRows; i++)
            final.Add(rows[i % rows.Count]);

        File.WriteAllLines(outJsonlPath, final, Encoding.UTF8);
        return outJsonlPath;
    }

    private static IEnumerable<string> ExtractRowsFromTraitFile(string path, string traitFileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        string traitId = root.TryGetProperty("id", out var idEl)
            ? idEl.GetString() ?? $"trait.{traitFileName}"
            : $"trait.{traitFileName}";

        if (!root.TryGetProperty("examples", out var examples))
            yield break;

        // Walk every array under examples (bandScenes, ignite, deescalate, trainSeeds, etc.)
        foreach (var prop in examples.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in prop.Value.EnumerateArray())
            {
                string? player =
                    GetStr(item, "player") ??
                    GetStr(item, "situation");

                string? inner =
                    GetStr(item, "inner") ??
                    GetStr(item, "expect");

                string? tags = GetStr(item, "tags");

                // trainSeeds often only have situation + expect — synthesize tags if missing
                if (string.IsNullOrWhiteSpace(player))
                    continue;

                if (string.IsNullOrWhiteSpace(inner))
                    inner = "Process the moment.";

                if (string.IsNullOrWhiteSpace(tags))
                    tags = "TAGS: none";

                if (!tags.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase))
                    tags = "TAGS: " + tags;

                string band = GetStr(item, "band") ?? "";
                string style = GetStr(item, "style") ?? "";
                string startHint = BuildStartHint(item, traitId, band, style);

                string user = $"{startHint} Player: {player}".Trim();
                string assistant = $"{inner.Trim()}\n{tags.Trim()}";

                yield return ToMessagesLine(user, assistant);
            }
        }
    }

    private static string BuildStartHint(JsonElement item, string traitId, string band, string style)
    {
        // Prefer explicit start meters if present
        var parts = new List<string>();

        foreach (var name in new[] { "startAnger", "startTension", "startPride", "startGuard", "startResentment", "startShame" })
        {
            if (item.TryGetProperty(name, out var v) && v.TryGetInt32(out int n))
            {
                string id = name.Replace("start", "trait.").ToLowerInvariant();
                // startAnger -> trait.anger
                id = "trait." + name["start".Length..].ToLowerInvariant();
                parts.Add($"{id}={n}");
            }
        }

        if (parts.Count == 0)
        {
            // fallback from band
            int approx = band switch
            {
                "off" => 20,
                "low" => 40,
                "mid" => 58,
                "high" => 75,
                "break" => 88,
                "extreme" => 96,
                _ => 50
            };
            parts.Add($"{traitId}={approx}");
        }

        if (!string.IsNullOrWhiteSpace(style))
            parts.Add($"style={style}");

        return "[" + string.Join(" ", parts) + "]";
    }

    private static string ToMessagesLine(string user, string assistant)
    {
        var obj = new
        {
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = user },
                new { role = "assistant", content = assistant }
            }
        };
        return JsonSerializer.Serialize(obj);
    }

    private static string? GetStr(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }
}