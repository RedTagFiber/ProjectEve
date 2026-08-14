using ProjectEve.Characters.Base;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProjectEve.Characters.Cognition
{
    /// <summary>
    /// Generates and refreshes cognition without turning IQ into personality.
    ///
    /// Generation model:
    /// - IQ baseline is generated once around a population mean.
    /// - Age changes maturity / experience and modestly affects processing speed.
    /// - Education changes vocabulary, formal language and academic exposure.
    /// - Job/life experience creates domain knowledge.
    /// - Traits help shape HOW the person expresses what they know.
    ///
    /// A job change does not automatically give an existing NPC a degree.
    /// </summary>
    public static class CognitiveProfileGenerator
    {
        public static CognitiveProfile EnsureGenerated(
            SimCharacter npc,
            Random? rng = null)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            rng ??= Random.Shared;

            npc.Cognition ??= new CognitiveProfile();

            if (!npc.Cognition.IsGenerated)
                GenerateBase(npc, rng);
            else
                RefreshDerived(npc, rng, preserveStableSpeechNoise: true);

            return npc.Cognition;
        }

        /// <summary>
        /// Finalize a newly generated NPC after CharacterFactory knows the job.
        /// Existing finalized NPCs keep their education; only job knowledge refreshes.
        /// </summary>
        public static CognitiveProfile FinalizeLifeContext(
            SimCharacter npc,
            Random? rng = null,
            string? minimumEducation = null,
            string? typicalEducation = null,
            string? fieldOfStudy = null,
            bool allowEducationUpgrade = true)
        {
            rng ??= Random.Shared;
            var p = EnsureGenerated(npc, rng);

            var min = CognitiveEducation.Parse(minimumEducation);
            var typical = CognitiveEducation.Parse(typicalEducation);

            if (min == EducationLevel.Unknown && typical == EducationLevel.Unknown)
            {
                var inferred = InferEducationForOccupation(npc.Occupation, npc.Job?.JobName);
                min = inferred.Minimum;
                typical = inferred.Typical;
                if (string.IsNullOrWhiteSpace(fieldOfStudy))
                    fieldOfStudy = inferred.Field;
            }

            if (!p.LifeContextFinalized || allowEducationUpgrade)
            {
                var selected = SelectEducation(
                    npc.DerivedAge(),
                    min,
                    typical,
                    p.IqScore,
                    rng);

                // Never silently downgrade an already established education.
                if (CognitiveEducation.Rank(selected) >= CognitiveEducation.Rank(p.EducationLevel))
                    p.EducationLevel = selected;

                p.EducationText = CognitiveEducation.Label(p.EducationLevel);
                if (!string.IsNullOrWhiteSpace(fieldOfStudy))
                    p.FieldOfStudy = fieldOfStudy!.Trim();

                p.EducationSource =
                    min != EducationLevel.Unknown || typical != EducationLevel.Unknown
                        ? "job/life context"
                        : "generated";
            }

            p.LifeContextFinalized = true;

            RefreshDerived(npc, rng, preserveStableSpeechNoise: false);
            RefreshDomainKnowledge(npc);

            return p;
        }

        /// <summary>
        /// Recalculate context-driven values without rerolling IQ or education.
        /// Safe after trait changes, age changes, or job experience changes.
        /// </summary>
        public static void RefreshForCurrentLife(SimCharacter npc, Random? rng = null)
        {
            if (npc == null) return;
            rng ??= Random.Shared;
            EnsureGenerated(npc, rng);
            RefreshDerived(npc, rng, preserveStableSpeechNoise: true);
            RefreshDomainKnowledge(npc);
        }

        private static void GenerateBase(SimCharacter npc, Random rng)
        {
            var p = npc.Cognition ??= new CognitiveProfile();

            // Conventional IQ-style distribution: mean ~100, SD ~15.
            // Wide enough for real variation without filling a small town with extremes.
            p.IqScore = Math.Clamp(
                (int)Math.Round(100 + NextGaussian(rng) * 15),
                55,
                145);

            int baseAbility = IqToProjectScale(p.IqScore);

            p.VerbalReasoning = Clamp100(baseAbility + Noise(rng, 9));
            p.WorkingMemory = Clamp100(baseAbility + Noise(rng, 9));
            p.ProcessingSpeed = Clamp100(baseAbility + Noise(rng, 10));
            p.PracticalReasoning = Clamp100(baseAbility + Noise(rng, 10));

            p.EducationLevel = BaselineEducationForAge(npc.DerivedAge(), rng);
            p.EducationText = CognitiveEducation.Label(p.EducationLevel);
            p.EducationSource = "age/background generation";
            p.LifeContextFinalized = false;

            p.Speech ??= new SpeechProfile();
            p.GenerationVersion = CognitiveProfile.CurrentGenerationVersion;

            RefreshDerived(npc, rng, preserveStableSpeechNoise: false);
            RefreshDomainKnowledge(npc);
        }

        private static void RefreshDerived(
            SimCharacter npc,
            Random rng,
            bool preserveStableSpeechNoise)
        {
            var p = npc.Cognition;
            int age = Math.Max(0, npc.DerivedAge());
            int eduRank = CognitiveEducation.Rank(p.EducationLevel);
            int baseAbility = IqToProjectScale(p.IqScore);

            p.CognitiveMaturity = MaturityForAge(age);

            // Processing speed peaks in young adulthood, then declines gradually.
            int speedAge = age switch
            {
                < 13 => -12,
                < 16 => -6,
                < 20 => 1,
                <= 35 => 5,
                <= 45 => 2,
                <= 55 => -2,
                <= 65 => -7,
                <= 75 => -13,
                _ => -20
            };

            // Practical reasoning grows with accumulated life experience.
            int practicalAge = age switch
            {
                < 16 => -14,
                < 20 => -7,
                < 25 => 0,
                < 35 => 7,
                < 50 => 12,
                < 65 => 15,
                _ => 12
            };

            // Preserve generated individual variation but gently anchor to IQ.
            p.VerbalReasoning = Blend(p.VerbalReasoning, baseAbility, 0.18);
            p.WorkingMemory = Blend(p.WorkingMemory, baseAbility, 0.15);
            p.ProcessingSpeed = Blend(
                p.ProcessingSpeed,
                Clamp100(baseAbility + speedAge),
                0.20);
            p.PracticalReasoning = Blend(
                p.PracticalReasoning,
                Clamp100(baseAbility + practicalAge),
                0.20);

            float openness = Trait(npc, "trait.openness", 50f);
            float playfulness = Trait(npc, "trait.playfulness", 50f);
            float pride = Trait(npc, "trait.pride", 50f);
            float guard = Trait(npc, "trait.guard", 50f);
            float patience = Trait(npc, "trait.patience", 50f);
            float anger = Trait(npc, "trait.anger", 50f);
            float midConfront = Trait(npc, "mid.confrontational", 50f);

            int ageReading = Math.Clamp((age - 12) / 2, 0, 25);
            int educationReading = eduRank * 5;
            int opennessReading = (int)Math.Round((openness - 50f) * 0.18f);

            int targetReading = Clamp100(
                28 + ageReading + educationReading + opennessReading +
                (p.VerbalReasoning - 50) / 3);

            p.ReadingExposure = Blend(p.ReadingExposure, targetReading, 0.35);

            int targetVocabulary = Clamp100(
                (int)Math.Round(
                    p.VerbalReasoning * 0.42 +
                    p.ReadingExposure * 0.28 +
                    Math.Min(100, 25 + eduRank * 10) * 0.20 +
                    p.CognitiveMaturity * 0.10));

            p.Vocabulary = Blend(p.Vocabulary, targetVocabulary, 0.45);

            p.Speech ??= new SpeechProfile();
            var s = p.Speech;

            int jobSocial = npc.Job?.SocialDemand ?? 50;
            int jobCog = npc.Job?.CognitiveDemand ?? 50;

            int complexity = Clamp100(
                (int)Math.Round(
                    p.VerbalReasoning * 0.38 +
                    p.Vocabulary * 0.34 +
                    baseAbility * 0.13 +
                    Math.Min(100, 25 + eduRank * 10) * 0.15));

            int formality = Clamp100(
                28 +
                eduRank * 6 +
                jobSocial / 6 +
                jobCog / 10 +
                (int)Math.Round((pride - 50f) * 0.08f));

            int slang = Clamp100(
                62 -
                formality / 3 +
                SlangAgeAdjustment(age) +
                (int)Math.Round((playfulness - 50f) * 0.18f) -
                (int)Math.Round((guard - 50f) * 0.05f));

            int verbosity = Clamp100(
                48 +
                (p.VerbalReasoning - 50) / 4 +
                (int)Math.Round((openness - 50f) * 0.20f) +
                (int)Math.Round((patience - 50f) * 0.10f));

            int profanity = Clamp100(
                30 +
                (int)Math.Round((anger - 50f) * 0.15f) +
                (int)Math.Round((midConfront - 50f) * 0.17f) +
                SlangAgeAdjustment(age) / 3);

            int codeSwitch = Clamp100(
                30 +
                p.VerbalReasoning / 4 +
                p.CognitiveMaturity / 5 +
                jobSocial / 5 +
                eduRank * 3);

            // Tiny stable-ish individuality. Avoid changing voice every refresh.
            int n1 = preserveStableSpeechNoise ? 0 : Noise(rng, 5);
            int n2 = preserveStableSpeechNoise ? 0 : Noise(rng, 5);
            int n3 = preserveStableSpeechNoise ? 0 : Noise(rng, 7);
            int n4 = preserveStableSpeechNoise ? 0 : Noise(rng, 5);

            s.SentenceComplexity = Blend(s.SentenceComplexity, Clamp100(complexity + n1), 0.45);
            s.VocabularyUse = Blend(s.VocabularyUse, Clamp100(p.Vocabulary + n2), 0.45);
            s.Formality = Blend(s.Formality, Clamp100(formality + n3), 0.40);
            s.SlangUse = Blend(s.SlangUse, Clamp100(slang - n3 / 2), 0.40);
            s.Verbosity = Blend(s.Verbosity, Clamp100(verbosity + n4), 0.35);
            s.ProfanityUse = Blend(s.ProfanityUse, profanity, 0.30);
            s.CodeSwitching = Blend(s.CodeSwitching, codeSwitch, 0.40);
            s.RegionalStyle = RegionalStyle(npc.Location, npc.Hometown);
        }

        public static void RefreshDomainKnowledge(SimCharacter npc)
        {
            if (npc?.Cognition == null) return;

            var p = npc.Cognition;
            int age = Math.Max(0, npc.DerivedAge());
            double yearsJob = 0;

            try
            {
                if (npc.Job != null)
                    yearsJob = Math.Max(
                        npc.Job.DaysWorked / 365.25,
                        Math.Max(0, (DateTime.Now - npc.Job.HireDate).TotalDays / 365.25));
            }
            catch { }

            int experience = Math.Clamp((int)Math.Round(yearsJob * 7), 0, 45);
            int maturity = p.CognitiveMaturity;
            int practical = p.PracticalReasoning;
            int jobCog = npc.Job?.CognitiveDemand ?? 40;

            int jobKnowledge = Clamp100(
                30 +
                experience +
                practical / 5 +
                jobCog / 6);

            foreach (var domain in InferDomains(npc))
                p.SetKnowledge(domain, Math.Max(p.GetKnowledge(domain), jobKnowledge));

            if (!string.IsNullOrWhiteSpace(p.FieldOfStudy))
            {
                string field = Slug(p.FieldOfStudy);
                int fieldValue = Clamp100(
                    45 +
                    CognitiveEducation.Rank(p.EducationLevel) * 6 +
                    p.VerbalReasoning / 8);
                p.SetKnowledge(field, Math.Max(p.GetKnowledge(field), fieldValue));
            }

            // General life/practical knowledge rises with age but is never "knows everything."
            p.SetKnowledge(
                "general_life",
                Math.Max(
                    p.GetKnowledge("general_life"),
                    Clamp100(20 + maturity / 2 + Math.Min(age, 60) / 3)));
        }

        private static IEnumerable<string> InferDomains(SimCharacter npc)
        {
            string s = string.Join(" ",
                npc.Occupation ?? "",
                npc.Job?.JobName ?? "",
                npc.Job?.IndustryPath ?? "",
                npc.Job?.Department ?? "").ToLowerInvariant();

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddIf(string domain, params string[] words)
            {
                if (words.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    result.Add(domain);
            }

            AddIf("food_service", "barista", "restaurant", "server", "cook", "chef", "cafe", "coffee");
            AddIf("retail", "retail", "cashier", "grocery", "store clerk", "sales associate");
            AddIf("fire_service", "firefighter", "fire department");
            AddIf("emergency_medical", "emt", "paramedic", "ems");
            AddIf("law_enforcement", "police", "sheriff", "deputy", "detective");
            AddIf("healthcare", "nurse", "doctor", "physician", "hospital", "medical", "clinic");
            AddIf("nursing", "nurse", "rn", "lpn");
            AddIf("education", "teacher", "school", "professor", "tutor");
            AddIf("law", "lawyer", "attorney", "paralegal", "legal");
            AddIf("finance", "accountant", "bank", "finance", "investment", "bookkeeper");
            AddIf("auto_repair", "mechanic", "automotive", "auto repair", "garage");
            AddIf("construction", "construction", "carpenter", "electrician", "plumber", "hvac");
            AddIf("manufacturing", "factory", "manufacturing", "machine operator", "warehouse");
            AddIf("graphic_design", "graphic design", "designer", "creative");
            AddIf("technology", "software", "developer", "programmer", "it ", "technology");
            AddIf("management", "manager", "supervisor", "director", "owner");

            if (result.Count == 0 && !string.IsNullOrWhiteSpace(npc.Occupation))
                result.Add("job_" + Slug(npc.Occupation));

            return result;
        }

        private static (EducationLevel Minimum, EducationLevel Typical, string Field)
            InferEducationForOccupation(string? occupation, string? jobName)
        {
            string s = ((occupation ?? "") + " " + (jobName ?? "")).ToLowerInvariant();

            if (ContainsAny(s, "doctor", "physician", "surgeon"))
                return (EducationLevel.ProfessionalDegree, EducationLevel.ProfessionalDegree, "medicine");

            if (ContainsAny(s, "lawyer", "attorney"))
                return (EducationLevel.ProfessionalDegree, EducationLevel.ProfessionalDegree, "law");

            if (ContainsAny(s, "psychologist"))
                return (EducationLevel.Master, EducationLevel.Doctorate, "psychology");

            if (ContainsAny(s, "engineer"))
                return (EducationLevel.Bachelor, EducationLevel.Bachelor, "engineering");

            if (ContainsAny(s, "teacher"))
                return (EducationLevel.Bachelor, EducationLevel.Bachelor, "education");

            if (ContainsAny(s, "registered nurse", " rn", "nurse"))
                return (EducationLevel.Associate, EducationLevel.Bachelor, "nursing");

            if (ContainsAny(s, "accountant", "financial analyst"))
                return (EducationLevel.Bachelor, EducationLevel.Bachelor, "finance/accounting");

            if (ContainsAny(s, "paralegal"))
                return (EducationLevel.Associate, EducationLevel.Bachelor, "legal studies");

            if (ContainsAny(s, "graphic designer", "designer"))
                return (EducationLevel.SomeCollege, EducationLevel.Bachelor, "graphic design");

            if (ContainsAny(s, "electrician", "plumber", "hvac", "welder", "mechanic"))
                return (EducationLevel.HighSchool, EducationLevel.TradeCertificate, "skilled trade");

            if (ContainsAny(s, "paramedic"))
                return (EducationLevel.HighSchool, EducationLevel.TradeCertificate, "emergency medical services");

            if (ContainsAny(s, "emt", "firefighter", "police", "deputy"))
                return (EducationLevel.HighSchool, EducationLevel.SomeCollege, "");

            if (ContainsAny(s, "manager", "supervisor"))
                return (EducationLevel.HighSchool, EducationLevel.SomeCollege, "management");

            if (ContainsAny(s, "barista", "cashier", "server", "retail", "warehouse", "laborer"))
                return (EducationLevel.Unknown, EducationLevel.HighSchool, "");

            return (EducationLevel.Unknown, EducationLevel.HighSchool, "");
        }

        private static EducationLevel SelectEducation(
            int age,
            EducationLevel minimum,
            EducationLevel typical,
            int iq,
            Random rng)
        {
            var maxByAge = MaxEducationByAge(age);

            if (typical == EducationLevel.Unknown)
                typical = BaselineEducationForAge(age, rng);

            if (minimum != EducationLevel.Unknown &&
                CognitiveEducation.Rank(typical) < CognitiveEducation.Rank(minimum))
                typical = minimum;

            EducationLevel selected = typical;

            // Controlled variation around "typical" for jobs where multiple routes are realistic.
            int roll = rng.Next(100);
            if (minimum != EducationLevel.Unknown && roll < 22)
                selected = minimum;
            else if (roll > 88)
                selected = StepEducation(selected, +1);

            // Higher raw reasoning slightly raises probability of academic continuation,
            // but it never guarantees education (money, family, goals, opportunity matter).
            if (iq >= 120 && rng.NextDouble() < 0.18)
                selected = StepEducation(selected, +1);

            if (iq <= 80 && rng.NextDouble() < 0.12 &&
                CognitiveEducation.Rank(selected) > CognitiveEducation.Rank(minimum))
                selected = StepEducation(selected, -1);

            // Hard realism gate by age.
            if (CognitiveEducation.Rank(selected) > CognitiveEducation.Rank(maxByAge))
                selected = maxByAge;

            // If the requested job requires education impossible for this age, do not
            // invent an impossible degree. The character/job validator can catch it later.
            return selected;
        }

        private static EducationLevel BaselineEducationForAge(int age, Random rng)
        {
            if (age < 6) return EducationLevel.Unknown;
            if (age < 14) return EducationLevel.GradeSchool;
            if (age < 18) return EducationLevel.SomeHighSchool;

            if (age == 18)
                return rng.NextDouble() < 0.12 ? EducationLevel.Ged : EducationLevel.HighSchool;

            if (age <= 20)
            {
                double r = rng.NextDouble();
                if (r < 0.18) return EducationLevel.TradeCertificate;
                if (r < 0.58) return EducationLevel.SomeCollege;
                return EducationLevel.HighSchool;
            }

            if (age <= 21)
            {
                double r = rng.NextDouble();
                if (r < 0.20) return EducationLevel.Associate;
                if (r < 0.55) return EducationLevel.SomeCollege;
                if (r < 0.72) return EducationLevel.TradeCertificate;
                return EducationLevel.HighSchool;
            }

            double x = rng.NextDouble();
            if (x < 0.12) return EducationLevel.TradeCertificate;
            if (x < 0.27) return EducationLevel.SomeCollege;
            if (x < 0.42) return EducationLevel.Associate;
            if (x < 0.70) return EducationLevel.Bachelor;
            if (age >= 24 && x < 0.78) return EducationLevel.Master;
            return EducationLevel.HighSchool;
        }

        private static EducationLevel MaxEducationByAge(int age)
        {
            if (age < 14) return EducationLevel.GradeSchool;
            if (age < 18) return EducationLevel.SomeHighSchool;
            if (age < 20) return EducationLevel.SomeCollege;
            if (age < 21) return EducationLevel.TradeCertificate;
            if (age < 22) return EducationLevel.Associate;
            if (age < 24) return EducationLevel.Bachelor;
            if (age < 25) return EducationLevel.Master;
            if (age < 27) return EducationLevel.ProfessionalDegree;
            return EducationLevel.Doctorate;
        }

        private static EducationLevel StepEducation(EducationLevel level, int direction)
        {
            // Use rank-like academic progression while keeping GED/trade as parallel routes.
            EducationLevel[] ladder =
            {
                EducationLevel.SomeHighSchool,
                EducationLevel.HighSchool,
                EducationLevel.SomeCollege,
                EducationLevel.Associate,
                EducationLevel.Bachelor,
                EducationLevel.Master,
                EducationLevel.Doctorate
            };

            int idx = Array.IndexOf(ladder, level);
            if (idx < 0)
            {
                if (level == EducationLevel.Ged) idx = 1;
                else if (level == EducationLevel.TradeCertificate) idx = 2;
                else if (level == EducationLevel.ProfessionalDegree) return level;
                else idx = 1;
            }

            idx = Math.Clamp(idx + direction, 0, ladder.Length - 1);
            return ladder[idx];
        }

        private static int MaturityForAge(int age)
        {
            if (age <= 10) return Math.Clamp(age * 3, 5, 30);
            if (age <= 14) return 30 + (age - 10) * 4;
            if (age <= 18) return 46 + (age - 14) * 4;
            if (age <= 25) return 62 + (age - 18) * 3;
            if (age <= 35) return 83 + (age - 25);
            if (age <= 65) return 93;
            if (age <= 80) return Math.Max(78, 93 - (age - 65));
            return Math.Max(65, 78 - (age - 80));
        }

        private static int SlangAgeAdjustment(int age) => age switch
        {
            < 13 => 8,
            < 18 => 15,
            < 26 => 12,
            < 40 => 5,
            < 60 => 0,
            _ => -6
        };

        private static int IqToProjectScale(int iq)
            => Clamp100((int)Math.Round(50 + (iq - 100) * 1.15));

        private static int Noise(Random rng, double sd)
            => (int)Math.Round(NextGaussian(rng) * sd);

        private static double NextGaussian(Random rng)
        {
            // Box-Muller.
            double u1 = Math.Max(1e-12, 1.0 - rng.NextDouble());
            double u2 = Math.Max(1e-12, 1.0 - rng.NextDouble());
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static int Blend(int current, int target, double weight)
        {
            if (current <= 0) return Clamp100(target);
            return Clamp100((int)Math.Round(current * (1.0 - weight) + target * weight));
        }

        private static float Trait(SimCharacter npc, string id, float fallback)
        {
            try
            {
                if (npc.Traits == null) return fallback;
                return npc.Traits.Get(id);
            }
            catch { return fallback; }
        }

        private static string RegionalStyle(string? location, string? hometown)
        {
            string s = ((location ?? "") + " " + (hometown ?? "")).ToLowerInvariant();

            if (s.Contains("ohio") || s.Contains("bellefontaine") || s.Contains("sidney"))
                return "West-central Ohio / general Midwestern American";

            if (!string.IsNullOrWhiteSpace(hometown))
                return $"speech influenced by {hometown}";

            if (!string.IsNullOrWhiteSpace(location))
                return $"local speech influenced by {location}";

            return "General American";
        }

        private static string Slug(string value)
        {
            string s = Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_");
            return s.Trim('_');
        }

        private static bool ContainsAny(string text, params string[] words)
            => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

        private static int Clamp100(int v) => Math.Clamp(v, 0, 100);
    }
}
