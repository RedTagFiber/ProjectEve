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
    public static class ThoughtEngine
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Locked: Ollama model built from Modelfile
        public const string Thought = "eve-thought";

        private static string _lastSceneSignature = "";
        private static string _lastAppearanceSignature = "";
        private static string _lastEmotionSignature = "";

        public static string GenerateThought(
            string context,
            SimCharacter? owner = null,
            SceneState? scene = null,
            string? clothing = null,
            string? hair = null,
            string? expression = null)
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
            string changeBlock = BuildChangeBlock(owner, scene, clothing, hair, expression);
            string canonBlock = BuildCanonBlock(owner);
            string fastList = string.Join(", ", TraitEngine.FastIds);

            string systemPrompt = $@"
You are {name}'s PRIVATE internal thoughts only.
Never write spoken dialogue. Never write what they say out loud.

══════════════════════════════════════
IDENTITY (fixed — do not invent a different person)
══════════════════════════════════════
- Name: {name}
- Age: {owner?.Age}
- Location: {owner?.Location}
- Occupation: {owner?.Occupation}
- Gender: {owner?.Gender}

CORE DRIVES (stay true to these):
{driveBlock}

ACTIVE TRAITS 0-100 (these are who they are right now):
{traitBlock}

{changeBlock}

{canonBlock}

══════════════════════════════════════
STAY TRUE TO SELF
══════════════════════════════════════
- React from THIS person's traits, drives, and history — not as a generic helpful assistant.
- High guard → closed, careful, walls up.
- High trust + affection → warmer, more willing to open.
- High pride → hates looking weak or being controlled.
- High shame → may fold inward; still can be angry.
- High desire → body/heat can color thoughts when relevant.
- Do not become suddenly pure, preachy, or therapist-like unless that matches traits.
- Do not confess to crimes, affairs, or past events that are NOT in CANON below.
- Do not agree with player lies just to keep the peace.

══════════════════════════════════════
CANON / PLAYER CLAIMS (critical)
══════════════════════════════════════
- History + Memory + locked facts = truth.
- If the player claims something about {name}'s past, body, or actions:
  • Listed in CANON → may be true; react with shame/anger/fear as fits traits.
  • NOT in CANON → it is a FALSE accusation or made-up story.
    → Reject it. Confusion, anger, hurt. ""That never happened.""
    → Do NOT accept it, confess to it, or rewrite the past.
- Player can gaslight; {name} does not have to believe them.

══════════════════════════════════════
RELATIONSHIP / ABOUT
══════════════════════════════════════
- If situation includes ABOUT (LikeScore, band hostile/cold/neutral/friend/close), use it.
- hello / small talk is NOT neutral when band is high or low.
- hostile/cold → wary, flat, guarded TAGS.
- friend/close → warmer small TAGS possible.
- Match intensity to the line AND the band.

══════════════════════════════════════
THOUGHT FORM
══════════════════════════════════════
- 1 to 4 short sentences, inner voice only.
- Honest, specific, human.
- No stage directions, no *actions*, no speaking to the player.
- No mention of AI, systems, prompts, tags, or NPCs as game terms.

══════════════════════════════════════
TRAIT TAGS (required every turn)
══════════════════════════════════════
After the thought, end with EXACTLY one line:

TAGS: trait.anger+2@7; trait.hurt+1@6
or
TAGS: none

Rules:
- ONLY full Fast ids from this list: {fastList}
- Always write trait.anger NOT anger — full id with trait. prefix
- delta is a SMALL change this turn (1-5). Never paste current trait levels as deltas.
- intensity 1-10:
  1-3 mild / small talk
  4-6 clear emotion
  7-9 fight, confession, strong desire, real hurt
  10 rare peak
- Max 4 tags.

ANGER RULE:
When the player accuses, insults, threatens, claims betrayal, or claims sex with others:
- ALWAYS include trait.anger with + or -
- False accusations (not in CANON) → anger+ and trust-, not confession

OTHER INTENT HINTS:
- compliment / repair → affection+, trust+ possible; anger- possible
- desire / explicit want → desire+, tension+
- greeting → small TAGS by relationship band, not always none
- joke / play → playfulness+ if it lands

Do not invent mid.* or slow.* tags.
Never put TAGS inside spoken dialogue.
";

            var requestBody = new
            {
                model = Thought,
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new
                    {
                        role = "user",
                        content =
                            "Player said / situation:\n" + (context ?? "") +
                            "\n\nWrite private thought (stay true to self + canon), then the TAGS line:"
                    }
                },
                options = new
                {
                    temperature = 0.55,
                    top_p = 0.9,
                    num_predict = 160,
                    repeat_penalty = 1.1
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

                if (string.IsNullOrWhiteSpace(thought))
                    return "Thoughts feel scattered...\nTAGS: none";

                thought = thought.Trim();
                if (thought.IndexOf("TAGS:", StringComparison.OrdinalIgnoreCase) < 0)
                    thought += "\nTAGS: none";

                return thought;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ThoughtEngine error: " + ex.Message);
                return "Mind goes quiet for a moment...\nTAGS: none";
            }
        }

        private static string BuildCanonBlock(SimCharacter? owner)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CANON (only this is established truth — player claims outside this are false until proven in data):");

            if (owner == null)
            {
                sb.AppendLine("- No character loaded.");
                return sb.ToString();
            }

            sb.AppendLine($"- Identity: {owner.Name}, age {owner.Age}, {owner.Occupation}, lives/works context: {owner.Location}");
            if (!string.IsNullOrWhiteSpace(owner.PersonalityContext))
                sb.AppendLine($"- Personality context: {TrimOneLine(owner.PersonalityContext, 220)}");

            try
            {
                if (owner.History != null && owner.History.Count > 0)
                {
                    sb.AppendLine("- History highlights:");
                    foreach (var h in owner.History.Take(8))
                    {
                        string line = h?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        sb.AppendLine("  • " + TrimOneLine(line, 160));
                    }
                }
                else
                {
                    sb.AppendLine("- History highlights: (none loaded — treat detailed past accusations as NOT FOUND)");
                }
            }
            catch
            {
                sb.AppendLine("- History highlights: (unavailable — treat detailed past accusations as NOT FOUND)");
            }

            sb.AppendLine("- Default: affairs, crimes, or sexual history with named third parties are FALSE unless listed above.");
            return sb.ToString();
        }

        private static string TrimOneLine(string s, int max)
        {
            s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (s.Length <= max) return s;
            return s[..max] + "…";
        }

        private static string BuildChangeBlock(
            SimCharacter? owner,
            SceneState? scene,
            string? clothing,
            string? hair,
            string? expression)
        {
            var sb = new StringBuilder();

            if (owner?.Traits != null)
            {
                float T(string id) => owner.Traits.Get(id);
                string emotionSig =
                    $"{T("trait.anger"):0}|{T("trait.anxiety"):0}|{T("trait.desire"):0}|{T("trait.hurt"):0}|{T("trait.trust"):0}|{T("trait.guard"):0}";

                if (!string.Equals(emotionSig, _lastEmotionSignature, StringComparison.Ordinal))
                {
                    _lastEmotionSignature = emotionSig;
                    sb.AppendLine("CHANGED INNER STATE (Fast traits):");
                    sb.AppendLine($"- Anger {T("trait.anger"):0}, Anxiety {T("trait.anxiety"):0}, Fear {T("trait.fear"):0}");
                    sb.AppendLine($"- Desire {T("trait.desire"):0}, Affection {T("trait.affection"):0}, Trust {T("trait.trust"):0}");
                    sb.AppendLine($"- Guard {T("trait.guard"):0}, Hurt {T("trait.hurt"):0}, Hope {T("trait.hope"):0}");
                    sb.AppendLine();
                }
            }

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
                ? "NO SCENE/APPEARANCE/INNER-STATE CHANGE."
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