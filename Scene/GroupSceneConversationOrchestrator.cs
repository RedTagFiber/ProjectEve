using Microsoft.Data.Sqlite;
using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Core.Knowledge;
using ProjectEve.Core.Scene;
using ProjectEve.Core.Time;
using ProjectEve.Relationships;
using ProjectEve.Traits;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Scene;

/// <summary>
/// Phase 9 multi-NPC in-person scene orchestrator.
///
/// Flow for one player turn:
/// 1. Store exact player action/speech as world transcript evidence.
/// 2. Phase 6 resolves what every person actually saw/heard.
/// 3. Cheap deterministic scoring decides which NPCs are interested enough to answer.
/// 4. Only the strongest few receive a full Brain call.
/// 5. Every responding NPC gets an observer-filtered scene context, never the hidden transcript.
/// 6. NPC replies are split into BODY/ACTION/SAY and run back through Phase 6.
/// 7. Later responders therefore perceive earlier responders naturally.
///
/// The exact transcript is permanent server evidence. Observer views are separate.
/// </summary>
public sealed class GroupSceneConversationOrchestrator : IGroupSceneConversationOrchestrator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SceneLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> NpcLocks = new();

    private readonly IScenePerceptionService _perception;
    private readonly ISceneSpatialInteractionService _spatial;
    private readonly INpcKnowledgeService _knowledge;
    private readonly IGameTimeService _clock;
    private readonly string _dbPath;

    public GroupSceneConversationOrchestrator(
        IScenePerceptionService perception,
        ISceneSpatialInteractionService spatial,
        INpcKnowledgeService knowledge,
        IGameTimeService clock)
    {
        _perception = perception;
        _spatial = spatial;
        _knowledge = knowledge;
        _clock = clock;

        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<GroupSceneSessionHandle> EnsureSceneAsync(
        string sceneId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("SceneId is required.", nameof(sceneId));

        sceneId = sceneId.Trim();
        var sceneGate = SceneLocks.GetOrAdd(sceneId, static _ => new SemaphoreSlim(1, 1));
        await sceneGate.WaitAsync(cancellationToken);
        try
        {
            long? open = GetOpenSessionId(sceneId);
            if (open.HasValue)
            {
                return new GroupSceneSessionHandle
                {
                    SessionId = open.Value,
                    SceneId = sceneId,
                    IsNew = false
                };
            }

            long id = GetOrStartSession(sceneId);
            return new GroupSceneSessionHandle
            {
                SessionId = id,
                SceneId = sceneId,
                IsNew = true
            };
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public async Task<GroupSceneDisplayEntry> AppendWorldEntryAsync(
        string sceneId,
        string entryType,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("SceneId is required.", nameof(sceneId));

        text = (text ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException("World entry text is required.", nameof(text));

        string kind = entryType?.Trim().ToLowerInvariant() == "scene_update"
            ? "scene_update"
            : "scene";

        sceneId = sceneId.Trim();
        var sceneGate = SceneLocks.GetOrAdd(sceneId, static _ => new SemaphoreSlim(1, 1));
        await sceneGate.WaitAsync(cancellationToken);
        try
        {
            long sessionId = GetOrStartSession(sceneId);
            string eventKey = $"world:{sessionId}:{_clock.Now.Ticks}:{Guid.NewGuid():N}";
            long entryId = InsertEntry(
                sessionId,
                eventKey,
                "world",
                null,
                null,
                "World",
                kind,
                text);

            TouchSession(sessionId);

            return new GroupSceneDisplayEntry
            {
                EntryId = entryId,
                Sequence = GetEntrySequence(entryId),
                EventKey = eventKey,
                ActorCharacterKey = "world",
                ActorName = "World",
                EntryType = kind,
                Text = text,
                PerceptionQuality = "clear",
                PerceptionConfidence = 1.0,
                GameTime = _clock.Now
            };
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public async Task<GroupSceneTurnResult> SubmitPlayerTurnAsync(
        GroupScenePlayerTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        string sceneId = request.SceneId.Trim();
        string playerId = Clean(request.PlayerId, "legacy-player");
        string playerName = Clean(request.PlayerName, "Player");
        string playerKey = string.IsNullOrWhiteSpace(request.PlayerCharacterKey)
            ? "player:" + playerId
            : request.PlayerCharacterKey.Trim();

        string action = (request.ActionText ?? "").Trim();
        string speech = (request.SpeechText ?? "").Trim();
        string voice = NormalizeVoice(request.VoiceLevel);
        IReadOnlyList<int> addressedNpcIds = request.AddressedNpcIds;
        int maxReplies = Math.Clamp(request.MaxNpcReplies, 1, 4);

        var sceneGate = SceneLocks.GetOrAdd(sceneId, static _ => new SemaphoreSlim(1, 1));
        await sceneGate.WaitAsync(cancellationToken);

        try
        {
            long sessionId = GetOrStartSession(sceneId);
            long startSequence = GetLastSequence(sessionId);
            string turnKey = $"group:{sessionId}:{_clock.Now.Ticks}:{Guid.NewGuid():N}";

            // Phase 11: convert player physical intent into world-state movement
            // before perception/Brain calls. The original text is preserved in
            // SceneSpatialInteractionEvent; GroupScene stores resolved world truth.
            var preparedSpatialTurn = await _spatial.PrepareActorTurnAsync(
                new SceneSpatialTurnRequest
                {
                    SceneId = sceneId,
                    ActorCharacterKey = playerKey,
                    ActorName = playerName,
                    ActionText = action,
                    SpeechText = speech,
                    VoiceLevel = voice,
                    AddressedNpcIds = addressedNpcIds
                },
                cancellationToken);

            action = preparedSpatialTurn.ActionText;
            speech = preparedSpatialTurn.SpeechText;
            voice = NormalizeVoice(preparedSpatialTurn.VoiceLevel);
            addressedNpcIds = preparedSpatialTurn.AddressedNpcIds;

            ScenePerceptionResult? actionPerception = null;
            ScenePerceptionResult? speechPerception = null;

            if (action.Length > 0)
            {
                string eventKey = turnKey + ":player-action";
                long entryId = InsertEntry(
                    sessionId,
                    eventKey,
                    playerKey,
                    null,
                    playerId,
                    playerName,
                    "action",
                    action);

                actionPerception = await _perception.ResolveVisualAsync(
                    new SceneVisualEvent
                    {
                        SceneId = sceneId,
                        ActorCharacterKey = playerKey,
                        Text = action,
                        VisualKind = "action",
                        Salience = 0.82,
                        EventKey = eventKey
                    },
                    cancellationToken);

                SavePerceptions(entryId, actionPerception);
                await ImportNpcPerceptionsAsync(actionPerception, cancellationToken);
            }

            if (speech.Length > 0)
            {
                string eventKey = turnKey + ":player-speech";
                long entryId = InsertEntry(
                    sessionId,
                    eventKey,
                    playerKey,
                    null,
                    playerId,
                    playerName,
                    "speech",
                    speech);

                speechPerception = await _perception.ResolveSpeechAsync(
                    new SceneSpeechEvent
                    {
                        SceneId = sceneId,
                        SpeakerCharacterKey = playerKey,
                        Text = speech,
                        VoiceLevel = voice,
                        IntendedListenerKeys = addressedNpcIds
                            .Where(x => x > 0)
                            .Distinct()
                            .Select(NpcKey)
                            .ToArray(),
                        EventKey = eventKey
                    },
                    cancellationToken);

                SavePerceptions(entryId, speechPerception);
                await ImportNpcPerceptionsAsync(speechPerception, cancellationToken);
            }

            request.AddressedNpcIds = addressedNpcIds;

            var candidates = BuildCandidates(
                request,
                playerName,
                action,
                speech,
                actionPerception,
                speechPerception,
                sessionId,
                turnKey);

            var selected = candidates
                .Where(x => x.SelectedForFullBrain)
                .OrderByDescending(x => x.ResponseScore)
                .Take(maxReplies)
                .ToList();

            var responses = new List<GroupSceneNpcResponse>();

            // Sequential on purpose. NPC #2 can hear/see NPC #1 before deciding
            // what to say, while each NPC still receives its own cognition call.
            foreach (var candidate in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response = await GenerateNpcResponseAsync(
                    sessionId,
                    sceneId,
                    playerId,
                    playerName,
                    playerKey,
                    candidate,
                    turnKey,
                    cancellationToken);

                if (response != null)
                    responses.Add(response);
            }

            TouchSession(sessionId);

            var playerView = await GetObserverViewAsync(
                sceneId,
                playerKey,
                actorPlayerId: playerId,
                actorNpcId: null,
                afterSequence: startSequence,
                limit: 200,
                cancellationToken);

            return new GroupSceneTurnResult
            {
                SessionId = sessionId,
                SceneId = sceneId,
                TurnKey = turnKey,
                GameTime = _clock.Now,
                Candidates = candidates,
                NpcResponses = responses,
                PlayerVisibleEntries = playerView
            };
        }
        finally
        {
            sceneGate.Release();
        }
    }

    public Task<IReadOnlyList<GroupSceneDisplayEntry>> GetPlayerViewAsync(
        string sceneId,
        string playerId,
        long afterSequence = 0,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("SceneId is required.", nameof(sceneId));
        if (string.IsNullOrWhiteSpace(playerId))
            throw new ArgumentException("PlayerId is required.", nameof(playerId));

        return GetObserverViewAsync(
            sceneId.Trim(),
            "player:" + playerId.Trim(),
            actorPlayerId: playerId.Trim(),
            actorNpcId: null,
            afterSequence,
            Math.Clamp(limit, 1, 1000),
            cancellationToken);
    }

    public Task<IReadOnlyList<GroupSceneDisplayEntry>> GetNpcViewAsync(
        string sceneId,
        int npcId,
        long afterSequence = 0,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("SceneId is required.", nameof(sceneId));
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        return GetObserverViewAsync(
            sceneId.Trim(),
            NpcKey(npcId),
            actorPlayerId: null,
            actorNpcId: npcId,
            afterSequence,
            Math.Clamp(limit, 1, 1000),
            cancellationToken);
    }

    public async Task EndSceneAsync(
        string sceneId,
        string reason = "scene ended",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            return;

        var gate = SceneLocks.GetOrAdd(sceneId.Trim(), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE GroupSceneSession
SET Status = 'closed', EndReason = $reason, EndedGameTime = $ended, LastGameTime = $ended
WHERE SceneId = $scene AND Status = 'open';";
            cmd.Parameters.AddWithValue("$reason", Clean(reason, "scene ended"));
            cmd.Parameters.AddWithValue("$ended", DbTime(_clock.Now));
            cmd.Parameters.AddWithValue("$scene", sceneId.Trim());
            cmd.ExecuteNonQuery();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GroupSceneNpcResponse?> GenerateNpcResponseAsync(
        long sessionId,
        string sceneId,
        string playerId,
        string playerName,
        string playerKey,
        GroupSceneResponseCandidate candidate,
        string turnKey,
        CancellationToken cancellationToken)
    {
        var npcGate = NpcLocks.GetOrAdd(candidate.NpcId, static _ => new SemaphoreSlim(1, 1));
        await npcGate.WaitAsync(cancellationToken);

        try
        {
            var npc = CharacterRepository.LoadCharacter(candidate.NpcId);
            if (npc == null)
                return null;

            npc.Traits ??= new NpcTraits();
            if (npc.Traits.GetAll().Count == 0)
            {
                try { TraitJsonLoader.ApplyRolledLayers(npc.Traits); }
                catch { npc.Traits.InitializeFastDefaults(); }
            }

            npc.Brain ??= new Brain();
            npc.Brain.Owner = npc;
            npc.Brain.LineBankSpeaker = SpeakerFor(npc.Id);

            var observerContext = await BuildNpcSceneContextAsync(
                sessionId,
                sceneId,
                npc.Id,
                cancellationToken);

            string knowledgeContext;
            try
            {
                knowledgeContext = await _knowledge.BuildPromptContextAsync(
                    npc.Id,
                    playerId,
                    playerName,
                    16,
                    cancellationToken);
            }
            catch
            {
                knowledgeContext = "(no additional personal knowledge context)";
            }

            string currentPerception = BuildCurrentPerceptionForNpc(
                candidate.NpcId,
                sessionId,
                turnKey);

            string spatialContext;
            try
            {
                spatialContext = await _spatial.BuildActorSpatialContextAsync(
                    sceneId, NpcKey(npc.Id), cancellationToken);
            }
            catch
            {
                spatialContext = "(spatial context unavailable)";
            }

            string durableContext =
                "[MULTI-PERSON IN-PERSON SCENE]\n" +
                $"Scene: {sceneId}\n" +
                $"You are {npc.Name}. Other NPCs and up to two players may be present.\n" +
                "CRITICAL: Each mind is separate. Use ONLY what this NPC perceived or already knows.\n" +
                "Do not assume you heard words that are absent or marked fragment/partial.\n" +
                "Do not answer for another NPC. Do not narrate another NPC's private thoughts.\n" +
                "The orchestrator already decided you are interested enough to respond now.\n\n" +
                "RECENT SCENE FROM YOUR POV:\n" + observerContext + "\n\n" +
                "PERSONAL KNOWLEDGE / BELIEFS:\n" + knowledgeContext + "\n\n" +
                "CURRENT PHYSICAL / DISTANCE STATE:\n" + spatialContext + "\n\n" +
                "PHYSICAL RULES:\n" +
                "Distance is world truth. You may approach, hold position, create distance, freeze, or leave space.\n" +
                "Do not explain the hidden reason for your body movement to the player unless you naturally choose to say it.\n" +
                "For a new hug/kiss/grab/strike/sexual-contact action, describe YOUR initiation/attempt only; do not declare the other person's response.\n" +
                "If a contact attempt is directed at you, you may clearly reciprocate, reject/avoid, hesitate, or freeze. Freeze/hesitation is not acceptance.";

            npc.Brain.ConversationContextOverride = durableContext;

            string brainInput =
                "Respond naturally to the current group moment from your own perspective.\n" +
                "Use exactly these labels on separate lines:\n" +
                "BODY: one visible involuntary/ambiguous cue, or NONE\n" +
                "ACTION: one deliberate physical action, or NONE\n" +
                "SAY: only words spoken aloud, or NONE\n" +
                "No Markdown. Do not combine BODY/ACTION/SAY.\n\n" +
                "CURRENT MOMENT YOU PERCEIVED:\n" + currentPerception;

            string raw = await Task.Run(() =>
            {
                npc.Brain.Think(brainInput);
                return npc.Brain.Reply(brainInput);
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(raw))
                raw = "BODY: NONE\nACTION: NONE\nSAY: ...";

            var parsed = ParseNpcReply(raw);
            var produced = new List<GroupSceneDisplayEntry>();

            if (!string.IsNullOrWhiteSpace(parsed.BodyLanguage))
            {
                var spatialBody = await _spatial.ApplyNpcCueAsync(
                    new SceneSpatialCueRequest
                    {
                        SceneId = sceneId,
                        ActorCharacterKey = NpcKey(npc.Id),
                        ActorName = npc.Name,
                        CueText = parsed.BodyLanguage,
                        CueKind = "body_language"
                    },
                    cancellationToken);
                string bodyText = spatialBody.ChangedWorldState
                    ? spatialBody.ResolvedText
                    : parsed.BodyLanguage;

                var item = await RecordNpcVisualAsync(
                    sessionId,
                    sceneId,
                    npc,
                    "body_language",
                    bodyText,
                    0.48,
                    turnKey + $":npc-{npc.Id}-body",
                    cancellationToken);
                if (item != null) produced.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(parsed.Action))
            {
                var spatialAction = await _spatial.ApplyNpcCueAsync(
                    new SceneSpatialCueRequest
                    {
                        SceneId = sceneId,
                        ActorCharacterKey = NpcKey(npc.Id),
                        ActorName = npc.Name,
                        CueText = parsed.Action,
                        CueKind = "action"
                    },
                    cancellationToken);
                string actionText = spatialAction.ChangedWorldState
                    ? spatialAction.ResolvedText
                    : parsed.Action;

                var item = await RecordNpcVisualAsync(
                    sessionId,
                    sceneId,
                    npc,
                    "action",
                    actionText,
                    0.82,
                    turnKey + $":npc-{npc.Id}-action",
                    cancellationToken);
                if (item != null) produced.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(parsed.Speech))
            {
                string eventKey = turnKey + $":npc-{npc.Id}-speech";
                long entryId = InsertEntry(
                    sessionId,
                    eventKey,
                    NpcKey(npc.Id),
                    npc.Id,
                    null,
                    npc.Name,
                    "speech",
                    parsed.Speech);

                var perception = await _perception.ResolveSpeechAsync(
                    new SceneSpeechEvent
                    {
                        SceneId = sceneId,
                        SpeakerCharacterKey = NpcKey(npc.Id),
                        Text = parsed.Speech,
                        VoiceLevel = "normal",
                        IntendedListenerKeys = new[] { playerKey },
                        EventKey = eventKey
                    },
                    cancellationToken);

                SavePerceptions(entryId, perception);
                await ImportNpcPerceptionsAsync(perception, cancellationToken);

                produced.Add(new GroupSceneDisplayEntry
                {
                    EntryId = entryId,
                    Sequence = GetEntrySequence(entryId),
                    EventKey = eventKey,
                    ActorCharacterKey = NpcKey(npc.Id),
                    ActorNpcId = npc.Id,
                    ActorName = npc.Name,
                    EntryType = "speech",
                    Text = parsed.Speech,
                    PerceptionQuality = "exact-produced",
                    PerceptionConfidence = 1.0,
                    GameTime = _clock.Now
                });
            }

            try { CharacterRepository.SaveTraits(npc.Id, npc.Traits); } catch { }

            return new GroupSceneNpcResponse
            {
                NpcId = npc.Id,
                NpcName = npc.Name,
                ResponseScore = candidate.ResponseScore,
                BrainSource = npc.Brain.LastReplySource,
                ExactProducedEntries = produced
            };
        }
        finally
        {
            npcGate.Release();
        }
    }

    private async Task<GroupSceneDisplayEntry?> RecordNpcVisualAsync(
        long sessionId,
        string sceneId,
        SimCharacter npc,
        string kind,
        string text,
        double salience,
        string eventKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        long entryId = InsertEntry(
            sessionId,
            eventKey,
            NpcKey(npc.Id),
            npc.Id,
            null,
            npc.Name,
            kind,
            text);

        var perception = await _perception.ResolveVisualAsync(
            new SceneVisualEvent
            {
                SceneId = sceneId,
                ActorCharacterKey = NpcKey(npc.Id),
                Text = text,
                VisualKind = kind,
                Salience = salience,
                EventKey = eventKey
            },
            cancellationToken);

        SavePerceptions(entryId, perception);
        await ImportNpcPerceptionsAsync(perception, cancellationToken);

        return new GroupSceneDisplayEntry
        {
            EntryId = entryId,
            Sequence = GetEntrySequence(entryId),
            EventKey = eventKey,
            ActorCharacterKey = NpcKey(npc.Id),
            ActorNpcId = npc.Id,
            ActorName = npc.Name,
            EntryType = kind,
            Text = text,
            PerceptionQuality = "exact-produced",
            PerceptionConfidence = 1.0,
            GameTime = _clock.Now
        };
    }

    private List<GroupSceneResponseCandidate> BuildCandidates(
        GroupScenePlayerTurnRequest request,
        string playerName,
        string action,
        string speech,
        ScenePerceptionResult? actionPerception,
        ScenePerceptionResult? speechPerception,
        long sessionId,
        string turnKey)
    {
        var ids = new HashSet<int>();

        if (actionPerception != null)
        {
            foreach (var row in actionPerception.Observers.Where(x => x.Perceived))
            {
                var id = TryParseNpcKey(row.ObserverCharacterKey);
                if (id.HasValue) ids.Add(id.Value);
            }
        }

        if (speechPerception != null)
        {
            foreach (var row in speechPerception.Observers.Where(x => x.Perceived))
            {
                var id = TryParseNpcKey(row.ObserverCharacterKey);
                if (id.HasValue) ids.Add(id.Value);
            }
        }

        var addressed = new HashSet<int>(request.AddressedNpcIds.Where(x => x > 0));
        string combined = (action + " " + speech).Trim();
        int? lastSpeakerNpcId = GetLastNpcSpeaker(sessionId);

        var rows = new List<GroupSceneResponseCandidate>();

        foreach (int npcId in ids.Take(10))
        {
            var npc = CharacterRepository.LoadCharacter(npcId);
            if (npc == null) continue;

            var speechView = speechPerception?.Find(NpcKey(npcId));
            var actionView = actionPerception?.Find(NpcKey(npcId));

            bool wasAddressed = addressed.Contains(npcId) || NameMentioned(combined, npc.Name);
            var reasons = new List<string>();
            double score = 15.0;

            if (wasAddressed)
            {
                score += 45;
                reasons.Add("directly_addressed");
            }

            string speechQuality = speechView?.Quality ?? "none";
            string actionQuality = actionView?.Quality ?? "none";

            score += QualityResponseWeight(speechQuality);
            if (speechView?.Perceived == true)
                reasons.Add("heard_" + speechQuality);

            if (actionView?.Perceived == true)
            {
                score += actionQuality.Equals("clear", StringComparison.OrdinalIgnoreCase) ? 7 : 4;
                reasons.Add("saw_" + actionQuality);
            }

            double distance = MinPositive(
                speechView?.DistanceFeet ?? 0,
                actionView?.DistanceFeet ?? 0);

            if (distance > 0)
            {
                if (distance <= 6) { score += 8; reasons.Add("close_range"); }
                else if (distance <= 12) score += 4;
                else if (distance > 25) { score -= 6; reasons.Add("far_away"); }
            }

            npc.Traits ??= new NpcTraits();
            score += (npc.Traits.Openness - 50) * 0.12;
            score += (50 - npc.Traits.Guard) * 0.10;
            score += (npc.Traits.Playfulness - 50) * 0.04;
            score += (npc.Traits.Tension - 50) * 0.04;

            var rel = FindPlayerRelationship(npc, playerName);
            if (rel != null)
            {
                score += (rel.Trust - 50) * 0.08;
                score += (rel.Affection - 50) * 0.04;
                if (rel.Trust >= 70) reasons.Add("trusted_player");
            }

            if (lastSpeakerNpcId == npcId)
            {
                score -= 10;
                reasons.Add("recent_speaker_cooldown");
            }

            score += StableJitter(turnKey, npcId, -4.0, 4.0);
            score = Math.Clamp(score, 0, 100);

            // Directly addressed NPCs still need to have perceived at least some
            // action/speech. Everyone else needs stronger spontaneous interest.
            bool perceivedAnything = speechView?.Perceived == true || actionView?.Perceived == true;
            bool wouldRespond = perceivedAnything && score >= (wasAddressed ? 28 : 42);

            rows.Add(new GroupSceneResponseCandidate
            {
                NpcId = npcId,
                NpcName = npc.Name,
                DistanceFeet = distance,
                ResponseScore = Math.Round(score, 1),
                Addressed = wasAddressed,
                SelectedForFullBrain = wouldRespond,
                SpeechQuality = speechQuality,
                ActionQuality = actionQuality,
                ReasonCodes = reasons
            });
        }

        // Enforce the Brain-call cap here too so the result clearly shows who
        // was actually selected, not just who crossed the interest threshold.
        int cap = Math.Clamp(request.MaxNpcReplies, 1, 4);
        var actual = rows
            .Where(x => x.SelectedForFullBrain)
            .OrderByDescending(x => x.Addressed)
            .ThenByDescending(x => x.ResponseScore)
            .Take(cap)
            .Select(x => x.NpcId)
            .ToHashSet();

        foreach (var row in rows)
            row.SelectedForFullBrain = actual.Contains(row.NpcId);

        return rows
            .OrderByDescending(x => x.SelectedForFullBrain)
            .ThenByDescending(x => x.ResponseScore)
            .ToList();
    }

    private async Task<string> BuildNpcSceneContextAsync(
        long sessionId,
        string sceneId,
        int npcId,
        CancellationToken cancellationToken)
    {
        var rows = await GetObserverViewAsync(
            sceneId,
            NpcKey(npcId),
            actorPlayerId: null,
            actorNpcId: npcId,
            afterSequence: 0,
            limit: 40,
            cancellationToken);

        if (rows.Count == 0)
            return "(no scene events clearly perceived yet)";

        var sb = new StringBuilder();
        foreach (var row in rows.TakeLast(28))
        {
            sb.Append('[')
              .Append(row.GameTime.ToString("h:mm tt", CultureInfo.InvariantCulture))
              .Append("] ")
              .Append(row.ActorName)
              .Append(' ')
              .Append(row.EntryType.ToUpperInvariant())
              .Append(": ")
              .Append(row.Text);

            if (!row.PerceptionQuality.Equals("clear", StringComparison.OrdinalIgnoreCase) &&
                !row.PerceptionQuality.Equals("exact-produced", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" [perception: ").Append(row.PerceptionQuality).Append(']');
            }

            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private string BuildCurrentPerceptionForNpc(int npcId, long sessionId, string turnKey)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT e.EntryType, e.ActorName, p.Quality, p.PerceivedText
FROM GroupSceneEntry e
JOIN GroupScenePerception p ON p.EntryId = e.Id
WHERE e.SessionId = $sid
  AND e.EventKey LIKE $turn
  AND p.ObserverCharacterKey = $observer
  AND p.Quality <> 'none'
ORDER BY e.Sequence;";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$turn", turnKey + "%");
        cmd.Parameters.AddWithValue("$observer", NpcKey(npcId));

        using var reader = cmd.ExecuteReader();
        var parts = new List<string>();
        while (reader.Read())
        {
            string kind = reader.GetString(0);
            string actor = reader.GetString(1);
            string quality = reader.GetString(2);
            string text = reader.GetString(3);
            parts.Add($"{actor} {kind.ToUpperInvariant()}: {text} [perception: {quality}]");
        }

        return parts.Count == 0
            ? "[No clear part of the current player turn was perceived.]"
            : string.Join("\n", parts);
    }

    private async Task<IReadOnlyList<GroupSceneDisplayEntry>> GetObserverViewAsync(
        string sceneId,
        string observerCharacterKey,
        string? actorPlayerId,
        int? actorNpcId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        long? sessionId = GetOpenOrLatestSessionId(sceneId);
        if (!sessionId.HasValue)
            return Array.Empty<GroupSceneDisplayEntry>();

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    e.Id,
    e.Sequence,
    e.EventKey,
    e.ActorCharacterKey,
    e.ActorNpcId,
    e.ActorPlayerId,
    e.ActorName,
    e.EntryType,
    e.ExactText,
    e.GameTime,
    p.Quality,
    p.PerceivedText,
    p.Confidence
FROM GroupSceneEntry e
LEFT JOIN GroupScenePerception p
    ON p.EntryId = e.Id
   AND p.ObserverCharacterKey = $observer
WHERE e.SessionId = $sid
  AND e.Sequence > $after
  AND (
      p.Quality IS NOT NULL AND p.Quality <> 'none'
      OR ($actorPlayer IS NOT NULL AND e.ActorPlayerId = $actorPlayer)
      OR ($actorNpc IS NOT NULL AND e.ActorNpcId = $actorNpc)
      OR e.EntryType IN ('scene','scene_update')
  )
ORDER BY e.Sequence
LIMIT $limit;";
        cmd.Parameters.AddWithValue("$observer", observerCharacterKey);
        cmd.Parameters.AddWithValue("$sid", sessionId.Value);
        cmd.Parameters.AddWithValue("$after", Math.Max(0, afterSequence));
        cmd.Parameters.AddWithValue("$actorPlayer", (object?)actorPlayerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$actorNpc", (object?)actorNpcId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        var list = new List<GroupSceneDisplayEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long entryId = reader.GetInt64(0);
            long sequence = reader.GetInt64(1);
            string eventKey = reader.GetString(2);
            string actorKey = reader.GetString(3);
            int? rowNpcId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
            string? rowPlayerId = reader.IsDBNull(5) ? null : reader.GetString(5);
            string actorName = reader.GetString(6);
            string kind = reader.GetString(7);
            string exact = reader.GetString(8);
            DateTimeOffset gameTime = ParseDbTime(reader.GetString(9));
            string? quality = reader.IsDBNull(10) ? null : reader.GetString(10);
            string? perceived = reader.IsDBNull(11) ? null : reader.GetString(11);
            double confidence = reader.IsDBNull(12) ? 1.0 : reader.GetDouble(12);

            bool own =
                (actorPlayerId != null && rowPlayerId != null &&
                 rowPlayerId.Equals(actorPlayerId, StringComparison.OrdinalIgnoreCase)) ||
                (actorNpcId.HasValue && rowNpcId == actorNpcId);

            list.Add(new GroupSceneDisplayEntry
            {
                EntryId = entryId,
                Sequence = sequence,
                EventKey = eventKey,
                ActorCharacterKey = actorKey,
                ActorNpcId = rowNpcId,
                ActorPlayerId = rowPlayerId,
                ActorName = actorName,
                EntryType = kind,
                Text = own || kind.Equals("scene", StringComparison.OrdinalIgnoreCase) ||
                       kind.Equals("scene_update", StringComparison.OrdinalIgnoreCase)
                    ? exact
                    : (perceived ?? ""),
                PerceptionQuality = own
                    ? "exact-produced"
                    : (kind.Equals("scene", StringComparison.OrdinalIgnoreCase) ||
                       kind.Equals("scene_update", StringComparison.OrdinalIgnoreCase)
                        ? "clear"
                        : (quality ?? "none")),
                PerceptionConfidence = own ||
                    kind.Equals("scene", StringComparison.OrdinalIgnoreCase) ||
                    kind.Equals("scene_update", StringComparison.OrdinalIgnoreCase)
                    ? 1.0
                    : confidence,
                GameTime = gameTime
            });
        }

        return list;
    }

    private async Task ImportNpcPerceptionsAsync(
        ScenePerceptionResult? result,
        CancellationToken cancellationToken)
    {
        if (result == null)
            return;

        var ids = result.Observers
            .Where(x => x.Perceived)
            .Select(x => TryParseNpcKey(x.ObserverCharacterKey))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        foreach (int npcId in ids)
        {
            try { await _knowledge.ImportScenePerceptionAsync(npcId, cancellationToken); }
            catch { }
        }
    }

    private void SavePerceptions(long entryId, ScenePerceptionResult? result)
    {
        if (result == null)
            return;

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        foreach (var row in result.Observers)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO GroupScenePerception
(
    EntryId, ObserverCharacterKey, ObserverNpcId, ObserverPlayerId,
    Quality, PerceivedText, Confidence, DistanceFeet, RecordedGameTime
)
VALUES
(
    $entry, $observer, $npc, $player,
    $quality, $text, $confidence, $distance, $time
)
ON CONFLICT(EntryId, ObserverCharacterKey) DO UPDATE SET
    Quality = excluded.Quality,
    PerceivedText = excluded.PerceivedText,
    Confidence = excluded.Confidence,
    DistanceFeet = excluded.DistanceFeet,
    RecordedGameTime = excluded.RecordedGameTime;";

            cmd.Parameters.AddWithValue("$entry", entryId);
            cmd.Parameters.AddWithValue("$observer", row.ObserverCharacterKey);

            var npcId = TryParseNpcKey(row.ObserverCharacterKey);
            cmd.Parameters.AddWithValue("$npc", (object?)npcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$player", (object?)TryParsePlayerKey(row.ObserverCharacterKey) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$quality", row.Quality ?? "none");
            cmd.Parameters.AddWithValue("$text", row.PerceivedText ?? "");
            cmd.Parameters.AddWithValue("$confidence", row.Confidence);
            cmd.Parameters.AddWithValue("$distance", row.DistanceFeet);
            cmd.Parameters.AddWithValue("$time", DbTime(_clock.Now));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private long InsertEntry(
        long sessionId,
        string eventKey,
        string actorCharacterKey,
        int? actorNpcId,
        string? actorPlayerId,
        string actorName,
        string entryType,
        string exactText)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        long sequence;
        using (var seq = conn.CreateCommand())
        {
            seq.Transaction = tx;
            seq.CommandText = "SELECT COALESCE(MAX(Sequence), 0) + 1 FROM GroupSceneEntry WHERE SessionId = $sid;";
            seq.Parameters.AddWithValue("$sid", sessionId);
            sequence = Convert.ToInt64(seq.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO GroupSceneEntry
(
    SessionId, Sequence, EventKey, ActorCharacterKey, ActorNpcId, ActorPlayerId,
    ActorName, EntryType, ExactText, GameTime
)
VALUES
(
    $sid, $seq, $event, $actor, $npc, $player,
    $name, $kind, $text, $time
);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$seq", sequence);
        cmd.Parameters.AddWithValue("$event", eventKey);
        cmd.Parameters.AddWithValue("$actor", actorCharacterKey);
        cmd.Parameters.AddWithValue("$npc", (object?)actorNpcId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$player", (object?)actorPlayerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", actorName);
        cmd.Parameters.AddWithValue("$kind", entryType);
        cmd.Parameters.AddWithValue("$text", exactText);
        cmd.Parameters.AddWithValue("$time", DbTime(_clock.Now));

        long id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        tx.Commit();
        return id;
    }

    private long GetOrStartSession(string sceneId)
    {
        using var conn = Open();

        using (var find = conn.CreateCommand())
        {
            find.CommandText = @"
SELECT Id
FROM GroupSceneSession
WHERE SceneId = $scene AND Status = 'open'
ORDER BY Id DESC
LIMIT 1;";
            find.Parameters.AddWithValue("$scene", sceneId);
            var value = find.ExecuteScalar();
            if (value != null && value != DBNull.Value)
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO GroupSceneSession
(SceneId, Status, StartedGameTime, LastGameTime, EndReason)
VALUES ($scene, 'open', $time, $time, '');
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        cmd.Parameters.AddWithValue("$time", DbTime(_clock.Now));
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private long? GetOpenSessionId(string sceneId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id
FROM GroupSceneSession
WHERE SceneId = $scene AND Status = 'open'
ORDER BY Id DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private long? GetOpenOrLatestSessionId(string sceneId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id
FROM GroupSceneSession
WHERE SceneId = $scene
ORDER BY CASE WHEN Status = 'open' THEN 0 ELSE 1 END, Id DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$scene", sceneId);
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value
            ? null
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private long GetLastSequence(long sessionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(Sequence), 0) FROM GroupSceneEntry WHERE SessionId = $sid;";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private long GetEntrySequence(long entryId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Sequence FROM GroupSceneEntry WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", entryId);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private int? GetLastNpcSpeaker(long sessionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT ActorNpcId
FROM GroupSceneEntry
WHERE SessionId = $sid AND EntryType = 'speech' AND ActorNpcId IS NOT NULL
ORDER BY Sequence DESC
LIMIT 1;";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        var value = cmd.ExecuteScalar();
        if (value == null || value == DBNull.Value)
            return null;
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private void TouchSession(long sessionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE GroupSceneSession SET LastGameTime = $time WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$time", DbTime(_clock.Now));
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS GroupSceneSession
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SceneId TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'open',
    StartedGameTime TEXT NOT NULL,
    LastGameTime TEXT NOT NULL,
    EndedGameTime TEXT NULL,
    EndReason TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS IX_GroupSceneSession_Scene_Status
ON GroupSceneSession(SceneId, Status, Id);

CREATE TABLE IF NOT EXISTS GroupSceneEntry
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId INTEGER NOT NULL,
    Sequence INTEGER NOT NULL,
    EventKey TEXT NOT NULL,
    ActorCharacterKey TEXT NOT NULL,
    ActorNpcId INTEGER NULL,
    ActorPlayerId TEXT NULL,
    ActorName TEXT NOT NULL,
    EntryType TEXT NOT NULL,
    ExactText TEXT NOT NULL,
    GameTime TEXT NOT NULL,
    FOREIGN KEY(SessionId) REFERENCES GroupSceneSession(Id)
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_GroupSceneEntry_Session_Sequence
ON GroupSceneEntry(SessionId, Sequence);

CREATE INDEX IF NOT EXISTS IX_GroupSceneEntry_Event
ON GroupSceneEntry(EventKey);

CREATE TABLE IF NOT EXISTS GroupScenePerception
(
    EntryId INTEGER NOT NULL,
    ObserverCharacterKey TEXT NOT NULL,
    ObserverNpcId INTEGER NULL,
    ObserverPlayerId TEXT NULL,
    Quality TEXT NOT NULL,
    PerceivedText TEXT NOT NULL,
    Confidence REAL NOT NULL,
    DistanceFeet REAL NOT NULL,
    RecordedGameTime TEXT NOT NULL,
    PRIMARY KEY(EntryId, ObserverCharacterKey),
    FOREIGN KEY(EntryId) REFERENCES GroupSceneEntry(Id)
);

CREATE INDEX IF NOT EXISTS IX_GroupScenePerception_Observer
ON GroupScenePerception(ObserverCharacterKey, EntryId);
";
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private static void Validate(GroupScenePlayerTurnRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.SceneId))
            throw new ArgumentException("SceneId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PlayerId))
            throw new ArgumentException("PlayerId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ActionText) && string.IsNullOrWhiteSpace(request.SpeechText))
            throw new ArgumentException("At least ActionText or SpeechText is required.", nameof(request));
    }

    private static string NormalizeVoice(string? value)
    {
        string v = Clean(value, "normal").ToLowerInvariant();
        return v is "whisper" or "quiet" or "normal" or "raised" or "shout"
            ? v
            : "normal";
    }

    private static double QualityResponseWeight(string quality)
        => (quality ?? "none").ToLowerInvariant() switch
        {
            "clear" => 18,
            "partial" => 11,
            "fragment" => 5,
            "glimpse" => 3,
            _ => 0
        };

    private static double MinPositive(double a, double b)
    {
        if (a <= 0) return Math.Max(0, b);
        if (b <= 0) return Math.Max(0, a);
        return Math.Min(a, b);
    }

    private static bool NameMentioned(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name))
            return false;

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.Length < 3) continue;
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(part)}\b", RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    private static Relationship? FindPlayerRelationship(SimCharacter npc, string playerName)
        => npc.Relationships?.FirstOrDefault(x =>
            x.TargetName.Equals(playerName, StringComparison.OrdinalIgnoreCase));

    private static double StableJitter(string key, int npcId, double min, double max)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key + "|" + npcId));
        uint raw = BitConverter.ToUInt32(bytes, 0);
        double unit = raw / (double)uint.MaxValue;
        return min + (max - min) * unit;
    }

    private static string SpeakerFor(int npcId)
        => npcId switch
        {
            1 => "eve2",
            2 => "adam",
            _ => "npc_" + npcId.ToString(CultureInfo.InvariantCulture)
        };

    private static string NpcKey(int npcId)
        => "npc:" + npcId.ToString(CultureInfo.InvariantCulture);

    private static int? TryParseNpcKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            !key.StartsWith("npc:", StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(key[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 0
            ? id
            : null;
    }

    private static string? TryParsePlayerKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            !key.StartsWith("player:", StringComparison.OrdinalIgnoreCase))
            return null;

        string value = key[7..].Trim();
        return value.Length == 0 ? null : value;
    }

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string DbTime(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDbTime(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private sealed record NpcReplyParts(string BodyLanguage, string Action, string Speech);

    private static NpcReplyParts ParseNpcReply(string raw)
    {
        var body = new List<string>();
        var action = new List<string>();
        var say = new List<string>();
        var loose = new List<string>();

        foreach (var sourceLine in (raw ?? "").Replace("\r", "").Split('\n'))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0) continue;

            if (TryTakeMixedLabel(line, "BODY LANGUAGE:", body, say) ||
                TryTakeMixedLabel(line, "BODY:", body, say) ||
                TryTakeMixedLabel(line, "PRESENTATION:", body, say) ||
                TryTakeMixedLabel(line, "LEAKS:", body, say) ||
                TryTakeMixedLabel(line, "ACTION:", action, say))
                continue;

            if (TryTakeSpeechLabel(line, "SAY:", say))
                continue;

            if (SplitMarkdownSpeech(line, out var presentation, out var spoken))
            {
                if (!string.IsNullOrWhiteSpace(presentation)) body.Add(presentation);
                if (!string.IsNullOrWhiteSpace(spoken)) say.Add(spoken);
                continue;
            }

            loose.Add(line);
        }

        if (loose.Count > 0)
        {
            if (say.Count == 0) say.Add(string.Join(" ", loose));
            else body.AddRange(loose);
        }

        return new NpcReplyParts(
            CleanPart(body),
            CleanPart(action),
            CleanSpeech(CleanPart(say)));
    }

    private static bool TryTakeMixedLabel(
        string line,
        string label,
        List<string> presentationTarget,
        List<string> speechTarget)
    {
        if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            return false;

        var value = line[label.Length..].Trim();
        if (value.Length == 0 || value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return true;

        if (SplitMarkdownSpeech(value, out var presentation, out var spoken))
        {
            if (!string.IsNullOrWhiteSpace(presentation)) presentationTarget.Add(presentation);
            if (!string.IsNullOrWhiteSpace(spoken)) speechTarget.Add(spoken);
        }
        else
        {
            presentationTarget.Add(CleanDisplayMarkup(value));
        }
        return true;
    }

    private static bool TryTakeSpeechLabel(string line, string label, List<string> target)
    {
        if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            return false;

        var value = line[label.Length..].Trim();
        if (value.Length > 0 && !value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            target.Add(CleanDisplayMarkup(value));
        return true;
    }

    private static bool SplitMarkdownSpeech(string value, out string presentation, out string spoken)
    {
        value ??= "";
        var matches = Regex.Matches(
            value,
            @"\*{2,3}(?<speech>.+?)\*{2,3}",
            RegexOptions.Singleline);

        if (matches.Count == 0)
        {
            presentation = "";
            spoken = "";
            return false;
        }

        var spokenParts = new List<string>();
        foreach (Match match in matches)
        {
            var part = match.Groups["speech"].Value.Trim();
            if (part.Length > 0) spokenParts.Add(part);
        }

        var remaining = Regex.Replace(
            value,
            @"\*{2,3}.+?\*{2,3}",
            " ",
            RegexOptions.Singleline);

        presentation = CleanDisplayMarkup(remaining);
        spoken = CleanDisplayMarkup(string.Join(" ", spokenParts));
        return spoken.Length > 0;
    }

    private static string CleanDisplayMarkup(string text)
    {
        var s = (text ?? "").Trim();
        s = Regex.Replace(s, @"(?<!\*)\*(?!\*)", "");
        s = s.Replace("**", "").Trim();
        s = Regex.Replace(s, @"\s{2,}", " ");
        return s.Trim(' ', '-', '—', '–');
    }

    private static string CleanPart(IEnumerable<string> values)
        => CleanDisplayMarkup(string.Join(" ", values));

    private static string CleanSpeech(string text)
    {
        var s = CleanDisplayMarkup(text);
        if (s.Length >= 2 &&
            ((s.StartsWith('"') && s.EndsWith('"')) ||
             (s.StartsWith('“') && s.EndsWith('”'))))
        {
            s = s[1..^1].Trim();
        }
        return s;
    }
}
