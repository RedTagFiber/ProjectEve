namespace ProjectEve.Characters.Traits.Core
{
    /// <summary>
    /// Trait represents a simple, static personality tag.
    ///
    /// IMPORTANT:
    /// - These traits come from JSON or your character factory.
    /// - They describe the character's general personality.
    /// - They do NOT change during gameplay.
    ///
    /// Examples:
    /// - "Friendly"
    /// - "Curious"
    /// - "Organized"
    /// - "Impulsive"
    ///
    /// These are lightweight descriptors compared to SpecialTraits.
    /// </summary>
    public class Trait
    {
        /// <summary>
        /// The name of the trait.
        /// Example: "Friendly", "Calm", "Adventurous"
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The category of the trait.
        /// Default is "General", but you can use:
        /// - Emotional
        /// - Social
        /// - Cognitive
        /// - Behavioral
        ///
        /// Categories help you organize traits logically.
        /// </summary>
        public string Category { get; set; } = "General";

        /// <summary>
        /// A short description explaining what the trait means.
        /// This helps you understand how the trait should influence the character.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Constructor for creating a Trait.
        /// Lets you set all values at once.
        ///
        /// category defaults to "General"
        /// description defaults to empty
        /// </summary>
        public Trait(string name, string category = "General", string description = "")
        {
            Name = name;
            Category = category;
            Description = description;
        }
    }
}
