using ProjectEve.Characters.Base;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Text-message dialogue.
    ///
    /// LLM is FIRST choice.
    /// LineBank is only a timeout/failure fallback supplied by the caller.
    /// A completed successful LLM line can be fed back to LineBank through storeLlmLine.
    /// </summary>
    public static class DialogueEngineText
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public const string Model = "eve-dialogue";

        public static async Task<TextDialogueResult> GenerateAsync(
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext,
            Func<string?>? pullFallbackLine = null,
            Action<string>? storeLlmLine = null,
            int softTimeoutMs = 3500,
            CancellationToken cancellationToken = default)
        {
            string prompt = BuildPrompt(
                owner, playerMessage, thought, recentChat, relationshipContext);

            Task<string> llmTask = CallModelAsync(prompt, cancellationToken);

            if (softTimeoutMs > 0)
            {
                Task completed = await Task.WhenAny(
                    llmTask,
                    Task.Delay(softTimeoutMs, cancellationToken));

                if (completed != llmTask)
                {
                    string? fallback = null;
                    try { fallback = pullFallbackLine?.Invoke(); } catch { }

                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        // Keep the LLM task alive only long enough to harvest/store a fresh line.
                        _ = HarvestLateLineAsync(llmTask, storeLlmLine);

                        return new TextDialogueResult
                        {
                            Text = CleanText(fallback),
                            Source = "bank_timeout"
                        };
                    }
                }
            }

            try
            {
                string fresh = CleanText(await llmTask);
                if (!string.IsNullOrWhiteSpace(fresh) && fresh != "...")
                {
                    try { storeLlmLine?.Invoke(fresh); } catch { }
                }

                return new TextDialogueResult
                {
                    Text = fresh,
                    Source = "llm"
                };
            }
            catch (Exception ex)
            {
                string? fallback = null;
                try { fallback = pullFallbackLine?.Invoke(); } catch { }

                return new TextDialogueResult
                {
                    Text = !string.IsNullOrWhiteSpace(fallback)
                        ? CleanText(fallback)
                        : "...",
                    Source = !string.IsNullOrWhiteSpace(fallback)
                        ? "bank_error"
                        : "error",
                    Error = ex.Message
                };
            }
        }

        private static string BuildPrompt(
            SimCharacter owner,
            string playerMessage,
            ThoughtPacket thought,
            string recentChat,
            string relationshipContext)
        {
            return $"""
SYSTEM ROLE
You write ONLY the text message that {owner.Name} chooses to send.
You do not write private thoughts, body language, narration, labels, or analysis.

CHARACTER
{DialoguePromptContext.BuildCharacterContext(owner)}

RELATIONSHIP TO THE PERSON/PEOPLE IN THIS CHAT
{relationshipContext}

PRIVATE THOUGHT — NEVER QUOTE IT DIRECTLY UNLESS THE CHARACTER WOULD ACTUALLY SAY IT
{thought.Thought}

CURRENT MEMORY / HISTORY
{DialoguePromptContext.BuildHistoryMemoryContext(owner)}

TEXTING REALISM
- Text cannot reveal physical body language the recipient cannot see.
- Let wording, punctuation, message length, delay-feel, warmth/coldness, fragments, emoji use, and avoidance carry tone.
- Do not explain emotions.
- Do not announce trait numbers.
- Do not become a therapist/help-desk voice.
- Do not magically know things this character has not learned.
- If the character is hurt but guarded, the message may understate it.
- If angry, a short reply may be stronger than an essay.
- If affectionate, warmth can appear naturally without constant declarations.
- If lying or hiding something, write the chosen message; never label it as a lie.
- Usually 1-3 short phone-message bubbles worth of text.
- Stay consistent with recent conversation.

RECENT CHAT
{recentChat}

LATEST MESSAGE
{playerMessage}

OUTPUT
Only what {owner.Name} sends. No MESSAGE: label.
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
                        content = "You are a naturalistic character text-message writer. Output the character's sent text only."
                    },
                    new { role = "user", content = prompt }
                },
                options = new
                {
                    temperature = 0.78,
                    top_p = 0.9,
                    num_predict = 130,
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
                .GetString() ?? "...";
        }

        private static async Task HarvestLateLineAsync(
            Task<string> llmTask,
            Action<string>? storeLlmLine)
        {
            if (storeLlmLine == null)
                return;

            try
            {
                string fresh = CleanText(await llmTask);
                if (!string.IsNullOrWhiteSpace(fresh) && fresh != "...")
                    storeLlmLine(fresh);
            }
            catch
            {
                // fallback already served; late generation must never hurt gameplay
            }
        }

        private static string CleanText(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "...";

            var sb = new StringBuilder();
            foreach (var lineRaw in raw.Replace("\r", "").Split('\n'))
            {
                string line = lineRaw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("LEAKS:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("OBSERVED:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("PRESENTATION:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.StartsWith("SAY:", StringComparison.OrdinalIgnoreCase))
                    line = line[4..].Trim();

                if (sb.Length > 0) sb.AppendLine();
                sb.Append(line.Trim('"'));
            }

            return sb.Length == 0 ? "..." : sb.ToString().Trim();
        }
    }

    public sealed class TextDialogueResult
    {
        public string Text { get; set; } = "...";
        public string Source { get; set; } = "";
        public string? Error { get; set; }
    }
}
