namespace ProjectEve.Characters.Traits.Core
{
    /// <summary>
    /// Holds all traits assigned to a character.
    /// Traits are static and do not change during gameplay.
    /// </summary>
    public class TraitProfile
    {
        /// <summary>
        /// A list of traits the character possesses.
        /// Example: Friendly, Curious, Impulsive
        /// </summary>
        public List<Trait> Traits { get; set; } = new();

        /// <summary>
        /// Adds a trait to the profile.
        /// </summary>
        public void AddTrait(Trait trait)
        {
            Traits.Add(trait);
        }

        /// <summary>
        /// Checks if the character has a specific trait.
        /// </summary>
        public bool HasTrait(string traitName)
        {
            return Traits.Any(t => t.Name.Equals(traitName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
