using System.Net.Http.Json;
using System.Text.Json;
using ProjectEve.NpcStudio.Data;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class NpcStudioService
{
    private readonly NpcStudioRepository _repo;

    public NpcStudioService(NpcStudioRepository repo)
    {
        _repo = repo;
    }

    public Task<NpcStudioDashboard> GetDashboardAsync()
        => _repo.GetDashboardAsync();

    public Task<List<NpcBrowserRow>> SearchNpcsAsync(string search, string status, int? tier)
        => _repo.SearchNpcsAsync(search, status, tier);

    public Task<NpcCharacterSheet?> GetCharacterSheetAsync(int npcId)
        => _repo.GetCharacterSheetAsync(npcId);

    public Task SaveCharacterCoreAsync(NpcCharacterSheet sheet)
        => _repo.SaveCharacterCoreAsync(sheet);

    public Task AddRelationshipAsync(NpcRelationshipDraft draft)
        => _repo.AddRelationshipAsync(draft);

    public Task SaveRelationshipAsync(NpcRelationshipRow rel)
        => _repo.SaveRelationshipAsync(rel);

    public Task<List<NpcRelationshipReason>> GetRelationshipReasonsAsync(string relationshipId)
        => _repo.GetRelationshipReasonsAsync(relationshipId);

    public Task AddRelationshipReasonAsync(NpcRelationshipReason reason)
        => _repo.AddRelationshipReasonAsync(reason);

    public Task SaveRelationshipReasonAsync(NpcRelationshipReason reason)
        => _repo.SaveRelationshipReasonAsync(reason);

    public Task SetRelationshipReasonActiveAsync(string id, int npcId, bool isActive)
        => _repo.SetRelationshipReasonActiveAsync(id, npcId, isActive);

    public Task DeleteRelationshipReasonAsync(string id)
        => _repo.DeleteRelationshipReasonAsync(id);

    public Task<List<NpcMemoryParticipantOption>> GetMemoryParticipantOptionsAsync()
        => _repo.GetMemoryParticipantOptionsAsync();

    public async Task<NpcSharedEventAiDraftResult> DraftSharedEventParticipantViewsAsync(
        NpcSharedEventDraft draft,
        NpcSharedEventAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(draft);
        options ??= new NpcSharedEventAiOptions();

        if (string.IsNullOrWhiteSpace(draft.TrueEventSummary))
            throw new InvalidOperationException("TRUE HISTORY is required before AI can draft subjective views.");

        if (draft.Participants.Count == 0)
            throw new InvalidOperationException("Select at least one participant before generating views.");

        var participantContext = new List<string>();

        foreach (var participant in draft.Participants)
        {
            var sheet = await _repo.GetCharacterSheetAsync(participant.CharacterId);
            if (sheet is null)
            {
                participantContext.Add($"CharacterId {participant.CharacterId}: profile unavailable.");
                continue;
            }

            participantContext.Add(BuildCharacterInputSummary(sheet));
        }

        var detailInstruction = (options.DetailLevel ?? "Normal").Trim().ToLowerInvariant() switch
        {
            "brief" => "Keep each participant view concise: about 1-2 sentences per field.",
            "rich" => "Give each participant a richer but still grounded view: about 3-5 sentences for knowledge and memory.",
            _ => "Use moderate detail: about 2-3 sentences for knowledge and memory."
        };

        var extras = new List<string>();
        if (options.SuggestPrivateMoments)
            extras.Add("Suggest plausible private moments only when supported by the event and participant context.");
        if (options.SuggestMisunderstandings)
            extras.Add("Allow plausible misunderstandings or incomplete interpretations without changing objective truth.");
        if (options.SuggestSecretsOrRumors)
            extras.Add("Suggest secrets, suspicions, or rumors as ideas only; never present them as objective truth.");
        if (options.SuggestRelationshipEffects)
            extras.Add("Suggest possible relationship effects as ideas only; do not save or apply score changes.");

        var participantJson = JsonSerializer.Serialize(
            draft.Participants.Select(x => new
            {
                characterId = x.CharacterId,
                isTrueEventParticipant = x.IsTrueEventParticipant,
                knowledgeLevel = x.KnowledgeLevel,
                createMemory = x.CreateMemory,
                currentKnownHistory = x.KnownHistoryOverride,
                currentMemoryView = x.MemoryViewOverride,
                currentInterpretation = x.Interpretation,
                currentEmotionalMeaning = x.EmotionalMeaning
            }));

        var prompt = $$"""
You are the Project Eve subjective-history drafting assistant.

STRICT CANON RULES:
1. TRUE HISTORY below is objective canon. Never contradict it.
2. NPC/Player Known History is what that person reasonably knows or thinks they know.
3. Personal Memory is how that person remembers/experienced the event.
4. Different participants may remember the same event differently.
5. A person must not know a private fact unless their knowledge scope and context justify it.
6. isTrueEventParticipant=true means the person objectively participated and must be written to EventParticipants.
7. knowledgeLevel rules:
   - FullTruth: may know all objective TRUE HISTORY.
   - Shared: may know the Shared Known History baseline, but not hidden/private truth unless justified.
   - Limited: must know LESS than the shared baseline; draft only what they plausibly saw/heard/learned.
   - None: return an empty knownHistory.
8. If isTrueEventParticipant=false, do not write a firsthand memory. If createMemory=true, draft a memory of learning/hearing about the event instead.
9. Do not invent a new named person as canon. If an extra person would improve the event, put the idea only in eventExtraIdeas and label it as requiring a real NPC before acceptance.
10. Do not save anything. Return drafts only.
11. Preserve CharacterId exactly.

DETAIL:
{{detailInstruction}}

OPTIONAL IDEA RULES:
{{string.Join("\n", extras)}}

TRUE EVENT:
Title: {{draft.Title}}
Type: {{draft.EventType}}
Place: {{draft.PlaceText}}
Game Time: {{draft.GameTime}}
TRUE HISTORY:
{{draft.TrueEventSummary}}

SHARED KNOWN HISTORY BASELINE:
{{draft.SharedKnownHistory}}

SHARED BASE MEMORY:
{{draft.SharedBaseMemory}}

PARTICIPANTS / EXISTING DRAFT STATE:
{{participantJson}}

PARTICIPANT CHARACTER CONTEXT:
{{string.Join("\n\n---\n\n", participantContext)}}

Return ONLY valid JSON in exactly this structure:
{
  "participants": [
    {
      "characterId": 1,
      "knownHistory": "what this person knows/thinks they know",
      "memoryView": "how this person personally remembers it",
      "interpretation": "what they think it meant",
      "emotionalMeaning": "how it felt / why it matters",
      "extraMemoryIdeas": ["optional additional memory idea"]
    }
  ],
  "eventExtraIdeas": ["optional extra event/private-moment ideas that require author review"]
}
""";

        var (baseUrl, model) = _repo.GetAiRuntimeConfig();
        var endpoint = (baseUrl ?? "http://localhost:11434").TrimEnd('/') + "/api/generate";

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(4)
        };

        var request = new
        {
            model = string.IsNullOrWhiteSpace(model) ? "qwen2.5" : model,
            prompt,
            stream = false,
            format = "json",
            options = new
            {
                temperature = 0.65
            }
        };

        using var response = await http.PostAsJsonAsync(endpoint, request);
        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"AI draft request failed ({(int)response.StatusCode}): {raw}");

        using var envelope = JsonDocument.Parse(raw);
        if (!envelope.RootElement.TryGetProperty("response", out var responseElement))
            throw new InvalidOperationException("AI response did not contain an Ollama response field.");

        var json = responseElement.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("AI returned an empty draft.");

        var result = JsonSerializer.Deserialize<NpcSharedEventAiDraftResult>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (result is null)
            throw new InvalidOperationException("AI draft JSON could not be parsed.");

        var allowedIds = draft.Participants.Select(x => x.CharacterId).ToHashSet();
        result.Participants = result.Participants
            .Where(x => allowedIds.Contains(x.CharacterId))
            .GroupBy(x => x.CharacterId)
            .Select(x => x.First())
            .ToList();

        return result;
    }

    public Task<NpcSharedEventSaveResult> SaveSharedEventAsync(NpcSharedEventDraft draft)
        => _repo.SaveSharedEventAsync(draft);

    public Task<List<NpcCanonicalHistoryEventOption>> GetCanonicalHistoryEventsAsync(int npcId)
        => _repo.GetCanonicalHistoryEventsAsync(npcId);

    public Task SavePersonalMemoryAsync(NpcPersonalMemoryDraft memory)
        => _repo.SavePersonalMemoryAsync(memory);

    public Task SaveKnowledgeItemAsync(NpcKnowledgeDraft item)
        => _repo.SaveKnowledgeItemAsync(item);

    public Task<List<NpcEmotionTrigger>> GetEmotionTriggersAsync(int npcId)
        => _repo.GetEmotionTriggersAsync(npcId);

    public Task SaveEmotionTriggerAsync(NpcEmotionTrigger trigger)
        => _repo.SaveEmotionTriggerAsync(trigger);

    public Task DeleteEmotionTriggerAsync(string id)
        => _repo.DeleteEmotionTriggerAsync(id);

    public Task SaveTraitAsync(NpcTraitRow trait)
        => _repo.SaveTraitAsync(trait);

    public Task AddHistoryEventAsync(NpcHistoryEvent item)
        => _repo.AddHistoryEventAsync(item);

    public Task DeleteHistoryEventAsync(string id)
        => _repo.DeleteHistoryEventAsync(id);

    public Task SaveAppearanceAsync(NpcAppearanceProfile profile)
        => _repo.SaveAppearanceAsync(profile);

    public Task SaveVoiceAsync(NpcVoiceProfile voice)
        => _repo.SaveVoiceAsync(voice);

    public Task AddIdeaAsync(NpcStudioIdea idea)
        => _repo.AddIdeaAsync(idea);

    public Task SavePromptGenerationAsync(NpcPromptGeneration prompt)
        => _repo.SavePromptGenerationAsync(prompt);

    public Task SaveAppearancePromptAsync(int npcId, string positivePrompt, string negativePrompt)
        => _repo.SaveAppearancePromptAsync(npcId, positivePrompt, negativePrompt);

    public Task AddImageGenerationAsync(NpcImageGeneration image)
        => _repo.AddImageGenerationAsync(image);

    public Task ApproveImageGenerationAsync(string imageId, int npcId, string imagePath, bool setAsCurrentReference)
        => _repo.ApproveImageGenerationAsync(imageId, npcId, imagePath, setAsCurrentReference);

    public Task MarkVoiceApprovedAsync(int npcId)
        => _repo.MarkVoiceApprovedAsync(npcId);

    public string BuildCharacterInputSummary(NpcCharacterSheet sheet)
    {
        var relationships = sheet.Relationships.Count == 0
            ? "No relationships recorded."
            : string.Join(Environment.NewLine, sheet.Relationships.Take(25).Select(r =>
                $"- {r.TargetName}: {r.RelationshipType}, Origin={r.RelationshipOrigin}, Trust={r.Trust}, Respect={r.Respect}, Affection={r.Affection}, Tension={r.Tension}, Notes={r.Notes}"));

        var traits = sheet.Traits.Count == 0
            ? "No traits recorded."
            : string.Join(", ", sheet.Traits.Take(35).Select(t => $"{t.TraitName}={t.CurrentValue}"));

        return $"""
        NPC CHARACTER SHEET

        Identity:
        Id: {sheet.Id}
        Name: {sheet.Name}
        Nickname: {sheet.Nickname}
        DisplayName: {sheet.DisplayName}
        Age: {sheet.Age}
        Gender: {sheet.Gender}
        Tier: {sheet.Tier}
        Status: {sheet.Status}
        Occupation: {sheet.Occupation}
        Location: {sheet.Location}
        Hometown: {sheet.Hometown}
        Address: {sheet.Address}

        Inner Life:
        Goal: {sheet.Goal}
        Need: {sheet.Need}
        Fear: {sheet.Fear}
        Want: {sheet.Want}
        Personality Context: {sheet.PersonalityContext}
        Archetypes: {sheet.Archetype1}, {sheet.Archetype2}, {sheet.Archetype3}
        IQ: {sheet.IQ}
        Public Persona: {sheet.PublicPersona}
        Private Persona: {sheet.PrivatePersona}
        Hidden Behavior: {sheet.HiddenBehavior}

        Traits:
        {traits}

        Relationships:
        {relationships}

        Appearance Direction:
        Status: {sheet.Appearance.AppearanceStatus}
        Hair: {sheet.Appearance.HairColor} / {sheet.Appearance.HairStyle}
        Eyes: {sheet.Appearance.EyeColor}
        Skin: {sheet.Appearance.SkinTone}
        Clothing: {sheet.Appearance.ClothingStyle}
        Notes: {sheet.Appearance.Notes}

        Voice Direction:
        Status: {sheet.Voice.VoiceStatus}
        Style: {sheet.Voice.VoiceStyle}
        Accent: {sheet.Voice.Accent}
        Energy: {sheet.Voice.Energy}
        Warmth: {sheet.Voice.Warmth}
        Notes: {sheet.Voice.Notes}
        """;
    }
}
