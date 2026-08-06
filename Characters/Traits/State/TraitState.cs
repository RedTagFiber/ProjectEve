namespace Project_Eve.Characters.Traits.State
{
    public class TraitState
    {
        public int Id { get; set; }
        public string CharacterName { get; set; } = "";
        public string TraitName { get; set; } = "";
        public int Intensity { get; set; }
        public int Control { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
