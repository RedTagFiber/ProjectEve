namespace Project_Eve.Characters.Traits.Special
{
    /// <summary>
    /// SpecialTrait represents a deeper personality trait.
    /// 
    /// IMPORTANT:
    /// - These traits come from JSON or your character factory.
    /// - They describe WHO the character is at a deeper level.
    /// - They do NOT change during gameplay (static definition).
    /// 
    /// Example special traits:
    /// - "Empath"
    /// - "Workaholic"
    /// - "Deep Thinker"
    /// - "Social Echo"
    /// 
    /// Their *effects* on emotions and relationships can be modified
    /// by TraitState (Intensity + Control), but the trait itself stays the same.
    /// </summary>
    public class SpecialTrait
    {
        /// <summary>
        /// The name of the trait.
        /// Example: "Empath", "JealousMind", "CalmFocus"
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Category of the trait.
        /// Default is "Identity", but you can use:
        /// - Emotional
        /// - Social
        /// - Cognitive
        /// - Behavioral
        /// - Trauma
        /// </summary>
        public string Category { get; set; } = "Identity";

        /// <summary>
        /// A short description explaining what the trait means.
        /// This helps you understand how the trait should behave.
        /// </summary>
        public string Description { get; set; } = "";

        // ============================================================
        // OPTIONAL EMOTIONAL / RELATIONSHIP MODIFIERS
        // ============================================================
        // These bonuses are applied when the character has this trait.
        // They give small boosts to emotional or relationship stats.
        //
        // Example:
        // - An "Empath" might get +5 ComfortBonus
        // - A "Romantic" might get +10 AttractionBonus
        // - A "Loyal" person might get +8 TrustBonus
        //
        // These are static modifiers — they do NOT change during gameplay.
        // TraitState (Intensity + Control) will later modify how strong
        // these bonuses actually feel.
        // ============================================================

        public int ComfortBonus { get; set; } = 0;
        public int TrustBonus { get; set; } = 0;
        public int AffectionBonus { get; set; } = 0;
        public int AttractionBonus { get; set; } = 0;

        /// <summary>
        /// Constructor for creating a SpecialTrait.
        /// Lets you set all values at once.
        /// </summary>
        public SpecialTrait(
            string name,
            string category = "Identity",
            string description = "",
            int comfortBonus = 0,
            int trustBonus = 0,
            int affectionBonus = 0,
            int attractionBonus = 0)
        {
            Name = name;
            Category = category;
            Description = description;
            ComfortBonus = comfortBonus;
            TrustBonus = trustBonus;
            AffectionBonus = affectionBonus;
            AttractionBonus = attractionBonus;
        }
    }
}
