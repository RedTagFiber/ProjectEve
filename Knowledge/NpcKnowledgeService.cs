using Microsoft.Data.Sqlite;
using ProjectEve.Core.Knowledge;
using ProjectEve.Core.Time;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Knowledge;

/// <summary>
/// SQLite-backed NPC personal knowledge / belief ledger.
///
/// Core rule:
/// - world truth != personal knowledge
/// - exact transcript != automatic knowledge
/// - relationship closeness != telepathy
/// - gossip creates a NEW reported claim owned by the recipient
/// </summary>
public sealed class NpcKnowledgeService : INpcKnowledgeService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IGameTimeService _clock;
    private readonly string _dbPath;

    public NpcKnowledgeService(IGameTimeService clock)
    {
        _clock = clock;
        _dbPath = Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        var parent = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        EnsureSchema();
    }

    public async Task<int> ImportConversationEventAsync(
        long conversationEventId,
        CancellationToken cancellationToken = default)
    {
        if (conversationEventId <= 0)
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();

            int holderNpcId;
            string playerId;
            DateTimeOffset learnedGameTime;

            using (var evt = conn.CreateCommand())
            {
                evt.CommandText = """
                    SELECT NpcId,PlayerId,EndedGameTime
                    FROM ConversationEvent
                    WHERE Id=$id
                    LIMIT 1;
                    """;
                evt.Parameters.AddWithValue("$id", conversationEventId);

                using var r = evt.ExecuteReader();
                if (!r.Read())
                    return 0;

                holderNpcId = r.GetInt32(0);
                playerId = r.IsDBNull(1) ? "" : r.GetString(1);
                learnedGameTime = ParseTime(r.GetString(2), _clock.Now);
            }

            var facts = new List<ConversationFactRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id,Subject,FactKey,FactValue,Confidence,SourceType
                    FROM ConversationFact
                    WHERE EventId=$event
                    ORDER BY Id;
                    """;
                cmd.Parameters.AddWithValue("$event", conversationEventId);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    facts.Add(new ConversationFactRow
                    {
                        Id = r.GetInt64(0),
                        Subject = r.GetString(1),
                        FactKey = r.GetString(2),
                        FactValue = r.GetString(3),
                        Confidence = r.GetInt32(4),
                        SourceType = r.GetString(5)
                    });
                }
            }

            var inserted = 0;
            foreach (var fact in facts)
            {
                if (ConversationFactAlreadyImported(conn, holderNpcId, fact.Id))
                    continue;

                var subjectKey = MapConversationSubject(
                    fact.Subject,
                    playerId,
                    holderNpcId);

                var request = new NpcKnowledgeRecordRequest
                {
                    HolderNpcId = holderNpcId,
                    PlayerId = ScopePlayerId(subjectKey, playerId),
                    SubjectKey = subjectKey,
                    ClaimKey = Clean(fact.FactKey, "fact"),
                    ClaimText = fact.FactValue.Trim(),
                    Confidence = fact.SourceType.Equals("claim", StringComparison.OrdinalIgnoreCase)
                        ? Math.Min(85, Math.Clamp(fact.Confidence, 0, 100))
                        : Math.Clamp(fact.Confidence, 0, 100),
                    SourceType = "conversation:" + Clean(fact.SourceType, "learned"),
                    SourceNpcId = fact.SourceType.Equals("direct_npc_disclosure", StringComparison.OrdinalIgnoreCase)
                        ? holderNpcId
                        : null,
                    SourceCharacterKey = ConversationSourceCharacterKey(
                        fact.SourceType,
                        playerId,
                        holderNpcId),
                    OriginConversationEventId = conversationEventId,
                    OriginConversationFactId = fact.Id,
                    Generation = 0,
                    Status = fact.SourceType.Equals("claim", StringComparison.OrdinalIgnoreCase)
                        ? "reported"
                        : "held",
                    LearnedGameTime = learnedGameTime
                };

                var id = InsertClaim(conn, request);
                if (id > 0)
                    inserted++;
            }

            return inserted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ImportScenePerceptionAsync(
        int holderNpcId,
        CancellationToken cancellationToken = default)
    {
        if (holderNpcId <= 0)
            return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            if (!TableExists(conn, "ScenePerceptionEvidence"))
                return 0;

            var observerKey = "npc:" + holderNpcId.ToString(CultureInfo.InvariantCulture);
            var evidence = new List<PerceptionRow>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id,EventKind,SourceCharacterKey,Quality,PerceivedText,
                           Confidence,GameTime
                    FROM ScenePerceptionEvidence
                    WHERE ObserverCharacterKey=$observer
                      AND TRIM(PerceivedText) <> ''
                    ORDER BY Id;
                    """;
                cmd.Parameters.AddWithValue("$observer", observerKey);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    evidence.Add(new PerceptionRow
                    {
                        Id = r.GetInt64(0),
                        EventKind = r.GetString(1),
                        SourceCharacterKey = r.GetString(2),
                        Quality = r.GetString(3),
                        PerceivedText = r.GetString(4),
                        Confidence = r.GetDouble(5),
                        GameTime = ParseTime(r.GetString(6), _clock.Now)
                    });
                }
            }

            var inserted = 0;
            foreach (var row in evidence)
            {
                if (PerceptionAlreadyImported(conn, holderNpcId, row.Id))
                    continue;

                var claimKey = row.EventKind.Equals("speech", StringComparison.OrdinalIgnoreCase)
                    ? "perceived_speech"
                    : row.EventKind.Equals("body_language", StringComparison.OrdinalIgnoreCase)
                        ? "perceived_body_language"
                        : "perceived_action";

                var sourceType = row.EventKind.Equals("speech", StringComparison.OrdinalIgnoreCase)
                    ? "direct_perception:speech"
                    : "direct_perception:" + Clean(row.EventKind, "visual");

                // A fragment remains a fragment. We store exactly what was perceived;
                // we do not reconstruct the missing words into a cleaner claim.
                var request = new NpcKnowledgeRecordRequest
                {
                    HolderNpcId = holderNpcId,
                    PlayerId = PlayerScopeFromCharacterKey(row.SourceCharacterKey),
                    SubjectKey = Clean(row.SourceCharacterKey, "unknown"),
                    ClaimKey = claimKey,
                    ClaimText = row.PerceivedText.Trim(),
                    Confidence = ConfidenceFromPerception(row.Quality, row.Confidence),
                    SourceType = sourceType,
                    SourceNpcId = ParseNpcIdOrNull(row.SourceCharacterKey),
                    SourceCharacterKey = Clean(row.SourceCharacterKey, "unknown"),
                    OriginPerceptionEvidenceId = row.Id,
                    Generation = 0,
                    Status = row.Quality.Equals("clear", StringComparison.OrdinalIgnoreCase)
                        ? "held"
                        : "uncertain",
                    LearnedGameTime = row.GameTime
                };

                var id = InsertClaim(conn, request);
                if (id > 0)
                    inserted++;
            }

            return inserted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NpcKnowledgeClaim?> RecordAsync(
        NpcKnowledgeRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.HolderNpcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.HolderNpcId));
        if (string.IsNullOrWhiteSpace(request.ClaimText))
            return null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            var id = InsertClaim(conn, request);
            return id > 0 ? LoadClaim(conn, id) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<NpcKnowledgeTransmissionResult> TransmitAsync(
        NpcKnowledgeTransmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.FromNpcId <= 0 || request.ToNpcId <= 0)
        {
            return new NpcKnowledgeTransmissionResult
            {
                Reason = "Both source and recipient NPC ids are required."
            };
        }

        if (request.FromNpcId == request.ToNpcId)
        {
            return new NpcKnowledgeTransmissionResult
            {
                Reason = "An NPC cannot gossip to itself."
            };
        }

        if (request.SourceClaimId <= 0)
        {
            return new NpcKnowledgeTransmissionResult
            {
                Reason = "A source claim is required."
            };
        }

        // Critical telephone-game rule: the report must be the actual words/meaning
        // this source NPC transmitted. We never grant the recipient the source NPC's
        // hidden evidence or exact original transcript automatically.
        if (string.IsNullOrWhiteSpace(request.ReportedText))
        {
            return new NpcKnowledgeTransmissionResult
            {
                Reason = "ReportedText is required so the recipient learns what was actually told, not the hidden source evidence."
            };
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            var source = LoadClaim(conn, request.SourceClaimId, tx);
            if (source == null)
            {
                tx.Rollback();
                return new NpcKnowledgeTransmissionResult
                {
                    Reason = "Source claim was not found."
                };
            }

            if (source.HolderNpcId != request.FromNpcId)
            {
                tx.Rollback();
                return new NpcKnowledgeTransmissionResult
                {
                    Reason = "The source NPC does not own that knowledge claim."
                };
            }

            var generation = Math.Max(1, source.Generation + 1);
            var confidence = request.RecipientConfidenceOverride ??
                Math.Clamp(source.Confidence - 10 - Math.Min(25, generation * 4), 10, 92);

            var gameTime = request.GameTime ?? _clock.Now;
            var recipientRequest = new NpcKnowledgeRecordRequest
            {
                HolderNpcId = request.ToNpcId,
                PlayerId = !string.IsNullOrWhiteSpace(source.PlayerId)
                    ? source.PlayerId
                    : Clean(request.PlayerId, ""),
                SubjectKey = source.SubjectKey,
                ClaimKey = "reported:" + Clean(source.ClaimKey, "statement"),
                ClaimText = request.ReportedText.Trim(),
                Confidence = Math.Clamp(confidence, 0, 100),
                SourceType = "gossip_report",
                SourceNpcId = request.FromNpcId,
                SourceCharacterKey = "npc:" + request.FromNpcId.ToString(CultureInfo.InvariantCulture),
                OriginClaimId = source.Id,
                RootOriginClaimId = source.RootOriginClaimId ?? source.Id,
                Generation = generation,
                Status = "reported",
                LearnedGameTime = gameTime
            };

            var recipientClaimId = InsertClaim(conn, recipientRequest, tx);
            var recipient = LoadClaim(conn, recipientClaimId, tx);

            using var transmission = conn.CreateCommand();
            transmission.Transaction = tx;
            transmission.CommandText = """
                INSERT INTO KnowledgeTransmission
                    (FromNpcId,ToNpcId,PlayerId,SourceClaimId,ResultClaimId,
                     ReportedText,Channel,SceneId,Generation,SourceConfidence,
                     ResultConfidence,GameTime,CreatedUtc)
                VALUES
                    ($from,$to,$player,$source,$result,
                     $text,$channel,$scene,$generation,$sourceConfidence,
                     $resultConfidence,$game,$utc);
                SELECT last_insert_rowid();
                """;
            transmission.Parameters.AddWithValue("$from", request.FromNpcId);
            transmission.Parameters.AddWithValue("$to", request.ToNpcId);
            transmission.Parameters.AddWithValue("$player", Clean(recipientRequest.PlayerId, ""));
            transmission.Parameters.AddWithValue("$source", source.Id);
            transmission.Parameters.AddWithValue("$result", recipientClaimId);
            transmission.Parameters.AddWithValue("$text", request.ReportedText.Trim());
            transmission.Parameters.AddWithValue("$channel", Clean(request.Channel, "in_person"));
            transmission.Parameters.AddWithValue("$scene", Clean(request.SceneId, ""));
            transmission.Parameters.AddWithValue("$generation", generation);
            transmission.Parameters.AddWithValue("$sourceConfidence", source.Confidence);
            transmission.Parameters.AddWithValue("$resultConfidence", recipient?.Confidence ?? confidence);
            transmission.Parameters.AddWithValue("$game", gameTime.ToString("O"));
            transmission.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

            var transmissionId = Convert.ToInt64(
                transmission.ExecuteScalar(),
                CultureInfo.InvariantCulture);

            tx.Commit();

            return new NpcKnowledgeTransmissionResult
            {
                Transmitted = true,
                Reason = "reported_claim_created",
                TransmissionId = transmissionId,
                RecipientClaim = recipient
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<NpcKnowledgeClaim>> GetKnowledgeAsync(
        int holderNpcId,
        string? playerId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (holderNpcId <= 0)
            return Array.Empty<NpcKnowledgeClaim>();

        limit = Math.Clamp(limit, 1, 500);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();

            if (string.IsNullOrWhiteSpace(playerId))
            {
                cmd.CommandText = """
                    SELECT Id,HolderNpcId,PlayerId,SubjectKey,ClaimKey,ClaimText,
                           Confidence,SourceType,SourceNpcId,SourceCharacterKey,
                           OriginConversationEventId,OriginConversationFactId,
                           OriginPerceptionEvidenceId,OriginClaimId,RootOriginClaimId,
                           Generation,Status,LearnedGameTime,LastReinforcedGameTime
                    FROM NpcKnowledgeClaim
                    WHERE HolderNpcId=$holder
                    ORDER BY Id DESC
                    LIMIT $limit;
                    """;
            }
            else
            {
                cmd.CommandText = """
                    SELECT Id,HolderNpcId,PlayerId,SubjectKey,ClaimKey,ClaimText,
                           Confidence,SourceType,SourceNpcId,SourceCharacterKey,
                           OriginConversationEventId,OriginConversationFactId,
                           OriginPerceptionEvidenceId,OriginClaimId,RootOriginClaimId,
                           Generation,Status,LearnedGameTime,LastReinforcedGameTime
                    FROM NpcKnowledgeClaim
                    WHERE HolderNpcId=$holder
                      AND (PlayerId='' OR PlayerId=$player)
                    ORDER BY Id DESC
                    LIMIT $limit;
                    """;
                cmd.Parameters.AddWithValue("$player", playerId.Trim());
            }

            cmd.Parameters.AddWithValue("$holder", holderNpcId);
            cmd.Parameters.AddWithValue("$limit", limit);

            var rows = new List<NpcKnowledgeClaim>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(ReadClaim(r));
            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<NpcKnowledgeLineageStep>> GetLineageAsync(
        long claimId,
        CancellationToken cancellationToken = default)
    {
        if (claimId <= 0)
            return Array.Empty<NpcKnowledgeLineageStep>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            var chain = new List<NpcKnowledgeLineageStep>();
            var seen = new HashSet<long>();
            long? current = claimId;

            for (var i = 0; i < 24 && current.HasValue && current.Value > 0; i++)
            {
                if (!seen.Add(current.Value))
                    break;

                var claim = LoadClaim(conn, current.Value);
                if (claim == null)
                    break;

                chain.Add(new NpcKnowledgeLineageStep
                {
                    ClaimId = claim.Id,
                    HolderNpcId = claim.HolderNpcId,
                    Generation = claim.Generation,
                    SourceType = claim.SourceType,
                    SourceNpcId = claim.SourceNpcId,
                    ClaimText = claim.ClaimText,
                    Confidence = claim.Confidence,
                    LearnedGameTime = claim.LearnedGameTime
                });

                current = claim.OriginClaimId;
            }

            chain.Reverse();
            return chain;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> BuildPromptContextAsync(
        int holderNpcId,
        string playerId,
        string playerName,
        int limit = 24,
        CancellationToken cancellationToken = default)
    {
        if (holderNpcId <= 0)
            return "PERSONAL KNOWLEDGE: none.";

        // Backfill any older conversation facts for this player/NPC pair so Phase 7
        // can be installed on an existing save without losing prior continuity.
        await ImportUnimportedConversationEventsAsync(
            holderNpcId,
            Clean(playerId, ""),
            cancellationToken);

        // Lazy import keeps Phase 6 decoupled: perception evidence becomes available
        // to the NPC the next time its cognition is requested without modifying the
        // scene engine or granting evidence to other NPCs.
        await ImportScenePerceptionAsync(holderNpcId, cancellationToken);

        var rows = await GetKnowledgeAsync(
            holderNpcId,
            Clean(playerId, ""),
            Math.Clamp(limit, 1, 60),
            cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("PERSONAL KNOWLEDGE / BELIEF LEDGER — THIS NPC ONLY");
        sb.AppendLine("These records describe what this NPC learned, perceived, or was told. They are NOT guaranteed world truth.");

        if (rows.Count == 0)
        {
            sb.AppendLine("- No stored personal knowledge records yet.");
        }
        else
        {
            foreach (var row in rows.OrderBy(x => x.Id))
            {
                var source = DescribeSource(row);
                sb.Append("- ")
                  .Append(row.SubjectKey)
                  .Append(" / ")
                  .Append(row.ClaimKey)
                  .Append(": ")
                  .Append(row.ClaimText)
                  .Append(" [confidence ")
                  .Append(row.Confidence)
                  .Append("; ")
                  .Append(source);

                if (row.Generation > 0)
                    sb.Append("; gossip generation ").Append(row.Generation);

                if (!string.IsNullOrWhiteSpace(row.Status) &&
                    !row.Status.Equals("held", StringComparison.OrdinalIgnoreCase))
                    sb.Append("; status ").Append(row.Status);

                sb.AppendLine("]");
            }
        }

        sb.AppendLine();
        sb.AppendLine("KNOWLEDGE BOUNDARY RULES");
        sb.AppendLine("- Relationship, family, marriage, friendship, or proximity never grants knowledge by itself.");
        sb.AppendLine("- A direct perception record means the NPC remembers what it perceived, including fragments; never repair missing words.");
        sb.AppendLine("- A gossip_report is only a report from another NPC. Do not silently substitute the source NPC's hidden evidence or original transcript.");
        sb.AppendLine("- Generation 1+ means telephone-game information. Later generations may differ from the original and confidence should remain limited.");
        sb.AppendLine("- A speaker can lie, exaggerate, misremember, omit context, or be wrong. Personal knowledge is not verified truth.");
        sb.AppendLine("- Do not tell another NPC this information unless an actual communication/observation event transfers it.");
        sb.AppendLine("- If the player-specific fact belongs to another PlayerId, do not use it for this player.");
        sb.AppendLine($"- Current player scope: {Clean(playerName, "Player")} / {Clean(playerId, "unknown-player")}");

        return sb.ToString().TrimEnd();
    }

    private async Task ImportUnimportedConversationEventsAsync(
        int holderNpcId,
        string playerId,
        CancellationToken cancellationToken)
    {
        var eventIds = new List<long>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var conn = Open();
            if (!TableExists(conn, "ConversationEvent") ||
                !TableExists(conn, "ConversationFact"))
                return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT e.Id
                FROM ConversationEvent e
                JOIN ConversationFact f ON f.EventId=e.Id
                LEFT JOIN NpcKnowledgeClaim k
                  ON k.HolderNpcId=e.NpcId
                 AND k.OriginConversationFactId=f.Id
                WHERE e.NpcId=$npc
                  AND ($player='' OR e.PlayerId=$player)
                  AND k.Id IS NULL
                ORDER BY e.Id DESC
                LIMIT 20;
                """;
            cmd.Parameters.AddWithValue("$npc", holderNpcId);
            cmd.Parameters.AddWithValue("$player", Clean(playerId, ""));

            using var r = cmd.ExecuteReader();
            while (r.Read())
                eventIds.Add(r.GetInt64(0));
        }
        finally
        {
            _gate.Release();
        }

        // Import outside the discovery lock because ImportConversationEventAsync
        // owns the same service gate.
        foreach (var eventId in eventIds.OrderBy(x => x))
            await ImportConversationEventAsync(eventId, cancellationToken);
    }

    private long InsertClaim(
        SqliteConnection conn,
        NpcKnowledgeRecordRequest request,
        SqliteTransaction? tx = null)
    {
        if (request.HolderNpcId <= 0 || string.IsNullOrWhiteSpace(request.ClaimText))
            return 0;

        if (request.OriginConversationFactId.HasValue)
        {
            using var existing = conn.CreateCommand();
            existing.Transaction = tx;
            existing.CommandText = """
                SELECT Id
                FROM NpcKnowledgeClaim
                WHERE HolderNpcId=$holder
                  AND OriginConversationFactId=$origin
                LIMIT 1;
                """;
            existing.Parameters.AddWithValue("$holder", request.HolderNpcId);
            existing.Parameters.AddWithValue("$origin", request.OriginConversationFactId.Value);
            var found = existing.ExecuteScalar();
            if (found != null && found != DBNull.Value)
                return Convert.ToInt64(found, CultureInfo.InvariantCulture);
        }

        if (request.OriginPerceptionEvidenceId.HasValue)
        {
            using var existing = conn.CreateCommand();
            existing.Transaction = tx;
            existing.CommandText = """
                SELECT Id
                FROM NpcKnowledgeClaim
                WHERE HolderNpcId=$holder
                  AND OriginPerceptionEvidenceId=$origin
                LIMIT 1;
                """;
            existing.Parameters.AddWithValue("$holder", request.HolderNpcId);
            existing.Parameters.AddWithValue("$origin", request.OriginPerceptionEvidenceId.Value);
            var found = existing.ExecuteScalar();
            if (found != null && found != DBNull.Value)
                return Convert.ToInt64(found, CultureInfo.InvariantCulture);
        }

        var learned = request.LearnedGameTime ?? _clock.Now;
        var confidence = Math.Clamp(request.Confidence, 0, 100);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO NpcKnowledgeClaim
                (HolderNpcId,PlayerId,SubjectKey,ClaimKey,ClaimText,
                 Confidence,SourceType,SourceNpcId,SourceCharacterKey,
                 OriginConversationEventId,OriginConversationFactId,
                 OriginPerceptionEvidenceId,OriginClaimId,RootOriginClaimId,
                 Generation,Status,LearnedGameTime,LastReinforcedGameTime,
                 CreatedUtc)
            VALUES
                ($holder,$player,$subject,$key,$text,
                 $confidence,$source,$sourceNpc,$sourceCharacter,
                 $conversationEvent,$conversationFact,
                 $perception,$originClaim,$rootClaim,
                 $generation,$status,$learned,$reinforced,$utc);
            SELECT last_insert_rowid();
            """;

        cmd.Parameters.AddWithValue("$holder", request.HolderNpcId);
        cmd.Parameters.AddWithValue("$player", Clean(request.PlayerId, ""));
        cmd.Parameters.AddWithValue("$subject", Clean(request.SubjectKey, "unknown"));
        cmd.Parameters.AddWithValue("$key", Clean(request.ClaimKey, "statement"));
        cmd.Parameters.AddWithValue("$text", request.ClaimText.Trim());
        cmd.Parameters.AddWithValue("$confidence", confidence);
        cmd.Parameters.AddWithValue("$source", Clean(request.SourceType, "learned"));
        cmd.Parameters.AddWithValue("$sourceNpc", request.SourceNpcId.HasValue ? (object)request.SourceNpcId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$sourceCharacter", string.IsNullOrWhiteSpace(request.SourceCharacterKey) ? DBNull.Value : request.SourceCharacterKey.Trim());
        cmd.Parameters.AddWithValue("$conversationEvent", request.OriginConversationEventId.HasValue ? (object)request.OriginConversationEventId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$conversationFact", request.OriginConversationFactId.HasValue ? (object)request.OriginConversationFactId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$perception", request.OriginPerceptionEvidenceId.HasValue ? (object)request.OriginPerceptionEvidenceId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$originClaim", request.OriginClaimId.HasValue ? (object)request.OriginClaimId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$rootClaim", request.RootOriginClaimId.HasValue ? (object)request.RootOriginClaimId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$generation", Math.Max(0, request.Generation));
        cmd.Parameters.AddWithValue("$status", Clean(request.Status, "held"));
        cmd.Parameters.AddWithValue("$learned", learned.ToString("O"));
        cmd.Parameters.AddWithValue("$reinforced", learned.ToString("O"));
        cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static NpcKnowledgeClaim? LoadClaim(
        SqliteConnection conn,
        long id,
        SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT Id,HolderNpcId,PlayerId,SubjectKey,ClaimKey,ClaimText,
                   Confidence,SourceType,SourceNpcId,SourceCharacterKey,
                   OriginConversationEventId,OriginConversationFactId,
                   OriginPerceptionEvidenceId,OriginClaimId,RootOriginClaimId,
                   Generation,Status,LearnedGameTime,LastReinforcedGameTime
            FROM NpcKnowledgeClaim
            WHERE Id=$id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadClaim(r) : null;
    }

    private static NpcKnowledgeClaim ReadClaim(SqliteDataReader r)
        => new()
        {
            Id = r.GetInt64(0),
            HolderNpcId = r.GetInt32(1),
            PlayerId = r.GetString(2),
            SubjectKey = r.GetString(3),
            ClaimKey = r.GetString(4),
            ClaimText = r.GetString(5),
            Confidence = r.GetInt32(6),
            SourceType = r.GetString(7),
            SourceNpcId = r.IsDBNull(8) ? null : r.GetInt32(8),
            SourceCharacterKey = r.IsDBNull(9) ? "" : r.GetString(9),
            OriginConversationEventId = r.IsDBNull(10) ? null : r.GetInt64(10),
            OriginConversationFactId = r.IsDBNull(11) ? null : r.GetInt64(11),
            OriginPerceptionEvidenceId = r.IsDBNull(12) ? null : r.GetInt64(12),
            OriginClaimId = r.IsDBNull(13) ? null : r.GetInt64(13),
            RootOriginClaimId = r.IsDBNull(14) ? null : r.GetInt64(14),
            Generation = r.GetInt32(15),
            Status = r.GetString(16),
            LearnedGameTime = ParseTime(r.GetString(17), DateTimeOffset.MinValue),
            LastReinforcedGameTime = ParseTime(r.GetString(18), DateTimeOffset.MinValue)
        };

    private static string DescribeSource(NpcKnowledgeClaim row)
    {
        if (row.SourceType.Equals("gossip_report", StringComparison.OrdinalIgnoreCase))
            return row.SourceNpcId.HasValue
                ? $"reported by NPC {row.SourceNpcId.Value}"
                : "reported by another NPC";

        if (row.SourceType.StartsWith("direct_perception", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(row.SourceCharacterKey)
                ? "direct perception"
                : "direct perception of " + row.SourceCharacterKey;

        if (row.SourceType.StartsWith("conversation:", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(row.SourceCharacterKey)
                ? "learned in direct conversation"
                : "learned in direct conversation from " + row.SourceCharacterKey;

        return row.SourceType;
    }

    private static int ConfidenceFromPerception(string quality, double raw)
    {
        var rawPct = (int)Math.Round(Math.Clamp(raw, 0, 1) * 100.0);
        var cap = quality.ToLowerInvariant() switch
        {
            "clear" => 96,
            "partial" => 75,
            "fragment" => 50,
            "glimpse" => 45,
            _ => 30
        };

        return Math.Clamp(Math.Min(cap, Math.Max(10, rawPct)), 0, 100);
    }

    private static string ConversationSourceCharacterKey(
        string sourceType,
        string playerId,
        int holderNpcId)
    {
        if (sourceType.Equals("direct_npc_disclosure", StringComparison.OrdinalIgnoreCase))
            return "npc:" + holderNpcId.ToString(CultureInfo.InvariantCulture);

        if (sourceType.Equals("direct_player_disclosure", StringComparison.OrdinalIgnoreCase) ||
            sourceType.Equals("claim", StringComparison.OrdinalIgnoreCase))
            return "player:" + Clean(playerId, "unknown-player");

        return "";
    }

    private static string MapConversationSubject(
        string subject,
        string playerId,
        int holderNpcId)
    {
        subject = Clean(subject, "unknown");
        if (subject.Equals("player", StringComparison.OrdinalIgnoreCase))
            return "player:" + Clean(playerId, "unknown-player");
        if (subject.Equals("npc", StringComparison.OrdinalIgnoreCase))
            return "npc:" + holderNpcId.ToString(CultureInfo.InvariantCulture);
        if (subject.StartsWith("other:", StringComparison.OrdinalIgnoreCase))
            return "person:" + subject[6..].Trim();
        return subject;
    }

    private static string ScopePlayerId(string subjectKey, string playerId)
        => subjectKey.StartsWith("player:", StringComparison.OrdinalIgnoreCase)
            ? Clean(playerId, "")
            : "";

    private static string PlayerScopeFromCharacterKey(string characterKey)
    {
        if (characterKey.StartsWith("player:", StringComparison.OrdinalIgnoreCase))
            return characterKey[7..].Trim();
        return "";
    }

    private static bool ConversationFactAlreadyImported(
        SqliteConnection conn,
        int holderNpcId,
        long factId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM NpcKnowledgeClaim
            WHERE HolderNpcId=$holder AND OriginConversationFactId=$fact
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$holder", holderNpcId);
        cmd.Parameters.AddWithValue("$fact", factId);
        return cmd.ExecuteScalar() != null;
    }

    private static bool PerceptionAlreadyImported(
        SqliteConnection conn,
        int holderNpcId,
        long evidenceId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM NpcKnowledgeClaim
            WHERE HolderNpcId=$holder AND OriginPerceptionEvidenceId=$evidence
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$holder", holderNpcId);
        cmd.Parameters.AddWithValue("$evidence", evidenceId);
        return cmd.ExecuteScalar() != null;
    }

    private static int? ParseNpcIdOrNull(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey) ||
            !characterKey.StartsWith("npc:", StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(
            characterKey[4..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var npcId) && npcId > 0
            ? npcId
            : null;
    }

    private static bool TableExists(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return cmd.ExecuteScalar() != null;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _dbPath);
        conn.Open();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcKnowledgeClaim(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                HolderNpcId INTEGER NOT NULL,
                PlayerId TEXT NOT NULL DEFAULT '',
                SubjectKey TEXT NOT NULL DEFAULT 'unknown',
                ClaimKey TEXT NOT NULL DEFAULT 'statement',
                ClaimText TEXT NOT NULL,
                Confidence INTEGER NOT NULL DEFAULT 70,
                SourceType TEXT NOT NULL DEFAULT 'learned',
                SourceNpcId INTEGER NULL,
                SourceCharacterKey TEXT NULL,
                OriginConversationEventId INTEGER NULL,
                OriginConversationFactId INTEGER NULL,
                OriginPerceptionEvidenceId INTEGER NULL,
                OriginClaimId INTEGER NULL,
                RootOriginClaimId INTEGER NULL,
                Generation INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'held',
                LearnedGameTime TEXT NOT NULL,
                LastReinforcedGameTime TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS KnowledgeTransmission(
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FromNpcId INTEGER NOT NULL,
                ToNpcId INTEGER NOT NULL,
                PlayerId TEXT NOT NULL DEFAULT '',
                SourceClaimId INTEGER NOT NULL,
                ResultClaimId INTEGER NOT NULL,
                ReportedText TEXT NOT NULL,
                Channel TEXT NOT NULL DEFAULT 'in_person',
                SceneId TEXT NOT NULL DEFAULT '',
                Generation INTEGER NOT NULL,
                SourceConfidence INTEGER NOT NULL,
                ResultConfidence INTEGER NOT NULL,
                GameTime TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL);

            CREATE INDEX IF NOT EXISTS IX_NpcKnowledgeClaim_HolderRecent
                ON NpcKnowledgeClaim(HolderNpcId,Id DESC);

            CREATE INDEX IF NOT EXISTS IX_NpcKnowledgeClaim_PlayerScope
                ON NpcKnowledgeClaim(HolderNpcId,PlayerId,Id DESC);

            CREATE INDEX IF NOT EXISTS IX_NpcKnowledgeClaim_Subject
                ON NpcKnowledgeClaim(HolderNpcId,SubjectKey,ClaimKey);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcKnowledgeClaim_ConversationFact
                ON NpcKnowledgeClaim(HolderNpcId,OriginConversationFactId)
                WHERE OriginConversationFactId IS NOT NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcKnowledgeClaim_PerceptionEvidence
                ON NpcKnowledgeClaim(HolderNpcId,OriginPerceptionEvidenceId)
                WHERE OriginPerceptionEvidenceId IS NOT NULL;

            CREATE INDEX IF NOT EXISTS IX_KnowledgeTransmission_ToNpc
                ON KnowledgeTransmission(ToNpcId,Id DESC);

            CREATE INDEX IF NOT EXISTS IX_KnowledgeTransmission_SourceClaim
                ON KnowledgeTransmission(SourceClaimId,Id DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    private static DateTimeOffset ParseTime(string value, DateTimeOffset fallback)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : fallback;

    private static string Clean(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed class ConversationFactRow
    {
        public long Id { get; set; }
        public string Subject { get; set; } = "";
        public string FactKey { get; set; } = "";
        public string FactValue { get; set; } = "";
        public int Confidence { get; set; }
        public string SourceType { get; set; } = "";
    }

    private sealed class PerceptionRow
    {
        public long Id { get; set; }
        public string EventKind { get; set; } = "";
        public string SourceCharacterKey { get; set; } = "";
        public string Quality { get; set; } = "";
        public string PerceivedText { get; set; } = "";
        public double Confidence { get; set; }
        public DateTimeOffset GameTime { get; set; }
    }
}
