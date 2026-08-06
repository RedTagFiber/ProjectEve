using System.Threading.Tasks;

namespace ProjectEve.Narrative.Texting
{
    public class TextConversationController
    {
        private readonly TypingBehavior typing = new();
        private readonly MessageHistory history = new();

        public async Task<string> SendNPCMessage(string rawThought, float mood, float friendliness)
        {
            string msg = MessageFormatter.FormatNPCMessage(rawThought);
            msg = ToneInference.ApplyTone(msg, mood);
            msg = EmojiPersonality.AddPersonalityEmoji(msg, friendliness);

            int delay = typing.GetTypingDelay(msg);
            await Task.Delay(delay);

            history.Add("Eve: " + msg);
            return msg;
        }

        public void SendPlayerMessage(string text)
        {
            string msg = MessageFormatter.FormatPlayerMessage(text);
            history.Add("You: " + msg);
        }

        public MessageHistory GetHistory()
        {
            return history;
        }
    }
}
