using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Narrative.Scenes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace ProjectEve.AI.Brain
{
    public static class ThoughtEngine
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private const string Model = "qwen-uncensored";

        // Last sent scene snapshot (per process). Good enough for now.
        private static string _lastSceneSignature = "";
        private static string _lastAppearanceSignature = "";
        private static string _lastEmotionSignature = "";

        public static string GenerateThought(string context, SimCharacter? owner = null, SceneState? scene = null, string? clothing = null, string? hair = null, string? expression = null)
        {
            return GenerateThoughtAsync(context, owner, scene, clothing, hair, expression)
                .GetAwaiter().GetResult();
        }

        public static async Task<string> GenerateThoughtAsync(
            string context,
            SimCharacter? owner = null,
            SceneState? scene = null,
            string? clothing = null,
            string? hair = null,
            string? expression = null)
        {
            string name = owner?.Name ?? "this person";
            string traitBlock = BuildTraitBlock(owner);
            string driveBlock = BuildDriveBlock(owner);

            // Only include changed scene/appearance/emotion blocks
            string changeBlock = BuildChangeBlock(owner, scene, clothing, hair, expression);

            string systemPrompt = $@"
You are {name}'s private internal thoughts.
This is monologue only — never spoken dialogue.

IDENTITY:
- Name: {name}
- Age: {owner?.Age}
- Location: {owner?.Location}
- Occupation: {owner?.Occupation}
- Gender: {owner?.Gender}

CORE DRIVES:
{driveBlock}

ACTIVE TRAITS:
{traitBlock}

{changeBlock}

THOUGHT RULES:
- Write as {name}'s inner voice only
- If a CHANGED section appears, let that shift the thought
- If no CHANGED section appears, continue from the situation only
- Do not re-describe the whole room every time
- Be honest, raw, specific, human
- Do not speak to the player
- Do not mention AI, systems, prompts, or being an NPC
- 1 to 4 short sentences max
- No stage directions
";

            var requestBody = new
            {
                model = Model,
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = "Situation:\n" + context + "\n\nPrivate thought:" }
                },
                options = new
                {
                    temperature = 0.92,
                    top_p = 0.9,
                    repeat_penalty = 1.08
                }
            };

            try
            {
                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync("http://localhost:11434/api/chat", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                var thought = doc.RootElement
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return string.IsNullOrWhiteSpace(thought)
                    ? "Thoughts feel scattered..."
                    : thought.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ThoughtEngine error: " + ex.Message);
                return "Mind goes quiet for a moment...";
            }
        }

        private static string BuildChangeBlock(
            SimCharacter? owner,
            SceneState? scene,
            string? clothing,
            string? hair,
            string? expression)
        {
            var sb = new StringBuilder();

            // Emotion change
            if (owner?.Emotion != null)
            {
                string emotionSig = $"{owner.Emotion.State}|{owner.Emotion.Intensity:0.00}|{owner.Emotion.Desire}|{owner.Emotion.Shame}|{owner.Emotion.Resentment}";
                if (!string.Equals(emotionSig, _lastEmotionSignature, StringComparison.Ordinal))
                {
                    _lastEmotionSignature = emotionSig;
                    sb.AppendLine("CHANGED EMOTION:");
                    sb.AppendLine($"- State: {owner.Emotion.State}");
                    sb.AppendLine($"- Intensity: {owner.Emotion.Intensity:0.00}");
                    sb.AppendLine($"- Desire: {owner.Emotion.Desire}, Shame: {owner.Emotion.Shame}, Resentment: {owner.Emotion.Resentment}");
                    sb.AppendLine();
                }
            }

            // Scene change
            if (scene != null)
            {
                string sceneSig =
                    $"{scene.Location}|{scene.TimeOfDay}|{scene.Weather}|{scene.Lighting}|{scene.Smell}|{scene.Crowd}|{scene.BuildingStyle}";

                if (!string.Equals(sceneSig, _lastSceneSignature, StringComparison.Ordinal))
                {
                    _lastSceneSignature = sceneSig;
                    sb.AppendLine("CHANGED SCENE:");
                    sb.AppendLine($"- Place: {scene.Location} ({scene.TimeOfDay})");
                    sb.AppendLine($"- Weather: {scene.Weather}");
                    sb.AppendLine($"- Light: {scene.Lighting}");
                    sb.AppendLine($"- Smell: {scene.Smell}");
                    sb.AppendLine($"- Crowd: {scene.Crowd}");
                    sb.AppendLine();
                }
            }

            // Appearance change
            clothing ??= "";
            hair ??= "";
            expression ??= "";
            if (!(clothing == "" && hair == "" && expression == ""))
            {
                string appSig = $"{clothing}|{hair}|{expression}";
                if (!string.Equals(appSig, _lastAppearanceSignature, StringComparison.Ordinal))
                {
                    _lastAppearanceSignature = appSig;
                    sb.AppendLine("CHANGED APPEARANCE:");
                    if (!string.IsNullOrWhiteSpace(clothing)) sb.AppendLine($"- Clothes: {clothing}");
                    if (!string.IsNullOrWhiteSpace(hair)) sb.AppendLine($"- Hair: {hair}");
                    if (!string.IsNullOrWhiteSpace(expression)) sb.AppendLine($"- Expression: {expression}");
                    sb.AppendLine();
                }
            }

            return sb.Length == 0
                ? "NO SCENE/APPEARANCE/EMOTION CHANGE."
                : sb.ToString().Trim();
        }

        private static string BuildDriveBlock(SimCharacter? owner)
        {
            if (owner == null) return "- unknown";
            return
                $"- Goal: {owner.Goal}\n" +
                $"- Need: {owner.Need}\n" +
                $"- Fear: {owner.Fear}\n" +
                $"- Want: {owner.Want}";
        }

        private static string BuildTraitBlock(SimCharacter? owner)
        {
            if (owner?.Traits == null)
                return "- no trait data";

            try
            {
                var summary = owner.Traits.BuildLlmSummary(12);
                if (!string.IsNullOrWhiteSpace(summary))
                    return summary;
            }
            catch { }

            try
            {
                var all = owner.Traits.GetAll();
                if (all == null || all.Count == 0)
                    return "- no trait data";

                var top = all.OrderByDescending(kv => Math.Abs(kv.Value - 50f)).Take(12);
                var sb = new StringBuilder();
                foreach (var kv in top)
                    sb.AppendLine($"- {kv.Key}: {kv.Value:0}");
                return sb.ToString();
            }
            catch
            {
                return "- no trait data";
            }
        }
    }
}