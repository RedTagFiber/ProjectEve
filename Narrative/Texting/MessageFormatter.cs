namespace ProjectEve.Narrative.Texting
{
    public static class MessageFormatter
    {
        public static string FormatNPCMessage(string rawThought)
        {
            // Later: add personality, tone, emoji, punctuation
            return rawThought.Trim();
        }

        public static string FormatPlayerMessage(string text)
        {
            return text.Trim();
        }
    }
}
