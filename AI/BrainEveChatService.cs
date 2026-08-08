using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Core.Chat;

namespace ProjectEve.Chat
{
    public class BrainEveChatService : IEveChatService
    {
        public Task<string> GetReplyAsync(string sessionId, string userMessage)
        {
            return Task.Run(() => ReplyWithBrain(userMessage));
        }

        static string ReplyWithBrain(string message)
        {
            try
            {
                var eve = CharacterRepository.LoadCharacter(1);
                if (eve == null)
                    return "...";

                if (eve.Brain == null)
                    eve.Brain = new Brain();

                eve.Brain.Owner = eve;

                eve.Brain.Think(message);
                var reply = eve.Brain.Reply(message);

                return string.IsNullOrWhiteSpace(reply) ? "..." : reply;
            }
            catch (Exception ex)
            {
                return $"(brain error: {ex.Message})";
            }
        }
    }
}