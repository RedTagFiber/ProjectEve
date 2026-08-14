using ProjectEve.Characters.Base;
using ProjectEve.Characters.Cognition;
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
            string selfBodyFact = SelfBodyFactContext.Build(owner, context);
            int eventStrength = SelfBodyFactContext.IsOrdinarySelfFactQuery(context)
                ? 0
                : EstimateEventStrength(context);

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

COGNITION / EDUCATION / SPEECH
{CognitionPromptContext.BuildForNpc(owner)}

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

SELF BODY FACT QUERY
{selfBodyFact}

RULES
- History + memory + locked facts are established truth.
- Do not invent crimes, affairs, past relationships, injuries, promises or events.
- If SELF BODY FACT QUERY says ORDINARY SELF FACT, this is a factual question the NPC already knows. Do not turn it into seduction, suspicion, guard, fear, or relationship pressure by default.
- For an ordinary self-body fact question, TAGS should normally be none.
- If SELF BODY FACT QUERY says PRIVATE ADULT SELF FACT, the NPC knows the fact but can dislike the question or refuse disclosure according to privacy/boundaries/context.
- Private anatomy knowledge is not sexual history and does not automatically create attraction.
- Unsupported player claims about the past are claims, not canon.
- React from this person's traits and history, not like a helpful assistant.
- Thought may be kind, truthful, loving, contradictory, petty, defensive, jealous, cruel, afraid, manipulative, sexual, criminal, remorseful, remorseless, or caring when supported by THIS character and THIS situation.
- Do not sanitize a character into a helpful or morally tidy assistant.
- Do not make a character dark merely because dark behavior is allowed.
- Intelligence affects reasoning style, not morality. Knowledge is limited to what this character has actually learned.
- Thought is 1-4 short sentences.
- Never write spoken dialogue.
- Never write what the player thinks.
- Never create mid.* or slow.* TAGS.

EVENT STRENGTH
- This moment is calibrated as strength {eventStrength} on a 0-3 scale.
- 0 = neutral/small talk. Usually TAGS: none; if anything changes, use only one tiny ±1 change.
- 1 = mild social signal such as a normal compliment, apology, reassurance, or light awkwardness. Usually 0-2 tags, max ±1 each.
- 2 = meaningful emotional pressure such as a real argument, rejection, jealousy, accusation, vulnerable disclosure, or strong affection. Max ±3.
- 3 = major event such as betrayal, threat, violence, traumatic discovery, serious confession, breakup, or immediate danger. Max ±5.
- Do NOT invent threat, manipulation, seduction, fear, or hidden motive merely because the speaker says hello, compliments someone, apologizes, or is friendly.
- Existing history/relationship may justify a stronger reaction, but the CURRENT message still controls how much traits move this turn.
- A neutral message can trigger a private thought colored by personality without moving traits much.

FAST TAGS
- Allowed ids only: {fastList}
- Max 4 tags.
- Delta usually 1-5.
- Intensity 1-10.
- Full id required: trait.anger, never just anger.
- Small talk should usually cause small changes.
- Strong accusation, betrayal, confession, danger, grief or intimacy may cause stronger changes.

OUTPUT CONTRACT — STRICT
Return EXACTLY three lines. No analysis before them. No explanation after them.
Do not output counts such as "LEAKS: 2 (...)". Do not output trait words such as "proud / angry".
Use only these three labels:

THOUGHT: <1-4 short private sentences>
LEAKS: none OR cue_id; cue_id; cue_id
TAGS: none OR trait.id+delta@intensity; trait.id+delta@intensity

VALID EXAMPLE:
THOUGHT: He knows more than I expected. Stay calm and find out what he actually has.
LEAKS: jaw_tight; eye_break_short
TAGS: trait.anxiety+2@6; trait.guard+2@7

INVALID:
LEAKS: 2 (clenched jaw, quick breathing)
TAGS: proud / guarded / angry
""";

            string userPrompt =
                "CURRENT SITUATION / PLAYER MESSAGE:\n" +
                (context ?? "") +
                "\n\nWrite the three required lines only.";

            var requestBody = new
            {
                model = Model,
                stream = false,
                think = false, // Qwen3: never return a visible reasoning trace
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
                raw = CalibrateTraitMovement(raw, eventStrength);
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
            string text = (raw ?? "").Replace("\r", "").Trim();

            string thought = "";
            string leaks = "none";
            string tags = "none";

            foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();

                if (line.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase))
                {
                    thought = line["THOUGHT:".Length..].Trim();
                    continue;
                }

                if (line.StartsWith("LEAKS:", StringComparison.OrdinalIgnoreCase))
                {
                    string v = line["LEAKS:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(v))
                        leaks = v;
                    continue;
                }

                if (line.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = "TAGS: " + line["TAGS:".Length..].Trim();
                    try
                    {
                        var parsed = TraitEngine.ParseThoughtTags(candidate);
                        if (parsed != null && parsed.Count > 0)
                            tags = candidate["TAGS:".Length..].Trim();
                    }
                    catch
                    {
                        tags = "none";
                    }
                    continue;
                }

                // Qwen should not emit extra lines, but keep a short unlabeled line
                // as thought rather than letting formatting noise destroy the turn.
                if (string.IsNullOrWhiteSpace(thought) &&
                    !line.StartsWith("Thinking", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("<think", StringComparison.OrdinalIgnoreCase))
                {
                    thought = line;
                }
            }

            if (string.IsNullOrWhiteSpace(thought))
                thought = "Mind goes quiet for a moment.";

            if (channel.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                leaks = "none";
            }
            else
            {
                leaks = NormalizeLeaks(leaks);
            }

            return $"THOUGHT: {thought}\nLEAKS: {leaks}\nTAGS: {tags}";
        }

        private static string NormalizeLeaks(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("none", StringComparison.OrdinalIgnoreCase))
                return "none";

            // Reject the exact failure form seen in manual testing:
            // "2 (clenched jaw, quickened breathing)".
            if (char.IsDigit(value.TrimStart()[0]) || value.Contains('(') || value.Contains(')'))
                return "none";

            var cues = value
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(SlugCue)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(3)
                .ToList();

            return cues.Count == 0 ? "none" : string.Join("; ", cues);
        }

        private static string SlugCue(string cue)
        {
            var sb = new StringBuilder();
            bool underscore = false;

            foreach (char ch in (cue ?? "").Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    underscore = false;
                }
                else if (!underscore && sb.Length > 0)
                {
                    sb.Append('_');
                    underscore = true;
                }
            }

            return sb.ToString().Trim('_');
        }

        private static int EstimateEventStrength(string? context)
        {
            string s = (context ?? "").Trim().ToLowerInvariant();
            if (s.Length == 0)
                return 0;

            bool Has(params string[] words) =>
                words.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase));

            // Major danger / betrayal / life-changing moments.
            if (Has(
                "i'm going to kill", "i will kill", "gun", "knife", "attack",
                "hit you", "hurt you", "body", "murder", "dead",
                "cheated on you", "slept with", "affair", "betrayed",
                "we're done", "we are done", "break up", "divorce",
                "i lied about", "i killed", "i did it"))
                return 3;

            // Meaningful pressure / accusation / rejection / vulnerable disclosure.
            if (Has(
                "you lied", "you're lying", "you are lying", "why did you lie",
                "don't trust you", "do not trust you", "hate you",
                "leave me alone", "get away", "jealous", "who were you with",
                "i love you", "love you", "i need you", "i'm scared",
                "i am scared", "i miss you", "miss you"))
                return 2;

            // Mild social events.
            if (Has(
                "sorry", "apolog", "thank you", "thanks",
                "beautiful", "pretty", "lovely", "handsome", "cute",
                "you look", "nice to see", "good to see",
                "just trying", "get to know you"))
                return 1;

            // Short greetings / ordinary small talk are neutral.
            if (s.Length <= 80 && Has(
                "hello", "hey", "hi", "good morning", "good afternoon",
                "good evening", "how are you", "how's it going", "whats up",
                "what's up"))
                return 0;

            // Ordinary conversation defaults mild-neutral, not suspicious.
            return s.Length <= 140 ? 1 : 2;
        }

        private static string CalibrateTraitMovement(string raw, int eventStrength)
        {
            var packet = ThoughtPacket.Parse(raw);

            int maxTags = eventStrength switch
            {
                0 => 1,
                1 => 2,
                2 => 3,
                _ => 4
            };

            float maxDelta = eventStrength switch
            {
                0 => 1f,
                1 => 1f,
                2 => 3f,
                _ => 5f
            };

            var calibrated = packet.Tags
                .Take(maxTags)
                .Select(t =>
                {
                    float d = Math.Clamp(t.Delta, -maxDelta, maxDelta);
                    int intensityCap = eventStrength switch
                    {
                        0 => 4,
                        1 => 5,
                        2 => 8,
                        _ => 10
                    };
                    int inten = Math.Clamp(t.Intensity, 1, intensityCap);
                    return (t.TraitId, Delta: d, Intensity: inten);
                })
                .Where(t => Math.Abs(t.Delta) >= 0.5f)
                .ToList();

            string tagLine = "none";
            if (calibrated.Count > 0)
            {
                tagLine = string.Join(
                    "; ",
                    calibrated.Select(t =>
                    {
                        string delta = t.Delta >= 0
                            ? "+" + Math.Abs(t.Delta).ToString("0")
                            : "-" + Math.Abs(t.Delta).ToString("0");
                        return $"{t.TraitId}{delta}@{t.Intensity}";
                    }));
            }

            return
                $"THOUGHT: {packet.Thought}\n" +
                $"LEAKS: {packet.LeakLine}\n" +
                $"TAGS: {tagLine}";
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

            if (owner.Appearance != null)
            {
                sb.AppendLine("ESTABLISHED SELF APPEARANCE:");
                sb.AppendLine(owner.Appearance.ToSelfKnowledgeFragment());
            }

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
