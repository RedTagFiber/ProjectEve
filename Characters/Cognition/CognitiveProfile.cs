using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Characters.Cognition
{
    /// <summary>
    /// Stable cognitive / education profile for one character.
    ///
    /// IMPORTANT PROJECT EVE RULE:
    /// IQ is only one broad reasoning baseline. It is NOT morality, wisdom,
    /// kindness, education, job skill, or "how good" a person is.
    ///
    /// Most sub-scores use Project Eve's familiar 0-100 style.
    /// IqScore uses a conventional IQ-style scale centered near 100.
    /// </summary>
    public sealed class CognitiveProfile
    {
        public const int CurrentGenerationVersion = 1;

        public int GenerationVersion { get; set; }

        // Broad reasoning baseline. Generated once and normally kept stable.
        public int IqScore { get; set; } = 100;

        // 0-100 cognitive expression / capacity dimensions.
        public int VerbalReasoning { get; set; } = 50;
        public int WorkingMemory { get; set; } = 50;
        public int ProcessingSpeed { get; set; } = 50;
        public int PracticalReasoning { get; set; } = 50;
        public int Vocabulary { get; set; } = 50;
        public int ReadingExposure { get; set; } = 50;
        public int CognitiveMaturity { get; set; } = 50;

        // Education is separate from IQ.
        public EducationLevel EducationLevel { get; set; } = EducationLevel.Unknown;
        public string EducationText { get; set; } = "";
        public string FieldOfStudy { get; set; } = "";
        public string EducationSource { get; set; } = "generated";

        /// <summary>
        /// False while a freshly generated NPC is waiting for job/life context.
        /// Once true, changing jobs must not magically grant a new degree.
        /// </summary>
        public bool LifeContextFinalized { get; set; }

        public SpeechProfile Speech { get; set; } = new();

        /// <summary>
        /// Expandable knowledge map, 0-100.
        /// Examples: food_service, nursing, auto_repair, graphic_design.
        /// </summary>
        public Dictionary<string, int> DomainKnowledge { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public bool IsGenerated =>
            GenerationVersion > 0 &&
            IqScore > 0;

        public int GetKnowledge(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return 0;
            return DomainKnowledge.TryGetValue(domain, out var v)
                ? Math.Clamp(v, 0, 100)
                : 0;
        }

        public void SetKnowledge(string domain, int value)
        {
            if (string.IsNullOrWhiteSpace(domain)) return;
            DomainKnowledge[domain.Trim()] = Math.Clamp(value, 0, 100);
        }

        public string EducationLabel()
            => !string.IsNullOrWhiteSpace(EducationText)
                ? EducationText
                : CognitiveEducation.Label(EducationLevel);

        public string StrongKnowledgeSummary(int max = 5)
        {
            if (DomainKnowledge.Count == 0)
                return "none established";

            return string.Join(", ",
                DomainKnowledge
                    .OrderByDescending(kv => kv.Value)
                    .Take(Math.Max(1, max))
                    .Select(kv => $"{kv.Key} {kv.Value}"));
        }
    }

    public sealed class SpeechProfile
    {
        // 0-100. These are tendencies, not hard restrictions.
        public int SentenceComplexity { get; set; } = 50;
        public int VocabularyUse { get; set; } = 50;
        public int SlangUse { get; set; } = 50;
        public int Formality { get; set; } = 50;
        public int Verbosity { get; set; } = 50;
        public int ProfanityUse { get; set; } = 35;
        public int CodeSwitching { get; set; } = 50;

        public string RegionalStyle { get; set; } = "General American";

        public string SummaryLine()
            => $"complexity {SentenceComplexity}, vocab {VocabularyUse}, slang {SlangUse}, " +
               $"formality {Formality}, verbosity {Verbosity}, profanity {ProfanityUse}, " +
               $"code-switch {CodeSwitching}, region {RegionalStyle}";
    }

    public enum EducationLevel
    {
        Unknown = 0,
        GradeSchool = 1,
        SomeHighSchool = 2,
        HighSchool = 3,
        Ged = 4,
        TradeCertificate = 5,
        SomeCollege = 6,
        Associate = 7,
        Bachelor = 8,
        Master = 9,
        Doctorate = 10,
        ProfessionalDegree = 11
    }

    public static class CognitiveEducation
    {
        public static int Rank(EducationLevel level) => level switch
        {
            EducationLevel.Unknown => 0,
            EducationLevel.GradeSchool => 1,
            EducationLevel.SomeHighSchool => 2,
            EducationLevel.HighSchool => 3,
            EducationLevel.Ged => 3,
            EducationLevel.TradeCertificate => 4,
            EducationLevel.SomeCollege => 4,
            EducationLevel.Associate => 5,
            EducationLevel.Bachelor => 6,
            EducationLevel.Master => 7,
            EducationLevel.Doctorate => 8,
            EducationLevel.ProfessionalDegree => 8,
            _ => 0
        };

        public static string Label(EducationLevel level) => level switch
        {
            EducationLevel.GradeSchool => "grade school",
            EducationLevel.SomeHighSchool => "some high school",
            EducationLevel.HighSchool => "high school diploma",
            EducationLevel.Ged => "GED",
            EducationLevel.TradeCertificate => "trade / technical certificate",
            EducationLevel.SomeCollege => "some college",
            EducationLevel.Associate => "associate degree",
            EducationLevel.Bachelor => "bachelor's degree",
            EducationLevel.Master => "master's degree",
            EducationLevel.Doctorate => "doctorate",
            EducationLevel.ProfessionalDegree => "professional degree",
            _ => "unknown"
        };

        public static EducationLevel Parse(string? value)
        {
            string s = (value ?? "")
                .Trim()
                .ToLowerInvariant()
                .Replace("-", "_")
                .Replace(" ", "_");

            return s switch
            {
                "" or "unknown" or "none" => EducationLevel.Unknown,
                "grade_school" or "elementary" => EducationLevel.GradeSchool,
                "some_high_school" => EducationLevel.SomeHighSchool,
                "high_school" or "high_school_diploma" or "hs" => EducationLevel.HighSchool,
                "ged" => EducationLevel.Ged,
                "trade" or "trade_school" or "trade_certificate" or "technical_certificate"
                    => EducationLevel.TradeCertificate,
                "some_college" => EducationLevel.SomeCollege,
                "associate" or "associate_degree" => EducationLevel.Associate,
                "bachelor" or "bachelors" or "bachelor_degree" or "ba" or "bs"
                    => EducationLevel.Bachelor,
                "master" or "masters" or "master_degree" or "ma" or "ms" or "mba"
                    => EducationLevel.Master,
                "doctorate" or "phd" => EducationLevel.Doctorate,
                "professional" or "professional_degree" or "jd" or "md" or "do"
                    => EducationLevel.ProfessionalDegree,
                _ => Enum.TryParse<EducationLevel>(value, true, out var parsed)
                    ? parsed
                    : EducationLevel.Unknown
            };
        }
    }
}
