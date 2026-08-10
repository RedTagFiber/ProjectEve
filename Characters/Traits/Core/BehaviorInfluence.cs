using System.Collections.Generic;

namespace ProjectEve.Characters.Traits.Core
{
    public class BehaviorInfluence
    {
        public string BehaviorName { get; set; } = "";
        public Dictionary<string, float> TraitWeights { get; set; } = new();
    }
}