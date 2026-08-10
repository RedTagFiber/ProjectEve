using System;

namespace ProjectEve.Characters.Traits.State
{
    public class TraitState
    {
        public int Id { get; set; }
        public int NpcId { get; set; }
        public string CharacterName { get; set; } = "";
        public string TraitId { get; set; } = "";

        public string TraitName
        {
            get => TraitId;
            set => TraitId = value ?? "";
        }

        public int Intensity { get; set; }
        public int Control { get; set; } = 50;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}