namespace ProjectEve.Narrative.Texting
{
    public class TypingBehavior
    {
        private readonly Random rng = new();

        public int GetTypingDelay(string message)
        {
            int baseDelay = message.Length * 40; // 40ms per character
            int variance = rng.Next(200, 600);
            return baseDelay + variance;
        }
    }
}
