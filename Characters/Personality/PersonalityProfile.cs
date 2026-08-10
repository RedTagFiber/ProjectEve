namespace ProjectEve.Characters.Personality
{
    /// <summary>
    /// Stable social read for prompts/UI.
    /// Prefer DeriveFromTraits — values mirror Mid/Fast, not a parallel RNG.
    /// </summary>
    public class PersonalityProfile
    {
        public PersonalityType Type { get; set; } = PersonalityType.Neutral;

        // Social surface (0–100), filled from traits
        public int Warmth { get; set; } = 50;
        public int Assertiveness { get; set; } = 50;
        public int Humor { get; set; } = 50;
        public int Sarcasm { get; set; } = 50;
        public int Shyness { get; set; } = 50;
        public int Boldness { get; set; } = 50;

        public string SummaryLine()
            => $"{Type}: warmth {Warmth}, assert {Assertiveness}, humor {Humor}, " +
               $"sarcasm {Sarcasm}, shy {Shyness}, bold {Boldness}";
    }
}