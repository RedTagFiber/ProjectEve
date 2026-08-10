using ProjectEve.Characters.Base;
using ProjectEve.Traits;
using System;

namespace ProjectEve.Characters.Emotion
{
    /// <summary>
    /// Context → Fast trait movement.
    /// Does NOT write EmotionalProfile (emotions are Fast traits now).
    /// Prefer Thought tags via TraitEngine.ApplyTags when available;
    /// this is the fallback / assist path.
    /// </summary>
    public static class TraitEmotionReactor
    {
        /// <summary>
        /// Call after a chat line (user or combined context).
        /// intensityHint 1–10 scales deltas (default 5).
        /// </summary>
        public static void ApplyTraitDrivenEmotion(
            SimCharacter character,
            string context,
            int intensityHint = 5)
        {
            if (character?.Traits == null)
                return;

            if (string.IsNullOrWhiteSpace(context))
                return;

            string c = context.ToLowerInvariant();
            int intensity = Math.Clamp(intensityHint, 1, 10);

            // ---------- Conflict / anger ----------
            if (ContainsAny(c, "hate you", "fuck you", "idiot", "shut up", "disrespect", "yell", "mad at"))
            {
                Push(character, "trait.anger", +4f, intensity);
                Push(character, "trait.hurt", +2f, intensity);
                Push(character, "trait.trust", -2f, intensity);
                Push(character, "trait.patience", -2f, intensity);
                Push(character, "trait.tension", +2f, intensity);
            }

            // ---------- Soft / repair / love ----------
            if (ContainsAny(c, "sorry", "i love you", "miss you", "forgive", "come here", "hold me", "stay with me"))
            {
                Push(character, "trait.affection", +3f, intensity);
                Push(character, "trait.hope", +2f, intensity);
                Push(character, "trait.hurt", -2f, intensity);
                Push(character, "trait.anger", -2f, intensity);
                Push(character, "trait.loneliness", -2f, intensity);
            }

            // ---------- Fear / threat ----------
            if (ContainsAny(c, "afraid", "scared", "danger", "don't leave", "please don't go"))
            {
                Push(character, "trait.fear", +3f, intensity);
                Push(character, "trait.anxiety", +2f, intensity);
                Push(character, "trait.guard", +1f, intensity);
            }

            // ---------- Jealousy / rival ----------
            if (ContainsAny(c, "other guy", "other girl", "with someone else", "who is she", "who is he", "jealous"))
            {
                Push(character, "trait.jealousy", +4f, intensity);
                Push(character, "trait.trust", -2f, intensity);
                Push(character, "trait.resentment", +1f, intensity);
                Push(character, "trait.tension", +2f, intensity);
            }

            // ---------- Desire / heat ----------
            if (ContainsAny(c, "want you", "need you", "kiss me", "come over", "in bed", "horny", "fuck me", "touch me"))
            {
                Push(character, "trait.desire", +3f, intensity);
                Push(character, "trait.attraction", +2f, intensity);
                Push(character, "trait.tension", +2f, intensity);
            }

            // ---------- Lonely / abandoned ----------
            if (ContainsAny(c, "alone", "ignored", "left me", "didn't text", "where are you", "nobody"))
            {
                Push(character, "trait.loneliness", +3f, intensity);
                Push(character, "trait.hurt", +2f, intensity);
                Push(character, "trait.hope", -1f, intensity);
            }

            // ---------- Shame / exposed ----------
            if (ContainsAny(c, "embarrassed", "ashamed", "caught", "shouldn't have", "humiliated"))
            {
                Push(character, "trait.shame", +3f, intensity);
                Push(character, "trait.guard", +2f, intensity);
                Push(character, "trait.openness", -2f, intensity);
            }

            // ---------- Guilt ----------
            if (ContainsAny(c, "my fault", "i messed up", "i shouldn't", "forgive me", "i was wrong"))
            {
                Push(character, "trait.guilt", +3f, intensity);
                Push(character, "trait.pride", -1f, intensity);
            }

            // ---------- Play / joke ----------
            if (ContainsAny(c, "haha", "lol", "kidding", "teasing", "joke", "just playing"))
            {
                Push(character, "trait.playfulness", +2f, intensity);
                Push(character, "trait.tension", -1f, intensity);
                Push(character, "trait.anger", -1f, intensity);
            }

            // ---------- Trust open ----------
            if (ContainsAny(c, "trust you", "tell you something", "between us", "i need to say"))
            {
                Push(character, "trait.trust", +2f, intensity);
                Push(character, "trait.openness", +2f, intensity);
                Push(character, "trait.guard", -1f, intensity);
            }

            // ---------- Guard / shut down ----------
            if (ContainsAny(c, "leave me alone", "i don't want to talk", "drop it", "whatever"))
            {
                Push(character, "trait.guard", +3f, intensity);
                Push(character, "trait.openness", -2f, intensity);
                Push(character, "trait.patience", -1f, intensity);
            }

            // ---------- Pride / status ----------
            if (ContainsAny(c, "i was right", "told you", "embarrass me", "make me look bad"))
            {
                Push(character, "trait.pride", +2f, intensity);
                if (ContainsAny(c, "embarrass", "look bad"))
                    Push(character, "trait.shame", +2f, intensity);
            }

            // ---------- Work pressure (light) ----------
            if (ContainsAny(c, "work", "shift", "boss", "manager", "customer", "rush"))
            {
                Push(character, "trait.anxiety", +1f, Math.Min(intensity, 4));
                Push(character, "trait.patience", -1f, Math.Min(intensity, 4));
            }
        }

        // ------------------------------------------------------------------
        private static void Push(SimCharacter character, string traitId, float baseDelta, int intensity)
        {
            TraitEngine.ApplyTag(character, traitId, baseDelta, intensity);
        }

        private static bool ContainsAny(string text, params string[] words)
        {
            foreach (var w in words)
            {
                if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}