namespace Project_Eve.Characters.Traits.History
{
    /// <summary>
    /// HistoryEvent represents something important that happened
    /// in a character's past.
    ///
    /// IMPORTANT:
    /// - These events help shape the character's personality.
    /// - They can later influence TraitState (Intensity + Control).
    /// - They can also influence memories, emotions, and relationships.
    ///
    /// Examples:
    /// - "First Job"
    /// - "Lost a Friend"
    /// - "Graduated School"
    /// - "Moved to a New City"
    /// </summary>
    public class HistoryEvent
    {
        /// <summary>
        /// The name/title of the event.
        /// Example: "Parents Divorced", "Won a Competition"
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A short description explaining what happened.
        /// This helps you understand how the event might affect the character.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The age at which the event happened.
        /// This allows you to build a timeline of the character's life.
        ///
        /// Later, you can use this to:
        /// - calculate emotional impact
        /// - determine long-term vs short-term effects
        /// - apply age-based trait drift
        /// </summary>
        public int Age { get; set; }

        /// <summary>
        /// How important the event was (1–100).
        ///
        /// Higher importance = stronger psychological impact.
        ///
        /// Examples:
        /// - Importance 1: small event (new hobby)
        /// - Importance 20: meaningful event (first relationship)
        /// - Importance 80: major trauma (loss of loved one)
        /// </summary>
        public int Importance { get; set; } = 1;

        /// <summary>
        /// Constructor for creating a HistoryEvent.
        /// Lets you set all values at once.
        ///
        /// importance defaults to 1 if not provided.
        /// </summary>
        public HistoryEvent(string name, string description, int age, int importance = 1)
        {
            Name = name;
            Description = description;
            Age = age;
            Importance = importance;
        }
    }
}
