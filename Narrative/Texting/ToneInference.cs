namespace ProjectEve.Narrative.Texting
{
    public static class ToneInference
    {
        public static string ApplyTone(string message, float moodValue)
        {
            if (moodValue > 0.7f)
                return message + " 😊";

            if (moodValue < 0.3f)
                return message + " …";

            return message;
        }
    }
}
