using ProjectEve.Traits;
using System;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Generic thought -> trait nudges for every NPC.
    /// Trait IDs must match TraitRegistry exactly.
    /// </summary>
    public static class AITraitEngine
    {
        public static void UpdateTraits(Brain brain, string? thought)
        {
            if (brain?.Owner?.Traits == null || string.IsNullOrWhiteSpace(thought))
                return;

            var traits = brain.Owner.Traits;
            string t = thought.ToLowerInvariant();

            // =====================================================
            // SOCIAL / PERSONALITY
            // =====================================================
            if (ContainsAny(t, "alone", "quiet", "solitude", "drained"))
            {
                Adjust(traits, "trait.introversion", +1);
                Adjust(traits, "trait.extroversion", -1);
            }

            if (ContainsAny(t, "people", "crowd", "talk", "text", "party", "group"))
            {
                Adjust(traits, "trait.extroversion", +1);
                Adjust(traits, "trait.introversion", -1);
                Adjust(traits, "trait.confidence", +1);
            }

            if (ContainsAny(t, "hope", "bright side", "it'll be fine", "good outcome"))
            {
                Adjust(traits, "trait.optimism", +1);
                Adjust(traits, "trait.pessimism", -1);
            }

            if (ContainsAny(t, "won't work", "bad idea", "this ends badly", "doubt"))
            {
                Adjust(traits, "trait.pessimism", +1);
                Adjust(traits, "trait.optimism", -1);
            }

            if (ContainsAny(t, "screw it", "now", "can't wait", "just do it"))
            {
                Adjust(traits, "trait.impulsiveness", +2);
            }

            if (ContainsAny(t, "i got this", "sure", "confident"))
            {
                Adjust(traits, "trait.confidence", +1);
                Adjust(traits, "trait.insecurity", -1);
                Adjust(traits, "trait.sexualConfidence", +1);
            }

            if (ContainsAny(t, "not enough", "what if i fail", "do they like me", "second guess"))
            {
                Adjust(traits, "trait.insecurity", +1);
                Adjust(traits, "trait.confidence", -1);
            }

            // =====================================================
            // EMOTION / STRESS
            // =====================================================
            if (ContainsAny(t, "anxious", "worry", "stressed", "overwhelmed", "panic"))
            {
                Adjust(traits, "trait.anxiety", +2);
                Adjust(traits, "trait.fearfulness", +1);
                Adjust(traits, "trait.moodStability", -1);
                Adjust(traits, "trait.stoicism", -1);
            }

            if (ContainsAny(t, "calm", "steady", "breathe", "composed"))
            {
                Adjust(traits, "trait.stoicism", +1);
                Adjust(traits, "trait.moodStability", +1);
                Adjust(traits, "trait.anxiety", -1);
            }

            if (ContainsAny(t, "hurt", "feel for", "poor them", "understand them"))
            {
                Adjust(traits, "trait.empathy", +1);
                Adjust(traits, "trait.sensitivity", +1);
            }

            if (ContainsAny(t, "mad", "angry", "pissed", "furious", "rage"))
            {
                Adjust(traits, "trait.anger", +2);
                Adjust(traits, "trait.moodStability", -1);
            }

            if (ContainsAny(t, "scared", "afraid", "dangerous", "risky"))
            {
                Adjust(traits, "trait.fearfulness", +1);
                Adjust(traits, "trait.anxiety", +1);
            }

            // =====================================================
            // COGNITIVE
            // =====================================================
            if (ContainsAny(t, "think", "analyze", "plan", "makes sense"))
            {
                Adjust(traits, "trait.logic", +1);
                Adjust(traits, "trait.focus", +1);
            }

            if (ContainsAny(t, "idea", "imagine", "what if", "create"))
            {
                Adjust(traits, "trait.creativity", +1);
            }

            if (ContainsAny(t, "learn", "figure out", "new skill"))
            {
                Adjust(traits, "trait.learningSpeed", +1);
                Adjust(traits, "trait.understanding", +1);
            }

            // =====================================================
            // SEXUAL / DESIRE
            // =====================================================
            if (ContainsAny(t, "horny", "wet", "turned on", "want him", "want her", "fuck", "cock", "pussy"))
            {
                Adjust(traits, "trait.sexualConfidence", +1);
                Adjust(traits, "trait.sexualCuriosity", +1);
                Adjust(traits, "trait.roughnessPreference", +1);
            }

            if (ContainsAny(t, "gentle", "soft", "slow", "kiss", "hold me"))
            {
                Adjust(traits, "trait.aftercareNeed", +1);
                Adjust(traits, "trait.praiseKink", +1);
                Adjust(traits, "trait.roughnessPreference", -1);
            }

            if (ContainsAny(t, "harder", "rough", "choke", "slap", "use me", "degrade"))
            {
                Adjust(traits, "trait.roughnessPreference", +2);
                Adjust(traits, "trait.degradationDesire", +1);
                Adjust(traits, "trait.objectificationDesire", +1);
                Adjust(traits, "trait.painPlay", +1);
            }

            if (ContainsAny(t, "good girl", "good boy", "proud of you", "just like that"))
            {
                Adjust(traits, "trait.praiseKink", +2);
            }

            if (ContainsAny(t, "mine", "own me", "belong", "claim"))
            {
                Adjust(traits, "trait.possessiveDesire", +1);
                Adjust(traits, "trait.ownershipDesire", +1);
            }

            if (ContainsAny(t, "tie", "restrain", "cuffs", "rope"))
            {
                Adjust(traits, "trait.bondageInterest", +1);
                Adjust(traits, "trait.submission", +1);
            }

            if (ContainsAny(t, "watch me", "see us", "almost caught", "public"))
            {
                Adjust(traits, "trait.exhibitionism", +1);
                Adjust(traits, "trait.publicRisk", +1);
            }

            if (ContainsAny(t, "watch them", "seeing her with", "seeing him with"))
            {
                Adjust(traits, "trait.voyeurism", +1);
                Adjust(traits, "trait.compersion", +1);
            }

            if (ContainsAny(t, "secret", "hide", "behind his back", "if he knew", "double life"))
            {
                Adjust(traits, "trait.secrecyKink", +2);
                Adjust(traits, "trait.doubleLifeComfort", +1);
                Adjust(traits, "trait.sexualCompartmentalization", +1);
            }

            if (ContainsAny(t, "threesome", "group", "another person", "both of you"))
            {
                Adjust(traits, "trait.groupInterest", +1);
                Adjust(traits, "trait.nonMonogamyComfort", +1);
            }

            if (ContainsAny(t, "with someone else", "sleep with others", "share me", "share him", "share her"))
            {
                Adjust(traits, "trait.cuckoldInterest", +1);
                Adjust(traits, "trait.nonMonogamyComfort", +1);
                Adjust(traits, "trait.compersion", +1);
            }

            if (ContainsAny(t, "mouth", "suck", "oral", "tongue"))
            {
                Adjust(traits, "trait.oralFixation", +1);
            }

            if (ContainsAny(t, "creampie", "breed", "inside me", "fill me"))
            {
                Adjust(traits, "trait.breedingKink", +1);
            }

            if (ContainsAny(t, "use me whenever", "free use", "available"))
            {
                Adjust(traits, "trait.freeuseInterest", +1);
                Adjust(traits, "trait.objectificationDesire", +1);
            }

            if (ContainsAny(t, "ashamed", "guilty", "shouldn't want this"))
            {
                Adjust(traits, "trait.sexualShame", +2);
                Adjust(traits, "trait.sexualConfidence", -1);
            }

            if (ContainsAny(t, "after", "hold me after", "stay close", "don't leave yet"))
            {
                Adjust(traits, "trait.aftercareNeed", +2);
            }

            // =====================================================
            // POWER
            // =====================================================
            if (ContainsAny(t, "take control", "on your knees", "do what i say"))
            {
                Adjust(traits, "trait.dominance", +1);
                Adjust(traits, "trait.submission", -1);
            }

            if (ContainsAny(t, "tell me what to do", "use me", "i'll obey", "yes sir", "yes ma'am"))
            {
                Adjust(traits, "trait.submission", +1);
                Adjust(traits, "trait.dominance", -1);
            }
        }

        // -------------------------------------------------
        private static void Adjust(dynamic traits, string traitId, int amount)
        {
            try
            {
                traits.Adjust(traitId, amount);
            }
            catch
            {
                try
                {
                    float current = traits.Get(traitId);
                    float next = Math.Clamp(current + amount, 0f, 100f);
                    traits.Set(traitId, next);
                }
                catch
                {
                    // trait id not present on this NPC pack
                }
            }
        }

        private static bool ContainsAny(string text, params string[] words)
        {
            foreach (var w in words)
                if (text.Contains(w))
                    return true;
            return false;
        }
    }
}