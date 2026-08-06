namespace ProjectEve.Narrative.Texting
{
    public static class EmojiPersonality
    {
        public static string AddPersonalityEmoji(string message, float friendliness)
        {
            if (friendliness > 0.8f)
                return message + " 🙂";

            if (friendliness < 0.3f)
                return message + " .";

            return message;
        }
    }
}
