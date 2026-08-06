using System;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Picks a high-level goal for any NPC from thought + real TraitRegistry ids.
    /// </summary>
    public static class GoalEngine
    {
        public static NPCGoal SelectGoal(Brain brain, string? thought)
        {
            thought = (thought ?? "").ToLowerInvariant();

            // Fast keyword path
            if (ContainsAny(thought, "romance", "kiss", "date", "love", "miss you", "want you"))
                return NPCGoal.SeekRomance;

            if (ContainsAny(thought, "conflict", "fight", "angry", "mad", "argue", "pissed"))
                return NPCGoal.ResolveConflict;

            if (ContainsAny(thought, "friend", "talk", "hang out", "check on", "text"))
                return NPCGoal.FindFriend;

            if (ContainsAny(thought, "avoid", "leave", "not now", "distance", "hide", "scared"))
                return NPCGoal.AvoidEnemy;

            if (ContainsAny(thought, "lonely", "sad", "empty", "better", "calm down"))
                return NPCGoal.ImproveMood;

            // Trait-weighted path (TraitRegistry ids)
            float romance =
                Trait(brain, "trait.sexualConfidence") * 0.25f +
                Trait(brain, "trait.confidence") * 0.15f +
                Trait(brain, "trait.extroversion") * 0.15f +
                Trait(brain, "trait.possessiveDesire") * 0.10f +
                brain.Attraction * 40f +
                brain.Affection * 25f;

            float fight =
                Trait(brain, "trait.anger") * 0.45f +
                Trait(brain, "trait.impulsiveness") * 0.25f +
                Trait(brain, "trait.dominance") * 0.15f +
                brain.Tension * 40f;

            float friendship =
                Trait(brain, "trait.empathy") * 0.30f +
                Trait(brain, "trait.extroversion") * 0.25f +
                Trait(brain, "trait.optimism") * 0.15f +
                brain.Affection * 30f +
                brain.Trust * 20f;

            float avoid =
                Trait(brain, "trait.anxiety") * 0.35f +
                Trait(brain, "trait.fearfulness") * 0.25f +
                Trait(brain, "trait.insecurity") * 0.15f +
                Trait(brain, "trait.introversion") * 0.10f +
                brain.Stress * 40f;

            float improveMood =
                (100f - Trait(brain, "trait.moodStability")) * 0.25f +
                Trait(brain, "trait.anxiety") * 0.15f +
                (1f - brain.Mood) * 50f +
                brain.Stress * 20f;

            float max = Max(romance, fight, friendship, avoid, improveMood);

            if (AlmostEqual(max, romance)) return NPCGoal.SeekRomance;
            if (AlmostEqual(max, fight)) return NPCGoal.ResolveConflict;
            if (AlmostEqual(max, friendship)) return NPCGoal.FindFriend;
            if (AlmostEqual(max, avoid)) return NPCGoal.AvoidEnemy;
            return NPCGoal.ImproveMood;
        }

        private static float Trait(Brain brain, string id)
        {
            // GetTrait should already clamp/fallback
            return brain.GetTrait(id);
        }

        private static bool ContainsAny(string text, params string[] words)
        {
            foreach (var w in words)
                if (text.Contains(w))
                    return true;
            return false;
        }

        private static float Max(params float[] values)
        {
            float m = values[0];
            for (int i = 1; i < values.Length; i++)
                if (values[i] > m) m = values[i];
            return m;
        }

        private static bool AlmostEqual(float a, float b)
        {
            return Math.Abs(a - b) < 0.0001f;
        }
    }
}