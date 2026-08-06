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
                    { "Anxiety Level", -0.4f },
                    { "Curiosity", 0.3f },
                    { "Stress Vulnerability", -0.2f },
                    { "Goal Persistence", 0.5f },
                    { "Optimism", 0.2f },
                    { "Pessimism", -0.2f }
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
                    { "Expressiveness", 0.5f },
                    { "Silence", -0.4f },
                    { "Humor", 0.3f },
                    { "Social Anxiety", -0.6f },
                    { "Empathic Accuracy", 0.4f }
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
                    { "Work Ethic", 0.6f },
                    { "Consistency", 0.5f },
                    { "Sloppiness", -0.5f },
                    { "Stress Vulnerability", -0.3f },
                    { "Perfectionism", 0.3f }
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
                    { "Stress Vulnerability", -0.6f },
                    { "Resilience", 0.5f },
                    { "Anxiety Level", -0.4f },
                    { "Emotional Stability", 0.4f }
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
                    { "Confrontation Tendency", 0.6f },
                    { "Diplomacy", 0.5f },
                    { "Sarcasm", -0.2f },
                    { "Trauma Sensitivity", -0.3f },
                    { "Resilience", 0.4f }
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
                    { "Player Attraction", 0.7f },
                    { "Player Trust", 0.5f },
                    { "Romantic Jealousy", 0.4f },
                    { "Anxiety Level", -0.3f },
                    { "Attachment Avoidance", -0.5f }
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
                    { "Routine Stability", 0.6f },
                    { "Flexibility", 0.2f },
                    { "Forgetfulness", -0.5f },
                    { "Sleep Quality", 0.3f },
                    { "Stress Impact on Body", -0.4f }
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
                    { "Risk Appetite", 0.7f },
                    { "Anxiety Level", -0.4f },
                    { "Confidence", 0.5f },
                    { "Impulsiveness", 0.3f }
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
                    { "Financial Discipline", 0.7f },
                    { "Impulsiveness", -0.4f },
                    { "Long-Term Planning", 0.5f },
                    { "Stress Vulnerability", -0.2f }
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
                    { "Health Awareness", 0.6f },
                    { "Routine Stability", 0.4f },
                    { "Stress Impact on Body", -0.5f },
                    { "Energy Level", 0.3f }
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
                    { "Motivation Level", 0.7f },
                    { "Goal Persistence", 0.6f },
                    { "Energy Level", 0.4f },
                    { "Stress Vulnerability", -0.3f }
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
                    { "Player Trust", 0.7f },
                    { "Affection", 0.5f },
                    { "Comfort", 0.4f },
                    { "Anxiety Level", -0.3f }
                }
            });
        }

        public static BehaviorInfluence? GetBehavior(string name)
        {
            return AllBehaviors.Find(b => b.BehaviorName == name);
        }
    }
}
