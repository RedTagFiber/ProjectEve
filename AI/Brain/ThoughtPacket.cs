using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Structured result from ThoughtEngine.
    /// Thought is private.
    /// Leaks are involuntary player-visible physical tells for in-person scenes only.
    /// Tags are parsed Fast-trait deltas.
    /// </summary>
    public sealed class ThoughtPacket
    {
        public string Thought { get; set; } = "";
        public List<string> Leaks { get; set; } = new();
        public List<(string TraitId, float Delta, int Intensity)> Tags { get; set; } = new();
        public string Raw { get; set; } = "";

        public string LeakLine =>
            Leaks.Count == 0 ? "none" : string.Join("; ", Leaks);

        public static ThoughtPacket Parse(string? raw)
        {
            var p = new ThoughtPacket { Raw = raw?.Trim() ?? "" };
            if (string.IsNullOrWhiteSpace(raw))
                return p;

            var lines = raw.Replace("\r", "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToList();

            var thoughtLines = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = line["THOUGHT:".Length..].Trim();
                    if (v.Length > 0) thoughtLines.Add(v);
                    continue;
                }

                if (line.StartsWith("LEAKS:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = line["LEAKS:".Length..].Trim();
                    if (!v.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        p.Leaks = v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(x => x.Length > 0)
                            .Take(3)
                            .ToList();
                    }
                    continue;
                }

                if (line.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase))
                {
                    p.Tags = TraitEngine.ParseThoughtTags(line);
                    continue;
                }

                // tolerate older Thought output that omitted THOUGHT:
                if (!line.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("LEAKS:", StringComparison.OrdinalIgnoreCase))
                    thoughtLines.Add(line);
            }

            p.Thought = string.Join(" ", thoughtLines).Trim();
            return p;
        }
    }
}
