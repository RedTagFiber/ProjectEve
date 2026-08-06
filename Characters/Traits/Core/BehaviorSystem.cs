namespace ProjectEve.Characters.Traits.Core;

using ProjectEve.Characters.Base;
using System;

public static class BehaviorSystem
{
    public static float CalculateBehaviorScore(SimCharacter npc, string behaviorName)
    {
        if (npc == null || string.IsNullOrWhiteSpace(behaviorName))
            return 0f;

        var behavior = BehaviorRegistry.GetBehavior(behaviorName);
        if (behavior == null)
            return 0f;

        float score = 0f;

        foreach (var pair in behavior.TraitWeights)
        {
            string traitId = pair.Key;
            float weight = pair.Value;

            float traitValue = npc.GetTraitValue(traitId);
            score += traitValue * weight;
        }

        // Keep in a usable range
        score = Math.Clamp(score, 0f, 100f);

        return score;
    }
}