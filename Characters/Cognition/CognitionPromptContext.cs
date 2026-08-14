using ProjectEve.Characters.Base;
using System;
using System.Linq;
using System.Text;

namespace ProjectEve.Characters.Cognition
{
    /// <summary>
    /// Converts the raw cognitive profile into useful behavior guidance for LLM prompts.
    /// Normally DO NOT feed the raw IQ number to dialogue models; use behavioral bands.
    /// </summary>
    public static class CognitionPromptContext
    {
        public static string BuildForNpc(SimCharacter? npc)
        {
            if (npc?.Cognition == null || !npc.Cognition.IsGenerated)
                return "No established cognitive profile.";

            var p = npc.Cognition;
            var sb = new StringBuilder();

            sb.AppendLine("COGNITIVE / COMMUNICATION PROFILE");
            sb.AppendLine($"- Reasoning capacity: {BandIq(p.IqScore)}");
            sb.AppendLine($"- Verbal reasoning: {Band(p.VerbalReasoning)}");
            sb.AppendLine($"- Working memory: {Band(p.WorkingMemory)}");
            sb.AppendLine($"- Processing speed: {Band(p.ProcessingSpeed)}");
            sb.AppendLine($"- Practical reasoning: {Band(p.PracticalReasoning)}");
            sb.AppendLine($"- Vocabulary depth: {Band(p.Vocabulary)}");
            sb.AppendLine($"- Education: {p.EducationLabel()}" +
                          (string.IsNullOrWhiteSpace(p.FieldOfStudy) ? "" : $" ({p.FieldOfStudy})"));

            var s = p.Speech ?? new SpeechProfile();
            sb.AppendLine(
                $"- Speech tendencies: complexity {Band(s.SentenceComplexity)}, " +
                $"vocabulary use {Band(s.VocabularyUse)}, slang {Band(s.SlangUse)}, " +
                $"formality {Band(s.Formality)}, verbosity {Band(s.Verbosity)}, " +
                $"code-switching {Band(s.CodeSwitching)}");
            sb.AppendLine($"- Regional style: {s.RegionalStyle}");

            var strong = p.DomainKnowledge
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Where(kv => kv.Value >= 35)
                .ToList();

            if (strong.Count > 0)
                sb.AppendLine("- Stronger knowledge: " +
                    string.Join(", ", strong.Select(kv => $"{kv.Key} ({Band(kv.Value)})")));

            // Current state can temporarily lower effective cognition without changing IQ.
            try
            {
                if (npc.Brain != null)
                {
                    double stress = Math.Clamp(npc.Brain.Stress, 0f, 1f);
                    double energy = Math.Clamp(npc.Brain.Energy, 0f, 1f);
                    int effectiveWorking = EffectiveWorkingMemory(p.WorkingMemory, stress, energy);

                    if (effectiveWorking <= p.WorkingMemory - 8)
                        sb.AppendLine(
                            $"- Current cognitive load: working memory is temporarily reduced " +
                            $"({Band(effectiveWorking)}) by stress/fatigue.");
                }
            }
            catch { }

            sb.AppendLine("- Intelligence does not grant knowledge the character has not learned.");
            sb.AppendLine("- Do not caricature intelligence. Education, domain experience, age, emotion, and relationship context all affect speech.");

            return sb.ToString();
        }

        public static int EffectiveWorkingMemory(
            int baseline,
            double stress01,
            double energy01)
        {
            stress01 = Math.Clamp(stress01, 0, 1);
            energy01 = Math.Clamp(energy01, 0, 1);

            double stressPenalty = Math.Max(0, stress01 - 0.45) * 30.0;
            double fatiguePenalty = Math.Max(0, 0.45 - energy01) * 35.0;

            return Math.Clamp(
                (int)Math.Round(baseline - stressPenalty - fatiguePenalty),
                0,
                100);
        }

        public static string Band(int v) => v switch
        {
            >= 85 => "very strong",
            >= 70 => "strong",
            >= 58 => "above average",
            >= 43 => "average",
            >= 30 => "below average",
            >= 15 => "weak",
            _ => "very weak"
        };

        public static string BandIq(int iq) => iq switch
        {
            >= 130 => "very high",
            >= 116 => "high",
            >= 108 => "above average",
            >= 93 => "average",
            >= 85 => "below average",
            >= 70 => "low",
            _ => "very low"
        };
    }
}
