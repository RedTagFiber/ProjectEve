using ProjectEve.Characters.Base;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// In-person dialogue.
    ///
    /// ThoughtEngine supplies involuntary LEAKS.
    /// This engine chooses conscious PRESENTATION and SAY.
    /// It never decides hidden truth; it only writes what the NPC deliberately does/says.
    /// </summary>
    public static class DialogueEngineInPerson
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public const string Model = "eve-dialogue";

        public static async Task<InPersonDialogueResult> GenerateAsync(
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext,
            string sceneContext,
            CancellationToken cancellationToken = default)
        {
            string prompt = BuildPrompt(
                owner,
                playerMessage,
                thought,
                recentChat,
                relationshipContext,
                sceneContext);

            try
            {
                string raw = await CallModelAsync(prompt, cancellationToken);
                return Parse(raw, thought);
            }
            catch (Exception ex)
            {
                return new InPersonDialogueResult
                {
                    Observed = RenderLeaks(thought),
                    Presentation = "",
                    Say = "...",
                    Source = "error",
                    Error = ex.Message
                };
            }
        }

        private static string BuildPrompt(
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext,
            string sceneContext)
        {
            return $"""
SYSTEM ROLE
You write {owner.Name}'s CONSCIOUS in-person social response.
ThoughtEngine has already decided the involuntary body leaks.
Do not replace or reinterpret those leaks.
Your job is:
1) decide any DELIBERATE presentation/body action,
2) write what {owner.Name} actually says.

CHARACTER
{DialoguePromptContext.BuildCharacterContext(owner)}

RELATIONSHIP
{relationshipContext}

SCENE / WHO IS PRESENT
{sceneContext}

PRIVATE THOUGHT — PRIVATE; DO NOT SIMPLY CONFESS IT
{thought.Thought}

INVOLUNTARY LEAKS ALREADY HAPPENING
{thought.LeakLine}

BODY LANGUAGE RULES
{BodyLanguageRuleContext.BuildForNpc(owner)}

MEMORY / HISTORY
{DialoguePromptContext.BuildHistoryMemoryContext(owner)}

REALISM RULES
- The body can leak one thing while the person consciously presents another.
- A guarded person may recover quickly, fold arms, go still, change distance, or control eye contact.
- A proud person may deliberately square shoulders or steady their voice.
- An affectionate person may stay close even while hurt.
- A skilled liar may consciously behave near their own baseline; do NOT make deception obvious by default.
- Never write "she is lying", "he looks guilty", "obviously nervous", or other mind-reading labels.
- Describe observable movement only.
- Small cues are usually better than theatrical acting.
- Do not invent history/canon.
- Do not speak for the player.
- Do not output private thought.
- Spoken words may disagree with private thought.

RECENT CONVERSATION
{recentChat}

LATEST PLAYER LINE / EVENT
{playerMessage}

OUTPUT EXACTLY TWO LINES:
PRESENTATION: <0-2 deliberate physical/social actions, or none>
SAY: <spoken words only>
""";
        }

        private static async Task<string> CallModelAsync(
            string prompt,
            CancellationToken cancellationToken)
        {
            var body = new
            {
                model = Model,
                stream = false,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content =
                            "You write naturalistic in-person character behavior. " +
                            "Output exactly PRESENTATION and SAY."
                    },
                    new { role = "user", content = prompt }
                },
                options = new
                {
                    temperature = 0.68,
                    top_p = 0.9,
                    num_predict = 120,
                    repeat_penalty = 1.12
                }
            };

            string json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(
                "http://localhost:11434/api/chat",
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);

            return doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "PRESENTATION: none\nSAY: ...";
        }

        private static InPersonDialogueResult Parse(
            string raw,
            ThoughtPacket thought)
        {
            string presentation = "";
            string say = "";

            foreach (var lineRaw in (raw ?? "").Replace("\r", "").Split('\n'))
            {
                string line = lineRaw.Trim();

                if (line.StartsWith("PRESENTATION:", StringComparison.OrdinalIgnoreCase))
                {
                    presentation = line["PRESENTATION:".Length..].Trim();
                    if (presentation.Equals("none", StringComparison.OrdinalIgnoreCase))
                        presentation = "";
                }
                else if (line.StartsWith("SAY:", StringComparison.OrdinalIgnoreCase))
                {
                    say = line["SAY:".Length..].Trim().Trim('"');
                }
            }

            if (string.IsNullOrWhiteSpace(say))
                say = "...";

            return new InPersonDialogueResult
            {
                Observed = RenderLeaks(thought),
                Presentation = presentation,
                Say = say,
                Source = "llm"
            };
        }

        private static string RenderLeaks(ThoughtPacket thought)
        {
            if (thought.Leaks.Count == 0)
                return "";

            return string.Join(
                ", ",
                thought.Leaks
                    .Take(3)
                    .Select(HumanizeCue));
        }

        private static string HumanizeCue(string cue)
        {
            return cue.Trim()
                .Replace("_", " ");
        }
    }

    public sealed class InPersonDialogueResult
    {
        public string Observed { get; set; } = "";
        public string Presentation { get; set; } = "";
        public string Say { get; set; } = "...";
        public string Source { get; set; } = "";
        public string? Error { get; set; }

        /// <summary>
        /// Basic player-facing render. A richer UI renderer can replace this later.
        /// </summary>
        public string ToPlayerText()
        {
            var actionBits = new[]
            {
                string.IsNullOrWhiteSpace(Observed) ? null : Observed,
                string.IsNullOrWhiteSpace(Presentation) ? null : Presentation
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

            string action = actionBits.Count == 0
                ? ""
                : "*" + string.Join(". ", actionBits) + ".*\n";

            return action + $"**{Say}**";
        }
    }
}
