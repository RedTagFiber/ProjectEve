namespace ProjectEve.NpcStudio.Models;

// ------------------------------------------------------------
// NPC Studio shared models.
// These are simple DTO/view models used by the repository,
// services, and Blazor pages.
//
// Phase 6 note:
// This file is included in this patch because Phase 5 accidentally
// did not include Models/NpcStudioModels.cs. It also keeps compatibility
// aliases used by Phase 4/5 pages such as OutputText, PositivePrompt,
// and NegativePrompt.
// ------------------------------------------------------------

public sealed class NpcStudioOptions
{
    public string MainDbPath { get; set; } = @"D:\ProjectEveData\Database\project_eve.db";
    public string HistoryDbPath { get; set; } = @"D:\ProjectEveData\Database\project_eve_history.db";
    public string RelationshipsDbPath { get; set; } = @"D:\ProjectEveData\Database\project_eve_relationships.db";
    public string NpcRoot { get; set; } = @"D:\ProjectEveData\NPC";

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen2.5";

    public string ComfyBaseUrl { get; set; } = "http://127.0.0.1:8188";
}

public sealed class NpcStudioDashboard
{
    public int TotalCharacters { get; set; }
    public int CoreCharacters { get; set; }
    public int TownCharacters { get; set; }
    public int HistoryCharacters { get; set; }

    public int RelationshipCount { get; set; }

    public int MissingReferenceImages { get; set; }
    public int MissingVoices { get; set; }

    public int ApprovedImages { get; set; }
    public int ApprovedVoices { get; set; }

    public List<NpcCountRow> StatusCounts { get; set; } = new();
    public List<NpcCountRow> TierCounts { get; set; } = new();
    public List<NpcCountRow> TopOccupations { get; set; } = new();
    public List<NpcCountRow> TopRelationshipCounts { get; set; } = new();

    // Compatibility aliases for older V0.1/V0.2 files.
    public int CoreCount
    {
        get => CoreCharacters;
        set => CoreCharacters = value;
    }

    public int TownCount
    {
        get => TownCharacters;
        set => TownCharacters = value;
    }

    public int HistoryCount
    {
        get => HistoryCharacters;
        set => HistoryCharacters = value;
    }

    public int MissingAppearanceCount
    {
        get => MissingReferenceImages;
        set => MissingReferenceImages = value;
    }

    public int MissingVoiceCount
    {
        get => MissingVoices;
        set => MissingVoices = value;
    }
}

public sealed class NpcCountRow
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public sealed class NpcBrowserRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public int Tier { get; set; }
    public string Status { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentLocationId { get; set; } = "";
    public string HomeLocationId { get; set; } = "";
    public string WorkLocationId { get; set; } = "";
    public string PortraitPath { get; set; } = "";

    public string ImageStatus { get; set; } = "";

    // Compatibility alias for pages that still call this AppearanceStatus.
    public string AppearanceStatus
    {
        get => ImageStatus;
        set => ImageStatus = value;
    }

    public string VoiceStatus { get; set; } = "";
    public int RelationshipCount { get; set; }
}

public sealed class NpcCharacterSheet
{
    public int Id { get; set; }
    public string NpcKey { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string FolderPath { get; set; } = "";

    public string Name { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string DirtyName { get; set; } = "";
    public string DarkName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";

    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentLocationId { get; set; } = "";
    public string HomeLocationId { get; set; } = "";
    public string WorkLocationId { get; set; } = "";
    public string Status { get; set; } = "";
    public int Tier { get; set; }

    public string Goal { get; set; } = "";
    public string Need { get; set; } = "";
    public string Fear { get; set; } = "";
    public string Want { get; set; } = "";
    public string PersonalityContext { get; set; } = "";
    public string Hometown { get; set; } = "";
    public string Address { get; set; } = "";

    // World Builder / dossier foundation. Canonical values are metric so each
    // world can choose its own display units without changing stored truth.
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public int IQ { get; set; }

    public string Archetype1 { get; set; } = "";
    public string Archetype2 { get; set; } = "";
    public string Archetype3 { get; set; } = "";

    public string PublicPersona { get; set; } = "";
    public string PrivatePersona { get; set; } = "";
    public string HiddenBehavior { get; set; } = "";
    public string AiSummary { get; set; } = "";
    public string StatusNotes { get; set; } = "";

    public NpcAppearanceProfile Appearance { get; set; } = new();
    public NpcVoiceProfile Voice { get; set; } = new();

    public List<NpcRelationshipRow> Relationships { get; set; } = new();
    public List<NpcTraitRow> Traits { get; set; } = new();
    public List<NpcStudioIdea> Ideas { get; set; } = new();
    public List<NpcImageGeneration> Images { get; set; } = new();
    public List<NpcRevisionRow> Revisions { get; set; } = new();
    public List<NpcHistoryEvent> HistoryEvents { get; set; } = new();

    // Golden-NPC canonical foundation bridge.
    // Read-only in Bridge 1; later phases add domain-specific editors.
    public NpcCanonicalFoundationSummary CanonicalFoundation { get; set; } = new();
}


public sealed class NpcCanonicalFoundationSummary
{
    public int EducationRecords { get; set; }
    public int ProfessionalProfiles { get; set; }
    public int Qualifications { get; set; }
    public int ProfessionalCompetencies { get; set; }

    public int Phones { get; set; }
    public int VehiclesOwnedOrDriven { get; set; }
    public int FinancialAccounts { get; set; }
    public int FinancialObligations { get; set; }

    public bool HasFormation =>
        EducationRecords > 0 ||
        ProfessionalProfiles > 0 ||
        Qualifications > 0 ||
        ProfessionalCompetencies > 0;

    public bool HasPropertyOrFinance =>
        Phones > 0 ||
        VehiclesOwnedOrDriven > 0 ||
        FinancialAccounts > 0 ||
        FinancialObligations > 0;
}

public sealed class NpcHistoryEvent
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }
    public string EventDate { get; set; } = "";
    public int AgeAtEvent { get; set; }
    public string EventType { get; set; } = "Life";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string Meaning { get; set; } = "";
    public bool IsCanon { get; set; } = true;
    public string CreatedRealAt { get; set; } = "";
}

public sealed class NpcRelationshipRow
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }

    public string SourceName { get; set; } = "";

    public int TargetNpcId { get; set; }
    public string TargetName { get; set; } = "";
    public string TargetNameSnapshot { get; set; } = "";

    public string RelationshipType { get; set; } = "";
    public string RelationshipOrigin { get; set; } = "";

    public int Trust { get; set; }
    public int Respect { get; set; }
    public int Affection { get; set; }
    public int Attraction { get; set; }
    public int Tension { get; set; }
    public int Anger { get; set; }
    public int Resentment { get; set; }
    public int Fear { get; set; }
    public int Jealousy { get; set; }
    public int Loyalty { get; set; }
    public int Importance { get; set; }
    public string RelationshipCategory { get; set; } = "Other";
    public string FamilyRole { get; set; } = "";

    public bool IsMutual { get; set; }
    public bool IsHidden { get; set; }
    public bool IsCoreRelationship { get; set; }
    public bool AffectsDialogue { get; set; } = true;

    public string Notes { get; set; } = "";
}

public sealed class NpcRelationshipDraft
{
    public int SourceNpcId { get; set; }
    public int TargetNpcId { get; set; }
    public string TargetName { get; set; } = "";

    public string RelationshipType { get; set; } = "friend";
    public string RelationshipOrigin { get; set; } = "Manual Studio edit";

    public int Trust { get; set; } = 50;
    public int Respect { get; set; } = 50;
    public int Affection { get; set; } = 50;
    public int Attraction { get; set; } = 0;
    public int Tension { get; set; } = 0;
    public int Anger { get; set; } = 0;
    public int Resentment { get; set; } = 0;
    public int Fear { get; set; } = 0;
    public int Jealousy { get; set; } = 0;
    public int Loyalty { get; set; } = 50;
    public int Importance { get; set; } = 50;
    public string RelationshipCategory { get; set; } = "Friend";
    public string FamilyRole { get; set; } = "";

    public bool IsMutual { get; set; } = true;
    public bool IsHidden { get; set; }
    public bool IsCoreRelationship { get; set; }
    public bool AffectsDialogue { get; set; } = true;

    public string Notes { get; set; } = "";
}

public sealed class NpcAppearanceProfile
{
    public int NpcId { get; set; }

    public string AppearanceStatus { get; set; } = "Missing";
    public string BodyType { get; set; } = "";
    public string HeightText { get; set; } = "";
    public string HairColor { get; set; } = "";
    public string HairStyle { get; set; } = "";
    public string EyeColor { get; set; } = "";
    public string SkinTone { get; set; } = "";
    public string ClothingStyle { get; set; } = "";
    public string WorkClothes { get; set; } = "";
    public string CasualClothes { get; set; } = "";
    public string DistinguishingFeatures { get; set; } = "";

    public string ImagePrompt { get; set; } = "";
    public string NegativePrompt { get; set; } = "";

    public string ReferenceImagePath { get; set; } = "";
    public string ProfileImagePath { get; set; } = "";
    public string ContactImagePath { get; set; } = "";

    public bool Approved { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class NpcVoiceProfile
{
    public int NpcId { get; set; }

    public string VoiceStatus { get; set; } = "Missing";
    public string VoiceProvider { get; set; } = "";
    public string VoiceId { get; set; } = "";
    public string VoiceName { get; set; } = "";
    public string VoiceStyle { get; set; } = "";
    public string Accent { get; set; } = "";
    public string AgeTone { get; set; } = "";
    public string Energy { get; set; } = "";
    public string Warmth { get; set; } = "";
    public string Roughness { get; set; } = "";
    public string Pace { get; set; } = "";
    public string Pitch { get; set; } = "";

    public string ReferenceAudioPath { get; set; } = "";
    public string SampleText { get; set; } = "";

    public bool Approved { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class NpcTraitRow
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }

    public string MainGroup { get; set; } = "";
    public string SubGroup { get; set; } = "";
    public string SubSubGroup { get; set; } = "";
    public string TraitId { get; set; } = "";
    public string TraitName { get; set; } = "";

    public bool IsEnabled { get; set; }
    public int StartingValue { get; set; }
    public int CurrentValue { get; set; }

    public string Notes { get; set; } = "";
}

public sealed class NpcStudioIdea
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }

    public string IdeaType { get; set; } = "";
    public string SourceModel { get; set; } = "";
    public string InputSummary { get; set; } = "";
    public string IdeaText { get; set; } = "";

    // Compatibility aliases.
    // The idea database stores text in IdeaText/Notes, but some pages
    // use prompt-style names while we are building Phase 4/5/6.
    public string OutputText
    {
        get => IdeaText;
        set => IdeaText = value;
    }

    public string PositivePrompt
    {
        get => IdeaText;
        set => IdeaText = value;
    }

    public string NegativePrompt
    {
        get => Notes;
        set => Notes = value;
    }

    public bool Approved { get; set; }
    public bool Rejected { get; set; }
    public bool AppliedToCharacter { get; set; }

    public string Notes { get; set; } = "";
    public string CreatedRealAt { get; set; } = "";
}

public sealed class NpcPromptGeneration
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }

    public string PromptType { get; set; } = "";
    public string SourceModel { get; set; } = "";
    public string InputJson { get; set; } = "";
    public string OutputText { get; set; } = "";
    public string PositivePrompt { get; set; } = "";
    public string NegativePrompt { get; set; } = "";

    public bool Approved { get; set; }
    public bool UsedForGeneration { get; set; }

    public string Notes { get; set; } = "";
    public string CreatedRealAt { get; set; } = "";
}

public sealed class NpcImageGeneration
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }

    public string ImageType { get; set; } = "";
    public string PromptGenerationId { get; set; } = "";

    public string PositivePrompt { get; set; } = "";
    public string NegativePrompt { get; set; } = "";

    public string Seed { get; set; } = "";
    public string WorkflowName { get; set; } = "";
    public string Checkpoint { get; set; } = "";

    public int Width { get; set; }
    public int Height { get; set; }
    public int Steps { get; set; }
    public double Cfg { get; set; }

    public string Sampler { get; set; } = "";
    public string ImagePath { get; set; } = "";

    public bool IsCurrent { get; set; }
    public bool Approved { get; set; }

    public string Notes { get; set; } = "";
    public string CreatedRealAt { get; set; } = "";
}

public sealed class NpcRevisionRow
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }

    public string RevisionType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";

    public string CreatedRealAt { get; set; } = "";
}

public sealed class PromptEngineerResult
{
    public string OutputText { get; set; } = "";
    public string PositivePrompt { get; set; } = "";
    public string NegativePrompt { get; set; } = "";
}

// ------------------------------------------------------------
// World Builder relationship meaning + live behavior test models.
// ------------------------------------------------------------
public sealed class NpcRelationshipReason
{
    public string Id { get; set; } = "";
    public string RelationshipId { get; set; } = "";
    public int NpcId { get; set; }
    public string Metric { get; set; } = "Resentment";
    public int Impact { get; set; }
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string HistoryEventId { get; set; } = "";
    public bool StillActive { get; set; } = true;
    public string CreatedRealAt { get; set; } = "";
}


public sealed class NpcMemoryParticipantOption
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public int Tier { get; set; } = 5;
    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentLocationId { get; set; } = "";
    public string HomeLocationId { get; set; } = "";
    public string WorkLocationId { get; set; } = "";
}

public sealed class NpcSharedEventParticipantDraft
{
    public int CharacterId { get; set; }

    // TRUE HISTORY participation is separate from subjective knowledge.
    public bool IsTrueEventParticipant { get; set; } = true;

    // Shared = shared-known baseline, FullTruth = objective truth, Limited = less/incomplete,
    // None = no KnowledgeItems row for this event.
    public string KnowledgeLevel { get; set; } = "Shared";

    // A non-participant may still have a memory of learning/hearing about the event.
    public bool CreateMemory { get; set; } = true;

    public string KnownHistoryOverride { get; set; } = "";
    public string MemoryViewOverride { get; set; } = "";
    public string Interpretation { get; set; } = "";
    public string EmotionalMeaning { get; set; } = "";
}

public sealed class NpcSharedEventDraft
{
    public int AuthoringCharacterId { get; set; }
    public string EventType { get; set; } = "Shared Experience";
    public string Title { get; set; } = "";
    public string TrueEventSummary { get; set; } = "";
    public string SharedKnownHistory { get; set; } = "";
    public string SharedBaseMemory { get; set; } = "";
    public string PlaceText { get; set; } = "";
    public string GameTime { get; set; } = "";
    public int Importance { get; set; } = 60;
    public int Strength { get; set; } = 75;
    public int Confidence { get; set; } = 100;
    public List<NpcSharedEventParticipantDraft> Participants { get; set; } = new();
}

public sealed class NpcSharedEventAiOptions
{
    public string DetailLevel { get; set; } = "Normal";
    public bool SuggestPrivateMoments { get; set; } = true;
    public bool SuggestMisunderstandings { get; set; } = true;
    public bool SuggestSecretsOrRumors { get; set; } = false;
    public bool SuggestRelationshipEffects { get; set; } = false;
}

public sealed class NpcSharedEventAiParticipantDraft
{
    public int CharacterId { get; set; }
    public string KnownHistory { get; set; } = "";
    public string MemoryView { get; set; } = "";
    public string Interpretation { get; set; } = "";
    public string EmotionalMeaning { get; set; } = "";
    public List<string> ExtraMemoryIdeas { get; set; } = new();
}

public sealed class NpcSharedEventAiDraftResult
{
    public List<NpcSharedEventAiParticipantDraft> Participants { get; set; } = new();
    public List<string> EventExtraIdeas { get; set; } = new();
}

public sealed class NpcSharedEventSaveResult
{
    public string EventId { get; set; } = "";
    public int ParticipantCount { get; set; }
    public int KnownHistoryRowsCreated { get; set; }
    public int MemoryRowsCreated { get; set; }
}

public sealed class NpcCanonicalHistoryEventOption
{
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string PlaceText { get; set; } = "";
    public string GameTime { get; set; } = "";

    public string DisplayLabel
    {
        get
        {
            var when = string.IsNullOrWhiteSpace(GameTime) ? "Undated" : GameTime;
            var title = string.IsNullOrWhiteSpace(Title)
                ? (string.IsNullOrWhiteSpace(Summary) ? EventId : Summary)
                : Title;
            var place = string.IsNullOrWhiteSpace(PlaceText) ? "" : $" · {PlaceText}";
            return $"{when} · {title}{place}";
        }
    }
}

public sealed class NpcPersonalMemoryDraft
{
    public string Id { get; set; } = "";
    public int KnowerCharacterId { get; set; }
    public int? SubjectCharacterId { get; set; }
    public string EventId { get; set; } = "";
    public string MemoryType { get; set; } = "General";
    public string MemoryText { get; set; } = "";
    public string Interpretation { get; set; } = "";
    public string EmotionalMeaning { get; set; } = "";
    public int Importance { get; set; } = 50;
    public int Strength { get; set; } = 50;
    public int Confidence { get; set; } = 70;
    public bool IsLockedPeak { get; set; }
}

public sealed class NpcKnowledgeDraft
{
    public string Id { get; set; } = "";
    public int KnowerCharacterId { get; set; }
    public int? SubjectCharacterId { get; set; }
    public string EventId { get; set; } = "";
    public string KnowledgeType { get; set; } = "Knowledge";
    public string WhatTheyKnow { get; set; } = "";
    public string HowTheyLearnedIt { get; set; } = "";
    public int? SourceCharacterId { get; set; }
    public int Confidence { get; set; } = 70;
    public bool IsRumor { get; set; }
    public bool IsSecret { get; set; }
    public bool IsFalseBelief { get; set; }
}

public sealed class NpcEmotionTrigger
{
    public string Id { get; set; } = "";
    public int NpcId { get; set; }
    public string Emotion { get; set; } = "Anger";
    public string TriggerText { get; set; } = "";
    public int Impact { get; set; } = 10;
    public string Reason { get; set; } = "";
    public string CalmedBy { get; set; } = "";
    public string MadeWorseBy { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class NpcBehaviorTestState
{
    public int Joy { get; set; } = 50;
    public int Anger { get; set; } = 10;
    public int Sadness { get; set; } = 10;
    public int Hurt { get; set; } = 10;
    public int Fear { get; set; } = 5;
    public int Attraction { get; set; } = 10;
    public int Jealousy { get; set; } = 5;
    public int Stress { get; set; } = 20;
    public int Affection { get; set; } = 50;
}

public sealed class NpcBehaviorTestResult
{
    public string Brain { get; set; } = "";
    public string Thought { get; set; } = "";
    public string Action { get; set; } = "";
    public string Dialogue { get; set; } = "";
    public string Raw { get; set; } = "";
}
