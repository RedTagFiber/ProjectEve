using ProjectEve.Characters.Base;
using System;

namespace ProjectEve.Characters.Traits.Core
{
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
                float traitValue = npc.GetTraitValue(pair.Key);
                score += traitValue * pair.Value;
            }

            // Soft money pressure on money / stress / risk / work-ish behaviors
            try
            {
                if (npc.Money != null)
                {
                    int stressBias = npc.Money.StressBias();
                    int fundBias = npc.Money.DesireFundingBias();

                    switch (behaviorName)
                    {
                        case "MoneyManagement":
                            score += stressBias * 0.4f;
                            break;
                        case "RiskTaking":
                            score += fundBias * 0.35f;
                            score -= Math.Max(0, stressBias) * 0.2f;
                            break;
                        case "WorkPerformance":
                        case "MotivationDrive":
                            score += Math.Max(0, stressBias) * 0.25f;
                            break;
                        case "RomanticBehavior":
                            score += fundBias * 0.15f;
                            break;
                    }
                }
            }
            catch { }

            return Math.Clamp(score, 0f, 100f);
        }
    }
}