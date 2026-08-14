using ProjectEve.Characters.NPCs;

namespace ProjectEve.Characters.NPCs.Body
{
    /// <summary>
    /// Authored body canon for Eve, Adam, Lisa and Edward Sinclair.
    /// Do not run the generic BodyGenerator over these four anchor NPCs.
    /// </summary>
    public static class SinclairBodyCanon
    {
        public static void ApplyEve(NPCAppearance a)
        {
            if (a == null) return;
            a.Gender="Female"; a.Age=25; a.Race="European";
            a.EyeColor="Hazel"; a.EyeStyle="Almond";
            a.HairColor="Light Brown"; a.HairStyle="Shoulder length waves";
            a.SkinTone="Fair"; a.BodyType="Curvy"; a.FaceShape="Soft oval";
            a.Style="Casual cute / work apron to sundress";
            a.UniqueFeature="Warm hazel eyes; confident smile";
            a.HeightCm=165; a.WeightKg=68; a.Glasses="none";
            a.SyncLegacyToBody();

            var b=a.Body;
            b.Identity.SexAtBirth="female"; b.Identity.GenderIdentity="Female";
            b.Identity.AgeYears=25; b.Identity.DevelopmentStage="adult";

            b.Dimensions.HeightCm=165; b.Dimensions.WeightKg=68;
            b.Dimensions.FrameSize="medium"; b.Dimensions.ShoulderWidth="average";
            b.Dimensions.BustCm=96; b.Dimensions.UnderbustCm=81;
            b.Dimensions.WaistCm=74; b.Dimensions.HipsCm=102;
            b.Dimensions.InseamCm=76; b.Dimensions.ShoeSizeUs=8;

            b.Composition.BodyType="curvy"; b.Composition.BodyShape="hourglass";
            b.Composition.BodyFatPercent=29; b.Composition.MuscleMass=46;
            b.Composition.MuscleDefinition=32; b.Composition.Softness=72;
            b.Composition.FitnessAppearance="average";

            b.Face.Shape="soft oval"; b.Face.Symmetry=82; b.Face.Jaw="soft";
            b.Face.Chin="rounded"; b.Face.Cheekbones="high";
            b.Face.NoseShape="straight small"; b.Face.LipShape="defined cupid bow";
            b.Face.LipFullness="full"; b.Face.NaturalLipColor="rose";
            b.Face.SmileStyle="warm, easy when genuine"; b.Face.RestingExpression="attentive";

            b.Eyes.Color="Hazel"; b.Eyes.Shape="almond"; b.Eyes.Lashes="long";
            b.Eyes.Vision="normal"; b.Eyes.CorrectiveLenses="none";

            b.Hair.NaturalColor="Light Brown"; b.Hair.CurrentColor="Light Brown";
            b.Hair.Texture="wavy"; b.Hair.Length="shoulder"; b.Hair.Density="thick";
            b.Hair.Style="shoulder length waves"; b.Hair.GrayPercent=0;

            b.Skin.Tone="Fair"; b.Skin.Undertone="neutral"; b.Skin.Complexion="clear";
            b.Skin.Oiliness=38; b.Skin.Dryness=28; b.Skin.Freckles="light across nose in summer";

            b.Ability.Strength=43; b.Ability.Endurance=48; b.Ability.Speed=47;
            b.Ability.Agility=52; b.Ability.Balance=58; b.Ability.Flexibility=62;
            b.Ability.Coordination=57; b.Ability.GripStrength=42; b.Ability.PainTolerance=58;
            b.Ability.TrainingFrequencyPerWeek=1.5; b.Ability.JobPhysicality=40;
            b.Ability.FitnessBackground.Clear();
            b.Ability.FitnessBackground.Add("on-feet service work");
            b.Ability.FitnessBackground.Add("casual exercise");

            b.Hygiene.NormalCleanliness=86; b.Hygiene.PreferredCleanliness=90;
            b.Hygiene.ShowerTrigger=67; b.Hygiene.OdorTolerance=25;
            b.Hygiene.SweatTolerance=38; b.Hygiene.DirtyClothesTolerance=24;
            b.Hygiene.HairMessTolerance=42; b.Hygiene.OilyHairTolerance=30;
            b.Hygiene.BadBreathTolerance=18; b.Hygiene.TypicalShowersPerDay=1.1;
            b.Hygiene.TypicalHairWashesPerWeek=4; b.Hygiene.TypicalTeethBrushesPerDay=2;
            b.Hygiene.PublicCleanlinessConcern=87; b.Hygiene.PartnerCleanlinessConcern=72;
            b.Hygiene.PostActivityShowerPreference=78;

            b.Grooming.OverallEffortBaseline=76; b.Grooming.HairCare=80; b.Grooming.SkinCare=62;
            b.Grooming.BodyHairGrooming=68; b.Grooming.NailCare=58; b.Grooming.DentalCare=84;
            b.Grooming.MakeupUse=68; b.Grooming.FragranceUse=52;
            b.Grooming.ClothingFitAwareness=82; b.Grooming.StyleEffort=77;
            b.Grooming.GroomingMotives.Clear();
            b.Grooming.GroomingMotives.Add("self_expression");
            b.Grooming.GroomingMotives.Add("professionalism");
            b.Grooming.GroomingMotives.Add("attraction");

            b.SelfImage.BodyConfidence=72; b.SelfImage.FaceConfidence=78;
            b.SelfImage.SexualConfidence=76; b.SelfImage.AppearanceAwareness=84;
            b.SelfImage.BodyAwareness=80; b.SelfImage.KnowsWhichFeaturesGetAttention=82;
            b.SelfImage.ComfortUsingAppearanceSocially=66; b.SelfImage.Modesty=48;
            b.SelfImage.NudityComfort=62; b.SelfImage.AttentionSeeking=42;
            b.SelfImage.FavoriteFeatures.Clear();
            b.SelfImage.FavoriteFeatures.Add("eyes"); b.SelfImage.FavoriteFeatures.Add("waist and hips");
            b.SelfImage.FavoriteFeatures.Add("smile");

            b.Presentation.UsesAppearanceAsSocialTool=69; b.Presentation.FlirtThroughAppearance=64;
            b.Presentation.IntimidateThroughAppearance=12;
            b.Presentation.FeatureEmphasis.Clear();
            b.Presentation.FeatureEmphasis.Add("eyes"); b.Presentation.FeatureEmphasis.Add("waist");
            b.Presentation.FeatureEmphasis.Add("hips");
            Contexts(b,18,80,94,82);

            b.AdultPrivate.Enabled=true;
            b.AdultPrivate.FemaleAnatomy=new FemaleAdultAnatomy
            {
                BreastSizeCategory="full", BraBandUs=36, BraCupUs="C",
                BreastShape="teardrop", BreastFirmness=58, BreastSymmetry=91,
                AugmentationStatus="natural",
                NippleColor="rose-brown", NippleSize="average", NippleProjection="moderate",
                AreolaColor="rose-brown", AreolaSize="medium", AreolaShape="round",
                LeftNipplePiercing=new BodyPiercing{Location="left_nipple",Pierced=false,JewelryType="none",State="healed",Privacy="partner_private"},
                RightNipplePiercing=new BodyPiercing{Location="right_nipple",Pierced=false,JewelryType="none",State="healed",Privacy="partner_private"}
            };
            b.AdultPrivate.IntimatePresentation.LikesDressingForPartner=78;
            b.AdultPrivate.IntimatePresentation.UsesClothingAsSignal=76;
            b.AdultPrivate.IntimatePresentation.LingerieOrUnderwearInterest=74;
            b.AdultPrivate.IntimatePresentation.FragranceForIntimacy=58;
            b.AdultPrivate.IntimatePresentation.BodyGroomingEffort=78;
            b.AdultPrivate.IntimatePresentation.PrefersSubtleSignals=62;
            b.AdultPrivate.IntimatePresentation.PrefersObviousSignals=58;
            b.AdultPrivate.IntimatePresentation.ConfidenceWhenDressedUp=82;

            b.AdultPrivate.Aftercare.PostSexCleanlinessPreference=58;
            b.AdultPrivate.Aftercare.FluidTolerance=74;
            b.AdultPrivate.Aftercare.SweatToleranceIntimate=70;
            b.AdultPrivate.Aftercare.ImmediateShowerPreference=34;
            b.AdultPrivate.Aftercare.LikesCuddlingBeforeCleanup=78;
            b.AdultPrivate.Aftercare.LikesLingeringBodyContact=76;
            b.AdultPrivate.Aftercare.NeedsPersonalSpaceAfter=22;

            b.AdultPrivate.Boundaries.PrivacyNeed=82;
            b.AdultPrivate.Boundaries.BodyCommentComfort=54;
            b.AdultPrivate.Boundaries.ExplicitBodyQuestionComfort=28;
            b.AdultPrivate.Boundaries.NudityWithPartnerComfort=70;
            b.AdultPrivate.Boundaries.PublicSexualAttentionComfort=75;

            StartState(b,90,12,8);
        }

        public static void ApplyAdam(NPCAppearance a)
        {
            if (a == null) return;
            a.Gender="Male"; a.Age=25; a.Race="European";
            a.EyeColor="Hazel"; a.EyeStyle="Deep set"; a.HairColor="Brown";
            a.HairStyle="Short practical"; a.SkinTone="Fair-tan";
            a.BodyType="Athletic solid"; a.FaceShape="Square-oval";
            a.Style="Station wear / jeans and flannel off duty";
            a.UniqueFeature="Same hazel as Eve; broader build; easy smirk";
            a.HeightCm=183; a.WeightKg=86; a.Glasses="none";
            a.SyncLegacyToBody();

            var b=a.Body;
            b.Identity.SexAtBirth="male"; b.Identity.GenderIdentity="Male"; b.Identity.AgeYears=25;
            b.Dimensions.HeightCm=183; b.Dimensions.WeightKg=86; b.Dimensions.FrameSize="large";
            b.Dimensions.ShoulderWidth="broad"; b.Dimensions.ChestCm=108;
            b.Dimensions.WaistCm=84; b.Dimensions.HipsCm=99; b.Dimensions.InseamCm=83;
            b.Dimensions.ShoeSizeUs=11;

            b.Composition.BodyType="athletic"; b.Composition.BodyShape="inverted_triangle";
            b.Composition.BodyFatPercent=16; b.Composition.MuscleMass=75;
            b.Composition.MuscleDefinition=68; b.Composition.Softness=24;
            b.Composition.FitnessAppearance="athletic";

            b.Face.Shape="square-oval"; b.Face.Symmetry=79; b.Face.Jaw="defined";
            b.Face.Cheekbones="average"; b.Face.NoseShape="straight";
            b.Face.SmileStyle="crooked easy smirk"; b.Face.RestingExpression="alert relaxed";

            b.Eyes.Color="Hazel"; b.Eyes.Shape="deep_set"; b.Eyes.Vision="normal";
            b.Hair.NaturalColor="Brown"; b.Hair.CurrentColor="Brown";
            b.Hair.Texture="wavy"; b.Hair.Length="short"; b.Hair.Density="thick";
            b.Hair.StrandThickness="coarse"; b.Hair.Style="short practical";
            b.FacialHair.GrowthLevel=82; b.FacialHair.Color="Brown";
            b.FacialHair.Style="clean shave or short stubble"; b.FacialHair.Density="thick";
            b.FacialHair.UsualGrooming="shaved for shift, stubble off duty";

            b.Ability.Strength=82; b.Ability.Endurance=84; b.Ability.Speed=69;
            b.Ability.Agility=68; b.Ability.Balance=75; b.Ability.Flexibility=54;
            b.Ability.Coordination=78; b.Ability.GripStrength=86; b.Ability.PainTolerance=79;
            b.Ability.HeatTolerance=76; b.Ability.ColdTolerance=67;
            b.Ability.TrainingFrequencyPerWeek=4; b.Ability.JobPhysicality=90;
            b.Ability.FitnessBackground.Clear();
            b.Ability.FitnessBackground.Add("firefighter conditioning");
            b.Ability.FitnessBackground.Add("strength training");

            b.Hygiene.NormalCleanliness=80; b.Hygiene.PreferredCleanliness=84;
            b.Hygiene.ShowerTrigger=58; b.Hygiene.OdorTolerance=38; b.Hygiene.SweatTolerance=62;
            b.Hygiene.DirtyClothesTolerance=42; b.Hygiene.HairMessTolerance=70;
            b.Hygiene.OilyHairTolerance=48; b.Hygiene.BadBreathTolerance=25;
            b.Hygiene.TypicalShowersPerDay=1.3; b.Hygiene.TypicalHairWashesPerWeek=5;
            b.Hygiene.PublicCleanlinessConcern=75; b.Hygiene.PartnerCleanlinessConcern=70;
            b.Hygiene.PostActivityShowerPreference=82;

            b.Grooming.OverallEffortBaseline=58; b.Grooming.HairCare=48; b.Grooming.SkinCare=24;
            b.Grooming.FacialHairCare=70; b.Grooming.BodyHairGrooming=36;
            b.Grooming.DentalCare=78; b.Grooming.FragranceUse=34;
            b.Grooming.ClothingFitAwareness=58; b.Grooming.StyleEffort=44;

            b.SelfImage.BodyConfidence=78; b.SelfImage.FaceConfidence=68;
            b.SelfImage.SexualConfidence=72; b.SelfImage.AppearanceAwareness=62;
            b.SelfImage.BodyAwareness=82; b.SelfImage.KnowsWhichFeaturesGetAttention=58;
            b.SelfImage.ComfortUsingAppearanceSocially=48; b.SelfImage.Modesty=45;
            b.SelfImage.NudityComfort=70; b.SelfImage.AttentionSeeking=25;
            b.SelfImage.FavoriteFeatures.Clear();
            b.SelfImage.FavoriteFeatures.Add("shoulders"); b.SelfImage.FavoriteFeatures.Add("arms");

            b.Presentation.UsesAppearanceAsSocialTool=38; b.Presentation.FlirtThroughAppearance=40;
            b.Presentation.IntimidateThroughAppearance=54;
            b.Presentation.FeatureEmphasis.Clear();
            b.Presentation.FeatureEmphasis.Add("shoulders"); b.Presentation.FeatureEmphasis.Add("arms");
            Contexts(b,12,62,75,62);

            b.AdultPrivate.Enabled=true;
            b.AdultPrivate.MaleAnatomy=new MaleAdultAnatomy
            {
                ErectLengthCm=15.3, ErectGirthCm=12.4, FlaccidLengthCm=9.5,
                FlaccidVisibility="noticeable", CircumcisionStatus="circumcised", Grooming="trimmed"
            };
            b.AdultPrivate.IntimatePresentation.LikesDressingForPartner=44;
            b.AdultPrivate.IntimatePresentation.UsesClothingAsSignal=48;
            b.AdultPrivate.IntimatePresentation.LingerieOrUnderwearInterest=36;
            b.AdultPrivate.IntimatePresentation.FragranceForIntimacy=38;
            b.AdultPrivate.IntimatePresentation.BodyGroomingEffort=52;
            b.AdultPrivate.IntimatePresentation.PrefersSubtleSignals=70;
            b.AdultPrivate.IntimatePresentation.PrefersObviousSignals=28;
            b.AdultPrivate.IntimatePresentation.ConfidenceWhenDressedUp=70;
            b.AdultPrivate.Aftercare.PostSexCleanlinessPreference=62;
            b.AdultPrivate.Aftercare.FluidTolerance=56;
            b.AdultPrivate.Aftercare.SweatToleranceIntimate=76;
            b.AdultPrivate.Aftercare.ImmediateShowerPreference=42;
            b.AdultPrivate.Aftercare.LikesCuddlingBeforeCleanup=58;
            b.AdultPrivate.Aftercare.LikesLingeringBodyContact=62;
            b.AdultPrivate.Boundaries.PrivacyNeed=72;
            b.AdultPrivate.Boundaries.ExplicitBodyQuestionComfort=35;
            b.AdultPrivate.Boundaries.NudityWithPartnerComfort=72;
            StartState(b,84,18,10);
        }

        public static void ApplyLisa(NPCAppearance a)
        {
            if (a == null) return;
            a.Gender="Female"; a.Age=44; a.Race="European";
            a.EyeColor="Hazel"; a.EyeStyle="Soft almond";
            a.HairColor="Brown with early grey"; a.HairStyle="Practical shoulder length";
            a.SkinTone="Fair"; a.BodyType="Average solid"; a.FaceShape="Oval";
            a.Style="Shop-ready casual / clean apron presence";
            a.UniqueFeature="Same family hazel; tired-kind eyes; firm voice";
            a.HeightCm=165; a.WeightKg=72; a.Glasses="reading";
            a.SyncLegacyToBody();

            var b=a.Body;
            b.Identity.SexAtBirth="female"; b.Identity.GenderIdentity="Female"; b.Identity.AgeYears=44;
            b.Dimensions.HeightCm=165; b.Dimensions.WeightKg=72; b.Dimensions.FrameSize="medium";
            b.Dimensions.BustCm=98; b.Dimensions.UnderbustCm=83; b.Dimensions.WaistCm=82;
            b.Dimensions.HipsCm=101; b.Dimensions.InseamCm=75; b.Dimensions.ShoeSizeUs=8;
            b.Composition.BodyType="average"; b.Composition.BodyShape="soft_hourglass";
            b.Composition.BodyFatPercent=31; b.Composition.MuscleMass=44;
            b.Composition.MuscleDefinition=26; b.Composition.Softness=70;

            b.Face.Shape="oval"; b.Face.Symmetry=78; b.Face.Jaw="soft";
            b.Face.Cheekbones="average"; b.Face.SmileStyle="warm but evaluative";
            b.Face.RestingExpression="focused";
            b.Eyes.Color="Hazel"; b.Eyes.Shape="almond"; b.Eyes.Vision="farsighted";
            b.Eyes.CorrectiveLenses="reading_glasses";
            b.Hair.NaturalColor="Brown"; b.Hair.CurrentColor="Brown with early grey";
            b.Hair.Texture="wavy"; b.Hair.Length="shoulder"; b.Hair.Density="average";
            b.Hair.Style="practical shoulder length"; b.Hair.GrayPercent=14;

            b.Ability.Strength=46; b.Ability.Endurance=58; b.Ability.Balance=60;
            b.Ability.Flexibility=52; b.Ability.Coordination=66; b.Ability.PainTolerance=66;
            b.Ability.TrainingFrequencyPerWeek=1; b.Ability.JobPhysicality=45;
            b.Ability.FitnessBackground.Clear(); b.Ability.FitnessBackground.Add("years on feet running a shop");

            b.Hygiene.NormalCleanliness=91; b.Hygiene.PreferredCleanliness=94;
            b.Hygiene.ShowerTrigger=72; b.Hygiene.OdorTolerance=15; b.Hygiene.SweatTolerance=32;
            b.Hygiene.DirtyClothesTolerance=12; b.Hygiene.HairMessTolerance=34;
            b.Hygiene.OilyHairTolerance=24; b.Hygiene.BadBreathTolerance=10;
            b.Hygiene.TypicalShowersPerDay=1.1; b.Hygiene.TypicalHairWashesPerWeek=4;
            b.Hygiene.PublicCleanlinessConcern=96; b.Hygiene.PartnerCleanlinessConcern=80;
            b.Hygiene.PostActivityShowerPreference=82;

            b.Grooming.OverallEffortBaseline=82; b.Grooming.HairCare=76; b.Grooming.SkinCare=58;
            b.Grooming.BodyHairGrooming=54; b.Grooming.NailCare=60; b.Grooming.DentalCare=88;
            b.Grooming.MakeupUse=44; b.Grooming.FragranceUse=38;
            b.Grooming.ClothingFitAwareness=72; b.Grooming.StyleEffort=68;

            b.SelfImage.BodyConfidence=62; b.SelfImage.FaceConfidence=70; b.SelfImage.SexualConfidence=60;
            b.SelfImage.AppearanceAwareness=78; b.SelfImage.BodyAwareness=72;
            b.SelfImage.KnowsWhichFeaturesGetAttention=62; b.SelfImage.Modesty=62;
            b.SelfImage.NudityComfort=54; b.SelfImage.AttentionSeeking=18;

            b.Presentation.UsesAppearanceAsSocialTool=56; b.Presentation.FlirtThroughAppearance=25;
            b.Presentation.IntimidateThroughAppearance=38;
            b.Presentation.FeatureEmphasis.Clear();
            b.Presentation.FeatureEmphasis.Add("eyes");
            b.Presentation.FeatureEmphasis.Add("clean put-together appearance");
            Contexts(b,22,92,78,58);

            b.AdultPrivate.Enabled=true;
            b.AdultPrivate.FemaleAnatomy=new FemaleAdultAnatomy
            {
                BreastSizeCategory="full", BraBandUs=36, BraCupUs="C",
                BreastShape="soft teardrop", BreastFirmness=44, BreastSymmetry=88,
                AugmentationStatus="natural", NippleColor="medium rose-brown",
                NippleSize="average", NippleProjection="moderate",
                AreolaColor="medium rose-brown", AreolaSize="medium", AreolaShape="round"
            };
            b.AdultPrivate.IntimatePresentation.LikesDressingForPartner=48;
            b.AdultPrivate.IntimatePresentation.UsesClothingAsSignal=42;
            b.AdultPrivate.IntimatePresentation.LingerieOrUnderwearInterest=44;
            b.AdultPrivate.IntimatePresentation.PrefersSubtleSignals=78;
            b.AdultPrivate.IntimatePresentation.PrefersObviousSignals=18;
            b.AdultPrivate.Aftercare.PostSexCleanlinessPreference=72;
            b.AdultPrivate.Aftercare.FluidTolerance=42;
            b.AdultPrivate.Aftercare.ImmediateShowerPreference=58;
            b.AdultPrivate.Aftercare.LikesCuddlingBeforeCleanup=64;
            b.AdultPrivate.Boundaries.PrivacyNeed=78;
            b.AdultPrivate.Boundaries.ExplicitBodyQuestionComfort=22;
            b.AdultPrivate.Boundaries.NudityWithPartnerComfort=62;
            StartState(b,94,8,6);
        }

        public static void ApplyEdward(NPCAppearance a)
        {
            if (a == null) return;
            a.Gender="Male"; a.Age=44; a.Race="European";
            a.EyeColor="Brown"; a.EyeStyle="Steady deep set";
            a.HairColor="Dark brown / grey at temples"; a.HairStyle="Short clean";
            a.SkinTone="Fair-tan"; a.BodyType="Solid command build"; a.FaceShape="Square";
            a.Style="Department brass / quiet civilian layers off duty";
            a.UniqueFeature="Grey at the temples; steady eyes; voice that does not need volume";
            a.HeightCm=185; a.WeightKg=94; a.Glasses="none";
            a.SyncLegacyToBody();

            var b=a.Body;
            b.Identity.SexAtBirth="male"; b.Identity.GenderIdentity="Male"; b.Identity.AgeYears=44;
            b.Dimensions.HeightCm=185; b.Dimensions.WeightKg=94; b.Dimensions.FrameSize="large";
            b.Dimensions.ShoulderWidth="broad"; b.Dimensions.ChestCm=112;
            b.Dimensions.WaistCm=94; b.Dimensions.HipsCm=103; b.Dimensions.InseamCm=84;
            b.Dimensions.ShoeSizeUs=12;

            b.Composition.BodyType="stocky"; b.Composition.BodyShape="inverted_triangle";
            b.Composition.BodyFatPercent=23; b.Composition.MuscleMass=68;
            b.Composition.MuscleDefinition=46; b.Composition.Softness=46;
            b.Composition.FitnessAppearance="powerful";

            b.Face.Shape="square"; b.Face.Symmetry=76; b.Face.Jaw="strong";
            b.Face.NoseShape="straight broad"; b.Face.SmileStyle="small, rare, genuine";
            b.Face.RestingExpression="steady";
            b.Eyes.Color="Brown"; b.Eyes.Shape="deep_set"; b.Eyes.Vision="normal";
            b.Hair.NaturalColor="Dark Brown"; b.Hair.CurrentColor="Dark brown / grey at temples";
            b.Hair.Texture="straight"; b.Hair.Length="short"; b.Hair.Density="average";
            b.Hair.Style="short clean"; b.Hair.GrayPercent=24; b.Hair.BaldingPattern="slight temple recession";
            b.FacialHair.GrowthLevel=78; b.FacialHair.Color="dark brown with grey";
            b.FacialHair.Style="clean shaven"; b.FacialHair.Density="thick";
            b.FacialHair.UsualGrooming="clean shaven for command";

            b.Ability.Strength=74; b.Ability.Endurance=70; b.Ability.Speed=52;
            b.Ability.Agility=52; b.Ability.Balance=70; b.Ability.Flexibility=42;
            b.Ability.Coordination=74; b.Ability.GripStrength=80; b.Ability.PainTolerance=82;
            b.Ability.HeatTolerance=72; b.Ability.ColdTolerance=68;
            b.Ability.TrainingFrequencyPerWeek=2.5; b.Ability.JobPhysicality=60;
            b.Ability.FitnessBackground.Clear();
            b.Ability.FitnessBackground.Add("career firefighter");
            b.Ability.FitnessBackground.Add("command fitness maintenance");

            b.Hygiene.NormalCleanliness=84; b.Hygiene.PreferredCleanliness=88;
            b.Hygiene.ShowerTrigger=62; b.Hygiene.OdorTolerance=32; b.Hygiene.SweatTolerance=58;
            b.Hygiene.DirtyClothesTolerance=30; b.Hygiene.HairMessTolerance=55;
            b.Hygiene.OilyHairTolerance=38; b.Hygiene.BadBreathTolerance=18;
            b.Hygiene.TypicalShowersPerDay=1.1; b.Hygiene.TypicalHairWashesPerWeek=5;
            b.Hygiene.PublicCleanlinessConcern=88; b.Hygiene.PartnerCleanlinessConcern=74;
            b.Hygiene.PostActivityShowerPreference=76;

            b.Grooming.OverallEffortBaseline=70; b.Grooming.HairCare=62; b.Grooming.SkinCare=22;
            b.Grooming.FacialHairCare=78; b.Grooming.BodyHairGrooming=30;
            b.Grooming.DentalCare=80; b.Grooming.FragranceUse=28;
            b.Grooming.ClothingFitAwareness=64; b.Grooming.StyleEffort=50;

            b.SelfImage.BodyConfidence=70; b.SelfImage.FaceConfidence=66;
            b.SelfImage.SexualConfidence=62; b.SelfImage.AppearanceAwareness=58;
            b.SelfImage.BodyAwareness=74; b.SelfImage.KnowsWhichFeaturesGetAttention=44;
            b.SelfImage.ComfortUsingAppearanceSocially=38; b.SelfImage.Modesty=58;
            b.SelfImage.NudityComfort=58; b.SelfImage.AttentionSeeking=12;

            b.Presentation.UsesAppearanceAsSocialTool=50; b.Presentation.FlirtThroughAppearance=18;
            b.Presentation.IntimidateThroughAppearance=68;
            b.Presentation.FeatureEmphasis.Clear();
            b.Presentation.FeatureEmphasis.Add("posture"); b.Presentation.FeatureEmphasis.Add("shoulders");
            Contexts(b,18,88,66,48);

            b.AdultPrivate.Enabled=true;
            b.AdultPrivate.MaleAnatomy=new MaleAdultAnatomy
            {
                ErectLengthCm=14.7, ErectGirthCm=12.0, FlaccidLengthCm=9.0,
                FlaccidVisibility="average", CircumcisionStatus="circumcised", Grooming="trimmed natural"
            };
            b.AdultPrivate.IntimatePresentation.LikesDressingForPartner=30;
            b.AdultPrivate.IntimatePresentation.UsesClothingAsSignal=26;
            b.AdultPrivate.IntimatePresentation.PrefersSubtleSignals=82;
            b.AdultPrivate.IntimatePresentation.PrefersObviousSignals=10;
            b.AdultPrivate.Aftercare.PostSexCleanlinessPreference=66;
            b.AdultPrivate.Aftercare.FluidTolerance=52;
            b.AdultPrivate.Aftercare.ImmediateShowerPreference=48;
            b.AdultPrivate.Aftercare.LikesCuddlingBeforeCleanup=60;
            b.AdultPrivate.Boundaries.PrivacyNeed=84;
            b.AdultPrivate.Boundaries.ExplicitBodyQuestionComfort=16;
            b.AdultPrivate.Boundaries.NudityWithPartnerComfort=60;
            StartState(b,88,8,8);
        }

        private static void Contexts(HumanBodyProfile b,double home,double work,double date,double intimate)
        {
            b.Presentation.Contexts["home_alone"]=Ctx(home,home+5,95);
            b.Presentation.Contexts["home_with_partner"]=Ctx(home+10,home+15,85,15);
            b.Presentation.Contexts["casual_public"]=Ctx((home+work)/2,(home+work)/2+5,60,10);
            b.Presentation.Contexts["work"]=Ctx(work,work+3,45,2,90);
            b.Presentation.Contexts["date"]=Ctx(date,date,40,50);
            b.Presentation.Contexts["formal_event"]=Ctx(System.Math.Min(100,date+5),System.Math.Min(100,date+5),30,25,70);
            b.Presentation.Contexts["exercise"]=Ctx(25,20,95);
            b.Presentation.Contexts["sleep"]=Ctx(5,8,100);
            b.Presentation.Contexts["adult_intimate"]=Ctx(intimate,System.Math.Min(100,intimate+5),65,75);
        }

        private static PresentationContext Ctx(double a,double g,double c,double signal=0,double professional=0,double status=0)
            => new PresentationContext
            {
                AppearanceEffort=a,GroomingEffort=g,ComfortPriority=c,
                SexualSignal=signal,Professionalism=professional,StatusDisplay=status
            };

        private static void StartState(HumanBodyProfile b,double clean,double hairMess,double hairOil)
        {
            b.State.Cleanliness=clean; b.State.Sweat=0; b.State.SweatWetness=0;
            b.State.BodyOdor=0; b.State.Dirt=0; b.State.Grime=0; b.State.Oiliness=8;
            b.State.HairMess=hairMess; b.State.HairOiliness=hairOil; b.State.BodyHeat=50;
            b.State.Breathlessness=0; b.State.Fatigue=10;
            b.State.ClothingCleanliness=95; b.State.ClothingDampness=0;
            b.State.CurrentHairState="normal";
        }
    }
}
