using System.Collections.Generic;

namespace ProjectEve.Characters.Traits.Core
{
    public static class BehaviorRegistry
    {
        public static List<BehaviorInfluence> AllBehaviors { get; } = new();

        public static void Load()
        {
            AllBehaviors.Clear();

            // ============================================================
            // Decision Making
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "DecisionMaking",
                TraitWeights =
                {
                    { "trait.anxiety", -0.4f },
                    { "trait.hope", 0.25f },
                    { "trait.fear", -0.2f },
                    { "trait.pride", 0.15f },
                    { "mid.ambitious", 0.35f },
                    { "mid.dutiful", 0.25f },
                    { "mid.perfectionist", 0.15f },
                    { "mid.resilient", 0.2f }
                }
            });

            // ============================================================
            // Social Interaction
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "SocialInteraction",
                TraitWeights =
                {
                    { "trait.openness", 0.45f },
                    { "trait.guard", -0.4f },
                    { "trait.playfulness", 0.3f },
                    { "trait.anxiety", -0.35f },
                    { "trait.loneliness", 0.15f },
                    { "mid.people_pleasing", 0.25f },
                    { "mid.open_book", 0.3f },
                    { "mid.guarded", -0.35f },
                    { "mid.blunt", 0.1f }
                }
            });

            // ============================================================
            // Work Performance
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "WorkPerformance",
                TraitWeights =
                {
                    { "mid.dutiful", 0.5f },
                    { "mid.ambitious", 0.4f },
                    { "mid.perfectionist", 0.25f },
                    { "mid.resilient", 0.25f },
                    { "trait.anxiety", -0.2f },
                    { "trait.patience", 0.2f },
                    { "trait.pride", 0.15f },
                    { "slow.life.work_ambition", 0.3f }
                }
            });

            // ============================================================
            // Stress Response
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "StressResponse",
                TraitWeights =
                {
                    { "trait.anxiety", -0.5f },
                    { "trait.fear", -0.25f },
                    { "trait.patience", 0.3f },
                    { "trait.hope", 0.2f },
                    { "mid.resilient", 0.45f },
                    { "mid.self_critical", -0.2f },
                    { "mid.self_assured", 0.25f }
                }
            });

            // ============================================================
            // Conflict Handling
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "ConflictHandling",
                TraitWeights =
                {
                    { "trait.anger", 0.45f },
                    { "trait.patience", -0.25f },
                    { "trait.pride", 0.2f },
                    { "trait.tension", 0.2f },
                    { "mid.confrontational", 0.5f },
                    { "mid.peacemaker", 0.35f },
                    { "mid.conflict_avoidant", -0.4f },
                    { "mid.passive_aggressive", 0.15f },
                    { "mid.blunt", 0.25f }
                }
            });

            // ============================================================
            // Romantic Behavior
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "RomanticBehavior",
                TraitWeights =
                {
                    { "trait.affection", 0.5f },
                    { "trait.desire", 0.45f },
                    { "trait.attraction", 0.4f },
                    { "trait.trust", 0.35f },
                    { "trait.jealousy", 0.2f },
                    { "trait.anxiety", -0.25f },
                    { "trait.guard", -0.2f },
                    { "mid.anxious_attach", 0.15f },
                    { "mid.avoidant", -0.35f },
                    { "mid.loyal", 0.25f }
                }
            });

            // ============================================================
            // Daily Routine Stability
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "DailyRoutineStability",
                TraitWeights =
                {
                    { "mid.dutiful", 0.4f },
                    { "mid.content", 0.25f },
                    { "mid.restless", -0.35f },
                    { "trait.anxiety", -0.2f },
                    { "trait.patience", 0.25f },
                    { "trait.hope", 0.15f }
                }
            });

            // ============================================================
            // Risk Taking
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "RiskTaking",
                TraitWeights =
                {
                    { "trait.fear", -0.4f },
                    { "trait.anxiety", -0.3f },
                    { "trait.desire", 0.25f },
                    { "trait.hope", 0.15f },
                    { "trait.pride", 0.15f },
                    { "mid.ambitious", 0.3f },
                    { "mid.restless", 0.25f },
                    { "mid.principled", -0.1f }
                }
            });

            // ============================================================
            // Money Management
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "MoneyManagement",
                TraitWeights =
                {
                    { "mid.dutiful", 0.35f },
                    { "mid.ambitious", 0.2f },
                    { "mid.perfectionist", 0.15f },
                    { "trait.anxiety", 0.1f },
                    { "trait.desire", -0.25f },
                    { "trait.patience", 0.2f },
                    { "mid.opportunistic", -0.2f }
                }
            });

            // ============================================================
            // Health Maintenance
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "HealthMaintenance",
                TraitWeights =
                {
                    { "mid.dutiful", 0.3f },
                    { "mid.resilient", 0.25f },
                    { "trait.anxiety", -0.15f },
                    { "trait.hope", 0.15f },
                    { "trait.patience", 0.2f },
                    { "slow.life.fitness", 0.35f }
                }
            });

            // ============================================================
            // Motivation Drive
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "MotivationDrive",
                TraitWeights =
                {
                    { "trait.hope", 0.4f },
                    { "trait.pride", 0.25f },
                    { "trait.desire", 0.2f },
                    { "trait.anxiety", -0.2f },
                    { "mid.ambitious", 0.5f },
                    { "mid.dutiful", 0.3f },
                    { "mid.content", -0.25f },
                    { "mid.restless", 0.2f }
                }
            });

            // ============================================================
            // Player Interaction
            // ============================================================
            AllBehaviors.Add(new BehaviorInfluence
            {
                BehaviorName = "PlayerInteraction",
                TraitWeights =
                {
                    { "trait.trust", 0.55f },
                    { "trait.affection", 0.45f },
                    { "trait.openness", 0.3f },
                    { "trait.guard", -0.3f },
                    { "trait.anxiety", -0.25f },
                    { "trait.hurt", -0.15f },
                    { "trait.playfulness", 0.2f },
                    { "mid.loyal", 0.25f },
                    { "mid.avoidant", -0.3f },
                    { "mid.people_pleasing", 0.15f }
                }
            });
        }

        public static BehaviorInfluence? GetBehavior(string name)
        {
            return AllBehaviors.Find(b => b.BehaviorName == name);
        }
    }
}