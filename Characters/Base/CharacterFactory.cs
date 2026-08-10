using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Money;
using ProjectEve.Traits;
using System;

namespace ProjectEve.Characters.Characters
{
    /// <summary>
    /// Create / load NPCs. Trait roll uses Fast/Mid/Slow (TraitJson).
    /// Tier-5 + history factory comes later.
    /// </summary>
    public static class CharacterFactory
    {
        public static SimCharacter? LoadCharacter(int npcId)
        {
            var npc = CharacterRepository.LoadCharacter(npcId);
            if (npc == null)
                return null;

            EnsureCore(npc);
            EnsureTraits(npc);
            return npc;
        }

        public static SimCharacter Create(
            string name,
            int age,
            string? gender = null,
            string? location = null,
            string? occupation = null)
        {
            var npc = new SimCharacter(name, age)
            {
                Gender = gender ?? "Unknown",
                Location = location ?? "Unknown",
                Occupation = occupation ?? ""
            };

            EnsureCore(npc);
            EnsureTraits(npc);
            return npc;
        }

        public static void EnsureCore(SimCharacter npc)
        {
            if (npc == null) return;

            npc.Brain ??= new Brain();
            npc.Brain.Owner = npc;
            npc.Money ??= new MoneyProfile();
            npc.Job ??= new JobProfile();
            npc.Traits ??= new NpcTraits();
        }

        public static void EnsureTraits(SimCharacter npc)
        {
            if (npc == null) return;

            npc.Traits ??= new NpcTraits();
            if (npc.Traits.GetAll().Count > 0)
                return;

            try
            {
                NpcTraitInitializer.GenerateBalancedTraits(npc.Traits);
            }
            catch
            {
                try { TraitJsonLoader.ApplyRolledLayers(npc.Traits); }
                catch { npc.Traits.InitializeFastDefaults(); }
            }
        }

        public static void RerollTraits(SimCharacter npc)
        {
            if (npc == null) return;

            npc.Traits ??= new NpcTraits();
            try
            {
                NpcTraitInitializer.GenerateBalancedTraits(npc.Traits);
            }
            catch
            {
                npc.Traits.InitializeFastDefaults();
            }
        }
    }
}