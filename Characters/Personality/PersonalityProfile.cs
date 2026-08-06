namespace Project_Eve.Characters.Personality
{
    /// <summary>
    /// PersonalityProfile defines the character's stable social behavior.
    /// Unlike EmotionalProfile, these values rarely change.
    /// </summary>
    public class PersonalityProfile
    {
        // Broad personality category
        public PersonalityType Type { get; set; } = PersonalityType.Neutral;

        // Social traits (0–100)
        public int Warmth { get; set; } = 50;
        public int Assertiveness { get; set; } = 50;
        public int Humor { get; set; } = 50;
        public int Sarcasm { get; set; } = 50;
        public int Shyness { get; set; } = 50;
        public int Boldness { get; set; } = 50;

        // Future expansion:
        // - Openness
        // - Agreeableness
        // - Conscientiousness
        // - Creativity
    }
}
