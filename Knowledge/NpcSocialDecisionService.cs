using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Core.Knowledge;
using ProjectEve.Core.Scene;
using ProjectEve.Core.Time;
using ProjectEve.Relationships;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Knowledge;

/// <summary>
/// Phase 8 social-disclosure decision layer.
///
/// It decides whether an NPC is inclined to share, gossip, warn, confront,
/// hint, distort, deflect, or keep a known claim private. It uses the source
/// NPC's current traits, relationship to the proposed recipient, claim
/// confidence/generation, scene audience, and explicit motive/risk context.
///
/// Critical rules:
/// - It does not invent a claim.
/// - It never grants knowledge just because two NPCs are close.
/// - It does not let a hidden bystander appear in player UI.
/// - In-person execution goes through Phase 6 hearing, so unintended people
///   can overhear.
/// - Phase 7 stores what recipients actually heard/read as their own claim.
/// </summary>
public sealed class NpcSocialDecisionService : INpcSocialDecisionService
{
    private readonly INpcKnowledgeService _knowledge;
    private readonly INpcKnowledgeCommunicationService _communication;
    private readonly IScenePerceptionService _scene;
    private readonly IGameTimeService _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dbPath;

    public NpcSocialDecisionService(
        INpcKnowledgeService knowledge,
        INpcKnowledgeCommunicationService communication,
        IScenePerceptionService scene,
        IGameTimeService clock)
    {
        _knowledge = knowledge;
        _communication = communication;
        _scene = scene;
        _clock = clock;

        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<NpcSocialDecisionResult> DecideAsync(
        NpcSocialDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateDecisionRequest(request);

        var evaluated = await EvaluateAsync(request, persist: false, cancellationToken: cancellationToken);
        var fingerprint = BuildFingerprint(evaluated.Snapshot);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();

            var existing = LoadRecentMatchingDecision(conn, fingerprint, _clock.Now);
            if (existing != null)
                return existing;

            var id = InsertDecision(conn, evaluated.Result, evaluated.Snapshot, fingerprint);
            evaluated.Result.DecisionId = id;
            return evaluated.Result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<NpcSocialRecipientOption>> RankRecipientsAsync(
        NpcSocialRecipientSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.SourceNpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.SourceNpcId));
        if (request.ClaimId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ClaimId));
        if (string.IsNullOrWhiteSpace(request.SceneId))
            return Array.Empty<NpcSocialRecipientOption>();

        var sourceKey = NpcKey(request.SourceNpcId);
        var perceived = await _scene.GetPerceivedPresenceAsync(
            request.SceneId.Trim(),
            sourceKey,
            cancellationToken);

        var options = new List<NpcSocialRecipientOption>();
        foreach (var person in perceived)
        {
            if (!person.NpcId.HasValue ||
                person.NpcId.Value <= 0 ||
                person.NpcId.Value == request.SourceNpcId)
            {
                continue;
            }

            var decisionRequest = new NpcSocialDecisionRequest
            {
                SourceNpcId = request.SourceNpcId,
                ClaimId = request.ClaimId,
                TargetNpcId = person.NpcId.Value,
                PlayerId = request.PlayerId ?? "",
                SceneId = request.SceneId ?? "",
                Channel = request.Channel ?? "in_person",
                Motive = request.Motive ?? "casual",
                Secrecy = request.Secrecy,
                Urgency = request.Urgency,
                ConsequenceRisk = request.ConsequenceRisk,
                SubjectNpcId = request.SubjectNpcId,
                PrivateChannel = request.PrivateChannel
            };

            var evaluated = await EvaluateAsync(
                decisionRequest,
                persist: false,
                cancellationToken: cancellationToken);

            options.Add(new NpcSocialRecipientOption
            {
                TargetNpcId = person.NpcId.Value,
                TargetName = person.DisplayName,
                DistanceFeet = person.DistanceFeet,
                ShareScore = evaluated.Result.ShareScore,
                PrivacyScore = evaluated.Result.PrivacyScore,
                SuggestedAction = evaluated.Result.Action,
                WouldSpeak = evaluated.Result.ShouldSpeak
            });
        }

        var max = Math.Clamp(request.MaxResults, 1, 12);
        return options
            .OrderByDescending(x => x.WouldSpeak)
            .ThenByDescending(x => x.ShareScore)
            .ThenBy(x => x.DistanceFeet)
            .Take(max)
            .ToList();
    }

    public async Task<NpcSocialExecutionResult> ExecuteAsync(
        NpcSocialExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.DecisionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.DecisionId));

        DecisionRow row;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            row = LoadDecisionRow(conn, request.DecisionId)
                ?? throw new InvalidOperationException(
                    $"Phase 8 social decision {request.DecisionId} was not found.");

            if (row.ExecutionStatus.Equals("executed", StringComparison.OrdinalIgnoreCase))
            {
                return new NpcSocialExecutionResult
                {
                    DecisionId = row.Id,
                    Executed = false,
                    Reason = "Decision was already executed."
                };
            }
        }
        finally
        {
            _gate.Release();
        }

        var actualText = (request.ActualText ?? "").Trim();
        var shouldSpeak = ActionShouldSpeak(row.Action);
        var shouldTransfer = ActionTransfersClaim(row.Action);

        if (shouldSpeak && actualText.Length == 0)
        {
            return new NpcSocialExecutionResult
            {
                DecisionId = row.Id,
                Reason = "ActualText is required because Phase 8 stores/transmits only the words actually spoken or sent."
            };
        }

        var result = new NpcSocialExecutionResult
        {
            DecisionId = row.Id,
            Executed = true,
            Reason = row.Action
        };

        if (!shouldSpeak)
        {
            await MarkExecutedAsync(row.Id, actualText, cancellationToken);
            return result;
        }

        var channel = Clean(row.Channel, "in_person").ToLowerInvariant();
        var voice = string.IsNullOrWhiteSpace(request.VoiceLevelOverride)
            ? Clean(row.VoiceLevel, "normal")
            : request.VoiceLevelOverride!.Trim();

        if (channel == "in_person" && !string.IsNullOrWhiteSpace(row.SceneId))
        {
            if (shouldTransfer)
            {
                var spoken = await _communication.SpeakKnownClaimAsync(
                    new NpcKnowledgeSpeechRequest
                    {
                        FromNpcId = row.SourceNpcId,
                        SourceClaimId = row.ClaimId,
                        SceneId = row.SceneId,
                        SpokenText = actualText,
                        VoiceLevel = voice,
                        IntendedNpcIds = new[] { row.TargetNpcId },
                        PlayerId = row.PlayerId,
                        Channel = "in_person"
                    },
                    cancellationToken);

                result.HeardByNpcCount = spoken.HeardByNpcCount;

                var intended = spoken.Recipients.FirstOrDefault(x => x.NpcId == row.TargetNpcId);
                if (intended != null)
                {
                    result.KnowledgeTransferred = intended.KnowledgeTransferred;
                    result.IntendedRecipientClaimId = intended.RecipientClaimId;
                    result.IntendedTransmissionId = intended.TransmissionId;
                }

                result.OtherNpcIdsWhoHeard = spoken.Recipients
                    .Where(x => x.NpcId != row.TargetNpcId)
                    .Select(x => x.NpcId)
                    .Distinct()
                    .ToArray();
            }
            else
            {
                // Hint/deflect speech is real speech, but it is not permission to
                // silently transfer the hidden source claim metadata. Phase 6 still
                // records what bystanders actually heard.
                var perception = await _scene.ResolveSpeechAsync(
                    new SceneSpeechEvent
                    {
                        SceneId = row.SceneId,
                        SpeakerCharacterKey = NpcKey(row.SourceNpcId),
                        Text = actualText,
                        VoiceLevel = voice,
                        IntendedListenerKeys = new[] { NpcKey(row.TargetNpcId) },
                        EventKey = $"social:{row.Id}:{Guid.NewGuid():N}"
                    },
                    cancellationToken);

                var heardNpcIds = perception.Observers
                    .Where(x => x.Perceived)
                    .Select(x => TryParseNpcKey(x.ObserverCharacterKey))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .ToArray();

                result.HeardByNpcCount = heardNpcIds.Length;
                result.OtherNpcIdsWhoHeard = heardNpcIds
                    .Where(x => x != row.TargetNpcId)
                    .ToArray();

                // Import the actual heard fragments as generic personal knowledge.
                // This does NOT copy the hidden claim subject/key to the listeners.
                foreach (var observer in perception.Observers.Where(x => x.Perceived))
                {
                    var observerNpcId = TryParseNpcKey(observer.ObserverCharacterKey);
                    if (!observerNpcId.HasValue || observerNpcId.Value == row.SourceNpcId)
                        continue;

                    var recorded = await _knowledge.RecordAsync(
                        new NpcKnowledgeRecordRequest
                        {
                            HolderNpcId = observerNpcId.Value,
                            PlayerId = row.PlayerId,
                            SubjectKey = NpcKey(row.SourceNpcId),
                            ClaimKey = row.Action.Equals("hint", StringComparison.OrdinalIgnoreCase)
                                ? "heard_social_hint"
                                : "heard_social_deflection",
                            ClaimText = observer.PerceivedText,
                            Confidence = Math.Clamp((int)Math.Round(observer.Confidence * 100.0), 10, 95),
                            SourceType = "social_" + row.Action,
                            SourceNpcId = row.SourceNpcId,
                            SourceCharacterKey = NpcKey(row.SourceNpcId),
                            OriginClaimId = row.ClaimId,
                            Generation = 0,
                            Status = observer.Quality.Equals("clear", StringComparison.OrdinalIgnoreCase)
                                ? "held"
                                : "uncertain",
                            LearnedGameTime = _clock.Now
                        },
                        cancellationToken);

                    if (observerNpcId.Value == row.TargetNpcId && recorded != null)
                        result.IntendedRecipientClaimId = recorded.Id;
                }
            }
        }
        else if (shouldTransfer)
        {
            // Direct phone/text/private channel. The intended recipient receives
            // exactly the actual text; no scene bystanders are implied.
            var transfer = await _knowledge.TransmitAsync(
                new NpcKnowledgeTransmissionRequest
                {
                    FromNpcId = row.SourceNpcId,
                    ToNpcId = row.TargetNpcId,
                    SourceClaimId = row.ClaimId,
                    PlayerId = row.PlayerId,
                    ReportedText = actualText,
                    Channel = channel,
                    SceneId = row.SceneId,
                    GameTime = _clock.Now
                },
                cancellationToken);

            result.KnowledgeTransferred = transfer.Transmitted;
            result.IntendedRecipientClaimId = transfer.RecipientClaim?.Id ?? 0;
            result.IntendedTransmissionId = transfer.TransmissionId;
            result.Reason = transfer.Reason;
        }
        else
        {
            // A direct-channel hint/deflection is not a transfer of the hidden
            // source claim. The normal conversation/message transcript remains
            // the evidence for the actual words.
            result.KnowledgeTransferred = false;
        }

        await MarkExecutedAsync(row.Id, actualText, cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<NpcSocialDecisionAudit>> GetRecentDecisionsAsync(
        int sourceNpcId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (sourceNpcId <= 0)
            return Array.Empty<NpcSocialDecisionAudit>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id,SourceNpcId,ClaimId,TargetNpcId,Action,
                       ShareScore,PrivacyScore,DistortionScore,ConfrontScore,
                       AudienceCount,Motive,Channel,DecisionGameTime,
                       ExecutionStatus,ExecutedGameTime
                FROM NpcSocialDecision
                WHERE SourceNpcId=$source
                ORDER BY Id DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$source", sourceNpcId);
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

            var rows = new List<NpcSocialDecisionAudit>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new NpcSocialDecisionAudit
                {
                    Id = r.GetInt64(0),
                    SourceNpcId = r.GetInt32(1),
                    ClaimId = r.GetInt64(2),
                    TargetNpcId = r.GetInt32(3),
                    Action = r.GetString(4),
                    ShareScore = r.GetDouble(5),
                    PrivacyScore = r.GetDouble(6),
                    DistortionScore = r.GetDouble(7),
                    ConfrontScore = r.GetDouble(8),
                    AudienceCount = r.GetInt32(9),
                    Motive = r.GetString(10),
                    Channel = r.GetString(11),
                    DecisionGameTime = ParseTime(r.GetString(12), _clock.Now),
                    ExecutionStatus = r.GetString(13),
                    ExecutedGameTime = r.IsDBNull(14)
                        ? null
                        : ParseTime(r.GetString(14), _clock.Now)
                });
            }

            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Evaluation> EvaluateAsync(
        NpcSocialDecisionRequest request,
        bool persist,
        CancellationToken cancellationToken)
    {
        _ = persist; // retained so future orchestrators can request non-persisting evaluation explicitly.

        var claims = await _knowledge.GetKnowledgeAsync(
            request.SourceNpcId,
            playerId: null,
            limit: 500,
            cancellationToken: cancellationToken);

        var claim = claims.FirstOrDefault(x => x.Id == request.ClaimId)
            ?? throw new InvalidOperationException(
                $"NPC {request.SourceNpcId} does not own knowledge claim {request.ClaimId}.");

        var source = CharacterRepository.LoadCharacter(request.SourceNpcId)
            ?? throw new InvalidOperationException(
                $"Could not load source NPC {request.SourceNpcId}.");

        var target = CharacterRepository.LoadCharacter(request.TargetNpcId)
            ?? throw new InvalidOperationException(
                $"Could not load target NPC {request.TargetNpcId}.");

        if (!source.Traits.GetAll().Any())
            source.Traits.InitializeFastDefaults();

        var relationship = FindRelationship(source, target);
        var subjectNpcId = request.SubjectNpcId ?? ParseNpcSubject(claim.SubjectKey);
        var targetIsSubject = subjectNpcId.HasValue && subjectNpcId.Value == target.Id;

        var audienceCount = 0;
        if (!request.PrivateChannel &&
            request.Channel.Equals("in_person", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(request.SceneId))
        {
            try
            {
                var perceived = await _scene.GetPerceivedPresenceAsync(
                    request.SceneId.Trim(),
                    NpcKey(source.Id),
                    cancellationToken);

                audienceCount = perceived.Count(x =>
                    !x.CharacterKey.Equals(NpcKey(source.Id), StringComparison.OrdinalIgnoreCase) &&
                    !x.CharacterKey.Equals(NpcKey(target.Id), StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // A missing scene should not manufacture an audience. The caller can
                // retry once the Phase 6 scene has been registered.
                audienceCount = 0;
            }
        }

        var secrecy = Clamp100(request.Secrecy);
        var urgency = Clamp100(request.Urgency);
        var risk = Clamp100(request.ConsequenceRisk);
        var motive = NormalizeMotive(request.Motive);

        var openness = source.Traits.Openness;
        var guard = source.Traits.Guard;
        var anger = source.Traits.Anger;
        var anxiety = source.Traits.Anxiety;
        var fear = source.Traits.Fear;
        var shame = source.Traits.Shame;
        var guilt = source.Traits.Guilt;
        var jealousy = source.Traits.Jealousy;
        var resentment = source.Traits.Resentment;
        var globalTrust = source.Traits.Trust;
        var affection = source.Traits.Affection;
        var pride = source.Traits.Pride;
        var patience = source.Traits.Patience;
        var tension = source.Traits.Tension;

        var relTrust = relationship?.Trust ?? 50;
        var relRespect = relationship?.Respect ?? 50;
        var relAffection = relationship?.Affection ?? 50;
        var relTension = relationship?.Tension ?? 0;

        var audiencePenalty = request.PrivateChannel
            ? 0.0
            : audienceCount * (2.1 + Math.Max(0, guard - 50) * 0.025 + secrecy * 0.012);

        var repeatPenalty = await RecentExecutedSharePenaltyAsync(
            request.SourceNpcId,
            request.ClaimId,
            request.TargetNpcId,
            request.AskedDirectly,
            cancellationToken);

        var motiveShare = MotiveShareBonus(motive);
        var motiveDistort = MotiveDistortionBonus(motive);
        var motiveConfront = MotiveConfrontBonus(motive);

        var jitter = StableSignedUnit(
            $"{source.Id}|{claim.Id}|{target.Id}|{motive}|{_clock.Now:yyyy-MM-dd}") * 5.0;

        var share = 55.0
            + (openness - 50) * 0.35
            - (guard - 50) * 0.35
            + (relTrust - 50) * 0.30
            + (relAffection - 50) * 0.12
            + (relRespect - 50) * 0.08
            + (globalTrust - 50) * 0.08
            + (urgency - 50) * 0.18
            + (claim.Confidence - 50) * 0.10
            - secrecy * 0.22
            - risk * 0.16
            - audiencePenalty
            - repeatPenalty
            + motiveShare
            + (request.AskedDirectly ? 15.0 : 0.0)
            + jitter;

        // Fear/shame/guilt are context-sensitive inhibitors, especially when the
        // claim is risky/private. They do not mean the NPC can never speak.
        share -= Math.Max(0, fear - 55) * 0.08;
        share -= Math.Max(0, shame - 55) * 0.08;
        share -= Math.Max(0, guilt - 60) * 0.05;

        var privacy = 40.0
            + (guard - 50) * 0.36
            + (fear - 50) * 0.12
            + (anxiety - 50) * 0.10
            + (shame - 50) * 0.10
            + secrecy * 0.33
            + risk * 0.22
            + audienceCount * 3.2
            - (openness - 50) * 0.25
            - (relTrust - 50) * 0.18
            - (urgency - 50) * 0.10;

        var distortion = 16.0
            + Math.Max(0, anger - 45) * 0.12
            + Math.Max(0, resentment - 40) * 0.18
            + Math.Max(0, jealousy - 45) * 0.14
            + Math.Max(0, pride - 55) * 0.10
            + Math.Max(0, tension - 55) * 0.07
            + Math.Max(0, 65 - claim.Confidence) * 0.18
            + Math.Max(0, claim.Generation) * 5.0
            + motiveDistort
            - Math.Max(0, guilt - 45) * 0.08
            - Math.Max(0, relRespect - 55) * 0.08
            - Math.Max(0, relAffection - 60) * 0.05;

        var confront = 18.0
            + Math.Max(0, anger - 40) * 0.28
            + Math.Max(0, resentment - 40) * 0.24
            + Math.Max(0, relTension - 20) * 0.18
            + Math.Max(0, tension - 50) * 0.12
            + Math.Max(0, pride - 55) * 0.10
            - Math.Max(0, patience - 50) * 0.12
            - Math.Max(0, fear - 60) * 0.10
            + motiveConfront
            + (targetIsSubject ? 15.0 : 0.0);

        share = ClampScore(share);
        privacy = ClampScore(privacy);
        distortion = ClampScore(distortion);
        confront = ClampScore(confront);

        var action = ChooseAction(
            share,
            privacy,
            distortion,
            confront,
            secrecy,
            motive,
            request.AskedDirectly,
            targetIsSubject,
            claim.SubjectKey);

        var disclosure = action switch
        {
            "keep_private" => 0.0,
            "deflect" => 0.0,
            "hint" => 0.25,
            "distort" => 0.72,
            "warn" => 0.88,
            "confront" => 0.90,
            "gossip" => 0.82,
            _ => 0.90
        };

        var voice = ChooseVoiceLevel(
            action,
            secrecy,
            audienceCount,
            anger,
            confront,
            request.PrivateChannel);

        var reasons = BuildReasonCodes(
            source,
            relationship,
            claim,
            audienceCount,
            request,
            action,
            repeatPenalty,
            targetIsSubject);

        var result = new NpcSocialDecisionResult
        {
            SourceNpcId = source.Id,
            ClaimId = claim.Id,
            TargetNpcId = target.Id,
            SubjectNpcId = subjectNpcId,
            Action = action,
            ShouldSpeak = ActionShouldSpeak(action),
            ShouldTransferClaim = ActionTransfersClaim(action),
            ShareScore = Math.Round(share, 1),
            PrivacyScore = Math.Round(privacy, 1),
            DistortionScore = Math.Round(distortion, 1),
            ConfrontScore = Math.Round(confront, 1),
            DisclosureLevel = disclosure,
            AudienceCount = audienceCount,
            SuggestedVoiceLevel = voice,
            Motive = motive,
            ExpressionDirective = BuildExpressionDirective(action, claim),
            ReasonCodes = reasons
        };

        return new Evaluation
        {
            Result = result,
            Snapshot = new DecisionSnapshot
            {
                SourceNpcId = source.Id,
                ClaimId = claim.Id,
                TargetNpcId = target.Id,
                SubjectNpcId = subjectNpcId,
                PlayerId = Clean(request.PlayerId, ""),
                SceneId = Clean(request.SceneId, ""),
                Channel = Clean(request.Channel, "in_person"),
                Motive = motive,
                Secrecy = secrecy,
                Urgency = urgency,
                Risk = risk,
                AskedDirectly = request.AskedDirectly,
                PrivateChannel = request.PrivateChannel,
                AudienceCount = audienceCount,
                TraitSignature = string.Join("|", new[]
                {
                    openness, guard, anger, anxiety, fear, shame, guilt,
                    jealousy, resentment, globalTrust, affection, pride,
                    patience, tension
                }.Select(x => Math.Round(x / 5.0) * 5.0).Select(x => x.ToString("0", CultureInfo.InvariantCulture))),
                RelationshipSignature = $"{relTrust}|{relRespect}|{relAffection}|{relTension}",
                ClaimConfidence = claim.Confidence,
                ClaimGeneration = claim.Generation
            }
        };
    }

    private async Task<double> RecentExecutedSharePenaltyAsync(
        int sourceNpcId,
        long claimId,
        int targetNpcId,
        bool askedDirectly,
        CancellationToken cancellationToken)
    {
        if (askedDirectly)
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT ExecutedGameTime
                FROM NpcSocialDecision
                WHERE SourceNpcId=$source
                  AND ClaimId=$claim
                  AND TargetNpcId=$target
                  AND ExecutionStatus='executed'
                  AND Action IN ('share','gossip','warn','confront','distort')
                  AND ExecutedGameTime IS NOT NULL
                ORDER BY Id DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$source", sourceNpcId);
            cmd.Parameters.AddWithValue("$claim", claimId);
            cmd.Parameters.AddWithValue("$target", targetNpcId);

            var raw = cmd.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            var when = ParseTime(raw, _clock.Now.AddDays(-99));
            var age = _clock.Now - when;
            if (age < TimeSpan.Zero)
                return 0;
            if (age <= TimeSpan.FromHours(2))
                return 35;
            if (age <= TimeSpan.FromHours(12))
                return 18;
            if (age <= TimeSpan.FromDays(2))
                return 8;
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MarkExecutedAsync(
        long decisionId,
        string actualText,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NpcSocialDecision
                SET ExecutionStatus='executed',
                    ExecutedGameTime=$game,
                    ActualText=$text
                WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$text", actualText ?? "");
            cmd.Parameters.AddWithValue("$id", decisionId);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _gate.Release();
        }
    }

    private long InsertDecision(
        SqliteConnection conn,
        NpcSocialDecisionResult result,
        DecisionSnapshot snapshot,
        string fingerprint)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcSocialDecision
                (SourceNpcId,ClaimId,TargetNpcId,SubjectNpcId,PlayerId,SceneId,
                 Channel,Motive,Action,ShareScore,PrivacyScore,DistortionScore,
                 ConfrontScore,DisclosureLevel,AudienceCount,VoiceLevel,
                 ReasonCodes,ExpressionDirective,Fingerprint,DecisionGameTime,DecisionRealUtc,
                 ExecutionStatus,ActualText)
            VALUES
                ($source,$claim,$target,$subject,$player,$scene,$channel,$motive,
                 $action,$share,$privacy,$distort,$confront,$disclosure,$audience,
                 $voice,$reasons,$directive,$fingerprint,$game,$real,'pending','');
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("$source", result.SourceNpcId);
        cmd.Parameters.AddWithValue("$claim", result.ClaimId);
        cmd.Parameters.AddWithValue("$target", result.TargetNpcId);
        cmd.Parameters.AddWithValue("$subject", (object?)result.SubjectNpcId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$player", snapshot.PlayerId);
        cmd.Parameters.AddWithValue("$scene", snapshot.SceneId);
        cmd.Parameters.AddWithValue("$channel", snapshot.Channel);
        cmd.Parameters.AddWithValue("$motive", snapshot.Motive);
        cmd.Parameters.AddWithValue("$action", result.Action);
        cmd.Parameters.AddWithValue("$share", result.ShareScore);
        cmd.Parameters.AddWithValue("$privacy", result.PrivacyScore);
        cmd.Parameters.AddWithValue("$distort", result.DistortionScore);
        cmd.Parameters.AddWithValue("$confront", result.ConfrontScore);
        cmd.Parameters.AddWithValue("$disclosure", result.DisclosureLevel);
        cmd.Parameters.AddWithValue("$audience", result.AudienceCount);
        cmd.Parameters.AddWithValue("$voice", result.SuggestedVoiceLevel);
        cmd.Parameters.AddWithValue("$reasons", string.Join("|", result.ReasonCodes));
        cmd.Parameters.AddWithValue("$directive", result.ExpressionDirective ?? "");
        cmd.Parameters.AddWithValue("$fingerprint", fingerprint);
        cmd.Parameters.AddWithValue("$game", _clock.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$real", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private NpcSocialDecisionResult? LoadRecentMatchingDecision(
        SqliteConnection conn,
        string fingerprint,
        DateTimeOffset now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id,SourceNpcId,ClaimId,TargetNpcId,SubjectNpcId,Motive,Action,
                   ShareScore,PrivacyScore,DistortionScore,ConfrontScore,
                   DisclosureLevel,AudienceCount,VoiceLevel,ReasonCodes,
                   ExpressionDirective,DecisionGameTime,ExecutionStatus
            FROM NpcSocialDecision
            WHERE Fingerprint=$fingerprint
              AND ExecutionStatus='pending'
            ORDER BY Id DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$fingerprint", fingerprint);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;

        var decisionTime = ParseTime(r.GetString(16), now.AddDays(-99));
        if (now - decisionTime > TimeSpan.FromMinutes(15))
            return null;

        var action = r.GetString(6);
        var reasons = r.IsDBNull(14) || string.IsNullOrWhiteSpace(r.GetString(14))
            ? Array.Empty<string>()
            : r.GetString(14).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new NpcSocialDecisionResult
        {
            DecisionId = r.GetInt64(0),
            SourceNpcId = r.GetInt32(1),
            ClaimId = r.GetInt64(2),
            TargetNpcId = r.GetInt32(3),
            SubjectNpcId = r.IsDBNull(4) ? null : r.GetInt32(4),
            Motive = r.GetString(5),
            Action = action,
            ShouldSpeak = ActionShouldSpeak(action),
            ShouldTransferClaim = ActionTransfersClaim(action),
            ShareScore = r.GetDouble(7),
            PrivacyScore = r.GetDouble(8),
            DistortionScore = r.GetDouble(9),
            ConfrontScore = r.GetDouble(10),
            DisclosureLevel = r.GetDouble(11),
            AudienceCount = r.GetInt32(12),
            SuggestedVoiceLevel = r.GetString(13),
            ExpressionDirective = r.IsDBNull(15) ? "" : r.GetString(15),
            ReasonCodes = reasons
        };
    }

    private DecisionRow? LoadDecisionRow(SqliteConnection conn, long id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id,SourceNpcId,ClaimId,TargetNpcId,PlayerId,SceneId,Channel,
                   Action,VoiceLevel,ExecutionStatus
            FROM NpcSocialDecision
            WHERE Id=$id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;

        return new DecisionRow
        {
            Id = r.GetInt64(0),
            SourceNpcId = r.GetInt32(1),
            ClaimId = r.GetInt64(2),
            TargetNpcId = r.GetInt32(3),
            PlayerId = r.GetString(4),
            SceneId = r.GetString(5),
            Channel = r.GetString(6),
            Action = r.GetString(7),
            VoiceLevel = r.GetString(8),
            ExecutionStatus = r.GetString(9)
        };
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcSocialDecision (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceNpcId INTEGER NOT NULL,
                ClaimId INTEGER NOT NULL,
                TargetNpcId INTEGER NOT NULL,
                SubjectNpcId INTEGER NULL,
                PlayerId TEXT NOT NULL DEFAULT '',
                SceneId TEXT NOT NULL DEFAULT '',
                Channel TEXT NOT NULL DEFAULT 'in_person',
                Motive TEXT NOT NULL DEFAULT 'casual',
                Action TEXT NOT NULL,
                ShareScore REAL NOT NULL,
                PrivacyScore REAL NOT NULL,
                DistortionScore REAL NOT NULL,
                ConfrontScore REAL NOT NULL,
                DisclosureLevel REAL NOT NULL,
                AudienceCount INTEGER NOT NULL DEFAULT 0,
                VoiceLevel TEXT NOT NULL DEFAULT 'normal',
                ReasonCodes TEXT NOT NULL DEFAULT '',
                ExpressionDirective TEXT NOT NULL DEFAULT '',
                Fingerprint TEXT NOT NULL,
                DecisionGameTime TEXT NOT NULL,
                DecisionRealUtc TEXT NOT NULL,
                ExecutionStatus TEXT NOT NULL DEFAULT 'pending',
                ExecutedGameTime TEXT NULL,
                ActualText TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_NpcSocialDecision_Source
                ON NpcSocialDecision(SourceNpcId, Id DESC);

            CREATE INDEX IF NOT EXISTS IX_NpcSocialDecision_ClaimTarget
                ON NpcSocialDecision(SourceNpcId, ClaimId, TargetNpcId, Id DESC);

            CREATE INDEX IF NOT EXISTS IX_NpcSocialDecision_Fingerprint
                ON NpcSocialDecision(Fingerprint, Id DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
        conn.Open();
        return conn;
    }

    private static Relationship? FindRelationship(SimCharacter source, SimCharacter target)
    {
        return source.Relationships.FirstOrDefault(x => x.TargetId == target.Id)
            ?? source.Relationships.FirstOrDefault(x =>
                x.TargetName.Equals(target.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static int? ParseNpcSubject(string? subjectKey)
    {
        if (string.IsNullOrWhiteSpace(subjectKey))
            return null;

        var clean = subjectKey.Trim();
        if (!clean.StartsWith("npc:", StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(clean[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
    }

    private static int? TryParseNpcKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            !key.StartsWith("npc:", StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(key[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
    }

    private static string ChooseAction(
        double share,
        double privacy,
        double distortion,
        double confront,
        int secrecy,
        string motive,
        bool askedDirectly,
        bool targetIsSubject,
        string subjectKey)
    {
        if (share < 35)
            return askedDirectly ? "deflect" : "keep_private";

        if (targetIsSubject && confront >= 60)
            return "confront";

        if ((motive == "warn" || motive == "protect") && share >= 45)
            return "warn";

        if (distortion >= 63 && share >= 45 &&
            (motive == "retaliate" || motive == "vent" || motive == "impress"))
        {
            return "distort";
        }

        if (share < 58 || secrecy >= 70 || privacy >= share + 18)
            return "hint";

        var subjectIsAnotherNpc = !string.IsNullOrWhiteSpace(subjectKey) &&
            subjectKey.StartsWith("npc:", StringComparison.OrdinalIgnoreCase) &&
            !targetIsSubject;

        if (subjectIsAnotherNpc &&
            motive is "casual" or "bond" or "vent" or "seek_advice" or "impress" or "retaliate")
        {
            return "gossip";
        }

        return "share";
    }

    private static string ChooseVoiceLevel(
        string action,
        int secrecy,
        int audienceCount,
        float anger,
        double confront,
        bool privateChannel)
    {
        if (privateChannel)
            return "normal";

        if (action == "confront" && confront >= 78 && anger >= 70)
            return "raised";

        if (secrecy >= 78 || audienceCount >= 3)
            return "whisper";

        if (secrecy >= 55 || audienceCount >= 1 || action == "gossip")
            return "quiet";

        return "normal";
    }

    private static IReadOnlyList<string> BuildReasonCodes(
        SimCharacter source,
        Relationship? relationship,
        NpcKnowledgeClaim claim,
        int audienceCount,
        NpcSocialDecisionRequest request,
        string action,
        double repeatPenalty,
        bool targetIsSubject)
    {
        var reasons = new List<string>();

        if (source.Traits.Openness >= 65) reasons.Add("high_openness");
        if (source.Traits.Guard >= 65) reasons.Add("high_guard");
        if (source.Traits.Anger >= 65) reasons.Add("anger_pressure");
        if (source.Traits.Resentment >= 65) reasons.Add("resentment_pressure");
        if (source.Traits.Jealousy >= 65) reasons.Add("jealousy_pressure");
        if (source.Traits.Fear >= 65) reasons.Add("fear_inhibits_disclosure");
        if (source.Traits.Shame >= 65) reasons.Add("shame_inhibits_disclosure");

        if ((relationship?.Trust ?? 50) >= 70) reasons.Add("trusted_recipient");
        if ((relationship?.Trust ?? 50) <= 30) reasons.Add("low_target_trust");
        if ((relationship?.Tension ?? 0) >= 60) reasons.Add("relationship_tension");

        if (request.Secrecy >= 70) reasons.Add("high_secrecy");
        if (request.ConsequenceRisk >= 70) reasons.Add("high_consequence_risk");
        if (request.Urgency >= 70) reasons.Add("high_urgency");
        if (request.AskedDirectly) reasons.Add("asked_directly");
        if (audienceCount > 0) reasons.Add("others_perceived_nearby");
        if (repeatPenalty > 0) reasons.Add("recently_shared_with_same_target");
        if (claim.Generation > 0) reasons.Add("telephone_game_generation_" + claim.Generation);
        if (claim.Confidence < 55) reasons.Add("uncertain_claim");
        if (targetIsSubject) reasons.Add("target_is_claim_subject");

        reasons.Add("action:" + action);
        return reasons;
    }

    private static string BuildExpressionDirective(string action, NpcKnowledgeClaim claim)
    {
        var uncertainty = claim.Confidence < 60 || claim.Generation > 0
            ? $" Preserve uncertainty: confidence {claim.Confidence}/100, gossip generation {claim.Generation}."
            : "";

        return action switch
        {
            "keep_private" =>
                "Do not reveal this knowledge claim. The NPC may stay silent or change the subject. Do not invent a cover story unless another system explicitly chooses deception.",

            "deflect" =>
                "The NPC does not disclose the hidden claim. If responding, deflect using only current conversation context. Do not leak the claim's hidden wording or subject metadata.",

            "hint" =>
                "The NPC may allude to concern or partial information but should not state the full claim. Do not recover hidden original wording from provenance." + uncertainty,

            "distort" =>
                "The NPC is willing to present the claim in a biased or misleading way. It may omit, exaggerate, or emotionally frame what THIS NPC believes, but it must not access hidden world truth or invent unrelated facts. The recipient learns only the exact words actually spoken." + uncertainty,

            "confront" =>
                "The NPC is inclined to address the claim directly with the person it concerns. Base the confrontation only on what THIS NPC currently believes; do not upgrade belief into verified truth." + uncertainty,

            "warn" =>
                "The NPC is inclined to warn/protect the recipient. State only what THIS NPC knows or believes and preserve source uncertainty." + uncertainty,

            "gossip" =>
                "The NPC is socially willing to pass this claim to a third party. Use only this NPC's current claim wording/meaning; do not restore the hidden original. The next listener receives a new telephone-game generation." + uncertainty,

            _ =>
                "The NPC may disclose this claim directly. Use only this NPC's current knowledge and preserve uncertainty. The source claim is not automatically world truth." + uncertainty
        };
    }

    private static bool ActionShouldSpeak(string action)
        => action is "deflect" or "hint" or "share" or "gossip" or "warn" or "confront" or "distort";

    private static bool ActionTransfersClaim(string action)
        => action is "share" or "gossip" or "warn" or "confront" or "distort";

    private static double MotiveShareBonus(string motive)
        => motive switch
        {
            "bond" => 12,
            "vent" => 14,
            "warn" => 22,
            "protect" => 24,
            "retaliate" => 18,
            "confront" => 12,
            "seek_advice" => 16,
            "impress" => 10,
            _ => 0
        };

    private static double MotiveDistortionBonus(string motive)
        => motive switch
        {
            "retaliate" => 25,
            "vent" => 12,
            "impress" => 14,
            "confront" => 5,
            _ => 0
        };

    private static double MotiveConfrontBonus(string motive)
        => motive switch
        {
            "confront" => 38,
            "retaliate" => 22,
            "protect" => 10,
            "warn" => 8,
            "vent" => 8,
            _ => 0
        };

    private static string NormalizeMotive(string? motive)
    {
        var m = Clean(motive, "casual").ToLowerInvariant();
        return m switch
        {
            "bond" or "vent" or "warn" or "protect" or "retaliate" or
            "confront" or "seek_advice" or "impress" => m,
            _ => "casual"
        };
    }

    private static string BuildFingerprint(DecisionSnapshot x)
    {
        var raw = string.Join("|", new object?[]
        {
            x.SourceNpcId, x.ClaimId, x.TargetNpcId, x.SubjectNpcId,
            x.PlayerId, x.SceneId, x.Channel, x.Motive,
            x.Secrecy, x.Urgency, x.Risk, x.AskedDirectly,
            x.PrivateChannel, x.AudienceCount,
            x.TraitSignature, x.RelationshipSignature,
            x.ClaimConfidence, x.ClaimGeneration
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static double StableSignedUnit(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var value = BitConverter.ToUInt64(hash, 0);
        var unit = value / (double)ulong.MaxValue;
        return unit * 2.0 - 1.0;
    }

    private static void ValidateDecisionRequest(NpcSocialDecisionRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.SourceNpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.SourceNpcId));
        if (request.TargetNpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.TargetNpcId));
        if (request.SourceNpcId == request.TargetNpcId)
            throw new ArgumentException("Source and target NPC cannot be the same.", nameof(request));
        if (request.ClaimId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ClaimId));
    }

    private static int Clamp100(int value) => Math.Clamp(value, 0, 100);
    private static double ClampScore(double value) => Math.Clamp(value, 0, 100);
    private static string NpcKey(int id) => "npc:" + id.ToString(CultureInfo.InvariantCulture);
    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static DateTimeOffset ParseTime(string raw, DateTimeOffset fallback)
        => DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : fallback;

    private sealed class Evaluation
    {
        public NpcSocialDecisionResult Result { get; set; } = new();
        public DecisionSnapshot Snapshot { get; set; } = new();
    }

    private sealed class DecisionSnapshot
    {
        public int SourceNpcId { get; set; }
        public long ClaimId { get; set; }
        public int TargetNpcId { get; set; }
        public int? SubjectNpcId { get; set; }
        public string PlayerId { get; set; } = "";
        public string SceneId { get; set; } = "";
        public string Channel { get; set; } = "";
        public string Motive { get; set; } = "";
        public int Secrecy { get; set; }
        public int Urgency { get; set; }
        public int Risk { get; set; }
        public bool AskedDirectly { get; set; }
        public bool PrivateChannel { get; set; }
        public int AudienceCount { get; set; }
        public string TraitSignature { get; set; } = "";
        public string RelationshipSignature { get; set; } = "";
        public int ClaimConfidence { get; set; }
        public int ClaimGeneration { get; set; }
    }

    private sealed class DecisionRow
    {
        public long Id { get; set; }
        public int SourceNpcId { get; set; }
        public long ClaimId { get; set; }
        public int TargetNpcId { get; set; }
        public string PlayerId { get; set; } = "";
        public string SceneId { get; set; } = "";
        public string Channel { get; set; } = "";
        public string Action { get; set; } = "";
        public string VoiceLevel { get; set; } = "normal";
        public string ExecutionStatus { get; set; } = "pending";
    }
}
