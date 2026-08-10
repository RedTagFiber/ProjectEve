using ProjectEve.Traits;
using System;

namespace ProjectEve.AI.Brain
{
    /// <summary>
    /// Thought → small Fast trait nudges.
    /// Primary chat movement is TraitEngine.UpdateTraitsAfterChat.
    /// This only reacts to internal monologue wording.
    /// </summary>
    public static class AITraitEngine
    {
        public static void UpdateTraits(Brain brain, string? thought)
        {
            if (brain?.Owner?.Traits == null || string.IsNullOrWhiteSpace(thought))
                return;

            var traits = brain.Owner.Traits;
            string t = thought.ToLowerInvariant();

            // Keep deltas small so we don't double-slam with TraitEngine
            if (ContainsAny(t, "alone", "ignored", "left out", "nobody"))
            {
                Adjust(traits, "trait.loneliness", +1);
                Adjust(traits, "trait.hurt", +1);
            }

            if (ContainsAny(t, "hope", "maybe we can", "still a chance"))
                Adjust(traits, "trait.hope", +1);

            if (ContainsAny(t, "won't work", "pointless", "why bother"))
                Adjust(traits, "trait.hope", -1);

            if (ContainsAny(t, "anxious", "worry", "what if", "overthinking"))
                Adjust(traits, "trait.anxiety", +1);

            if (ContainsAny(t, "calm", "breathe", "steady"))
                Adjust(traits, "trait.anxiety", -1);

            if (ContainsAny(t, "mad", "angry", "pissed", "how dare"))
                Adjust(traits, "trait.anger", +1);

            if (ContainsAny(t, "scared", "afraid", "dangerous"))
            {
                Adjust(traits, "trait.fear", +1);
                Adjust(traits, "trait.anxiety", +1);
            }

            if (ContainsAny(t, "ashamed", "embarrassed", "humiliated"))
                Adjust(traits, "trait.shame", +1);

            if (ContainsAny(t, "my fault", "i shouldn't", "owe"))
                Adjust(traits, "trait.guilt", +1);

            if (ContainsAny(t, "hurt", "that stung", "why would they"))
                Adjust(traits, "trait.hurt", +1);

            if (ContainsAny(t, "other guy", "other girl", "with someone else", "jealous"))
                Adjust(traits, "trait.jealousy", +1);

            if (ContainsAny(t, "unfair", "always does this", "never listens"))
                Adjust(traits, "trait.resentment", +1);

            if (ContainsAny(t, "trust", "safe with", "can tell"))
                Adjust(traits, "trait.trust", +1);

            if (ContainsAny(t, "don't trust", "lying", "hiding something"))
                Adjust(traits, "trait.trust", -1);

            if (ContainsAny(t, "miss", "care about", "warm", "love"))
                Adjust(traits, "trait.affection", +1);

            if (ContainsAny(t, "want them", "turned on", "need them", "horny"))
            {
                Adjust(traits, "trait.desire", +1);
                Adjust(traits, "trait.attraction", +1);
            }

            if (ContainsAny(t, "charged", "on edge", "tension"))
                Adjust(traits, "trait.tension", +1);

            if (ContainsAny(t, "funny", "laugh", "tease", "joke"))
                Adjust(traits, "trait.playfulness", +1);

            if (ContainsAny(t, "right", "won't look weak", "status"))
                Adjust(traits, "trait.pride", +1);

            if (ContainsAny(t, "patience", "hold back", "not yet"))
                Adjust(traits, "trait.patience", +1);

            if (ContainsAny(t, "shut down", "walls up", "not talking"))
            {
                Adjust(traits, "trait.guard", +1);
                Adjust(traits, "trait.openness", -1);
            }

            if (ContainsAny(t, "open up", "tell them", "say it"))
            {
                Adjust(traits, "trait.openness", +1);
                Adjust(traits, "trait.guard", -1);
            }
        }

        private static void Adjust(NpcTraits traits, string traitId, float amount)
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
                    traits.Set(traitId, Math.Clamp(current + amount, 0f, 100f));
                }
                catch { }
            }
        }

        private static bool ContainsAny(string text, params string[] words)
        {
            foreach (var w in words)
                if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}