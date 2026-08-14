using ProjectEve.Characters.Base;
using System;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Text-message dialogue.
    ///
    /// Qwen writes the final reply.
    /// LineBank may provide an OPTIONAL seed/candidate, but never speaks directly
    /// unless Qwen fails. Successful final Qwen lines can be stored back into LineBank.
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
            string? lineBankSeed = null,
            string? previousNpcReply = null,
            Action<string>? storeFinalLine = null,
            CancellationToken cancellationToken = default)
        {
            string prompt = BuildPrompt(
                owner,
                playerMessage,
                thought,
                recentChat,
                relationshipContext,
                lineBankSeed,
                previousNpcReply);

            try
            {
                string fresh = CleanText(
                    await CallModelAsync(prompt, cancellationToken),
                    owner.Name);

                bool repeated = SameReply(fresh, previousNpcReply);
                bool copiedPlayer = CopiesPlayerMessage(fresh, playerMessage);

                if (repeated || copiedPlayer)
                {
                    // One retry only, and strip the seed so a sticky catalog line
                    // cannot drag Qwen back into the same answer.
                    string retryPrompt = BuildPrompt(
                        owner,
                        playerMessage,
                        thought,
                        recentChat,
                        relationshipContext,
                        lineBankSeed: null,
                        previousNpcReply: previousNpcReply) +
                        "\n\nRETRY RULE: The prior draft repeated the NPC or copied the player. " +
                        "Write a genuinely new NPC response. Do not echo the player's sentence.";

                    fresh = CleanText(
                        await CallModelAsync(retryPrompt, cancellationToken),
                        owner.Name);
                }

                if (!string.IsNullOrWhiteSpace(fresh) && fresh != "...")
                {
                    try { storeFinalLine?.Invoke(fresh); } catch { }
                }

                return new TextDialogueResult
                {
                    Text = fresh,
                    Source = (repeated || copiedPlayer)
                        ? "ai_retry_no_seed"
                        : string.IsNullOrWhiteSpace(lineBankSeed)
                            ? "ai_new"
                            : "ai_with_bank_seed"
                };
            }
            catch (Exception ex)
            {
                // Only on a real model failure may the bank line speak directly.
                string fallback = CleanText(lineBankSeed, owner.Name);

                return new TextDialogueResult
                {
                    Text = !string.IsNullOrWhiteSpace(lineBankSeed)
                        ? fallback
                        : "...",
                    Source = !string.IsNullOrWhiteSpace(lineBankSeed)
                        ? "bank_error_fallback"
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
            string relationshipContext,
            string? lineBankSeed,
            string? previousNpcReply)
        {
            string selfBodyFact = SelfBodyFactContext.Build(owner, playerMessage);

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

SELF BODY FACT QUERY
{selfBodyFact}

IMMEDIATELY PREVIOUS NPC REPLY
{(string.IsNullOrWhiteSpace(previousNpcReply) ? "none" : previousNpcReply)}

OPTIONAL LINEBANK SEED
{BuildSeedBlock(lineBankSeed)}

TEXTING REALISM
- Text cannot reveal physical body language the recipient cannot see.
- Let wording, punctuation, message length, delay-feel, warmth/coldness, fragments, emoji use, and avoidance carry tone.
- Do not explain emotions.
- Do not announce trait numbers.
- Do not become a therapist/help-desk voice.
- Do not magically know things this character has not learned.
- SELF BODY FACT QUERY is authoritative ProjectEve body truth for this exact question.
- If it says ORDINARY SELF FACT, never invent or substitute a different body fact.
- If it says PRIVATE ADULT SELF FACT, the NPC knows it but may answer, evade, tease, refuse, or redirect according to relationship and boundaries.
- If a body fact is UNKNOWN, do not guess.
- Do not copy the player's latest message back as the NPC's answer.
- If the character is hurt but guarded, the message may understate it.
- If angry, a short reply may be stronger than an essay.
- If affectionate, warmth can appear naturally without constant declarations.
- If lying or hiding something, write the chosen message; never label it as a lie.
- The character may be truthful, kind, loving, cruel, manipulative, profane, deceptive, sexually direct, frightened, cold, or remorseful when their state supports it.
- Do not sanitize ugly human behavior into polite assistant language, but do not force darkness when it is not supported.
- Profanity, fragments, contradiction, evasion, insults, tenderness, humor, and awkwardness are allowed when natural for this character.
- Do not confess hidden truth merely because PRIVATE THOUGHT contains it. Decide what this person would actually reveal.
- Usually 1-3 short phone-message bubbles worth of text.
- Stay consistent with recent conversation.
- A LineBank seed is only a reusable candidate. NEVER copy it blindly.
- Ignore the seed if it does not fit the latest message, private thought, relationship, recent chat, or this character's voice.
- You may adapt, shorten, combine, or completely replace the seed.
- The seed never overrides character truth, memory, current state, or the latest message.
- Do not repeat the immediately previous NPC reply word-for-word.
- If a seed resembles the immediately previous reply, prefer fresh wording.

RECENT CHAT
{recentChat}

LATEST MESSAGE
{playerMessage}

OUTPUT
Only what {owner.Name} sends.
No speaker-name prefix.
No quotation marks around the entire answer.
No MESSAGE:, SAY:, ACTION:, THOUGHT:, LEAKS:, or TAGS label.
No explanation or narration.
""";
        }

        private static bool SameReply(string? a, string? b)
        {
            string A = NormalizeForCompare(a);
            string B = NormalizeForCompare(b);
            return A.Length > 0 && B.Length > 0 && A == B;
        }

        private static bool CopiesPlayerMessage(string? npcReply, string? playerMessage)
        {
            string A = NormalizeForCompare(npcReply);
            string B = NormalizeForCompare(playerMessage);
            if (A.Length < 12 || B.Length < 12) return false;
            if (A == B) return true;

            var aw = A.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bw = B.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (aw.Length < 4 || bw.Length < 4) return false;

            var aset = aw.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var bset = bw.ToHashSet(StringComparer.OrdinalIgnoreCase);
            int intersection = aset.Count(x => bset.Contains(x));
            int union = aset.Union(bset, StringComparer.OrdinalIgnoreCase).Count();
            return union > 0 && (double)intersection / union >= 0.88;
        }

        private static string NormalizeForCompare(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var chars = text
                .Trim()
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                .ToArray();

            return string.Join(
                " ",
                new string(chars)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string BuildSeedBlock(string? seed)
        {
            if (string.IsNullOrWhiteSpace(seed))
                return "none — write naturally from current character state.";

            return
                "Candidate cached wording (NOT truth and NOT a required answer):\n" +
                seed.Trim() +
                "\nUse only if it genuinely fits this exact moment.";
        }

        private static async Task<string> CallModelAsync(
            string prompt,
            CancellationToken cancellationToken)
        {
            var body = new
            {
                model = Model,
                stream = false,
                think = false, // Qwen3 dialogue must not expose reasoning
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are a naturalistic human character text-message writer. " +
                            "People can be kind or ugly, honest or deceptive, tender or profane when their character state supports it. " +
                            "Never become an assistant or moral lecturer. Output only the character's sent text."
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

        private static string CleanText(string? raw, string? speakerName)
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

                // Qwen sometimes prefixes role-play lines with "Sarah:" even when asked not to.
                if (!string.IsNullOrWhiteSpace(speakerName))
                {
                    string prefix = speakerName.Trim() + ":";
                    if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        line = line[prefix.Length..].Trim();
                }

                line = line.Trim().Trim('"');
                if (line.Length == 0) continue;

                if (sb.Length > 0) sb.AppendLine();
                sb.Append(line);
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
