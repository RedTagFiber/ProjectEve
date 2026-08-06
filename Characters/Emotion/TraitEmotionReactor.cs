using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using System;

namespace ProjectEve.Characters.Emotion
{
    /// <summary>
    /// Traits push emotional meters for any NPC.
    /// Uses TraitRegistry ids only.
    /// </summary>
    public static class TraitEmotionReactor
    {
        public static void ApplyTraitDrivenEmotion(SimCharacter character, string context)
        {
            if (character?.Emotion == null)
                return;

            var emotion = character.Emotion;
            context = (context ?? "").ToLowerInvariant();

            float anxiety = Get(character, "trait.anxiety");
            float fear = Get(character, "trait.fearfulness");
            float angerTrait = Get(character, "trait.anger");
            float impulsiveness = Get(character, "trait.impulsiveness");
            float optimism = Get(character, "trait.optimism");
            float pessimism = Get(character, "trait.pessimism");
            float confidence = Get(character, "trait.confidence");
            float insecurity = Get(character, "trait.insecurity");
            float empathy = Get(character, "trait.empathy");
            float sensitivity = Get(character, "trait.sensitivity");
            float stoicism = Get(character, "trait.stoicism");
            float moodStability = Get(character, "trait.moodStability");

            float sexualConfidence = Get(character, "trait.sexualConfidence");
            float sexualCuriosity = Get(character, "trait.sexualCuriosity");
            float sexualShame = Get(character, "trait.sexualShame");
            float secrecyKink = Get(character, "trait.secrecyKink");
            float doubleLife = Get(character, "trait.doubleLifeComfort");
            float degradation = Get(character, "trait.degradationDesire");
            float roughness = Get(character, "trait.roughnessPreference");
            float aftercare = Get(character, "trait.aftercareNeed");
            float dominance = Get(character, "trait.dominance");
            float submission = Get(character, "trait.submission");
            float jealousyRelated = Get(character, "trait.possessiveDesire");
            float nonMono = Get(character, "trait.nonMonogamyComfort");
            float compersion = Get(character, "trait.compersion");

            // =====================================================
            // BASE TRAIT PRESSURE (always mild)
            // =====================================================
            if (anxiety > 60) emotion.AddStress(Scale(anxiety, 60, 2));
            if (fear > 60) emotion.AddStress(Scale(fear, 60, 1));
            if (angerTrait > 60) emotion.AddAnger(Scale(angerTrait, 60, 2));
            if (optimism > 65) emotion.AddHappiness(Scale(optimism, 65, 1));
            if (pessimism > 65) emotion.AddSadness(Scale(pessimism, 65, 1));
            if (insecurity > 65) emotion.AddStress(Scale(insecurity, 65, 1));
            if (confidence > 70) emotion.AdjustComfort(1);
            if (stoicism > 70) emotion.AddStress(-1);
            if (moodStability > 70) emotion.AddStress(-1);
            if (moodStability < 35) emotion.AddStress(1);

            // =====================================================
            // CONTEXT REACTIONS
            // =====================================================

            // Social / connection
            if (ContainsAny(context, "miss", "hold", "stay", "with you", "love"))
            {
                emotion.AddAffection(2);
                if (empathy > 50) emotion.AddHappiness(1);
                if (aftercare > 60) emotion.AdjustComfort(2);
            }

            if (ContainsAny(context, "alone", "ignored", "left me", "didn't text"))
            {
                emotion.AddSadness(2);
                emotion.AddRestlessness(2);
                if (insecurity > 55) emotion.AddStress(2);
            }

            // Conflict
            if (ContainsAny(context, "fight", "argue", "mad", "yell", "disrespect"))
            {
                emotion.AddAnger(Scale(Math.Max(angerTrait, 40), 40, 3));
                emotion.AddStress(2);
                if (impulsiveness > 60) emotion.AddAnger(2);
                if (stoicism > 70) emotion.AddAnger(-1);
            }

            // Jealousy / possession
            if (ContainsAny(context, "other guy", "other girl", "with someone else", "jealous"))
            {
                emotion.AddResentment(Scale(Math.Max(jealousyRelated, 40), 40, 3));
                emotion.AddStress(1);
                if (nonMono > 70 && compersion > 60)
                {
                    // can flip toward desire instead of only resentment
                    emotion.AddDesire(2);
                    emotion.AddResentment(-1);
                }
            }

            // Sexual pressure
            if (ContainsAny(context, "horny", "fuck", "wet", "cock", "pussy", "kiss me", "come over", "bed"))
            {
                emotion.AddDesire(Scale(Math.Max(sexualConfidence, sexualCuriosity), 40, 3));
                if (roughness > 60 || degradation > 55)
                    emotion.AddDesire(1);
                if (sexualShame > 55)
                    emotion.AddShame(2);
            }

            // Secrecy / double life thrill
            if (ContainsAny(context, "secret", "hide", "don't tell", "behind", "if he knew", "sneak"))
            {
                if (secrecyKink > 55 || doubleLife > 55)
                {
                    emotion.AddDesire(2);
                    emotion.AddHappiness(1); // thrill
                }
                else
                {
                    emotion.AddStress(2);
                    emotion.AddShame(1);
                }
            }

            // Power tones
            if (ContainsAny(context, "on your knees", "do what i say", "use me", "obey"))
            {
                if (submission > 55) emotion.AddDesire(2);
                if (dominance > 55) emotion.AddDesire(2);
            }

            // Aftercare / soft come-down
            if (ContainsAny(context, "after", "hold me", "stay close", "you okay"))
            {
                if (aftercare > 50)
                {
                    emotion.AdjustComfort(3);
                    emotion.AddStress(-2);
                    emotion.AddShame(-1);
                }
            }

            // Embarrassment / shame spikes
            if (ContainsAny(context, "embarrassed", "caught", "ashamed", "shouldn't"))
            {
                emotion.AddShame(Scale(Math.Max(sexualShame, sensitivity), 40, 3));
                emotion.AddStress(1);
            }

            // Boredom / empty time
            if (ContainsAny(context, "bored", "nothing to do", "same day", "scrolling"))
            {
                emotion.AddRestlessness(3);
                if (impulsiveness > 60) emotion.AddDesire(1);
            }

            // Work pressure
            if (ContainsAny(context, "work", "shift", "customer", "rush", "manager"))
            {
                emotion.AddStress(1);
                emotion.AddEnergy(-1);
                if (anxiety > 60) emotion.AddStress(1);
            }

            // Final resolve
            emotion.Recalculate();
        }

        // -------------------------------------------------
        private static float Get(SimCharacter character, string traitId)
        {
            try
            {
                if (character.Brain != null)
                    return character.Brain.GetTrait(traitId);
            }
            catch { }

            try
            {
                if (character.Traits != null)
                    return character.Traits.Get(traitId);
            }
            catch { }

            return 50f;
        }

        private static int Scale(float traitValue, float threshold, int maxAdd)
        {
            if (traitValue <= threshold) return 1;
            float t = (traitValue - threshold) / (100f - threshold);
            return Math.Clamp((int)Math.Round(1 + t * (maxAdd - 1)), 1, maxAdd);
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