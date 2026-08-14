using ProjectEve.Characters.Base;
using System.Text;

namespace ProjectEve.Conversations
{
    /// <summary>
    /// Builds the conversation context for Thought + Dialogue.
    /// Exact evidence is retained permanently, while an NPC may receive an
    /// observer-specific perception view of player lines it did not fully see/hear.
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
            string location,
            string? currentPlayerPerceptionOverride = null,
            string? personalKnowledgeContext = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine(
                ConversationManager.BuildContinuityContext(
                    playerId,
                    owner.Id,
                    playerName,
                    location,
                    channel));

            if (!string.IsNullOrWhiteSpace(personalKnowledgeContext))
            {
                sb.AppendLine();
                sb.AppendLine(personalKnowledgeContext.Trim());
            }

            if (activeSessionId.HasValue && activeSessionId.Value > 0)
            {
                sb.AppendLine();

                // Preferred path: persistent observer-specific overlays for every
                // player message this NPC did not perceive exactly.
                if (ConversationPerceptionStore.HasAnyOverlay(activeSessionId.Value, owner.Id))
                {
                    sb.AppendLine(
                        ConversationPerceptionStore.BuildActiveTranscriptForNpc(
                            activeSessionId.Value,
                            owner.Id));
                }
                else if (!string.IsNullOrWhiteSpace(currentPlayerPerceptionOverride))
                {
                    // Backward-compatible one-turn fallback for callers that have not
                    // yet migrated to ConversationPerceptionStore.
                    sb.AppendLine(
                        BuildLatestTurnOverrideTranscript(
                            activeSessionId.Value,
                            currentPlayerPerceptionOverride));
                }
                else
                {
                    sb.AppendLine(
                        ConversationManager.BuildActiveTranscript(
                            activeSessionId.Value));
                }
            }

            sb.AppendLine();
            sb.AppendLine("CONVERSATION TRUTH RULES");
            sb.AppendLine(
                "- Exact ConversationMessage transcript is evidence for what physically occurred; it is not automatic NPC knowledge.");
            sb.AppendLine(
                "- When an NPC PERCEPTION VIEW is present, that view is authoritative for what THIS NPC heard/saw in those player turns.");
            sb.AppendLine(
                "- Never reconstruct hidden, inaudible, or missed words from context.");
            sb.AppendLine(
                "- Completed event summaries describe earlier sections; do not rewrite them into new history.");
            sb.AppendLine(
                "- Known facts are learned facts, not guesses.");
            sb.AppendLine(
                "- Unresolved plans carry across text, phone, and in-person channels only when this NPC actually perceived/learned them.");
            sb.AppendLine(
                "- If a fact is absent, do not invent it.");
            sb.AppendLine(
                "- A prior text conversation can be remembered during a later in-person interaction.");
            sb.AppendLine(
                "- Do not treat relationship closeness as automatic knowledge.");
            sb.AppendLine(
                "- Another NPC does not know this conversation unless they actually received, heard, observed, or were told the information.");
            sb.AppendLine(
                "- PERSONAL KNOWLEDGE / BELIEF records are observer-owned claims, not verified world truth.");
            sb.AppendLine(
                "- Gossip generation 1+ means the wording may have changed through the telephone game; never recover hidden original wording from provenance.");

            return sb.ToString().TrimEnd();
        }

        private static string BuildLatestTurnOverrideTranscript(
            long sessionId,
            string currentPlayerPerceptionOverride)
        {
            var rows = ConversationManager.GetTranscript(sessionId);
            if (rows.Count == 0)
                return "No active conversation section.";

            var lastPlayerIndex = -1;
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                if (rows[i].Role.Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    lastPlayerIndex = i;
                    break;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("ACTIVE CONVERSATION SECTION — NPC PERCEPTION VIEW");
            sb.AppendLine("Exact transcript evidence is stored separately. The latest player turn below is what this NPC perceived.");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var message = i == lastPlayerIndex
                    ? currentPlayerPerceptionOverride.Trim()
                    : row.MessageText;

                sb.AppendLine(
                    row.Role.Equals("system", StringComparison.OrdinalIgnoreCase)
                        ? $"[SYSTEM] {message}"
                        : $"{row.Speaker}: {message}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
