using ProjectEve.Characters.Emotion;

namespace ProjectEve.Characters.Emotion
{
    /// <summary>
    /// Light tone filter for already-generated dialogue.
    /// Keep changes small. Brain/model does the heavy lifting.
    /// </summary>
    public static class EmotionSpeechEngine
    {
        public static string ApplyEmotionTone(EmotionalProfile emotion, string baseLine, bool inPerson = false)
        {
            if (string.IsNullOrWhiteSpace(baseLine) || emotion == null)
                return baseLine ?? "";

            string line = baseLine.Trim();

            return emotion.State switch
            {
                EmotionState.Happy => Happy(line),
                EmotionState.Content => Content(line),
                EmotionState.Calm => Calm(line),
                EmotionState.Soft => Soft(line),
                EmotionState.Sad => Sad(line),
                EmotionState.Lonely => Lonely(line),
                EmotionState.Angry => Angry(line),
                EmotionState.Irritated => Irritated(line),
                EmotionState.Anxious => Anxious(line),
                EmotionState.Uneasy => Uneasy(line),
                EmotionState.Stressed => Stressed(line),
                EmotionState.Overwhelmed => Overwhelmed(line),
                EmotionState.Scared => Scared(line),
                EmotionState.Embarrassed => Embarrassed(line),
                EmotionState.Excited => Excited(line),
                EmotionState.Playful => Playful(line),
                EmotionState.Horny => Horny(line),
                EmotionState.Affectionate => Affectionate(line),
                EmotionState.InLove => InLove(line),
                EmotionState.Jealous => Jealous(line),
                EmotionState.Guilty => Guilty(line),
                EmotionState.Numb => Numb(line),
                EmotionState.Tired => Tired(line),
                EmotionState.Focused => Focused(line),

                // shadow states
                EmotionState.Tempted => Tempted(line),
                EmotionState.Reckless => Reckless(line),
                EmotionState.Spiteful => Spiteful(line),
                EmotionState.Vindictive => Vindictive(line),
                EmotionState.Predatory => Predatory(line),
                EmotionState.Possessive => Possessive(line),
                EmotionState.Obsessive => Obsessive(line),
                EmotionState.Ashamed => Ashamed(line),
                EmotionState.GuiltyPleasure => GuiltyPleasure(line),
                EmotionState.Restless => Restless(line),
                EmotionState.Hollow => Hollow(line),
                EmotionState.Bitter => Bitter(line),
                EmotionState.Smug => Smug(line),
                EmotionState.SelfDestructive => SelfDestructive(line),
                EmotionState.Cruel => Cruel(line),
                EmotionState.Detached => Detached(line),
                EmotionState.Manic => Manic(line),
                EmotionState.Hungry => Hungry(line),
                EmotionState.Corrupted => Corrupted(line),

                _ => line
            };
        }

        // ============================
        // NORMAL
        // ============================
        private static string Happy(string line) => line.EndsWith("!") ? line : line + "!";
        private static string Content(string line) => line;
        private static string Calm(string line) => line;
        private static string Soft(string line) => line;
        private static string Focused(string line) => line;

        private static string Sad(string line) => line.StartsWith("...") ? line : "... " + line;
        private static string Lonely(string line) => line.StartsWith("...") ? line : "... " + line;
        private static string Tired(string line) => line;
        private static string Numb(string line) => StripExtraPunch(line);

        private static string Angry(string line) => line.EndsWith(".") ? line + " Now." : line + ".";
        private static string Irritated(string line) => line;
        private static string Anxious(string line) => line.Contains("...") ? line : line + "...";
        private static string Uneasy(string line) => line.Contains("...") ? line : line + "...";
        private static string Stressed(string line) => line;
        private static string Overwhelmed(string line) => line.StartsWith("...") ? line : "... " + line;
        private static string Scared(string line) => line.Contains("...") ? line : line + "...";
        private static string Embarrassed(string line) => line.Contains("...") ? line : line + "...";

        private static string Excited(string line) => line.EndsWith("!") ? line : line + "!";
        private static string Playful(string line) => line;
        private static string Horny(string line) => line;
        private static string Affectionate(string line) => line;
        private static string InLove(string line) => line;
        private static string Jealous(string line) => line;
        private static string Guilty(string line) => line.StartsWith("...") ? line : "... " + line;

        // ============================
        // SHADOW
        // ============================
        private static string Tempted(string line)
            => line.Contains("...") ? line : line + "...";

        private static string Reckless(string line)
            => line;

        private static string Spiteful(string line)
            => StripExtraPunch(line);

        private static string Vindictive(string line)
            => StripExtraPunch(line);

        private static string Predatory(string line)
            => line;

        private static string Possessive(string line)
            => line;

        private static string Obsessive(string line)
            => line;

        private static string Ashamed(string line)
            => line.StartsWith("...") ? line : "... " + line;

        private static string GuiltyPleasure(string line)
            => line;

        private static string Restless(string line)
            => line;

        private static string Hollow(string line)
            => StripExtraPunch(line);

        private static string Bitter(string line)
            => StripExtraPunch(line);

        private static string Smug(string line)
            => line;

        private static string SelfDestructive(string line)
            => line;

        private static string Cruel(string line)
            => StripExtraPunch(line);

        private static string Detached(string line)
            => StripExtraPunch(line);

        private static string Manic(string line)
            => line.EndsWith("!") ? line : line + "!";

        private static string Hungry(string line)
            => line;

        private static string Corrupted(string line)
            => line;

        // ============================
        // HELPERS
        // ============================
        private static string StripExtraPunch(string line)
        {
            // keep cold states from sounding bouncy
            return line.Replace("!!", ".").Replace("!", ".");
        }
    }
}