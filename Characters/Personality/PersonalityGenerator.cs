using System;

namespace Project_Eve.Characters.Personality
{
    /// <summary>
    /// Generates personality profiles for NPCs.
    /// Later this will use:
    /// - history events
    /// - traits
    /// - psychology
    /// - upbringing
    /// </summary>
    public static class PersonalityGenerator
    {
        private static Random rng = new Random();

        public static PersonalityProfile Generate()
        {
            var profile = new PersonalityProfile();

            // Random baseline values
            profile.Warmth = rng.Next(20, 80);
            profile.Assertiveness = rng.Next(20, 80);
            profile.Humor = rng.Next(20, 80);
            profile.Sarcasm = rng.Next(20, 80);
            profile.Shyness = rng.Next(20, 80);
            profile.Boldness = rng.Next(20, 80);

            // Determine personality type
            profile.Type = DetermineType(profile);

            return profile;
        }

        private static PersonalityType DetermineType(PersonalityProfile p)
        {
            if (p.Shyness > 70) return PersonalityType.Shy;
            if (p.Boldness > 70) return PersonalityType.Bold;
            if (p.Warmth > 70) return PersonalityType.Warm;
            if (p.Sarcasm > 70) return PersonalityType.Sarcastic;
            if (p.Assertiveness > 70) return PersonalityType.Serious;
            if (p.Humor > 70) return PersonalityType.Playful;

            return PersonalityType.Neutral;
        }
    }
}
