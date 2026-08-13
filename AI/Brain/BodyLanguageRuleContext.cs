using ProjectEve.Characters.Base;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Loads the body-language rules JSON once, then builds a compact prompt fragment
    /// for only the NPC's currently relevant Fast traits.
    /// JSON remains definition truth; this class is only a prompt-context cache.
    /// </summary>
    public static class BodyLanguageRuleContext
    {
        private static readonly object Gate = new();
        private static JsonDocument? _doc;
        private static string? _loadedPath;

        public static string? RulesPath { get; set; }

        public static string ResolveDefaultPath()
        {
            var env = Environment.GetEnvironmentVariable("EVE_BODY_LANGUAGE_RULES");
            if (!string.IsNullOrWhiteSpace(env))
                return env;

            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Data", "World", "Ohio", "body_language_rules.json"),
                Path.Combine(baseDir, "Data", "body_language_rules.json"),
                Path.Combine(baseDir, "body_language_rules.json")
            };

            foreach (var c in candidates)
                if (File.Exists(c))
                    return c;

            return candidates[0];
        }

        public static string BuildForNpc(SimCharacter? npc, int maxTraits = 5)
        {
            if (npc?.Traits == null)
                return "No body-language rule context.";

            try
            {
                var doc = GetDocument();
                if (doc == null)
                    return BuildFallback(npc);

                if (!doc.RootElement.TryGetProperty("traits", out var traitsObj))
                    return BuildFallback(npc);

                var ranked = TraitEngine.FastIds
                    .Select(id => (Id: id, Value: npc.Traits.Get(id)))
                    .OrderByDescending(x => Math.Abs(x.Value - npc.Traits.GetSetPoint(x.Id)))
                    .ThenByDescending(x => x.Value)
                    .Take(maxTraits)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine("BODY-LANGUAGE RULE HINTS (ambiguous cues, never proof):");

                foreach (var (id, value) in ranked)
                {
                    if (!traitsObj.TryGetProperty(id, out var def))
                        continue;

                    string band = Band(value);
                    string label = def.TryGetProperty("label", out var lp) ? lp.GetString() ?? id : id;

                    sb.Append($"- {label} {value:0} ({band}): ");

                    if (def.TryGetProperty(band, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        var cues = arr.EnumerateArray()
                            .Select(x => x.GetString())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Take(5);
                        sb.Append(string.Join(", ", cues!));
                    }

                    if (def.TryGetProperty("cover", out var cover) && cover.ValueKind == JsonValueKind.Array)
                    {
                        var covers = cover.EnumerateArray()
                            .Select(x => x.GetString())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Take(3);
                        sb.Append(" | possible conscious cover: " + string.Join(", ", covers!));
                    }

                    sb.AppendLine();
                }

                sb.AppendLine("- A cue can have many causes. Prefer a small cluster or a change from this NPC's baseline.");
                sb.AppendLine("- Involuntary LEAKS should be subtle unless pressure is high.");
                return sb.ToString();
            }
            catch
            {
                return BuildFallback(npc);
            }
        }

        private static JsonDocument? GetDocument()
        {
            string path = RulesPath ?? ResolveDefaultPath();
            if (!File.Exists(path))
                return null;

            lock (Gate)
            {
                if (_doc != null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
                    return _doc;

                _doc?.Dispose();
                _doc = JsonDocument.Parse(File.ReadAllText(path));
                _loadedPath = path;
                return _doc;
            }
        }

        private static string BuildFallback(SimCharacter npc)
        {
            var top = TraitEngine.FastIds
                .Select(id => (Id: id, V: npc.Traits!.Get(id)))
                .OrderByDescending(x => Math.Abs(x.V - 50f))
                .Take(5);

            return "BODY-LANGUAGE STATE:\n" +
                   string.Join("\n", top.Select(x => $"- {x.Id}: {x.V:0}"));
        }

        private static string Band(float v) =>
            v >= 85 ? "extreme" :
            v >= 70 ? "high" :
            v >= 50 ? "mid" :
            v >= 30 ? "low" : "off";
    }
}
