using ProjectEve.Characters.Base;
using ProjectEve.Traits;
using System;

namespace ProjectEve.Traits
{
    public static class TraitEngine
    {
        // ============================================================
        // 1. DAILY DECAY — unused/high traits can drift slowly
        // ============================================================
        public static void ApplyDailyDecay(SimCharacter npc)
        {
            if (npc?.Traits == null) return;

            // Light daily drift on a few common traits
            // (Keep this small so personality stays stable)
            npc.Traits.Adjust("trait.anxiety", -1);
            npc.Traits.Adjust("trait.stressVulnerability", -1);
            npc.Traits.Adjust("trait.energyLevel", +1);
        }

        // ============================================================
        // 2. MOTIVATION → TRAIT INFLUENCE
        // ============================================================
        public static void ApplyMotivationInfluence(SimCharacter npc)
        {
            if (npc?.Traits == null) return;

            // GOAL
            if (Contains(npc.Goal, "career"))
                npc.Traits.Adjust("trait.ambition", +3);

            if (Contains(npc.Goal, "relationship"))
                npc.Traits.Adjust("trait.romanticDrive", +3);

            if (Contains(npc.Goal, "explore"))
                npc.Traits.Adjust("trait.curiosity", +3);

            // NEED
            if (Contains(npc.Need, "balance"))
                npc.Traits.Adjust("trait.moodStability", +2);

            if (Contains(npc.Need, "support"))
                npc.Traits.Adjust("trait.driveForBelonging", +2);

            // FEAR
            if (Contains(npc.Fear, "failure"))
                npc.Traits.Adjust("trait.anxiety", +3);

            if (Contains(npc.Fear, "abandon") || Contains(npc.Fear, "losing"))
                npc.Traits.Adjust("trait.attachmentStyle", +2);

            // WANT
            if (Contains(npc.Want, "learn"))
                npc.Traits.Adjust("trait.curiosity", +3);

            if (Contains(npc.Want, "close"))
                npc.Traits.Adjust("trait.romanticDrive", +3);
        }

        // ============================================================
        // 3. RELATIONSHIPS → TRAIT INFLUENCE
        // ============================================================
        public static void ApplyRelationshipInfluence(SimCharacter npc)
        {
            if (npc?.Traits == null || npc.Relationships == null) return;

            foreach (var rel in npc.Relationships)
            {
                if (rel.Affection > 70)
                    npc.Traits.Adjust("trait.empathy", +1);

                if (rel.Attraction > 70)
                    npc.Traits.Adjust("trait.sexualConfidence", +1);

                if (rel.Trust < 30)
                    npc.Traits.Adjust("trait.anxiety", +2);
            }
        }

        // ============================================================
        // 4. MEMORY → TRAIT INFLUENCE
        // ============================================================
        public static void ApplyMemoryInfluence(SimCharacter npc)
        {
            if (npc?.Traits == null || npc.MemoryDB == null) return;

            // Safe placeholder — only runs if your MemoryDB supports it
            // If GetMemories doesn't exist yet, comment this whole method body out for now.
            /*
            foreach (var mem in npc.MemoryDB.GetMemories(npc.Name))
            {
                if (mem.Category == "Emotional")
                    npc.Traits.Adjust("trait.empathy", +1);

                if (mem.Category == "Social")
                    npc.Traits.Adjust("trait.driveForBelonging", +1);

                if (mem.Category == "Stress")
                    npc.Traits.Adjust("trait.anxiety", +2);

                if (mem.Category == "Achievement")
                    npc.Traits.Adjust("trait.ambition", +2);
            }
            */
        }

        // ============================================================
        // 5. HELPERS
        // ============================================================
        public static void IncreaseTrait(SimCharacter npc, string traitId, float amount)
        {
            npc?.Traits?.Adjust(traitId, amount);
        }

        public static void DecreaseTrait(SimCharacter npc, string traitId, float amount)
        {
            npc?.Traits?.Adjust(traitId, -amount);
        }

        private static bool Contains(string? text, string value)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        // 6. FULL UPDATE
        // ============================================================
        public static void UpdateTraits(SimCharacter npc)
        {
            if (npc == null) return;

            ApplyDailyDecay(npc);
            ApplyMotivationInfluence(npc);
            ApplyRelationshipInfluence(npc);
            ApplyMemoryInfluence(npc);
        }
    }
}