using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Conversations;
using ProjectEve.Core.Chat;
using ProjectEve.Core.Knowledge;
using ProjectEve.Traits;
using System.Collections.Concurrent;

namespace ProjectEve.Chat;

/// <summary>
/// Server-side bridge between clients and ProjectEve's one true NPC state.
///
/// Important:
/// - exact player line is archived before cognition
/// - the Brain sees the complete active section + relevant closed events
/// - exact NPC reply is archived after generation
/// - channel/location changes close the prior section into a ConversationEvent
/// - NPC cognition is serialized per NPC so two clients cannot mutate one Brain simultaneously
/// </summary>
public sealed class ProjectEveConversationService : IConversationChatService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> NpcLocks = new();
    private readonly INpcKnowledgeService _knowledge;

    public ProjectEveConversationService(INpcKnowledgeService knowledge)
    {
        _knowledge = knowledge;
    }

    public async Task<ConversationAcceptResult> AcceptPlayerMessageAsync(
        ConversationPlayerMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        string message = (request.Message ?? "").Trim();
        if (message.Length == 0)
            throw new ArgumentException("Message is required.", nameof(request));

        string playerId = Clean(request.PlayerId, ConversationManager.LegacyPlayerId);
        string playerName = Clean(request.PlayerName, "Player");
        string channel = Clean(request.Channel, "text");
        string location = Clean(request.Location, channel == "text" ? "phone" : "unknown");

        var npc = LoadNpc(request.NpcId);
        string npcName = !string.IsNullOrWhiteSpace(request.NpcNameHint)
            ? request.NpcNameHint.Trim()
            : npc.Name;

        // A different channel or meaningful location is a new section.
        // Close/summarize the old section BEFORE creating the new one so its
        // event/facts/plans are immediately available as continuity.
        var closedSections = await ConversationManager.EndOpenSectionsExceptAsync(
            playerId,
            npc.Id,
            playerName,
            channel,
            location,
            reason: $"conversation changed to {channel} @ {location}",
            cancellationToken);

        foreach (var closedSection in closedSections)
        {
            if (closedSection.EventId <= 0)
                continue;

            try
            {
                await _knowledge.ImportConversationEventAsync(
                    closedSection.EventId,
                    cancellationToken);
            }
            catch
            {
                // The closed conversation event remains authoritative even if the
                // personal-knowledge import needs to retry later.
            }
        }

        long sessionId = ConversationManager.StartOrResume(
            playerId,
            npc.Id,
            npcName,
            playerName,
            channel,
            location,
            DateTime.Now);

        // Store the exact player words now. This is deliberately before Thought.
        ConversationManager.AppendPlayer(
            sessionId,
            playerName,
            message,
            DateTime.Now);

        return new ConversationAcceptResult
        {
            SessionId = sessionId,
            Accepted = true
        };
    }

    public async Task<ConversationTurnResult> GenerateNpcReplyAsync(
        ConversationReplyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var session = ConversationManager.GetSession(request.SessionId)
            ?? throw new InvalidOperationException(
                $"Conversation session {request.SessionId} was not found.");

        if (!session.Status.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            // A delayed phone reply can become obsolete if the player has already
            // moved into a different channel/location with this NPC. Do not resurrect
            // the closed text section; let the client mark that queued reply obsolete.
            return new ConversationTurnResult
            {
                SessionId = session.Id,
                Reply = "",
                Source = "section_closed"
            };
        }

        if (request.NpcId > 0 && request.NpcId != session.NpcId)
            throw new InvalidOperationException(
                $"Session {request.SessionId} belongs to NPC {session.NpcId}, not NPC {request.NpcId}.");

        string brainInput = (request.PlayerMessage ?? "").Trim();
        if (brainInput.Length == 0)
            throw new ArgumentException(
                "PlayerMessage is required for the NPC reply.",
                nameof(request));

        // One NPC = one state. Serialize mutation even if two remote players
        // happen to reach the same NPC at nearly the same moment.
        var gate = NpcLocks.GetOrAdd(
            session.NpcId,
            static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);

        try
        {
            var npc = LoadNpc(session.NpcId);

            // Preserve the same trait initialization safety used by the
            // existing BrainEveChatService.
            npc.Traits ??= new NpcTraits();

            if (npc.Traits.GetAll().Count == 0)
            {
                try
                {
                    TraitJsonLoader.ApplyRolledLayers(npc.Traits);
                }
                catch
                {
                    npc.Traits.InitializeFastDefaults();
                }
            }

            npc.Brain ??= new Brain();
            npc.Brain.Owner = npc;
            npc.Brain.LineBankSpeaker = SpeakerFor(npc.Id);

            if (!string.IsNullOrWhiteSpace(request.PerceivedPlayerMessage))
            {
                ConversationPerceptionStore.UpsertLatestPlayerPerception(
                    session.Id,
                    session.NpcId,
                    request.PerceivedPlayerMessage,
                    request.PerceptionSourceKey);
            }

            // Pull in only THIS NPC's personal knowledge. Scene evidence is lazily
            // imported by the knowledge service and remains observer-specific.
            string personalKnowledge = await _knowledge.BuildPromptContextAsync(
                npc.Id,
                session.PlayerId,
                session.PlayerName,
                cancellationToken: cancellationToken);

            string context = ConversationPromptContext.Build(
                npc,
                session.PlayerId,
                session.PlayerName,
                session.Id,
                session.Channel,
                session.Location,
                personalKnowledgeContext: personalKnowledge);

            npc.Brain.ConversationContextOverride = context;

            npc.Brain.Think(brainInput);
            string reply = npc.Brain.Reply(brainInput);

            if (string.IsNullOrWhiteSpace(reply))
                reply = "...";

            // Archive exactly what the NPC outwardly produced.
            ConversationManager.AppendNpc(
                session.Id,
                npc.Id,
                npc.Name,
                reply,
                DateTime.Now);

            try
            {
                if (npc.Traits != null)
                    CharacterRepository.SaveTraits(npc.Id, npc.Traits);
            }
            catch
            {
                // A persistence-side issue must not fabricate another reply.
                // The exact conversation transcript is already stored.
            }

            return new ConversationTurnResult
            {
                SessionId = session.Id,
                Reply = reply,
                Source = npc.Brain.LastReplySource
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ConversationTurnResult> ReplyNowAsync(
        ConversationPlayerMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var accepted = await AcceptPlayerMessageAsync(
            request,
            cancellationToken);

        return await GenerateNpcReplyAsync(
            new ConversationReplyRequest
            {
                SessionId = accepted.SessionId,
                NpcId = request.NpcId,
                PlayerMessage = request.Message,
                Channel = request.Channel,
                Location = request.Location
            },
            cancellationToken);
    }

    public async Task<ConversationEndResult> EndSectionAsync(
        ConversationEndRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        long? sessionId = request.SessionId;

        if (!sessionId.HasValue || sessionId.Value <= 0)
        {
            sessionId = ConversationManager.GetActiveSessionId(
                Clean(request.PlayerId, ConversationManager.LegacyPlayerId),
                request.NpcId,
                Clean(request.PlayerName, "Player"));
        }

        if (!sessionId.HasValue || sessionId.Value <= 0)
            return new ConversationEndResult();

        var closed = await ConversationManager.EndSectionAsync(
            sessionId.Value,
            Clean(request.Reason, "conversation ended"),
            DateTime.Now,
            cancellationToken);

        if (closed == null)
            return new ConversationEndResult
            {
                SessionId = sessionId.Value
            };

        // ConversationFact rows are already NPC-perception-scoped by Phase 6.
        // Import them into the unified personal knowledge ledger only after the
        // section closes, preserving the exact transcript separately as evidence.
        if (closed.EventId > 0)
        {
            try
            {
                await _knowledge.ImportConversationEventAsync(
                    closed.EventId,
                    cancellationToken);
            }
            catch
            {
                // Knowledge persistence must not erase/alter a successfully
                // archived conversation event.
            }
        }

        return new ConversationEndResult
        {
            SessionId = closed.SessionId,
            EventId = closed.EventId,
            Summary = closed.Summary
        };
    }

    private static SimCharacter LoadNpc(int npcId)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        var npc = CharacterRepository.LoadCharacter(npcId);
        if (npc == null)
            throw new InvalidOperationException(
                $"ProjectEve could not load NPC {npcId}.");

        return npc;
    }

    private static string SpeakerFor(int npcId)
        => npcId switch
        {
            1 => "eve2",
            2 => "adam",
            _ => "eve2"
        };

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
}
