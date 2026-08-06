namespace ProjectEve.Characters.Traits.Core;

using System.Collections.Generic;

public class BehaviorInfluence
{
    public string BehaviorName { get; set; } = "";
    public Dictionary<string, float> TraitWeights { get; set; } = new();
}
