using ProjectEve.Traits;
using System;

namespace ProjectEve.Characters.Personality
{
    /// <summary>
    /// Builds PersonalityProfile from live NpcTraits (Mid character + Fast surface).
    /// No independent random personality engine.
    /// </summary>
    public static class PersonalityGenerator
    {
        /// <summary>
        /// Preferred: derive from trait bag.
        /// </summary>
        public static PersonalityProfile FromTraits(NpcTraits? traits)
        {
            var p = new PersonalityProfile();
            if (traits == null)
                return p;

            float T(string id) => traits.Get(id);

            // Surface from Fast + Mid (mid.* may be missing → Get returns ~50)
            p.Warmth = Round(
                T("trait.affection") * 0.45f +
                T("trait.openness") * 0.25f +
                T("mid.loyal") * 0.15f +
                T("mid.people_pleasing") * 0.15f);

            p.Assertiveness = Round(
                T("trait.pride") * 0.35f +
                (100f - T("trait.guard")) * 0.20f +
                T("mid.confrontational") * 0.25f +
                T("trait.anger") * 0.20f);

            p.Humor = Round(
                T("trait.playfulness") * 0.70f +
                T("trait.hope") * 0.15f +
                (100f - T("trait.hurt")) * 0.15f);

            p.Sarcasm = Round(
                T("trait.resentment") * 0.35f +
                T("trait.pride") * 0.25f +
                T("trait.playfulness") * 0.20f +
                T("mid.guarded") * 0.20f);

            p.Shyness = Round(
                T("trait.guard") * 0.35f +
                T("trait.anxiety") * 0.30f +
                T("trait.shame") * 0.20f +
                (100f - T("trait.openness")) * 0.15f);

            p.Boldness = Round(
                T("trait.openness") * 0.25f +
                T("trait.desire") * 0.20f +
                T("trait.pride") * 0.25f +
                (100f - T("trait.fear")) * 0.15f +
                (100f - T("trait.anxiety")) * 0.15f);

            p.Type = DetermineType(p);
            return p;
        }

        /// <summary>
        /// Legacy no-arg: neutral defaults only (do not use for real NPCs).
        /// </summary>
        public static PersonalityProfile Generate()
        {
            return new PersonalityProfile
            {
                Type = PersonalityType.Neutral,
                Warmth = 50,
                Assertiveness = 50,
                Humor = 50,
                Sarcasm = 50,
                Shyness = 50,
                Boldness = 50
            };
        }

        private static PersonalityType DetermineType(PersonalityProfile p)
        {
            // Strongest signal wins
            if (p.Shyness >= 72 && p.Boldness < 55) return PersonalityType.Anxious;
            if (p.Warmth >= 72 && p.Sarcasm < 55) return PersonalityType.Warm;
            if (p.Humor >= 70 && p.Warmth >= 50) return PersonalityType.Playful;
            if (p.Sarcasm >= 70) return PersonalityType.Sharp;
            if (p.Boldness >= 72) return PersonalityType.Bold;
            if (p.Assertiveness >= 70 && p.Humor < 50) return PersonalityType.Serious;
            if (p.Shyness >= 65) return PersonalityType.Guarded;
            if (p.Warmth >= 55 && p.Assertiveness < 45) return PersonalityType.Soft;
            if (Math.Abs(p.Warmth - 50) < 12 && Math.Abs(p.Humor - 50) < 12)
                return PersonalityType.Steady;

            return PersonalityType.Neutral;
        }

        private static int Round(float v)
            => Math.Clamp((int)Math.Round(v), 0, 100);
    }
}