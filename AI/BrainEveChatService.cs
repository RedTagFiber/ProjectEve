using ProjectEve.AI;
using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Core.Chat;
using ProjectEve.Traits;
using System;
using System.Threading.Tasks;

namespace ProjectEve.Chat
{
    /// <summary>
    /// Phone / Blazor path into the real Brain.
    /// Order: load → traits → Think → Reply (LineBank then LLM for text).
    /// </summary>
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

                eve.Brain ??= new Brain();
                eve.Brain.Owner = eve;
                eve.Brain.LineBankSpeaker = "eve2";

                eve.Traits ??= new NpcTraits();
                if (eve.Traits.GetAll().Count == 0)
                {
                    try
                    {
                        TraitJsonLoader.ApplyRolledLayers(eve.Traits);
                    }
                    catch
                    {
                        eve.Traits.InitializeFastDefaults();
                    }
                }

                eve.Brain.Think(message ?? "");
                string reply = eve.Brain.Reply(message ?? "");

                try
                {
                    CharacterRepository.SaveTraits(eve.Id, eve.Traits);
                }
                catch
                {
                    // non-fatal
                }

                // Optional: Console.WriteLine($"[LineBank] source={eve.Brain.LastReplySource}");
                return string.IsNullOrWhiteSpace(reply) ? "..." : reply.Trim();
            }
            catch (Exception ex)
            {
                return $"(brain error: {ex.Message})";
            }
        }
    }
}
