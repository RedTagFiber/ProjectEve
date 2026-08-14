using System;
using System.Collections.Generic;

namespace ProjectEve.Characters.NPCs.Body
{
    /// <summary>
    /// Generates stable NPC body values. This is simulation truth, not dialogue.
    /// The generator deliberately creates correlated clusters instead of rolling every field independently.
    /// </summary>
    public static class BodyGenerator
    {
        public static HumanBodyProfile Generate(string gender, int age, string? ancestry = null, Random? rng = null)
        {
            rng ??= new Random();
            var body = new HumanBodyProfile();
            FillMissing(body, gender, age, ancestry, rng);
            return body;
        }

        public static void FillMissing(
            HumanBodyProfile body,
            string gender,
            int age,
            string? ancestry = null,
            Random? rng = null)
        {
            if (body == null) return;
            rng ??= new Random();

            bool male = IsMale(gender);
            bool female = IsFemale(gender);
            bool adult = age >= 18;

            body.Identity.AgeYears = age;
            body.Identity.GenderIdentity = string.IsNullOrWhiteSpace(gender) ? "Unknown" : gender;
            body.Identity.DevelopmentStage = AgeStage(age);
            if (!string.IsNullOrWhiteSpace(ancestry) && body.Identity.AncestryBackground.Count == 0)
                body.Identity.AncestryBackground.Add(ancestry);

            // ---------- dimensions ----------
            if (body.Dimensions.HeightCm <= 0)
                body.Dimensions.HeightCm = adult
                    ? Clamp(Gaussian(rng, male ? 176 : female ? 163 : 169, male ? 8 : 7), 140, 210)
                    : ChildHeight(age, rng);

            if (string.IsNullOrWhiteSpace(body.Dimensions.FrameSize) || body.Dimensions.FrameSize == "medium")
                body.Dimensions.FrameSize = Weighted(rng,
                    ("small", 20), ("medium", 58), ("large", 22));

            // Body build is correlated with composition/fitness, not a separate disconnected roll.
            if (string.IsNullOrWhiteSpace(body.Composition.BodyType) || body.Composition.BodyType == "average")
            {
                body.Composition.BodyType = male
                    ? Weighted(rng, ("slim",18),("average",42),("athletic",18),("muscular",8),("stocky",9),("heavy",5))
                    : female
                    ? Weighted(rng, ("slim",18),("average",38),("athletic",15),("curvy",16),("soft",9),("heavy",4))
                    : Weighted(rng, ("slim",18),("average",44),("athletic",18),("soft",10),("stocky",6),("heavy",4));
            }

            ApplyCompositionCluster(body, rng);

            if (body.Dimensions.WeightKg <= 0)
            {
                double h = body.Dimensions.HeightCm / 100.0;
                double bmi = body.Composition.BodyType switch
                {
                    "slim" => Gaussian(rng, 20.0, 1.5),
                    "athletic" => Gaussian(rng, 23.0, 1.8),
                    "muscular" => Gaussian(rng, 25.5, 2.0),
                    "curvy" => Gaussian(rng, 25.0, 2.4),
                    "soft" => Gaussian(rng, 26.0, 2.8),
                    "stocky" => Gaussian(rng, 28.0, 2.6),
                    "heavy" => Gaussian(rng, 32.0, 4.0),
                    _ => Gaussian(rng, 23.5, 2.5)
                };
                body.Dimensions.WeightKg = Math.Round(Clamp(bmi * h * h, 35, 250), 1);
            }

            // ---------- face / hair / movement ----------
            if (string.IsNullOrWhiteSpace(body.Face.Jaw) || body.Face.Jaw == "average")
                body.Face.Jaw = Pick(rng, "soft","average","average","defined","square","narrow","strong");
            if (string.IsNullOrWhiteSpace(body.Face.Cheekbones) || body.Face.Cheekbones == "average")
                body.Face.Cheekbones = Pick(rng, "low","average","average","high","prominent");

            if (string.IsNullOrWhiteSpace(body.Hair.Texture) || body.Hair.Texture == "Unknown")
                body.Hair.Texture = Pick(rng, "straight","straight","wavy","wavy","curly","coily","mixed");
            if (string.IsNullOrWhiteSpace(body.Hair.Density) || body.Hair.Density == "average")
                body.Hair.Density = Pick(rng, "thin","average","average","average","thick","very_thick");

            body.Ability.Strength = Clamp(Gaussian(rng, 50, 15) + (body.Composition.MuscleMass - 50) * .35, 5, 98);
            body.Ability.Endurance = Clamp(Gaussian(rng, 50, 15), 5, 98);
            body.Ability.Speed = Clamp(Gaussian(rng, 50, 14), 5, 98);
            body.Ability.Agility = Clamp(Gaussian(rng, 50, 14), 5, 98);
            body.Ability.Balance = Clamp(Gaussian(rng, 55, 12), 5, 98);
            body.Ability.Flexibility = Clamp(Gaussian(rng, female ? 58 : 48, 14), 5, 98);
            body.Ability.Coordination = Clamp(Gaussian(rng, 52, 13), 5, 98);
            body.Ability.GripStrength = Clamp(body.Ability.Strength + Gaussian(rng, 0, 8), 5, 98);
            body.Ability.PainTolerance = Clamp(Gaussian(rng, 50, 17), 3, 99);
            body.Ability.HeatTolerance = Clamp(Gaussian(rng, 50, 15), 5, 95);
            body.Ability.ColdTolerance = Clamp(Gaussian(rng, 50, 15), 5, 95);

            // ---------- hygiene / grooming cluster ----------
            GenerateHygieneCluster(body, rng);
            GenerateGroomingCluster(body, gender, rng);
            GenerateSelfImage(body, rng);
            GeneratePresentation(body, rng);

            // Current state starts near this person's normal baseline, not at a universal 80.
            body.State.Cleanliness = Clamp(body.Hygiene.NormalCleanliness + Gaussian(rng, 0, 6), 5, 100);
            body.State.ClothingCleanliness = Clamp(body.Hygiene.NormalCleanliness + Gaussian(rng, 8, 7), 5, 100);
            body.State.HairMess = Clamp(100 - body.Grooming.HairCare + Gaussian(rng, 0, 12), 0, 100);
            body.State.HairOiliness = Clamp(100 - body.Hygiene.OilyHairTolerance + Gaussian(rng, -25, 12), 0, 100);

            // ---------- adult private layer ----------
            body.AdultPrivate.Enabled = adult;
            if (!adult)
            {
                body.AdultPrivate.FemaleAnatomy = null;
                body.AdultPrivate.MaleAnatomy = null;
                return;
            }

            GenerateAdultPrivatePreferences(body, rng);

            if (female)
                GenerateAdultFemaleAnatomy(body, rng);
            else if (male)
                GenerateAdultMaleAnatomy(body, rng);
        }

        private static void ApplyCompositionCluster(HumanBodyProfile b, Random rng)
        {
            switch (b.Composition.BodyType)
            {
                case "slim":
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 17, 4), 7, 32);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 40, 10), 10, 70);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 48, 15), 5, 90);
                    b.Composition.Softness = Clamp(Gaussian(rng, 35, 15), 5, 80);
                    b.Composition.FitnessAppearance = "average";
                    break;
                case "athletic":
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 18, 4), 7, 32);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 68, 10), 35, 92);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 70, 10), 35, 95);
                    b.Composition.Softness = Clamp(Gaussian(rng, 25, 12), 3, 65);
                    b.Composition.FitnessAppearance = "athletic";
                    break;
                case "muscular":
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 16, 4), 6, 30);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 82, 8), 55, 98);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 78, 10), 45, 98);
                    b.Composition.Softness = Clamp(Gaussian(rng, 18, 10), 2, 50);
                    b.Composition.FitnessAppearance = "muscular";
                    break;
                case "curvy":
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 29, 5), 17, 45);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 48, 10), 20, 75);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 35, 12), 5, 70);
                    b.Composition.Softness = Clamp(Gaussian(rng, 68, 10), 35, 95);
                    b.Composition.FitnessAppearance = "average";
                    break;
                case "soft":
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 31, 6), 18, 50);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 42, 10), 15, 70);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 25, 10), 3, 60);
                    b.Composition.Softness = Clamp(Gaussian(rng, 78, 9), 45, 98);
                    b.Composition.FitnessAppearance = "soft";
                    break;
                case "stocky":
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 27, 6), 14, 48);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 65, 12), 30, 92);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 45, 14), 10, 80);
                    b.Composition.Softness = Clamp(Gaussian(rng, 55, 13), 20, 90);
                    b.Composition.FitnessAppearance = "powerful";
                    break;
                case "heavy":
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 38, 7), 24, 60);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 52, 12), 20, 82);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 20, 10), 2, 55);
                    b.Composition.Softness = Clamp(Gaussian(rng, 85, 7), 55, 100);
                    b.Composition.FitnessAppearance = "soft";
                    break;
                default:
                    b.Composition.BodyFatPercent = Clamp(Gaussian(rng, 25, 6), 12, 45);
                    b.Composition.MuscleMass = Clamp(Gaussian(rng, 50, 11), 20, 80);
                    b.Composition.MuscleDefinition = Clamp(Gaussian(rng, 42, 13), 8, 80);
                    b.Composition.Softness = Clamp(Gaussian(rng, 52, 14), 15, 90);
                    b.Composition.FitnessAppearance = "average";
                    break;
            }
        }

        private static void GenerateHygieneCluster(HumanBodyProfile b, Random rng)
        {
            string type = Weighted(rng,
                ("low", 18), ("average", 57), ("well_groomed", 20), ("meticulous", 5));

            switch (type)
            {
                case "low":
                    b.Hygiene.NormalCleanliness = Range(rng, 35, 58);
                    b.Hygiene.PreferredCleanliness = Range(rng, 40, 62);
                    b.Hygiene.ShowerTrigger = Range(rng, 18, 38);
                    b.Hygiene.OdorTolerance = Range(rng, 65, 90);
                    b.Hygiene.SweatTolerance = Range(rng, 65, 90);
                    b.Hygiene.DirtyClothesTolerance = Range(rng, 65, 90);
                    b.Hygiene.TypicalShowersPerDay = Range(rng, .35, .8);
                    break;
                case "well_groomed":
                    b.Hygiene.NormalCleanliness = Range(rng, 78, 92);
                    b.Hygiene.PreferredCleanliness = Range(rng, 82, 96);
                    b.Hygiene.ShowerTrigger = Range(rng, 58, 76);
                    b.Hygiene.OdorTolerance = Range(rng, 12, 38);
                    b.Hygiene.SweatTolerance = Range(rng, 20, 45);
                    b.Hygiene.DirtyClothesTolerance = Range(rng, 15, 42);
                    b.Hygiene.TypicalShowersPerDay = Range(rng, .9, 1.6);
                    break;
                case "meticulous":
                    b.Hygiene.NormalCleanliness = Range(rng, 90, 98);
                    b.Hygiene.PreferredCleanliness = Range(rng, 94, 100);
                    b.Hygiene.ShowerTrigger = Range(rng, 72, 90);
                    b.Hygiene.OdorTolerance = Range(rng, 2, 20);
                    b.Hygiene.SweatTolerance = Range(rng, 5, 25);
                    b.Hygiene.DirtyClothesTolerance = Range(rng, 3, 22);
                    b.Hygiene.TypicalShowersPerDay = Range(rng, 1.5, 3.0);
                    break;
                default:
                    b.Hygiene.NormalCleanliness = Range(rng, 62, 80);
                    b.Hygiene.PreferredCleanliness = Range(rng, 68, 86);
                    b.Hygiene.ShowerTrigger = Range(rng, 38, 60);
                    b.Hygiene.OdorTolerance = Range(rng, 35, 65);
                    b.Hygiene.SweatTolerance = Range(rng, 42, 70);
                    b.Hygiene.DirtyClothesTolerance = Range(rng, 35, 68);
                    b.Hygiene.TypicalShowersPerDay = Range(rng, .7, 1.15);
                    break;
            }

            b.Hygiene.HairMessTolerance = Clamp(Gaussian(rng, 55, 18), 5, 95);
            b.Hygiene.OilyHairTolerance = Clamp(Gaussian(rng, 45, 18), 5, 95);
            b.Hygiene.BadBreathTolerance = Clamp(Gaussian(rng, 30, 14), 3, 90);
            b.Hygiene.TypicalHairWashesPerWeek = Range(rng, 2, 7);
            b.Hygiene.TypicalTeethBrushesPerDay = rng.NextDouble() < .82 ? 2 : (rng.NextDouble() < .65 ? 1 : 3);
            b.Hygiene.PublicCleanlinessConcern = Clamp(100 - b.Hygiene.OdorTolerance + Gaussian(rng, 15, 10), 5, 100);
            b.Hygiene.PartnerCleanlinessConcern = Clamp(Gaussian(rng, b.Hygiene.PublicCleanlinessConcern, 12), 5, 100);
            b.Hygiene.PostActivityShowerPreference = Clamp(100 - b.Hygiene.SweatTolerance + Gaussian(rng, 20, 12), 0, 100);
        }

        private static void GenerateGroomingCluster(HumanBodyProfile b, string gender, Random rng)
        {
            double baseEffort = Clamp(Gaussian(rng, b.Hygiene.PublicCleanlinessConcern, 18), 5, 98);
            b.Grooming.OverallEffortBaseline = baseEffort;
            b.Grooming.HairCare = Clamp(baseEffort + Gaussian(rng, 0, 14), 0, 100);
            b.Grooming.SkinCare = Clamp(baseEffort - 10 + Gaussian(rng, 0, 18), 0, 100);
            b.Grooming.FacialHairCare = IsMale(gender) ? Clamp(baseEffort + Gaussian(rng, 0, 18), 0, 100) : 0;
            b.Grooming.BodyHairGrooming = Clamp(Gaussian(rng, 45, 25), 0, 100);
            b.Grooming.NailCare = Clamp(baseEffort - 15 + Gaussian(rng, 0, 20), 0, 100);
            b.Grooming.DentalCare = Clamp(65 + Gaussian(rng, 0, 15), 5, 100);
            b.Grooming.MakeupUse = IsFemale(gender) ? Clamp(Gaussian(rng, 42, 28), 0, 100) : Clamp(Gaussian(rng, 3, 7), 0, 35);
            b.Grooming.FragranceUse = Clamp(Gaussian(rng, 40, 23), 0, 100);
            b.Grooming.ClothingFitAwareness = Clamp(baseEffort + Gaussian(rng, -5, 18), 0, 100);
            b.Grooming.StyleEffort = Clamp(baseEffort + Gaussian(rng, -8, 20), 0, 100);

            b.Grooming.GroomingMotives.Clear();
            b.Grooming.GroomingMotives.Add(Pick(rng, "comfort","social_acceptance","professionalism","self_expression","attraction","routine"));
            if (rng.NextDouble() < .4)
                b.Grooming.GroomingMotives.Add(Pick(rng, "confidence","status","partner_attention","habit","control","culture"));
        }

        private static void GenerateSelfImage(HumanBodyProfile b, Random rng)
        {
            b.SelfImage.BodyConfidence = Clamp(Gaussian(rng, 52, 20), 2, 98);
            b.SelfImage.FaceConfidence = Clamp(Gaussian(rng, 55, 18), 2, 98);
            b.SelfImage.SexualConfidence = Clamp(Gaussian(rng, b.SelfImage.BodyConfidence, 20), 0, 100);
            b.SelfImage.AppearanceAwareness = Clamp(Gaussian(rng, 55, 20), 0, 100);
            b.SelfImage.BodyAwareness = Clamp(Gaussian(rng, 55, 20), 0, 100);
            b.SelfImage.KnowsWhichFeaturesGetAttention = Clamp(
                (b.SelfImage.AppearanceAwareness + b.SelfImage.BodyConfidence) / 2 + Gaussian(rng, -5, 15), 0, 100);
            b.SelfImage.ComfortUsingAppearanceSocially = Clamp(
                b.SelfImage.BodyConfidence * .55 + b.SelfImage.KnowsWhichFeaturesGetAttention * .25 + Gaussian(rng, 0, 15), 0, 100);
            b.SelfImage.Modesty = Clamp(Gaussian(rng, 55, 23), 0, 100);
            b.SelfImage.NudityComfort = Clamp(100 - b.SelfImage.Modesty + Gaussian(rng, 0, 18), 0, 100);
            b.SelfImage.AttentionSeeking = Clamp(Gaussian(rng, 35, 22), 0, 100);
        }

        private static void GeneratePresentation(HumanBodyProfile b, Random rng)
        {
            b.Presentation.UsesAppearanceAsSocialTool = Clamp(
                b.SelfImage.ComfortUsingAppearanceSocially * .55 + b.SelfImage.AppearanceAwareness * .25 + Gaussian(rng, 0, 15), 0, 100);
            b.Presentation.FlirtThroughAppearance = Clamp(
                b.Presentation.UsesAppearanceAsSocialTool * .55 + Gaussian(rng, 0, 18), 0, 100);
            b.Presentation.IntimidateThroughAppearance = Clamp(Gaussian(rng, 22, 18), 0, 100);

            double publicEffort = Clamp(b.Grooming.OverallEffortBaseline + Gaussian(rng, 5, 8), 0, 100);
            b.Presentation.Contexts["home_alone"] = Ctx(Clamp(publicEffort - Range(rng, 35, 70), 0, 100), 90);
            b.Presentation.Contexts["home_with_partner"] = Ctx(Clamp(publicEffort - Range(rng, 20, 55), 0, 100), 80);
            b.Presentation.Contexts["casual_public"] = Ctx(publicEffort, 55);
            b.Presentation.Contexts["work"] = Ctx(Clamp(publicEffort + Range(rng, 0, 15), 0, 100), 45, professionalism:75);
            b.Presentation.Contexts["date"] = Ctx(Clamp(publicEffort + Range(rng, 10, 30), 0, 100), 35, sexualSignal:Clamp(b.Presentation.FlirtThroughAppearance,0,75));
            b.Presentation.Contexts["formal_event"] = Ctx(Clamp(publicEffort + Range(rng, 15, 35), 0, 100), 25, status:55);
            b.Presentation.Contexts["exercise"] = Ctx(Range(rng, 15, 40), 92);
            b.Presentation.Contexts["sleep"] = Ctx(Range(rng, 0, 15), 100);
            b.Presentation.Contexts["adult_intimate"] = Ctx(
                Clamp(publicEffort + Gaussian(rng, 0, 18), 0, 100), 60,
                sexualSignal:Clamp(Gaussian(rng, 60, 22),0,100));
        }

        private static void GenerateAdultPrivatePreferences(HumanBodyProfile b, Random rng)
        {
            var p = b.AdultPrivate;
            p.IntimatePresentation.LikesDressingForPartner = Clamp(Gaussian(rng, 50, 25), 0, 100);
            p.IntimatePresentation.UsesClothingAsSignal = Clamp(Gaussian(rng, 48, 25), 0, 100);
            p.IntimatePresentation.LingerieOrUnderwearInterest = Clamp(Gaussian(rng, 45, 27), 0, 100);
            p.IntimatePresentation.FragranceForIntimacy = Clamp(Gaussian(rng, 40, 25), 0, 100);
            p.IntimatePresentation.BodyGroomingEffort = Clamp(Gaussian(rng, 52, 25), 0, 100);
            p.IntimatePresentation.PrefersSubtleSignals = Clamp(Gaussian(rng, 52, 22), 0, 100);
            p.IntimatePresentation.PrefersObviousSignals = Clamp(Gaussian(rng, 38, 23), 0, 100);
            p.IntimatePresentation.ConfidenceWhenDressedUp = Clamp(
                b.SelfImage.SexualConfidence * .6 + Gaussian(rng, 20, 15), 0, 100);

            p.Aftercare.PostSexCleanlinessPreference = Clamp(Gaussian(rng, b.Hygiene.PreferredCleanliness, 18), 0, 100);
            p.Aftercare.FluidTolerance = Clamp(Gaussian(rng, 50, 28), 0, 100);
            p.Aftercare.SweatToleranceIntimate = Clamp(Gaussian(rng, 55, 24), 0, 100);
            p.Aftercare.ImmediateShowerPreference = Clamp(
                p.Aftercare.PostSexCleanlinessPreference * .65 + (100 - p.Aftercare.FluidTolerance) * .25 + Gaussian(rng, -10, 15), 0, 100);
            p.Aftercare.LikesCuddlingBeforeCleanup = Clamp(Gaussian(rng, 62, 24), 0, 100);
            p.Aftercare.LikesLingeringBodyContact = Clamp(Gaussian(rng, 62, 24), 0, 100);
            p.Aftercare.NeedsPersonalSpaceAfter = Clamp(Gaussian(rng, 30, 22), 0, 100);

            p.Boundaries.PrivacyNeed = Clamp(Gaussian(rng, 60, 22), 0, 100);
            p.Boundaries.BodyCommentComfort = Clamp(Gaussian(rng, 50, 25), 0, 100);
            p.Boundaries.ExplicitBodyQuestionComfort = Clamp(Gaussian(rng, 35, 25), 0, 100);
            p.Boundaries.NudityWithPartnerComfort = Clamp(Gaussian(rng, 60, 25), 0, 100);
            p.Boundaries.PublicSexualAttentionComfort = Clamp(Gaussian(rng, 25, 22), 0, 100);
        }

        private static void GenerateAdultFemaleAnatomy(HumanBodyProfile b, Random rng)
        {
            var a = new FemaleAdultAnatomy();
            a.BreastSizeCategory = Weighted(rng,
                ("very_small",7),("small",19),("medium",35),("full",24),("large",12),("very_large",3));
            a.BreastShape = Pick(rng, "round","teardrop","shallow","projected","wide_set","close_set","asymmetric");
            a.BreastFirmness = Clamp(Gaussian(rng, 55, 18), 5, 95);
            a.BreastSymmetry = Clamp(Gaussian(rng, 88, 8), 45, 100);
            a.AugmentationStatus = rng.NextDouble() < .06 ? "augmented" : "natural";
            a.NippleSize = Weighted(rng, ("small",25),("average",55),("large",20));
            a.NippleProjection = Weighted(rng, ("flat",5),("slight",20),("moderate",55),("prominent",15),("inverted",5));
            a.AreolaSize = Weighted(rng, ("small",25),("medium",55),("large",20));
            a.AreolaShape = Weighted(rng, ("round",70),("oval",25),("irregular",5));
            b.AdultPrivate.FemaleAnatomy = a;
        }

        private static void GenerateAdultMaleAnatomy(HumanBodyProfile b, Random rng)
        {
            var a = new MaleAdultAnatomy();
            // Broad human ranges; generated as private truth, never inferred by observers.
            a.ErectLengthCm = Math.Round(Clamp(Gaussian(rng, 13.8, 1.8), 8, 21), 1);
            a.ErectGirthCm = Math.Round(Clamp(Gaussian(rng, 11.7, 1.2), 8, 16), 1);
            a.FlaccidLengthCm = Math.Round(Clamp(Gaussian(rng, 9.0, 1.8), 4, 15), 1);
            a.FlaccidVisibility = Weighted(rng, ("low",25),("average",55),("noticeable",20));
            a.CircumcisionStatus = Weighted(rng, ("circumcised",60),("uncircumcised",38),("unknown",2));
            b.AdultPrivate.MaleAnatomy = a;
        }

        private static PresentationContext Ctx(double effort, double comfort, double sexualSignal = 0, double professionalism = 0, double status = 0)
            => new()
            {
                AppearanceEffort = effort,
                GroomingEffort = effort,
                ComfortPriority = comfort,
                SexualSignal = sexualSignal,
                Professionalism = professionalism,
                StatusDisplay = status
            };

        private static bool IsMale(string? g)
            => string.Equals(g, "Male", StringComparison.OrdinalIgnoreCase)
               || string.Equals(g, "Man", StringComparison.OrdinalIgnoreCase);

        private static bool IsFemale(string? g)
            => string.Equals(g, "Female", StringComparison.OrdinalIgnoreCase)
               || string.Equals(g, "Woman", StringComparison.OrdinalIgnoreCase);

        private static string AgeStage(int age)
            => age < 2 ? "infant"
             : age < 13 ? "child"
             : age < 18 ? "teen"
             : age < 60 ? "adult"
             : age < 75 ? "older_adult"
             : "elder";

        private static double ChildHeight(int age, Random rng)
        {
            if (age <= 0) return Range(rng, 45, 55);
            double mean = age switch
            {
                <= 2 => 75 + age * 10,
                <= 5 => 90 + (age - 2) * 7,
                <= 10 => 111 + (age - 5) * 6,
                <= 13 => 141 + (age - 10) * 6,
                _ => 159 + (age - 13) * 3
            };
            return Clamp(Gaussian(rng, mean, 6), 45, 195);
        }

        private static string Pick(Random rng, params string[] values)
            => values[rng.Next(values.Length)];

        private static string Weighted(Random rng, params (string value, int weight)[] items)
        {
            int total = 0;
            foreach (var i in items) total += i.weight;
            int roll = rng.Next(total);
            int sum = 0;
            foreach (var i in items)
            {
                sum += i.weight;
                if (roll < sum) return i.value;
            }
            return items[^1].value;
        }

        private static double Range(Random rng, double min, double max)
            => min + rng.NextDouble() * (max - min);

        private static double Gaussian(Random rng, double mean, double sd)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + z * sd;
        }

        private static double Clamp(double v, double min, double max)
            => Math.Max(min, Math.Min(max, v));
    }
}
