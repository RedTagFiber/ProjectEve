using System.Net.Http.Json;
using System.Text.Json;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class OllamaPromptEngineerService
{
    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;
    private readonly NpcStudioService _studio;

    public OllamaPromptEngineerService(HttpClient http, NpcStudioOptions options, NpcStudioService studio)
    {
        _http = http;
        _options = options;
        _studio = studio;
    }

    public async Task<NpcStudioIdea> AnalyzeCharacterAsync(NpcCharacterSheet sheet, string ideaType)
    {
        var inputSummary = _studio.BuildCharacterInputSummary(sheet);

        var system = """
        You are the NPC Studio Prompt Engineer for Project Eve.
        You are not roleplaying the NPC.
        Study the full character sheet and suggest grounded, useful creative ideas.
        Do not overwrite canon facts.
        Do not invent celebrity likenesses.
        Keep the ideas practical for image generation, voice design, relationships, secrets, conflict, and daily life.
        """;

        var prompt = $"""
        {system}

        TASK:
        Analyze this NPC and produce ideas for: {ideaType}

        Return sections:
        - Character Read
        - Appearance Ideas
        - Voice Ideas
        - Relationship Ideas
        - Secrets / Conflict
        - Comfy Image Direction
        - Notes to Human Creator

        CHARACTER SHEET:
        {inputSummary}
        """;

        var output = await GenerateAsync(prompt);

        return new NpcStudioIdea
        {
            Id = Guid.NewGuid().ToString("N"),
            NpcId = sheet.Id,
            IdeaType = ideaType,
            SourceModel = _options.OllamaModel,
            InputSummary = inputSummary,
            IdeaText = output,
            Approved = false,
            Rejected = false,
            AppliedToCharacter = false
        };
    }

    public async Task<string> BuildComfyPromptAsync(NpcCharacterSheet sheet)
    {
        var inputSummary = _studio.BuildCharacterInputSummary(sheet);

        var prompt = $"""
        You are the NPC Studio Prompt Engineer for Project Eve.
        Create a ComfyUI image prompt for a realistic cinematic white-background NPC reference portrait.

        Rules:
        - Use only supplied character data and grounded inference.
        - Clear face, natural lighting, realistic skin, no celebrity likeness.
        - Include clothing and expression ideas from personality/job.
        - Include a negative prompt.
        - Return:
          POSITIVE PROMPT:
          NEGATIVE PROMPT:

        CHARACTER SHEET:
        {inputSummary}
        """;

        return await GenerateAsync(prompt);
    }

    public async Task<string> BuildVoiceDirectionAsync(NpcCharacterSheet sheet)
    {
        var inputSummary = _studio.BuildCharacterInputSummary(sheet);

        var prompt = $"""
        You are the NPC Studio Prompt Engineer for Project Eve.
        Create voice direction from this character sheet.

        Return:
        - Voice age
        - Accent
        - Tone
        - Pace
        - Warmth
        - Energy
        - Roughness
        - Emotional restraint
        - Sample line

        CHARACTER SHEET:
        {inputSummary}
        """;

        return await GenerateAsync(prompt);
    }

    public async Task<NpcBehaviorTestResult> GenerateBehaviorTestAsync(
        NpcCharacterSheet sheet,
        string interactionMode,
        string playerText,
        string playerAction,
        NpcBehaviorTestState state,
        IReadOnlyList<string>? recentTranscript = null)
    {
        var inputSummary = _studio.BuildCharacterInputSummary(sheet);
        var recent = recentTranscript is { Count: > 0 }
            ? string.Join(Environment.NewLine, recentTranscript.TakeLast(8))
            : "No prior turns in this test.";

        var prompt = $"""
        You are the World Builder NPC behavior test engine.
        This is a controlled authoring sandbox for testing whether the NPC behaves consistently.

        HARD RULES:
        - Never use occupation, hobbies, or biography as filler.
        - Do not force the conversation back to a familiar topic.
        - If the player's meaning is unclear, the NPC may ask a short clarification or admit uncertainty.
        - Do not invent facts that are not in the supplied dossier or transcript.
        - Do not repeat recent phrases, metaphors, anecdotes, or topic bridges.
        - The NPC may be brief, silent-looking, irritated, affectionate, defensive, playful, or uncertain when appropriate.
        - Keep the NPC's public/private contradictions, history, IQ, traits, relationships, and current emotion mixture in mind.
        - In-Person mode may use observable body language. Message mode should sound like texting and should not narrate body language.

        Return EXACTLY these four sections, each on its own line:
        BRAIN: one concise sentence describing the dominant emotional/behavioral state and what matters most right now.
        THOUGHT: one concise private interpretation of what the player meant and what the NPC wants to do.
        ACTION: one short observable action. For Message mode use NONE unless a message behavior such as pauses, leaves on read, or double-texts is appropriate.
        DIALOGUE: only the NPC's actual spoken/text reply.

        INTERACTION MODE: {interactionMode}

        TEMPORARY TEST STATE (0-100):
        Joy={state.Joy}
        Anger={state.Anger}
        Sadness={state.Sadness}
        Hurt={state.Hurt}
        Fear={state.Fear}
        Attraction={state.Attraction}
        Jealousy={state.Jealousy}
        Stress={state.Stress}
        Affection={state.Affection}

        PLAYER ACTION:
        {playerAction}

        PLAYER MESSAGE:
        {playerText}

        RECENT TEST TRANSCRIPT:
        {recent}

        NPC DOSSIER:
        {inputSummary}
        """;

        var raw = await GenerateAsync(prompt);
        return ParseBehaviorResult(raw);
    }

    private static NpcBehaviorTestResult ParseBehaviorResult(string raw)
    {
        var result = new NpcBehaviorTestResult { Raw = raw ?? "" };
        foreach (var line in (raw ?? "").Replace("\r", "").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("BRAIN:", StringComparison.OrdinalIgnoreCase)) result.Brain = trimmed[6..].Trim();
            else if (trimmed.StartsWith("THOUGHT:", StringComparison.OrdinalIgnoreCase)) result.Thought = trimmed[8..].Trim();
            else if (trimmed.StartsWith("ACTION:", StringComparison.OrdinalIgnoreCase)) result.Action = trimmed[7..].Trim();
            else if (trimmed.StartsWith("DIALOGUE:", StringComparison.OrdinalIgnoreCase)) result.Dialogue = trimmed[9..].Trim();
        }
        if (string.IsNullOrWhiteSpace(result.Dialogue)) result.Dialogue = raw ?? "";
        return result;
    }

    private async Task<string> GenerateAsync(string prompt)
    {
        try
        {
            _http.BaseAddress = new Uri(_options.OllamaBaseUrl);

            var request = new
            {
                model = _options.OllamaModel,
                prompt,
                stream = false
            };

            var response = await _http.PostAsJsonAsync("/api/generate", request);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            if (doc.RootElement.TryGetProperty("response", out var value))
                return value.GetString() ?? "";

            return "Ollama returned no response text.";
        }
        catch (Exception ex)
        {
            return "Ollama prompt engineer failed: " + ex.Message + Environment.NewLine +
                   "Make sure Ollama is running and the model is installed.";
        }
    }
}
