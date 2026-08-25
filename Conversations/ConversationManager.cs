using Microsoft.Data.Sqlite;
using ProjectEve.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectEve.Conversations
{
    /// <summary>
    /// Persistent conversation-section truth.
    ///
    /// ACTIVE SECTION:
    /// complete exact transcript is preserved and may be fed to Brain.
    ///
    /// CLOSED SECTION:
    /// exact transcript remains; event summary + learned facts + plans are indexed.
    ///
    /// PlayerId is string-based to match PhoneOS player profiles and future multiplayer.
    /// </summary>
    public static class ConversationManager
    {
        public const string LegacyPlayerId = "legacy-player";
        private static string DbPath =>
            ProjectEveDatabaseSetup.HistoryDatabasePath;

        private static string ConnStr => "Data Source=" + DbPath;

        public static void Initialize()
        {
            ProjectEveDatabaseSetup.EnsureAll();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            EnsureSchema(conn);
            EnsurePlayerIdMigration(conn);
            MigrateLegacyMainConversationDataIfNeeded(conn);
        }

        // -----------------------------------------------------------------
        // START / RESUME
        // -----------------------------------------------------------------

        public static long StartOrResume(
            int npcId,
            string npcName,
            string playerName,
            string channel,
            string location,
            DateTime? gameTime = null)
            => StartOrResume(
                LegacyPlayerId,
                npcId,
                npcName,
                playerName,
                channel,
                location,
                gameTime);

        public static long StartOrResume(
            string playerId,
            int npcId,
            string npcName,
            string playerName,
            string channel,
            string location,
            DateTime? gameTime = null)
        {
            Initialize();

            playerId = Clean(playerId, LegacyPlayerId);
            playerName = Clean(playerName, "Player");
            npcName = Clean(npcName, "NPC");
            channel = Clean(channel, "text");
            location = Clean(location, "unknown");
            gameTime ??= DateTime.Now;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using (var find = conn.CreateCommand())
            {
                find.CommandText = """
                    SELECT Id
                    FROM ConversationSession
                    WHERE PlayerId=$playerId
                      AND NpcId=$npc
                      AND PlayerName=$playerName
                      AND Status='open'
                      AND Channel=$channel
                      AND Location=$location
                    ORDER BY Id DESC
                    LIMIT 1;
                    """;
                find.Parameters.AddWithValue("$playerId", playerId);
                find.Parameters.AddWithValue("$npc", npcId);
                find.Parameters.AddWithValue("$playerName", playerName);
                find.Parameters.AddWithValue("$channel", channel);
                find.Parameters.AddWithValue("$location", location);

                object? existing = find.ExecuteScalar();
                if (existing != null && existing != DBNull.Value)
                    return Convert.ToInt64(existing, CultureInfo.InvariantCulture);
            }

            using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO ConversationSession
                (PlayerId,NpcId,NpcName,PlayerName,Channel,Location,
                 StartedGameTime,StartedUtc,LastMessageUtc,Status)
                VALUES
                ($playerId,$npc,$npcName,$playerName,$channel,$location,
                 $game,$utc,$utc,'open');
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$playerId", playerId);
            insert.Parameters.AddWithValue("$npc", npcId);
            insert.Parameters.AddWithValue("$npcName", npcName);
            insert.Parameters.AddWithValue("$playerName", playerName);
            insert.Parameters.AddWithValue("$channel", channel);
            insert.Parameters.AddWithValue("$location", location);
            insert.Parameters.AddWithValue("$game", gameTime.Value.ToString("O"));
            insert.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

            return Convert.ToInt64(
                insert.ExecuteScalar(),
                CultureInfo.InvariantCulture);
        }

        public static long? GetActiveSessionId(
            int npcId,
            string playerName)
            => GetActiveSessionId(
                LegacyPlayerId,
                npcId,
                playerName);

        public static long? GetActiveSessionId(
            string playerId,
            int npcId,
            string playerName)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id
                FROM ConversationSession
                WHERE PlayerId=$playerId
                  AND NpcId=$npc
                  AND PlayerName=$playerName
                  AND Status='open'
                ORDER BY Id DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$playerId", Clean(playerId, LegacyPlayerId));
            cmd.Parameters.AddWithValue("$npc", npcId);
            cmd.Parameters.AddWithValue("$playerName", Clean(playerName, "Player"));

            object? v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value
                ? null
                : Convert.ToInt64(v, CultureInfo.InvariantCulture);
        }

        public static ConversationSessionRow? GetSession(long sessionId)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id,PlayerId,NpcId,NpcName,PlayerName,
                       Channel,Location,StartedGameTime,Status
                FROM ConversationSession
                WHERE Id=$id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", sessionId);

            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;

            return new ConversationSessionRow
            {
                Id = r.GetInt64(0),
                PlayerId = r.GetString(1),
                NpcId = r.GetInt32(2),
                NpcName = r.GetString(3),
                PlayerName = r.GetString(4),
                Channel = r.GetString(5),
                Location = r.GetString(6),
                StartedGameTime = ParseDate(r.GetString(7)),
                Status = r.GetString(8)
            };
        }

        /// <summary>
        /// A change of channel/location is a section boundary.
        /// Example: text -> in_person closes/summarizes text before the meetup begins.
        /// </summary>
        public static async Task<IReadOnlyList<ConversationCloseResult>>
            EndOpenSectionsExceptAsync(
                string playerId,
                int npcId,
                string playerName,
                string keepChannel,
                string keepLocation,
                string reason = "conversation context changed",
                CancellationToken cancellationToken = default)
        {
            Initialize();

            playerId = Clean(playerId, LegacyPlayerId);
            playerName = Clean(playerName, "Player");
            keepChannel = Clean(keepChannel, "");
            keepLocation = Clean(keepLocation, "");

            var ids = new List<long>();

            using (var conn = new SqliteConnection(ConnStr))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id
                    FROM ConversationSession
                    WHERE PlayerId=$playerId
                      AND NpcId=$npc
                      AND PlayerName=$playerName
                      AND Status='open'
                      AND NOT (Channel=$channel AND Location=$location)
                    ORDER BY Id;
                    """;
                cmd.Parameters.AddWithValue("$playerId", playerId);
                cmd.Parameters.AddWithValue("$npc", npcId);
                cmd.Parameters.AddWithValue("$playerName", playerName);
                cmd.Parameters.AddWithValue("$channel", keepChannel);
                cmd.Parameters.AddWithValue("$location", keepLocation);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                    ids.Add(r.GetInt64(0));
            }

            var closed = new List<ConversationCloseResult>();
            foreach (long id in ids)
            {
                var result = await EndSectionAsync(
                    id,
                    reason,
                    DateTime.Now,
                    cancellationToken);

                if (result != null)
                    closed.Add(result);
            }

            return closed;
        }

        // -----------------------------------------------------------------
        // EXACT TRANSCRIPT
        // -----------------------------------------------------------------

        public static void AppendPlayer(
            long sessionId,
            string playerName,
            string text,
            DateTime? gameTime = null)
            => AppendMessage(
                sessionId,
                "player",
                playerName,
                text,
                gameTime,
                null);

        public static void AppendNpc(
            long sessionId,
            int npcId,
            string npcName,
            string text,
            DateTime? gameTime = null)
            => AppendMessage(
                sessionId,
                "npc",
                npcName,
                text,
                gameTime,
                npcId);

        public static void AppendSystem(
            long sessionId,
            string text,
            DateTime? gameTime = null)
            => AppendMessage(
                sessionId,
                "system",
                "ProjectEve",
                text,
                gameTime,
                null);

        public static void AppendMessage(
            long sessionId,
            string role,
            string speaker,
            string text,
            DateTime? gameTime = null,
            int? speakerNpcId = null)
        {
            if (sessionId <= 0 || string.IsNullOrWhiteSpace(text))
                return;

            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO ConversationMessage
                    (SessionId,Sequence,Role,Speaker,SpeakerNpcId,
                     MessageText,GameTime,CreatedUtc)
                    VALUES
                    ($session,
                     COALESCE(
                        (SELECT MAX(Sequence)+1
                         FROM ConversationMessage
                         WHERE SessionId=$session),1),
                     $role,$speaker,$speakerNpc,$text,$game,$utc);
                    """;
                cmd.Parameters.AddWithValue("$session", sessionId);
                cmd.Parameters.AddWithValue("$role", Clean(role, "unknown"));
                cmd.Parameters.AddWithValue("$speaker", Clean(speaker, "unknown"));
                cmd.Parameters.AddWithValue("$speakerNpc", (object?)speakerNpcId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$text", text.Trim());
                cmd.Parameters.AddWithValue("$game", (gameTime ?? DateTime.Now).ToString("O"));
                cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            using (var touch = conn.CreateCommand())
            {
                touch.Transaction = tx;
                touch.CommandText = """
                    UPDATE ConversationSession
                    SET LastMessageUtc=$utc
                    WHERE Id=$id;
                    """;
                touch.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
                touch.Parameters.AddWithValue("$id", sessionId);
                touch.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public static string BuildActiveTranscript(long sessionId)
        {
            if (sessionId <= 0)
                return "No active conversation section.";

            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Role,Speaker,MessageText
                FROM ConversationMessage
                WHERE SessionId=$session
                ORDER BY Sequence;
                """;
            cmd.Parameters.AddWithValue("$session", sessionId);

            var sb = new StringBuilder();
            sb.AppendLine("ACTIVE CONVERSATION SECTION â€” EXACT TRANSCRIPT");
            sb.AppendLine("This is authoritative for what was actually said.");

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string role = r.GetString(0);
                string speaker = r.GetString(1);
                string message = r.GetString(2);

                sb.AppendLine(
                    role.Equals("system", StringComparison.OrdinalIgnoreCase)
                        ? $"[SYSTEM] {message}"
                        : $"{speaker}: {message}");
            }

            return sb.ToString().TrimEnd();
        }

        public static IReadOnlyList<ConversationMessageRow>
            GetTranscript(long sessionId)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id,SessionId,Sequence,Role,Speaker,SpeakerNpcId,
                       MessageText,GameTime,CreatedUtc
                FROM ConversationMessage
                WHERE SessionId=$session
                ORDER BY Sequence;
                """;
            cmd.Parameters.AddWithValue("$session", sessionId);

            var rows = new List<ConversationMessageRow>();
            using var r = cmd.ExecuteReader();

            while (r.Read())
            {
                rows.Add(new ConversationMessageRow
                {
                    Id = r.GetInt64(0),
                    SessionId = r.GetInt64(1),
                    Sequence = r.GetInt32(2),
                    Role = r.GetString(3),
                    Speaker = r.GetString(4),
                    SpeakerNpcId = r.IsDBNull(5) ? null : r.GetInt32(5),
                    MessageText = r.GetString(6),
                    GameTime = ParseDate(r.GetString(7)),
                    CreatedUtc = ParseDate(r.GetString(8))
                });
            }

            return rows;
        }

        // -----------------------------------------------------------------
        // CLOSE / EVENT SUMMARY
        // -----------------------------------------------------------------

        public static async Task<ConversationCloseResult?> EndSectionAsync(
            long sessionId,
            string reason = "conversation ended",
            DateTime? endedGameTime = null,
            CancellationToken cancellationToken = default)
        {
            if (sessionId <= 0)
                return null;

            Initialize();

            var session = GetSession(sessionId);
            if (session == null)
                return null;

            if (!session.Status.Equals("open", StringComparison.OrdinalIgnoreCase))
                return GetCloseResult(sessionId);

            var rows = GetTranscript(sessionId);

            // ConversationMessage remains the exact evidence transcript.
            // The NPC's event summary/facts/plans must be based on what THAT NPC
            // actually perceived, otherwise missed/partial speech becomes telepathy.
            string npcPerceptionTranscript = ConversationPerceptionStore.BuildTranscriptTextForNpc(
                rows,
                sessionId,
                session.NpcId);

            ConversationSummaryResult summary =
                rows.Count == 0
                    ? ConversationSummaryResult.Empty(
                        "Conversation ended without messages.")
                    : await ConversationSummaryEngine.SummarizeAsync(
                        session,
                        npcPerceptionTranscript,
                        cancellationToken);

            endedGameTime ??= DateTime.Now;

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            long eventId;

            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO ConversationEvent
                    (SessionId,PlayerId,NpcId,NpcName,PlayerName,
                     Channel,Location,StartedGameTime,EndedGameTime,
                     Summary,EmotionalOutcome,RelationshipOutcome,
                     EndReason,CreatedUtc)
                    VALUES
                    ($session,$playerId,$npc,$npcName,$playerName,
                     $channel,$location,$started,$ended,
                     $summary,$emotion,$relationship,$reason,$utc);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("$session", sessionId);
                cmd.Parameters.AddWithValue("$playerId", session.PlayerId);
                cmd.Parameters.AddWithValue("$npc", session.NpcId);
                cmd.Parameters.AddWithValue("$npcName", session.NpcName);
                cmd.Parameters.AddWithValue("$playerName", session.PlayerName);
                cmd.Parameters.AddWithValue("$channel", session.Channel);
                cmd.Parameters.AddWithValue("$location", session.Location);
                cmd.Parameters.AddWithValue("$started", session.StartedGameTime.ToString("O"));
                cmd.Parameters.AddWithValue("$ended", endedGameTime.Value.ToString("O"));
                cmd.Parameters.AddWithValue("$summary", summary.Summary);
                cmd.Parameters.AddWithValue("$emotion", summary.EmotionalOutcome ?? "");
                cmd.Parameters.AddWithValue("$relationship", summary.RelationshipOutcome ?? "");
                cmd.Parameters.AddWithValue("$reason", Clean(reason, "conversation ended"));
                cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));

                eventId = Convert.ToInt64(
                    cmd.ExecuteScalar(),
                    CultureInfo.InvariantCulture);
            }

            foreach (var fact in summary.Facts)
                InsertFact(conn, tx, eventId, session, fact);

            foreach (var plan in summary.Plans)
                InsertPlan(conn, tx, eventId, session, plan);

            using (var close = conn.CreateCommand())
            {
                close.Transaction = tx;
                close.CommandText = """
                    UPDATE ConversationSession
                    SET Status='closed',
                        EndedGameTime=$ended,
                        EndedUtc=$utc,
                        EndReason=$reason
                    WHERE Id=$session;
                    """;
                close.Parameters.AddWithValue("$ended", endedGameTime.Value.ToString("O"));
                close.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
                close.Parameters.AddWithValue("$reason", Clean(reason, "conversation ended"));
                close.Parameters.AddWithValue("$session", sessionId);
                close.ExecuteNonQuery();
            }

            tx.Commit();

            return new ConversationCloseResult
            {
                SessionId = sessionId,
                EventId = eventId,
                Summary = summary.Summary,
                FactsStored = summary.Facts.Count,
                PlansStored = summary.Plans.Count
            };
        }

        // -----------------------------------------------------------------
        // CROSS-SECTION CONTINUITY
        // -----------------------------------------------------------------

        public static string BuildContinuityContext(
            int npcId,
            string playerName,
            string? currentLocation = null,
            string? currentChannel = null,
            int maxEvents = 5)
            => BuildContinuityContext(
                LegacyPlayerId,
                npcId,
                playerName,
                currentLocation,
                currentChannel,
                maxEvents);

        public static string BuildContinuityContext(
            string playerId,
            int npcId,
            string playerName,
            string? currentLocation = null,
            string? currentChannel = null,
            int maxEvents = 5)
        {
            Initialize();

            playerId = Clean(playerId, LegacyPlayerId);
            playerName = Clean(playerName, "Player");
            maxEvents = Math.Clamp(maxEvents, 1, 10);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            var sb = new StringBuilder();
            sb.AppendLine("CROSS-SECTION CONTINUITY â€” ESTABLISHED PAST");

            using (var plans = conn.CreateCommand())
            {
                plans.CommandText = """
                    SELECT Description,TimeText,Location,Status
                    FROM ConversationPlan
                    WHERE PlayerId=$playerId
                      AND NpcId=$npc
                      AND PlayerName=$playerName
                      AND Status IN ('agreed','pending','planned','open')
                    ORDER BY Id DESC
                    LIMIT 8;
                    """;
                plans.Parameters.AddWithValue("$playerId", playerId);
                plans.Parameters.AddWithValue("$npc", npcId);
                plans.Parameters.AddWithValue("$playerName", playerName);

                using var r = plans.ExecuteReader();
                bool any = false;

                while (r.Read())
                {
                    if (!any)
                    {
                        sb.AppendLine();
                        sb.AppendLine("UNRESOLVED / ACTIVE PLANS:");
                        any = true;
                    }

                    sb.Append("- ").Append(r.GetString(0));

                    if (!r.IsDBNull(1) &&
                        !string.IsNullOrWhiteSpace(r.GetString(1)))
                        sb.Append(" | time: ").Append(r.GetString(1));

                    if (!r.IsDBNull(2) &&
                        !string.IsNullOrWhiteSpace(r.GetString(2)))
                        sb.Append(" | place: ").Append(r.GetString(2));

                    sb.Append(" | status: ")
                      .Append(r.GetString(3))
                      .AppendLine();
                }
            }

            using (var facts = conn.CreateCommand())
            {
                facts.CommandText = """
                    SELECT Subject,FactKey,FactValue,Confidence,SourceType
                    FROM ConversationFact
                    WHERE PlayerId=$playerId
                      AND NpcId=$npc
                      AND PlayerName=$playerName
                    ORDER BY Id DESC
                    LIMIT 16;
                    """;
                facts.Parameters.AddWithValue("$playerId", playerId);
                facts.Parameters.AddWithValue("$npc", npcId);
                facts.Parameters.AddWithValue("$playerName", playerName);

                using var r = facts.ExecuteReader();
                bool any = false;

                while (r.Read())
                {
                    if (!any)
                    {
                        sb.AppendLine();
                        sb.AppendLine("KNOWN FACTS FROM PRIOR CONVERSATIONS:");
                        any = true;
                    }

                    sb.AppendLine(
                        $"- {r.GetString(0)} / {r.GetString(1)} = {r.GetString(2)} " +
                        $"[confidence {r.GetInt32(3)}; {r.GetString(4)}]");
                }
            }

            using (var events = conn.CreateCommand())
            {
                events.CommandText = """
                    SELECT Id,Channel,Location,EndedGameTime,
                           Summary,EmotionalOutcome,RelationshipOutcome
                    FROM ConversationEvent
                    WHERE PlayerId=$playerId
                      AND NpcId=$npc
                      AND PlayerName=$playerName
                    ORDER BY Id DESC
                    LIMIT $max;
                    """;
                events.Parameters.AddWithValue("$playerId", playerId);
                events.Parameters.AddWithValue("$npc", npcId);
                events.Parameters.AddWithValue("$playerName", playerName);
                events.Parameters.AddWithValue("$max", maxEvents);

                using var r = events.ExecuteReader();
                bool any = false;

                while (r.Read())
                {
                    if (!any)
                    {
                        sb.AppendLine();
                        sb.AppendLine("RECENT COMPLETED CONVERSATION EVENTS:");
                        any = true;
                    }

                    sb.AppendLine(
                        $"- Event {r.GetInt64(0)} | {r.GetString(1)} | " +
                        $"{r.GetString(2)} | {r.GetString(3)}");
                    sb.AppendLine("  " + r.GetString(4));

                    if (!r.IsDBNull(5) &&
                        !string.IsNullOrWhiteSpace(r.GetString(5)))
                        sb.AppendLine(
                            "  Emotional outcome: " + r.GetString(5));

                    if (!r.IsDBNull(6) &&
                        !string.IsNullOrWhiteSpace(r.GetString(6)))
                        sb.AppendLine(
                            "  Relationship outcome: " + r.GetString(6));
                }
            }

            if (!string.IsNullOrWhiteSpace(currentLocation))
            {
                sb.AppendLine();
                sb.AppendLine(
                    "CURRENT LOCATION: " + currentLocation.Trim());
                sb.AppendLine(
                    "If an unresolved plan says they agreed to meet here, " +
                    "that meetup is established continuity.");
            }

            if (!string.IsNullOrWhiteSpace(currentChannel))
                sb.AppendLine(
                    "CURRENT CHANNEL: " + currentChannel.Trim());

            return sb.ToString().TrimEnd();
        }

        public static string GetExactEventTranscript(long eventId)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT SessionId
                FROM ConversationEvent
                WHERE Id=$id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", eventId);

            object? v = cmd.ExecuteScalar();
            if (v == null || v == DBNull.Value)
                return "";

            return BuildExactTranscriptText(
                GetTranscript(
                    Convert.ToInt64(
                        v,
                        CultureInfo.InvariantCulture)));
        }

        public static void UpdatePlanStatus(
            long planId,
            string status)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE ConversationPlan
                SET Status=$status,
                    UpdatedUtc=$utc
                WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$status", Clean(status, "completed"));
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", planId);
            cmd.ExecuteNonQuery();
        }

        // -----------------------------------------------------------------
        // INTERNAL
        // -----------------------------------------------------------------

        private static ConversationCloseResult? GetCloseResult(
            long sessionId)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id,Summary
                FROM ConversationEvent
                WHERE SessionId=$session
                ORDER BY Id DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$session", sessionId);

            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;

            return new ConversationCloseResult
            {
                SessionId = sessionId,
                EventId = r.GetInt64(0),
                Summary = r.GetString(1)
            };
        }

        private static string BuildExactTranscriptText(
            IReadOnlyList<ConversationMessageRow> rows)
        {
            var sb = new StringBuilder();

            foreach (var m in rows)
            {
                sb.AppendLine(
                    m.Role.Equals(
                        "system",
                        StringComparison.OrdinalIgnoreCase)
                        ? $"[SYSTEM] {m.MessageText}"
                        : $"{m.Speaker}: {m.MessageText}");
            }

            return sb.ToString().TrimEnd();
        }

        private static void InsertFact(
            SqliteConnection conn,
            SqliteTransaction tx,
            long eventId,
            ConversationSessionRow session,
            ConversationFactCandidate fact)
        {
            if (string.IsNullOrWhiteSpace(fact.FactKey) ||
                string.IsNullOrWhiteSpace(fact.FactValue))
                return;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = """
                INSERT INTO ConversationFact
                (EventId,PlayerId,NpcId,PlayerName,Subject,
                 FactKey,FactValue,Confidence,SourceType,CreatedUtc)
                VALUES
                ($event,$playerId,$npc,$playerName,$subject,
                 $key,$value,$confidence,$source,$utc);
                """;
            cmd.Parameters.AddWithValue("$event", eventId);
            cmd.Parameters.AddWithValue("$playerId", session.PlayerId);
            cmd.Parameters.AddWithValue("$npc", session.NpcId);
            cmd.Parameters.AddWithValue("$playerName", session.PlayerName);
            cmd.Parameters.AddWithValue("$subject", Clean(fact.Subject, "unknown"));
            cmd.Parameters.AddWithValue("$key", fact.FactKey.Trim());
            cmd.Parameters.AddWithValue("$value", fact.FactValue.Trim());
            cmd.Parameters.AddWithValue(
                "$confidence",
                Math.Clamp(fact.Confidence, 0, 100));
            cmd.Parameters.AddWithValue(
                "$source",
                Clean(fact.SourceType, "conversation"));
            cmd.Parameters.AddWithValue(
                "$utc",
                DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        private static void InsertPlan(
            SqliteConnection conn,
            SqliteTransaction tx,
            long eventId,
            ConversationSessionRow session,
            ConversationPlanCandidate plan)
        {
            if (string.IsNullOrWhiteSpace(plan.Description))
                return;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = """
                INSERT INTO ConversationPlan
                (EventId,PlayerId,NpcId,PlayerName,Description,
                 TimeText,Location,Status,CreatedUtc,UpdatedUtc)
                VALUES
                ($event,$playerId,$npc,$playerName,$description,
                 $time,$location,$status,$utc,$utc);
                """;
            cmd.Parameters.AddWithValue("$event", eventId);
            cmd.Parameters.AddWithValue("$playerId", session.PlayerId);
            cmd.Parameters.AddWithValue("$npc", session.NpcId);
            cmd.Parameters.AddWithValue("$playerName", session.PlayerName);
            cmd.Parameters.AddWithValue(
                "$description",
                plan.Description.Trim());
            cmd.Parameters.AddWithValue(
                "$time",
                Clean(plan.TimeText, ""));
            cmd.Parameters.AddWithValue(
                "$location",
                Clean(plan.Location, ""));
            cmd.Parameters.AddWithValue(
                "$status",
                Clean(plan.Status, "planned"));
            cmd.Parameters.AddWithValue(
                "$utc",
                DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }


        /// <summary>
        /// One-time compatibility migration:
        /// if canonical history conversation tables are empty, copy any existing
        /// conversation rows from the legacy main DB. Legacy rows are never deleted.
        /// </summary>
        private static void MigrateLegacyMainConversationDataIfNeeded(
            SqliteConnection conn)
        {
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM ConversationSession;";
                long existing = Convert.ToInt64(count.ExecuteScalar() ?? 0);
                if (existing > 0)
                    return;
            }

            string legacyMainPath = ProjectEveDatabaseSetup.MainDatabasePath;

            if (!System.IO.File.Exists(legacyMainPath) ||
                string.Equals(
                    legacyMainPath,
                    DbPath,
                    StringComparison.OrdinalIgnoreCase))
                return;

            string escaped = legacyMainPath.Replace("'", "''");

            using var tx = conn.BeginTransaction();

            try
            {
                using (var attach = conn.CreateCommand())
                {
                    attach.Transaction = tx;
                    attach.CommandText = $"ATTACH DATABASE '{escaped}' AS legacy_main;";
                    attach.ExecuteNonQuery();
                }

                if (!LegacyTableExists(conn, tx, "ConversationSession"))
                {
                    tx.Rollback();
                    return;
                }

                CopyLegacyTable(
                    conn, tx, "ConversationSession",
                    "Id,PlayerId,NpcId,NpcName,PlayerName,Channel,Location," +
                    "StartedGameTime,EndedGameTime,StartedUtc,EndedUtc," +
                    "LastMessageUtc,Status,EndReason");

                CopyLegacyTable(
                    conn, tx, "ConversationMessage",
                    "Id,SessionId,Sequence,Role,Speaker,SpeakerNpcId," +
                    "MessageText,GameTime,CreatedUtc");

                CopyLegacyTable(
                    conn, tx, "ConversationEvent",
                    "Id,SessionId,PlayerId,NpcId,NpcName,PlayerName,Channel," +
                    "Location,StartedGameTime,EndedGameTime,Summary," +
                    "EmotionalOutcome,RelationshipOutcome,EndReason,CreatedUtc");

                CopyLegacyTable(
                    conn, tx, "ConversationFact",
                    "Id,EventId,PlayerId,NpcId,PlayerName,Subject,FactKey," +
                    "FactValue,Confidence,SourceType,CreatedUtc");

                CopyLegacyTable(
                    conn, tx, "ConversationPlan",
                    "Id,EventId,PlayerId,NpcId,PlayerName,Description,TimeText," +
                    "Location,Status,CreatedUtc,UpdatedUtc");

                using (var detach = conn.CreateCommand())
                {
                    detach.Transaction = tx;
                    detach.CommandText = "DETACH DATABASE legacy_main;";
                    detach.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                // Migration is compatibility-only. Never block startup or delete legacy data.
            }
        }

        private static bool LegacyTableExists(
            SqliteConnection conn,
            SqliteTransaction tx,
            string tableName)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM legacy_main.sqlite_master
                WHERE type='table' AND name=$name;
                """;
            cmd.Parameters.AddWithValue("$name", tableName);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
        }

        private static void CopyLegacyTable(
            SqliteConnection conn,
            SqliteTransaction tx,
            string tableName,
            string columns)
        {
            if (!LegacyTableExists(conn, tx, tableName))
                return;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                $"INSERT OR IGNORE INTO {tableName} ({columns}) " +
                $"SELECT {columns} FROM legacy_main.{tableName};";
            cmd.ExecuteNonQuery();
        }
        private static void EnsureSchema(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                PRAGMA foreign_keys=ON;

                CREATE TABLE IF NOT EXISTS ConversationSession(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PlayerId TEXT NOT NULL DEFAULT 'legacy-player',
                    NpcId INTEGER NOT NULL,
                    NpcName TEXT NOT NULL,
                    PlayerName TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    Location TEXT NOT NULL,
                    StartedGameTime TEXT NOT NULL,
                    EndedGameTime TEXT NULL,
                    StartedUtc TEXT NOT NULL,
                    EndedUtc TEXT NULL,
                    LastMessageUtc TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'open',
                    EndReason TEXT NULL);

                CREATE TABLE IF NOT EXISTS ConversationMessage(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL,
                    Sequence INTEGER NOT NULL,
                    Role TEXT NOT NULL,
                    Speaker TEXT NOT NULL,
                    SpeakerNpcId INTEGER NULL,
                    MessageText TEXT NOT NULL,
                    GameTime TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    FOREIGN KEY(SessionId)
                        REFERENCES ConversationSession(Id)
                        ON DELETE CASCADE,
                    UNIQUE(SessionId,Sequence));

                CREATE TABLE IF NOT EXISTS ConversationEvent(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL UNIQUE,
                    PlayerId TEXT NOT NULL DEFAULT 'legacy-player',
                    NpcId INTEGER NOT NULL,
                    NpcName TEXT NOT NULL,
                    PlayerName TEXT NOT NULL,
                    Channel TEXT NOT NULL,
                    Location TEXT NOT NULL,
                    StartedGameTime TEXT NOT NULL,
                    EndedGameTime TEXT NOT NULL,
                    Summary TEXT NOT NULL,
                    EmotionalOutcome TEXT NOT NULL DEFAULT '',
                    RelationshipOutcome TEXT NOT NULL DEFAULT '',
                    EndReason TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    FOREIGN KEY(SessionId)
                        REFERENCES ConversationSession(Id)
                        ON DELETE CASCADE);

                CREATE TABLE IF NOT EXISTS ConversationFact(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventId INTEGER NOT NULL,
                    PlayerId TEXT NOT NULL DEFAULT 'legacy-player',
                    NpcId INTEGER NOT NULL,
                    PlayerName TEXT NOT NULL,
                    Subject TEXT NOT NULL,
                    FactKey TEXT NOT NULL,
                    FactValue TEXT NOT NULL,
                    Confidence INTEGER NOT NULL DEFAULT 100,
                    SourceType TEXT NOT NULL DEFAULT 'conversation',
                    CreatedUtc TEXT NOT NULL,
                    FOREIGN KEY(EventId)
                        REFERENCES ConversationEvent(Id)
                        ON DELETE CASCADE);

                CREATE TABLE IF NOT EXISTS ConversationPlan(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    EventId INTEGER NOT NULL,
                    PlayerId TEXT NOT NULL DEFAULT 'legacy-player',
                    NpcId INTEGER NOT NULL,
                    PlayerName TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    TimeText TEXT NOT NULL DEFAULT '',
                    Location TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'planned',
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    FOREIGN KEY(EventId)
                        REFERENCES ConversationEvent(Id)
                        ON DELETE CASCADE);
                """;

            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Existing v1 DBs did not have PlayerId.
        /// ALTER TABLE is applied only when the column is missing.
        /// </summary>
        private static void EnsurePlayerIdMigration(
            SqliteConnection conn)
        {
            EnsureColumn(
                conn,
                "ConversationSession",
                "PlayerId",
                "TEXT NOT NULL DEFAULT 'legacy-player'");

            EnsureColumn(
                conn,
                "ConversationEvent",
                "PlayerId",
                "TEXT NOT NULL DEFAULT 'legacy-player'");

            EnsureColumn(
                conn,
                "ConversationFact",
                "PlayerId",
                "TEXT NOT NULL DEFAULT 'legacy-player'");

            EnsureColumn(
                conn,
                "ConversationPlan",
                "PlayerId",
                "TEXT NOT NULL DEFAULT 'legacy-player'");

            using var indexes = conn.CreateCommand();
            indexes.CommandText = """
                CREATE INDEX IF NOT EXISTS
                    IX_ConversationSession_PlayerOpen
                    ON ConversationSession(
                        PlayerId,NpcId,PlayerName,Status);

                CREATE INDEX IF NOT EXISTS
                    IX_ConversationMessage_Session
                    ON ConversationMessage(SessionId,Sequence);

                CREATE INDEX IF NOT EXISTS
                    IX_ConversationEvent_PlayerPeople
                    ON ConversationEvent(
                        PlayerId,NpcId,PlayerName,Id DESC);

                CREATE INDEX IF NOT EXISTS
                    IX_ConversationFact_PlayerPeople
                    ON ConversationFact(
                        PlayerId,NpcId,PlayerName,FactKey);

                CREATE INDEX IF NOT EXISTS
                    IX_ConversationPlan_PlayerOpen
                    ON ConversationPlan(
                        PlayerId,NpcId,PlayerName,Status);
                """;
            indexes.ExecuteNonQuery();
        }

        private static void EnsureColumn(
            SqliteConnection conn,
            string table,
            string column,
            string sqlTypeAndDefault)
        {
            bool exists = false;

            using (var info = conn.CreateCommand())
            {
                info.CommandText = $"PRAGMA table_info({table});";

                using var r = info.ExecuteReader();
                while (r.Read())
                {
                    if (string.Equals(
                        r.GetString(1),
                        column,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (exists)
                return;

            using var alter = conn.CreateCommand();
            alter.CommandText =
                $"ALTER TABLE {table} ADD COLUMN {column} {sqlTypeAndDefault};";
            alter.ExecuteNonQuery();
        }

        private static DateTime ParseDate(string value)
            => DateTime.TryParse(
                value,
                null,
                DateTimeStyles.RoundtripKind,
                out var d)
                ? d
                : DateTime.MinValue;

        private static string Clean(
            string? value,
            string fallback)
            => string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
    }

    public sealed class ConversationSessionRow
    {
        public long Id { get; set; }
        public string PlayerId { get; set; } =
            ConversationManager.LegacyPlayerId;
        public int NpcId { get; set; }
        public string NpcName { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string Channel { get; set; } = "text";
        public string Location { get; set; } = "unknown";
        public DateTime StartedGameTime { get; set; }
        public string Status { get; set; } = "open";
    }

    public sealed class ConversationMessageRow
    {
        public long Id { get; set; }
        public long SessionId { get; set; }
        public int Sequence { get; set; }
        public string Role { get; set; } = "";
        public string Speaker { get; set; } = "";
        public int? SpeakerNpcId { get; set; }
        public string MessageText { get; set; } = "";
        public DateTime GameTime { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class ConversationCloseResult
    {
        public long SessionId { get; set; }
        public long EventId { get; set; }
        public string Summary { get; set; } = "";
        public int FactsStored { get; set; }
        public int PlansStored { get; set; }
    }
}

