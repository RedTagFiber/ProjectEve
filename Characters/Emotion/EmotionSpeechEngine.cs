using ProjectEve.Characters.Base;
using ProjectEve.Traits;
using System;

namespace ProjectEve.Characters.Emotion
{
    /// <summary>
    /// Light post-filter on dialogue from Fast trait levels.
    /// Keep changes small — model does the heavy lifting.
    /// </summary>
    public static class EmotionSpeechEngine
    {
        public static string ApplyEmotionTone(SimCharacter character, string baseLine, bool inPerson = false)
        {
            if (string.IsNullOrWhiteSpace(baseLine))
                return baseLine ?? "";

            string line = baseLine.Trim();
            if (character?.Traits == null)
                return line;

            // Priority: strongest Fast signal wins one light touch
            float anger = character.Traits.Get("trait.anger");
            float anxiety = character.Traits.Get("trait.anxiety");
            float fear = character.Traits.Get("trait.fear");
            float shame = character.Traits.Get("trait.shame");
            float guilt = character.Traits.Get("trait.guilt");
            float hurt = character.Traits.Get("trait.hurt");
            float jealousy = character.Traits.Get("trait.jealousy");
            float resentment = character.Traits.Get("trait.resentment");
            float affection = character.Traits.Get("trait.affection");
            float desire = character.Traits.Get("trait.desire");
            float playfulness = character.Traits.Get("trait.playfulness");
            float pride = character.Traits.Get("trait.pride");
            float guard = character.Traits.Get("trait.guard");
            float loneliness = character.Traits.Get("trait.loneliness");
            float hope = character.Traits.Get("trait.hope");
            float tension = character.Traits.Get("trait.tension");

            // Pick dominant “mood label” from Fast
            var dominant = PickDominant(
                ("anger", anger),
                ("anxiety", anxiety),
                ("fear", fear),
                ("shame", shame),
                ("guilt", guilt),
                ("hurt", hurt),
                ("jealousy", jealousy),
                ("resentment", resentment),
                ("desire", desire),
                ("affection", affection),
                ("playfulness", playfulness),
                ("pride", pride),
                ("guard", guard),
                ("loneliness", loneliness),
                ("tension", tension),
                ("hope", hope)
            );

            // Only style if clearly on (band mid+)
            if (dominant.Value < 50f)
                return line;

            return dominant.Name switch
            {
                "anger" when dominant.Value >= 70 => Angry(line),
                "anger" => Irritated(line),

                "anxiety" when dominant.Value >= 70 => Anxious(line),
                "anxiety" => Uneasy(line),

                "fear" => Scared(line),
                "shame" => Ashamed(line),
                "guilt" => Guilty(line),
                "hurt" => Hurt(line),
                "jealousy" => Jealous(line),
                "resentment" => Bitter(line),

                "desire" when dominant.Value >= 70 => DesireHot(line),
                "desire" => DesireSoft(line),

                "affection" when dominant.Value >= 70 => InLove(line),
                "affection" => Affectionate(line),

                "playfulness" => Playful(line),
                "pride" => Proud(line),
                "guard" => Guarded(line),
                "loneliness" => Lonely(line),
                "tension" => Tense(line),
                "hope" => Hopeful(line),

                _ => line
            };
        }

        /// <summary>
        /// Back-compat overload if something still passes EmotionalProfile.
        /// Ignores profile; uses character Fast traits if linked later.
        /// Prefer the SimCharacter overload.
        /// </summary>
        public static string ApplyEmotionTone(EmotionalProfile? emotion, string baseLine, bool inPerson = false)
        {
            // Profile no longer drives tone — return clean line
            return string.IsNullOrWhiteSpace(baseLine) ? "" : baseLine.Trim();
        }

        // ------------------------------------------------------------------
        private static (string Name, float Value) PickDominant(params (string Name, float Value)[] items)
        {
            string best = "none";
            float bestVal = -1f;
            foreach (var item in items)
            {
                if (item.Value > bestVal)
                {
                    bestVal = item.Value;
                    best = item.Name;
                }
            }
            return (best, bestVal);
        }

        // ============================
        // LIGHT TOUCHES ONLY
        // ============================
        private static string Angry(string line)
            => line.EndsWith(".") ? line + " Now." : line.EndsWith("!") ? line : line + ".";

        private static string Irritated(string line) => line;

        private static string Anxious(string line)
            => line.Contains("...") ? line : line + "...";

        private static string Uneasy(string line)
            => line.Contains("...") ? line : line + "...";

        private static string Scared(string line)
            => line.Contains("...") ? line : line + "...";

        private static string Ashamed(string line)
            => line.StartsWith("...") ? line : "... " + line;

        private static string Guilty(string line)
            => line.StartsWith("...") ? line : "... " + line;

        private static string Hurt(string line)
            => line.StartsWith("...") ? line : "... " + line;

        private static string Jealous(string line) => line;

        private static string Bitter(string line)
            => StripExtraPunch(line);

        private static string DesireHot(string line) => line;

        private static string DesireSoft(string line) => line;

        private static string Affectionate(string line) => line;

        private static string InLove(string line) => line;

        private static string Playful(string line) => line;

        private static string Proud(string line) => line;

        private static string Guarded(string line)
            => StripExtraPunch(line);

        private static string Lonely(string line)
            => line.StartsWith("...") ? line : "... " + line;

        private static string Tense(string line) => line;

        private static string Hopeful(string line) => line;

        private static string StripExtraPunch(string line)
            => line.Replace("!!", ".").Replace("!", ".");
    }
}