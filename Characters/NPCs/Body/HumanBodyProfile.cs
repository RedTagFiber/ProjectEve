using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.NPCs.Body
{
    /// <summary>
    /// Stable + semi-stable human body truth for an NPC.
    /// ProjectEve owns these facts; the LLM may describe them but must not invent them.
    /// Large option pools, generation weights, privacy rules, activity effects, and prompt gates
    /// live in Data/World/Human/body_system_v1.json.
    /// </summary>
    public sealed class HumanBodyProfile
    {
        public string SchemaVersion { get; set; } = "1.0";
        public IdentityBiology Identity { get; set; } = new();
        public BodyDimensions Dimensions { get; set; } = new();
        public BodyComposition Composition { get; set; } = new();
        public FaceProfile Face { get; set; } = new();
        public EyeProfile Eyes { get; set; } = new();
        public HairProfile Hair { get; set; } = new();
        public SkinProfile Skin { get; set; } = new();
        public FacialHairProfile FacialHair { get; set; } = new();
        public MarksProfile Marks { get; set; } = new();
        public HealthBodyProfile Health { get; set; } = new();
        public PhysicalAbilityProfile Ability { get; set; } = new();
        public HygieneProfile Hygiene { get; set; } = new();
        public GroomingProfile Grooming { get; set; } = new();
        public BodySelfImageProfile SelfImage { get; set; } = new();
        public PresentationProfile Presentation { get; set; } = new();
        public AdultPrivateBodyProfile AdultPrivate { get; set; } = new();
        public HumanBodyState State { get; set; } = new();
        public List<BodyHistoryEvent> History { get; set; } = new();
    }

    public sealed class IdentityBiology
    {
        public string SexAtBirth { get; set; } = "unknown";
        public string GenderIdentity { get; set; } = "unknown";
        public int AgeYears { get; set; }
        public string DevelopmentStage { get; set; } = "adult";
        public List<string> AncestryBackground { get; set; } = new();
        public string SkinTone { get; set; } = "Unknown";
    }

    public sealed class BodyDimensions
    {
        public double HeightCm { get; set; }
        public double WeightKg { get; set; }
        public string FrameSize { get; set; } = "medium";
        public string ShoulderWidth { get; set; } = "average";
        public string TorsoLength { get; set; } = "average";
        public string LegLength { get; set; } = "average";
        public string ArmLength { get; set; } = "average";
        public double? ChestCm { get; set; }
        public double? BustCm { get; set; }
        public double? UnderbustCm { get; set; }
        public double? WaistCm { get; set; }
        public double? HipsCm { get; set; }
        public double? InseamCm { get; set; }
        public double? ShoeSizeUs { get; set; }
    }

    public sealed class BodyComposition
    {
        public string BodyType { get; set; } = "average";
        public string BodyShape { get; set; } = "custom";
        public double BodyFatPercent { get; set; } = 25;
        public double MuscleMass { get; set; } = 50;
        public double MuscleDefinition { get; set; } = 50;
        public double Softness { get; set; } = 50;
        public string FitnessAppearance { get; set; } = "average";
    }

    public sealed class FaceProfile
    {
        public string Shape { get; set; } = "oval";
        public double Symmetry { get; set; } = 50;
        public string Jaw { get; set; } = "average";
        public string Chin { get; set; } = "average";
        public string Cheekbones { get; set; } = "average";
        public string NoseShape { get; set; } = "average";
        public string LipShape { get; set; } = "average";
        public string LipFullness { get; set; } = "average";
        public string NaturalLipColor { get; set; } = "";
        public string SmileStyle { get; set; } = "";
        public string RestingExpression { get; set; } = "neutral";
    }

    public sealed class EyeProfile
    {
        public string Color { get; set; } = "Unknown";
        public string Shape { get; set; } = "Unknown";
        public string Size { get; set; } = "average";
        public string Spacing { get; set; } = "average";
        public string Lashes { get; set; } = "average";
        public string Vision { get; set; } = "normal";
        public string CorrectiveLenses { get; set; } = "none";
    }

    public sealed class HairProfile
    {
        public string NaturalColor { get; set; } = "Unknown";
        public string CurrentColor { get; set; } = "Unknown";
        public string Texture { get; set; } = "Unknown";
        public string Length { get; set; } = "Unknown";
        public string Density { get; set; } = "average";
        public string StrandThickness { get; set; } = "medium";
        public string Style { get; set; } = "Unknown";
        public string Hairline { get; set; } = "average";
        public double GrayPercent { get; set; }
        public string BaldingPattern { get; set; } = "none";
    }

    public sealed class SkinProfile
    {
        public string Tone { get; set; } = "Unknown";
        public string Undertone { get; set; } = "unknown";
        public string Complexion { get; set; } = "clear";
        public double Oiliness { get; set; } = 30;
        public double Dryness { get; set; } = 30;
        public string Freckles { get; set; } = "none";
        public List<BodyMark> Moles { get; set; } = new();
        public List<BodyMark> Birthmarks { get; set; } = new();
        public List<BodyMark> StretchMarks { get; set; } = new();
    }

    public sealed class FacialHairProfile
    {
        public double GrowthLevel { get; set; }
        public string Color { get; set; } = "";
        public string Style { get; set; } = "none";
        public string Density { get; set; } = "none";
        public string UsualGrooming { get; set; } = "none";
    }

    public sealed class MarksProfile
    {
        public List<BodyMark> Scars { get; set; } = new();
        public List<BodyMark> Tattoos { get; set; } = new();
        public List<BodyPiercing> Piercings { get; set; } = new();
        public List<string> Prosthetics { get; set; } = new();
        public List<string> CosmeticProcedures { get; set; } = new();
    }

    public sealed class BodyMark
    {
        public string Type { get; set; } = "";
        public string Location { get; set; } = "";
        public string Description { get; set; } = "";
        public string Cause { get; set; } = "";
        public bool Permanent { get; set; } = true;
        public double Visibility { get; set; } = 50;
    }

    public sealed class BodyPiercing
    {
        public string Location { get; set; } = "";
        public bool Pierced { get; set; }
        public string JewelryType { get; set; } = "none";
        public string State { get; set; } = "healed";
        public string Privacy { get; set; } = "public";
    }

    public sealed class HealthBodyProfile
    {
        public double VisionBaseline { get; set; } = 100;
        public double HearingBaseline { get; set; } = 100;
        public double MobilityBaseline { get; set; } = 100;
        public List<string> ChronicConditions { get; set; } = new();
        public List<string> Allergies { get; set; } = new();
        public List<string> Medications { get; set; } = new();
        public List<string> OldInjuries { get; set; } = new();
        public List<string> Disabilities { get; set; } = new();
        public double PainBaseline { get; set; }
        public double InjuryRecoveryRate { get; set; } = 50;
        public double IllnessRecoveryRate { get; set; } = 50;
    }

    public sealed class PhysicalAbilityProfile
    {
        public double Strength { get; set; } = 50;
        public double Endurance { get; set; } = 50;
        public double Speed { get; set; } = 50;
        public double Agility { get; set; } = 50;
        public double Balance { get; set; } = 50;
        public double Flexibility { get; set; } = 50;
        public double Coordination { get; set; } = 50;
        public double GripStrength { get; set; } = 50;
        public double PainTolerance { get; set; } = 50;
        public double HeatTolerance { get; set; } = 50;
        public double ColdTolerance { get; set; } = 50;
        public List<string> FitnessBackground { get; set; } = new();
        public double TrainingFrequencyPerWeek { get; set; }
        public double JobPhysicality { get; set; }
    }

    public sealed class HygieneProfile
    {
        public double NormalCleanliness { get; set; } = 70;
        public double PreferredCleanliness { get; set; } = 75;
        public double ShowerTrigger { get; set; } = 45;
        public double OdorTolerance { get; set; } = 45;
        public double SweatTolerance { get; set; } = 55;
        public double DirtyClothesTolerance { get; set; } = 45;
        public double HairMessTolerance { get; set; } = 55;
        public double OilyHairTolerance { get; set; } = 45;
        public double BadBreathTolerance { get; set; } = 35;
        public double TypicalShowersPerDay { get; set; } = 1.0;
        public double TypicalHairWashesPerWeek { get; set; } = 4;
        public double TypicalTeethBrushesPerDay { get; set; } = 2;
        public double PublicCleanlinessConcern { get; set; } = 70;
        public double PartnerCleanlinessConcern { get; set; } = 65;
        public double PostActivityShowerPreference { get; set; } = 60;
    }

    public sealed class GroomingProfile
    {
        public double OverallEffortBaseline { get; set; } = 55;
        public double HairCare { get; set; } = 55;
        public double SkinCare { get; set; } = 40;
        public double FacialHairCare { get; set; } = 50;
        public double BodyHairGrooming { get; set; } = 40;
        public double NailCare { get; set; } = 35;
        public double DentalCare { get; set; } = 70;
        public double MakeupUse { get; set; }
        public double FragranceUse { get; set; } = 35;
        public double ClothingFitAwareness { get; set; } = 55;
        public double StyleEffort { get; set; } = 50;
        public List<string> GroomingMotives { get; set; } = new();
    }

    public sealed class BodySelfImageProfile
    {
        public double BodyConfidence { get; set; } = 50;
        public double FaceConfidence { get; set; } = 50;
        public double SexualConfidence { get; set; } = 50;
        public double AppearanceAwareness { get; set; } = 50;
        public double BodyAwareness { get; set; } = 50;
        public double KnowsWhichFeaturesGetAttention { get; set; } = 40;
        public double ComfortUsingAppearanceSocially { get; set; } = 35;
        public double Modesty { get; set; } = 50;
        public double NudityComfort { get; set; } = 50;
        public double AttentionSeeking { get; set; } = 35;
        public List<string> FavoriteFeatures { get; set; } = new();
        public List<string> DislikedFeatures { get; set; } = new();
        public List<string> Insecurities { get; set; } = new();
    }

    public sealed class PresentationProfile
    {
        public double UsesAppearanceAsSocialTool { get; set; } = 35;
        public double FlirtThroughAppearance { get; set; } = 30;
        public double IntimidateThroughAppearance { get; set; } = 20;
        public List<string> FeatureEmphasis { get; set; } = new();
        public List<string> FeatureHiding { get; set; } = new();
        public Dictionary<string, PresentationContext> Contexts { get; set; } = new();
    }

    public sealed class PresentationContext
    {
        public double AppearanceEffort { get; set; } = 50;
        public double GroomingEffort { get; set; } = 50;
        public double ComfortPriority { get; set; } = 50;
        public double SexualSignal { get; set; }
        public double Professionalism { get; set; }
        public double StatusDisplay { get; set; }
    }

    /// <summary>Only valid for adults age 18+; context gate required.</summary>
    public sealed class AdultPrivateBodyProfile
    {
        public bool Enabled { get; set; }
        public AdultIntimatePresentation IntimatePresentation { get; set; } = new();
        public AdultAftercarePreferences Aftercare { get; set; } = new();
        public AdultBodyBoundaries Boundaries { get; set; } = new();
        public FemaleAdultAnatomy? FemaleAnatomy { get; set; }
        public MaleAdultAnatomy? MaleAnatomy { get; set; }
    }

    public sealed class AdultIntimatePresentation
    {
        public double LikesDressingForPartner { get; set; } = 50;
        public double UsesClothingAsSignal { get; set; } = 45;
        public double LingerieOrUnderwearInterest { get; set; } = 40;
        public double FragranceForIntimacy { get; set; } = 35;
        public double BodyGroomingEffort { get; set; } = 50;
        public double PrefersSubtleSignals { get; set; } = 50;
        public double PrefersObviousSignals { get; set; } = 35;
        public double ConfidenceWhenDressedUp { get; set; } = 50;
    }

    public sealed class AdultAftercarePreferences
    {
        public double PostSexCleanlinessPreference { get; set; } = 50;
        public double FluidTolerance { get; set; } = 50;
        public double SweatToleranceIntimate { get; set; } = 55;
        public double ImmediateShowerPreference { get; set; } = 40;
        public double LikesCuddlingBeforeCleanup { get; set; } = 60;
        public double LikesLingeringBodyContact { get; set; } = 60;
        public double NeedsPersonalSpaceAfter { get; set; } = 30;
    }

    public sealed class AdultBodyBoundaries
    {
        public double PrivacyNeed { get; set; } = 60;
        public double BodyCommentComfort { get; set; } = 50;
        public double ExplicitBodyQuestionComfort { get; set; } = 35;
        public double NudityWithPartnerComfort { get; set; } = 50;
        public double PublicSexualAttentionComfort { get; set; } = 25;
    }

    public sealed class FemaleAdultAnatomy
    {
        public string BreastSizeCategory { get; set; } = "medium";
        public int? BraBandUs { get; set; }
        public string BraCupUs { get; set; } = "";
        public string BreastShape { get; set; } = "";
        public double BreastFirmness { get; set; } = 50;
        public double BreastSymmetry { get; set; } = 90;
        public string AugmentationStatus { get; set; } = "natural";
        public string NippleColor { get; set; } = "";
        public string NippleSize { get; set; } = "average";
        public string NippleProjection { get; set; } = "moderate";
        public string AreolaColor { get; set; } = "";
        public string AreolaSize { get; set; } = "medium";
        public string AreolaShape { get; set; } = "round";
        public BodyPiercing LeftNipplePiercing { get; set; } = new() { Location = "left_nipple", Privacy = "partner_private" };
        public BodyPiercing RightNipplePiercing { get; set; } = new() { Location = "right_nipple", Privacy = "partner_private" };
    }

    public sealed class MaleAdultAnatomy
    {
        public double? ErectLengthCm { get; set; }
        public double? ErectGirthCm { get; set; }
        public double? FlaccidLengthCm { get; set; }
        public string FlaccidVisibility { get; set; } = "average";
        public string CircumcisionStatus { get; set; } = "unknown";
        public string Grooming { get; set; } = "";
        public List<BodyPiercing> Piercings { get; set; } = new();
    }

    public sealed class HumanBodyState
    {
        public double Cleanliness { get; set; } = 80;
        public double Sweat { get; set; }
        public double SweatWetness { get; set; }
        public double BodyOdor { get; set; }
        public double Dirt { get; set; }
        public double Grime { get; set; }
        public double Oiliness { get; set; } = 10;
        public double HairMess { get; set; } = 10;
        public double HairOiliness { get; set; } = 10;
        public double SkinWetness { get; set; }
        public double BodyHeat { get; set; } = 50;
        public double Breathlessness { get; set; }
        public double Fatigue { get; set; } = 10;
        public double SleepDebt { get; set; }
        public double MuscleSoreness { get; set; }
        public double Pain { get; set; }
        public double Hunger { get; set; } = 20;
        public double Thirst { get; set; } = 20;
        public double BladderNeed { get; set; } = 10;
        public double BowelNeed { get; set; } = 5;
        public double SexualAftercareCleanupNeed { get; set; }
        public double ClothingCleanliness { get; set; } = 90;
        public double ClothingDampness { get; set; }
        public string CurrentMakeupState { get; set; } = "none";
        public string CurrentHairState { get; set; } = "normal";
        public List<string> CurrentInjuries { get; set; } = new();
        public List<string> CurrentIllness { get; set; } = new();
    }

    public sealed class BodyHistoryEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");
        public int Age { get; set; }
        public DateTime? Date { get; set; }
        public string Category { get; set; } = "other";
        public string Cause { get; set; } = "";
        public string BodyRegion { get; set; } = "";
        public string Change { get; set; } = "";
        public bool Permanent { get; set; }
        public double Visibility { get; set; }
        public string SelfImageEffect { get; set; } = "";
        public List<string> KnownBy { get; set; } = new();
    }
}
