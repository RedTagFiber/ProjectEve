using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ProjectEve.Traits
{
    /// <summary>
    /// Reads TraitJson Fast/Mid/Slow parent files into dictionaries.
    /// Does not require every file — missing folders are skipped.
    /// </summary>
    public static class TraitJsonLoader
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// Root folder that contains FastTraits, MidTraits, SlowTraits, …
        /// Example:
        /// D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean\Characters\Traits\TraitJson
        /// </summary>
        public static string? TraitJsonRoot { get; set; }

        public static void SetRoot(string path)
        {
            TraitJsonRoot = path;
        }

        public static string ResolveDefaultRoot()
        {
            // 1) Explicit
            if (!string.IsNullOrWhiteSpace(TraitJsonRoot) && Directory.Exists(TraitJsonRoot))
                return TraitJsonRoot;

            // 2) Next to app
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "TraitJson"),
                Path.Combine(baseDir, "Characters", "Traits", "TraitJson"),
                @"D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean\Characters\Traits\TraitJson"
            };

            foreach (var c in candidates)
            {
                if (Directory.Exists(c))
                    return c;
            }

            return candidates[^1];
        }

        // ------------------------------------------------------------------
        // PUBLIC API
        // ------------------------------------------------------------------

        /// <summary>
        /// Fast 20 starting values (from stubs or defaults).
        /// </summary>
        public static Dictionary<string, float> BuildFastDefaults(float value = 45f)
        {
            var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            string[] ids =
            {
                "trait.anger", "trait.anxiety", "trait.fear", "trait.shame", "trait.guilt",
                "trait.hurt", "trait.jealousy", "trait.resentment", "trait.trust", "trait.affection",
                "trait.desire", "trait.attraction", "trait.tension", "trait.playfulness", "trait.pride",
                "trait.patience", "trait.guard", "trait.openness", "trait.loneliness", "trait.hope"
            };
            foreach (var id in ids)
                map[id] = value;
            return map;
        }

        /// <summary>
        /// Load all Mid parent JSON under MidTraits\**\*.json
        /// Returns id → priorIntensity (or default 50).
        /// </summary>
        public static Dictionary<string, float> LoadMidParents(string? root = null)
        {
            root ??= ResolveDefaultRoot();
            var midRoot = Path.Combine(root, "MidTraits");
            return LoadParentIntensities(midRoot);
        }

        /// <summary>
        /// Load all Slow parent JSON under SlowTraits\**\*.json
        /// </summary>
        public static Dictionary<string, float> LoadSlowParents(string? root = null)
        {
            root ??= ResolveDefaultRoot();
            var slowRoot = Path.Combine(root, "SlowTraits");
            return LoadParentIntensities(slowRoot);
        }

        /// <summary>
        /// Pick a playable Mid set: 3–6 traits at meaningful intensity.
        /// Others stay out of the bag (not every Mid on every NPC).
        /// </summary>
        public static Dictionary<string, float> RollMidForNpc(
            int minCount = 3,
            int maxCount = 6,
            Random? rng = null)
        {
            rng ??= new Random();
            var all = LoadMidParents();
            if (all.Count == 0)
                return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            var keys = new List<string>(all.Keys);
            Shuffle(keys, rng);

            int take = Math.Clamp(rng.Next(minCount, maxCount + 1), 1, keys.Count);
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < take; i++)
            {
                string id = keys[i];
                float prior = all[id];
                // scatter around prior a bit
                float v = prior + rng.Next(-8, 9);
                result[id] = Math.Clamp(v, 25f, 90f);
            }

            return result;
        }

        /// <summary>
        /// Roll Slow parents: most stay low; a few may pass fillThreshold 50.
        /// </summary>
        public static Dictionary<string, float> RollSlowForNpc(
            int maxHigh = 4,
            Random? rng = null)
        {
            rng ??= new Random();
            var all = LoadSlowParents();
            var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (all.Count == 0)
                return result;

            var keys = new List<string>(all.Keys);
            Shuffle(keys, rng);

            int highLeft = maxHigh;
            foreach (var id in keys)
            {
                float prior = all[id];
                if (highLeft > 0 && rng.NextDouble() < 0.35)
                {
                    // above threshold — catalog may attach later
                    result[id] = Math.Clamp(prior + rng.Next(0, 15), 50f, 95f);
                    highLeft--;
                }
                else
                {
                    // vague knowledge only
                    result[id] = Math.Clamp(rng.Next(10, 45), 0f, 49f);
                }
            }

            return result;
        }

        /// <summary>
        /// One-shot bag for a new NPC / Eve seed.
        /// </summary>
        public static void ApplyRolledLayers(NpcTraits traits, Random? rng = null)
        {
            if (traits == null) return;
            rng ??= new Random();

            var fast = BuildFastDefaults(45f);
            // slight scatter on Fast
            var keys = new List<string>(fast.Keys);
            foreach (var k in keys)
                fast[k] = Math.Clamp(fast[k] + rng.Next(-5, 6), 20f, 80f);

            var mid = RollMidForNpc(3, 6, rng);
            var slow = RollSlowForNpc(4, rng);

            traits.InitializeFromLayers(fast, mid, slow);
        }

        // ------------------------------------------------------------------
        // INTERNALS
        // ------------------------------------------------------------------

        private static Dictionary<string, float> LoadParentIntensities(string folder)
        {
            var map = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(folder))
                return map;

            foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string text = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id", out var idProp))
                        continue;

                    string? id = idProp.GetString();
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    float prior = 50f;
                    if (root.TryGetProperty("priorIntensity", out var p) && p.ValueKind == JsonValueKind.Number)
                        prior = p.GetInt32(); // JSON had int
                    else if (root.TryGetProperty("priorIntensity", out var p2) && p2.ValueKind == JsonValueKind.Number)
                        prior = (float)p2.GetDouble();

                    // scale: our Mid JSON used 1–10 style priors in design docs,
                    // but the PowerShell writer used priorIntensity as 4–6 numbers.
                    // If value <= 10, treat as 1–10 scale → *10
                    if (prior > 0 && prior <= 10)
                        prior *= 10f;

                    map[id] = Math.Clamp(prior, 0f, 100f);
                }
                catch
                {
                    // skip bad file
                }
            }

            return map;
        }

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}