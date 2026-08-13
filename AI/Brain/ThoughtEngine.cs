using ProjectEve.Characters.Base;
using ProjectEve.Narrative.Scenes;
using ProjectEve.Traits;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Private interpretation engine.
    ///
    /// Output contract:
    /// THOUGHT: ...
    /// LEAKS: cue_id; cue_id  OR none
    /// TAGS: trait.x+N@I; ...   OR none
    ///
    /// Thought is private.
    /// LEAKS are involuntary physical tells, used only when the player could observe them.
    /// TAGS are Fast-trait deltas and are applied by Brain/TraitEngine.
    /// </summary>
    public static class ThoughtEngine
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public const string Model = "eve-thought";

        public static ThoughtPacket GeneratePacket(
            string context,
            SimCharacter? owner = null,
            SceneState? scene = null,
            string? clothing = null,
            string? hair = null,
            string? expression = null,
            string channel = "text",
            string audience = "")
        {
            return GeneratePacketAsync(
                context, owner, scene, clothing, hair, expression, channel, audience)
                .GetAwaiter().GetResult();
        }

        // Compatibility with older Brain code.
        public static string GenerateThought(
            string context,
            SimCharacter? owner = null,
            SceneState? scene = null,
            string? clothing = null,
            string? hair = null,
            string? expression = null)
        {
            return GeneratePacket(context, owner, scene, clothing, hair, expression).Raw;
        }

        public static async Task<ThoughtPacket> GeneratePacketAsync(
            string context,
            SimCharacter? owner = null,
            SceneState? scene = null,
            string? clothing = null,
            string? hair = null,
            string? expression = null,
            string channel = "text",
            string audience = "")
        {
            string name = owner?.Name ?? "this person";
            string fastList = string.Join(", ", TraitEngine.FastIds);

            string systemPrompt = $"""
You are {name}'s PRIVATE mind. You are not the dialogue writer.

Your job is to decide:
1) what {name} privately thinks/feels about THIS moment,
2) what involuntary physical reactions leak out before conscious social control,
3) the SMALL Fast-trait changes caused by this moment.

IDENTITY / DRIVES
- Name: {name}
- Age: {owner?.Age}
- Gender: {owner?.Gender}
- Occupation: {owner?.Occupation}
- Location: {owner?.Location}
- Goal: {owner?.Goal}
- Need: {owner?.Need}
- Fear: {owner?.Fear}
- Want: {owner?.Want}

ACTIVE TRAITS
{BuildTraitBlock(owner)}

BODY LANGUAGE
{BodyLanguageRuleContext.BuildForNpc(owner)}

COMMUNICATION
- Channel: {channel}
- Audience/present people: {audience}
- If channel is text, LEAKS must be none because the other person cannot see the body.
- If in_person, choose 0-3 subtle involuntary observable cues.
- LEAKS are NOT a lie detector. Anxiety, shame, hurt, attraction, anger and fear can mimic deception.
- Never output hidden labels such as "she is lying" as a leak.
- Cue wording should be compact ids/phrases such as eye_break_short, jaw_tight, breath_catch, smile_falters.

CANON / MEMORY / HISTORY
{BuildCanonBlock(owner)}

RULES
- History + memory + locked facts are established truth.
- Do not invent crimes, affairs, past relationships, injuries, promises or events.
- Unsupported player claims about the past are claims, not canon.
- React from this person's traits and history, not like a helpful assistant.
- Thought may be contradictory, petty, warm, defensive, jealous, afraid, caring, etc. when supported.
- Thought is 1-4 short sentences.
- Never write spoken dialogue.
- Never write what the player thinks.
- Never create mid.* or slow.* TAGS.

FAST TAGS
- Allowed ids only: {fastList}
- Max 4 tags.
- Delta usually 1-5.
- Intensity 1-10.
- Full id required: trait.anger, never just anger.
- Small talk should usually cause small changes.
- Strong accusation, betrayal, confession, danger, grief or intimacy may cause stronger changes.

OUTPUT EXACTLY THREE LINES:
THOUGHT: <private inner thought>
LEAKS: <0-3 semicolon-separated involuntary cues, or none>
TAGS: <Fast tags, or none>
""";

            string userPrompt =
                "CURRENT SITUATION / PLAYER MESSAGE:\n" +
                (context ?? "") +
                "\n\nWrite the three required lines only.";

            var requestBody = new
            {
                model = Model,
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                options = new
                {
                    temperature = 0.50,
                    top_p = 0.9,
                    num_predict = 180,
                    repeat_penalty = 1.12
                }
            };

            try
            {
                string json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync("http://localhost:11434/api/chat", content);
                response.EnsureSuccessStatusCode();

                string responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                string raw = doc.RootElement
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()?
                    .Trim() ?? "";

                if (string.IsNullOrWhiteSpace(raw))
                    raw = "THOUGHT: Mind goes quiet for a moment.\nLEAKS: none\nTAGS: none";

                raw = Normalize(raw, channel);
                return ThoughtPacket.Parse(raw);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ThoughtEngine error: " + ex.Message);
                return ThoughtPacket.Parse(
                    "THOUGHT: Mind goes quiet for a moment.\nLEAKS: none\nTAGS: none");
            }
        }

        private static string Normalize(string raw, string channel)
        {
            string text = raw.Replace("\r", "").Trim();

            bool hasThought = text.Contains("THOUGHT:", StringComparison.OrdinalIgnoreCase);
            bool hasLeaks = text.Contains("LEAKS:", StringComparison.OrdinalIgnoreCase);
            bool hasTags = text.Contains("TAGS:", StringComparison.OrdinalIgnoreCase);

            if (!hasThought)
                text = "THOUGHT: " + text;

            if (!hasLeaks)
                text += "\nLEAKS: none";

            if (!hasTags)
                text += "\nTAGS: none";

            if (channel.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                var lines = text.Split('\n')
                    .Select(x => x.StartsWith("LEAKS:", StringComparison.OrdinalIgnoreCase)
                        ? "LEAKS: none"
                        : x);
                text = string.Join("\n", lines);
            }

            return text;
        }

        private static string BuildTraitBlock(SimCharacter? owner)
        {
            if (owner?.Traits == null)
                return "- no trait data";

            try
            {
                return owner.Traits.BuildLlmSummary(14);
            }
            catch
            {
                return string.Join(
                    "\n",
                    owner.Traits.GetAll()
                        .OrderByDescending(kv => Math.Abs(kv.Value - 50f))
                        .Take(14)
                        .Select(kv => $"- {kv.Key}: {kv.Value:0}"));
            }
        }

        private static string BuildCanonBlock(SimCharacter? owner)
        {
            var sb = new StringBuilder();

            if (owner == null)
                return "- no character loaded";

            sb.AppendLine("IDENTITY:");
            sb.AppendLine($"- {owner.Name}, age {owner.Age}, {owner.Occupation}, {owner.Location}");

            if (!string.IsNullOrWhiteSpace(owner.PersonalityContext))
                sb.AppendLine("- Personality: " + OneLine(owner.PersonalityContext, 220));

            try
            {
                if (owner.History != null && owner.History.Count > 0)
                {
                    sb.AppendLine("LIFE HISTORY:");
                    foreach (var h in owner.History
                        .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Summary))
                        .OrderByDescending(h => h.Importance)
                        .Take(8))
                    {
                        sb.AppendLine(
                            $"- Age {h.Age}: {OneLine(h.Summary ?? "", 170)} " +
                            $"[{h.Category ?? "other"}; importance {h.Importance}]");
                    }
                }
                else
                {
                    sb.AppendLine("LIFE HISTORY: none loaded.");
                }
            }
            catch
            {
                sb.AppendLine("LIFE HISTORY: unavailable.");
            }

            try
            {
                if (owner.MemoryDB != null)
                {
                    var memories = owner.MemoryDB.GetMemories(owner.Id, 12)
                        .OrderByDescending(m => m.Importance)
                        .ThenByDescending(m => m.Strength)
                        .Take(6)
                        .ToList();

                    if (memories.Count > 0)
                    {
                        sb.AppendLine("RETRIEVED PERSONAL MEMORIES:");
                        foreach (var m in memories)
                        {
                            string eventBit = string.IsNullOrWhiteSpace(m.EventId)
                                ? ""
                                : $" event={m.EventId}";
                            sb.AppendLine(
                                $"- {OneLine(m.Summary, 170)} " +
                                $"[{m.Category}; imp {m.Importance}; strength {m.Strength:0}{eventBit}]");
                        }
                    }
                }
            }
            catch
            {
                // Memory retrieval must never break thought generation.
            }

            sb.AppendLine(
                "Default: detailed past claims not supported above are NOT established canon.");
            return sb.ToString();
        }

        private static string OneLine(string? s, int max)
        {
            string v = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return v.Length <= max ? v : v[..max] + "…";
        }
    }
}
