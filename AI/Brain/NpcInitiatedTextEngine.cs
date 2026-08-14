using ProjectEve.Characters.Base;
using ProjectEve.Conversations;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.AI.Brain;

/// <summary>
/// Generates the wording of a contact ProjectEve already decided should happen.
///
/// It does NOT decide world truth and does NOT call Brain.Think().
/// That prevents an internal "send an apology" directive from being treated like
/// a fake player statement and changing traits.
/// </summary>
public static class NpcInitiatedTextEngine
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public const string Model = "eve-dialogue";

    public static async Task<NpcInitiatedTextResult> GenerateAsync(
        SimCharacter npc,
        string playerName,
        string triggerKind,
        string motive,
        string validatedContext,
        string conversationContext,
        string previousNpcText,
        string phoneStyle,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        if (npc == null)
            throw new ArgumentNullException(nameof(npc));

        string prompt = BuildPrompt(
            npc,
            playerName,
            triggerKind,
            motive,
            validatedContext,
            conversationContext,
            previousNpcText,
            phoneStyle,
            Math.Clamp(maxCharacters, 40, 1200));

        try
        {
            string raw = await CallModelAsync(prompt, cancellationToken);
            string text = CleanText(raw, npc.Name);

            if (text.Length == 0 || text == "...")
            {
                return new NpcInitiatedTextResult
                {
                    Text = "",
                    Source = "error",
                    Error = "The model returned no usable outbound text."
                };
            }

            int max = Math.Clamp(maxCharacters, 40, 1200);
            if (text.Length > max)
                text = text[..max].TrimEnd();

            return new NpcInitiatedTextResult
            {
                Text = text,
                Source = "ai_initiated"
            };
        }
        catch (Exception ex)
        {
            return new NpcInitiatedTextResult
            {
                Text = "",
                Source = "error",
                Error = ex.Message
            };
        }
    }

    private static string BuildPrompt(
        SimCharacter npc,
        string playerName,
        string triggerKind,
        string motive,
        string validatedContext,
        string conversationContext,
        string previousNpcText,
        string phoneStyle,
        int maxCharacters)
        => $"""
SYSTEM ROLE
Write ONLY the text message {npc.Name} chooses to send to {playerName}.
This is an OUTBOUND message. The player did not just send a new line.

CHARACTER
{DialoguePromptContext.BuildCharacterContext(npc)}

LIFE HISTORY / MEMORY
{DialoguePromptContext.BuildHistoryMemoryContext(npc)}

WHY PROJECT EVE ALREADY DECIDED CONTACT SHOULD HAPPEN
Kind: {triggerKind}
Motive: {motive}

VALIDATED CONTACT CONTEXT
{(string.IsNullOrWhiteSpace(validatedContext)
    ? "No external event is asserted. This is a natural check-in."
    : validatedContext)}

PHONE STYLE
{phoneStyle}

CURRENT CONVERSATION CONTINUITY
{conversationContext}

IMMEDIATELY PREVIOUS TEXT SENT BY THIS NPC
{(string.IsNullOrWhiteSpace(previousNpcText) ? "none" : previousNpcText)}

TRUTH / KNOWLEDGE RULES
- ProjectEve already decided THAT the NPC contacts the player. You decide wording only.
- Do not invent a new event, promise, appointment, secret, relationship milestone, crime, job change, family fact, medical fact, or shared memory.
- VALIDATED CONTACT CONTEXT is the only special trigger fact for this outbound message.
- Conversation continuity and personal knowledge are authoritative only for what this NPC actually learned/perceived.
- Do not recover hidden words from gossip provenance or from another NPC's private conversation.
- Relationship closeness is not telepathy.
- If this is spontaneous_check_in, do not pretend something happened. A simple natural "hey", joke, question, or check-in is enough.
- If the context includes a personal knowledge claim, the NPC may phrase what they know naturally, but must not add hidden evidence that was not supplied.
- Never announce trait values, AI/system language, or internal reasoning.
- Do not write narration, body language, or labels.
- The NPC may be warm, awkward, irritated, funny, profane, guarded, loving, cold, blunt, apologetic, flirtatious, worried, or manipulative only when current character state/context supports it.
- Do not force romance, conflict, or therapy language.
- Do not repeat the immediately previous NPC message word-for-word.
- Keep it believable as phone text.
- Hard maximum: {maxCharacters} characters.

OUTPUT
Only the message text.
No speaker prefix.
No quotation marks around the whole message.
No THOUGHT:, SAY:, ACTION:, MESSAGE:, TAGS:, or explanation.
""";

    private static async Task<string> CallModelAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            model = Model,
            stream = false,
            think = false,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content =
                        "You write naturalistic outbound text messages for one simulated human NPC. " +
                        "Use only the supplied character/context truth. Output only the sent text."
                },
                new { role = "user", content = prompt }
            },
            options = new
            {
                temperature = 0.76,
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

        string responseJson =
            await response.Content.ReadAsStringAsync(cancellationToken);

        using var doc = JsonDocument.Parse(responseJson);

        return doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private static string CleanText(string? raw, string speakerName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var sb = new StringBuilder();

        foreach (var rawLine in raw.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("TAGS:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("LEAKS:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("PRESENTATION:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("SAY:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("MESSAGE:", StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                line = colon >= 0 ? line[(colon + 1)..].Trim() : line;
            }

            string prefix = speakerName.Trim() + ":";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                line = line[prefix.Length..].Trim();

            line = line.Trim().Trim('"');
            if (line.Length == 0)
                continue;

            if (sb.Length > 0)
                sb.AppendLine();

            sb.Append(line);
        }

        return sb.ToString().Trim();
    }
}

public sealed class NpcInitiatedTextResult
{
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Error { get; set; }
}
