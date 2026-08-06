using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectEve.AI.Training
{
    // ============================================================
    // MODELS
    // ============================================================
    public class EveMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    public class EveExample
    {
        [JsonPropertyName("messages")]
        public List<EveMessage> Messages { get; set; } = new();
    }

    public class EvePackFile
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("notes")]
        public string Notes { get; set; } = "";

        [JsonPropertyName("examples")]
        public List<EveExample> Examples { get; set; } = new();
    }

    public class EveDatasetIndex
    {
        [JsonPropertyName("meta")]
        public EveMeta Meta { get; set; } = new();

        [JsonPropertyName("inlinePacks")]
        public Dictionary<string, EvePackFile> InlinePacks { get; set; } = new();

        [JsonPropertyName("packIndex")]
        public List<string> PackIndex { get; set; } = new();
    }

    public class EveMeta
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("character")]
        public string Character { get; set; } = "";

        [JsonPropertyName("age")]
        public int Age { get; set; }

        [JsonPropertyName("notes")]
        public List<string> Notes { get; set; } = new();

        [JsonPropertyName("packsFolder")]
        public string PacksFolder { get; set; } = "Packs";
    }

    // ============================================================
    // LOADER
    // ============================================================
    public static class EvePackLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        public static void ExportShareGpt(string outputPath, List<EveExample> examples)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("outputPath is required");

            if (examples == null)
                throw new ArgumentNullException(nameof(examples));

            var fullPath = Path.GetFullPath(outputPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var share = examples
                .Where(ex => ex?.Messages != null && ex.Messages.Count > 0)
                .Select(ex => new
                {
                    messages = ex.Messages.Select(m => new
                    {
                        role = m.Role ?? "",
                        content = m.Content ?? ""
                    }).ToList()
                })
                .ToList();

            var json = JsonSerializer.Serialize(share, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(fullPath, json);
            Console.WriteLine($"ShareGPT export: {share.Count} samples -> {fullPath}");
        }
        /// <summary>
        /// Loads eve.json index + all enabled pack files from Packs folder.
        /// Returns flattened training examples.
        /// </summary>
        public static List<EveExample> LoadAll(string eveJsonPath)
        {
            if (!File.Exists(eveJsonPath))
                throw new FileNotFoundException("eve.json not found.", eveJsonPath);

            string rootDir = Path.GetDirectoryName(eveJsonPath)!;
            string indexJson = File.ReadAllText(eveJsonPath);
            var index = JsonSerializer.Deserialize<EveDatasetIndex>(indexJson, JsonOptions)
                        ?? throw new InvalidOperationException("Failed to parse eve.json");

            var all = new List<EveExample>();
            var loadedPacks = new List<string>();
            var skippedPacks = new List<string>();

            // 1) Inline packs
            foreach (var kvp in index.InlinePacks)
            {
                var pack = kvp.Value;
                if (pack == null || !pack.Enabled)
                {
                    skippedPacks.Add($"{kvp.Key} (inline disabled)");
                    continue;
                }

                all.AddRange(pack.Examples ?? new List<EveExample>());
                loadedPacks.Add($"{kvp.Key} (inline) [{pack.Examples?.Count ?? 0}]");
            }

            // 2) External packs from Packs folder
            string packsFolder = Path.Combine(rootDir, index.Meta.PacksFolder ?? "Packs");

            if (!Directory.Exists(packsFolder))
            {
                Console.WriteLine($"WARNING: Packs folder not found: {packsFolder}");
            }
            else
            {
                // Prefer packIndex order if provided
                var names = (index.PackIndex != null && index.PackIndex.Count > 0)
                    ? index.PackIndex
                    : Directory.GetFiles(packsFolder, "*.json")
                               .Select(Path.GetFileNameWithoutExtension)
                               .Where(n => !string.IsNullOrWhiteSpace(n))
                               .Select(n => n!)
                               .ToList();

                foreach (var name in names)
                {
                    string path = Path.Combine(packsFolder, name + ".json");
                    if (!File.Exists(path))
                    {
                        skippedPacks.Add($"{name} (missing file)");
                        continue;
                    }

                    try
                    {
                        string packJson = File.ReadAllText(path);
                        var pack = JsonSerializer.Deserialize<EvePackFile>(packJson, JsonOptions);

                        if (pack == null)
                        {
                            skippedPacks.Add($"{name} (parse null)");
                            continue;
                        }

                        if (!pack.Enabled)
                        {
                            skippedPacks.Add($"{name} (disabled)");
                            continue;
                        }

                        int count = pack.Examples?.Count ?? 0;
                        if (count == 0)
                        {
                            skippedPacks.Add($"{name} (empty)");
                            continue;
                        }

                        all.AddRange(pack.Examples!);
                        loadedPacks.Add($"{name} [{count}]");
                    }
                    catch (Exception ex)
                    {
                        skippedPacks.Add($"{name} (error: {ex.Message})");
                    }
                }
            }

            Console.WriteLine("=== Eve Pack Loader ===");
            Console.WriteLine($"Dataset: {index.Meta.Name} v{index.Meta.Version}");
            Console.WriteLine($"Loaded packs: {loadedPacks.Count}");
            foreach (var p in loadedPacks)
                Console.WriteLine("  + " + p);

            if (skippedPacks.Count > 0)
            {
                Console.WriteLine($"Skipped: {skippedPacks.Count}");
                foreach (var s in skippedPacks)
                    Console.WriteLine("  - " + s);
            }

            Console.WriteLine($"Total examples: {all.Count}");
            Console.WriteLine("=======================");

            return all;
        }

        /// <summary>
        /// Converts examples into simple prompt/response pairs for LoRA training.
        /// Format:
        /// prompt = system + all user turns
        /// response = final assistant message
        /// </summary>
        public static List<(string Prompt, string Response)> ToTrainingPairs(List<EveExample> examples)
        {
            var pairs = new List<(string Prompt, string Response)>();

            foreach (var ex in examples)
            {
                if (ex.Messages == null || ex.Messages.Count == 0)
                    continue;

                string system = ex.Messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";

                var userLines = ex.Messages
                    .Where(m => m.Role == "user")
                    .Select(m => m.Content)
                    .ToList();

                // Prefer last assistant as the target response
                string response = ex.Messages.LastOrDefault(m => m.Role == "assistant")?.Content ?? "";

                if (string.IsNullOrWhiteSpace(response))
                    continue;

                // For multi-turn, include prior assistant lines in the prompt context
                var transcript = new List<string>();
                if (!string.IsNullOrWhiteSpace(system))
                    transcript.Add("SYSTEM:\n" + system.Trim());

                foreach (var msg in ex.Messages)
                {
                    if (msg.Role == "system") continue;

                    // Stop before final assistant so it becomes the label
                    if (ReferenceEquals(msg, ex.Messages.LastOrDefault(m => m.Role == "assistant")))
                        break;

                    string tag = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "USER" : "ASSISTANT";
                    transcript.Add($"{tag}:\n{msg.Content.Trim()}");
                }

                // Always ensure final user content is present
                if (userLines.Count > 0 && !transcript.Any(t => t.StartsWith("USER:")))
                {
                    transcript.Add("USER:\n" + string.Join("\n", userLines));
                }

                transcript.Add("ASSISTANT:");

                string prompt = string.Join("\n\n", transcript);
                pairs.Add((prompt, response.Trim()));
            }

            return pairs;
        }

        /// <summary>
        /// Optional helper: export merged training pairs to a JSONL-ish file for inspection.
        /// </summary>
        public static void ExportPairs(string outputPath, List<(string Prompt, string Response)> pairs)
        {
            var export = pairs.Select(p => new
            {
                prompt = p.Prompt,
                response = p.Response
            });

            string json = JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(outputPath, json);
            Console.WriteLine($"Exported {pairs.Count} pairs to {outputPath}");
        }
    }
}