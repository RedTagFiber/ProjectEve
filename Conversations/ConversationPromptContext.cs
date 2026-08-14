using ProjectEve.Characters.Base;
using System.Text;

namespace ProjectEve.Conversations
{
    /// <summary>
    /// Builds the conversation truth block for Thought + Dialogue.
    /// Completed events provide continuity; the active section provides exact chat.
    /// </summary>
    public static class ConversationPromptContext
    {
        // Backward-compatible v1 entry point.
        public static string Build(
            SimCharacter owner,
            string playerName,
            long? activeSessionId,
            string channel,
            string location)
            => Build(
                owner,
                ConversationManager.LegacyPlayerId,
                playerName,
                activeSessionId,
                channel,
                location);

        public static string Build(
            SimCharacter owner,
            string playerId,
            string playerName,
            long? activeSessionId,
            string channel,
            string location)
        {
            var sb = new StringBuilder();

            sb.AppendLine(
                ConversationManager.BuildContinuityContext(
                    playerId,
                    owner.Id,
                    playerName,
                    location,
                    channel));

            if (activeSessionId.HasValue &&
                activeSessionId.Value > 0)
            {
                sb.AppendLine();
                sb.AppendLine(
                    ConversationManager.BuildActiveTranscript(
                        activeSessionId.Value));
            }

            sb.AppendLine();
            sb.AppendLine("CONVERSATION TRUTH RULES");
            sb.AppendLine(
                "- Exact active transcript is authoritative for what was said in this section.");
            sb.AppendLine(
                "- Completed event summaries describe earlier sections; do not rewrite them into new history.");
            sb.AppendLine(
                "- Known facts are learned facts, not guesses.");
            sb.AppendLine(
                "- Unresolved plans carry across text, phone, and in-person channels.");
            sb.AppendLine(
                "- If a fact is absent, do not invent it.");
            sb.AppendLine(
                "- A prior text conversation can be remembered during a later in-person interaction.");
            sb.AppendLine(
                "- Do not treat relationship closeness as automatic knowledge.");
            sb.AppendLine(
                "- Another NPC does not know this conversation unless they actually received, heard, observed, or were told the information.");

            return sb.ToString().TrimEnd();
        }
    }
}
