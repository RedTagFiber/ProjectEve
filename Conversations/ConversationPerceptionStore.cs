using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ProjectEve.Conversations
{
    /// <summary>
    /// Observer-specific overlay for exact conversation evidence.
    /// ConversationMessage remains the immutable exact record of what physically occurred.
    /// This store records what the NPC participant actually perceived for a player message.
    /// </summary>
    public static class ConversationPerceptionStore
    {
        private static string DbPath =>
            Environment.GetEnvironmentVariable("EVE_DB_PATH")
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "project_eve.db");

        private static string ConnStr => "Data Source=" + DbPath;

        public static void UpsertLatestPlayerPerception(
            long sessionId,
            int observerNpcId,
            string perceivedText,
            string? sourceEventKey = null)
        {
            if (sessionId <= 0 || observerNpcId <= 0 || string.IsNullOrWhiteSpace(perceivedText))
                return;

            EnsureSchema();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            long? messageId = null;
            using (var find = conn.CreateCommand())
            {
                find.CommandText = """
                    SELECT Id
                    FROM ConversationMessage
                    WHERE SessionId=$session
                      AND Role='player'
                    ORDER BY Sequence DESC
                    LIMIT 1;
                    """;
                find.Parameters.AddWithValue("$session", sessionId);
                var found = find.ExecuteScalar();
                if (found != null && found != DBNull.Value)
                    messageId = Convert.ToInt64(found);
            }

            if (!messageId.HasValue)
                return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ConversationMessagePerception
                    (MessageId,SessionId,ObserverNpcId,PerceivedText,SourceEventKey,CreatedUtc)
                VALUES($message,$session,$npc,$text,$source,$utc)
                ON CONFLICT(MessageId,ObserverNpcId) DO UPDATE SET
                    PerceivedText=excluded.PerceivedText,
                    SourceEventKey=excluded.SourceEventKey,
                    CreatedUtc=excluded.CreatedUtc;
                """;
            cmd.Parameters.AddWithValue("$message", messageId.Value);
            cmd.Parameters.AddWithValue("$session", sessionId);
            cmd.Parameters.AddWithValue("$npc", observerNpcId);
            cmd.Parameters.AddWithValue("$text", perceivedText.Trim());
            cmd.Parameters.AddWithValue("$source", string.IsNullOrWhiteSpace(sourceEventKey) ? DBNull.Value : (object)sourceEventKey.Trim());
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        public static string BuildActiveTranscriptForNpc(long sessionId, int observerNpcId)
        {
            var rows = ConversationManager.GetTranscript(sessionId);
            if (rows.Count == 0)
                return "No active conversation section.";

            var overlays = GetOverlays(sessionId, observerNpcId);
            var sb = new StringBuilder();
            sb.AppendLine("ACTIVE CONVERSATION SECTION — NPC PERCEPTION VIEW");
            sb.AppendLine("Exact transcript evidence is stored separately. Player lines below reflect what this NPC perceived when an overlay exists.");

            foreach (var row in rows)
            {
                string message = row.MessageText;
                if (row.Role.Equals("player", StringComparison.OrdinalIgnoreCase) &&
                    overlays.TryGetValue(row.Id, out var perceived))
                {
                    message = perceived;
                }

                sb.AppendLine(
                    row.Role.Equals("system", StringComparison.OrdinalIgnoreCase)
                        ? $"[SYSTEM] {message}"
                        : $"{row.Speaker}: {message}");
            }

            return sb.ToString().TrimEnd();
        }

        public static string BuildTranscriptTextForNpc(
            IReadOnlyList<ConversationMessageRow> rows,
            long sessionId,
            int observerNpcId)
        {
            if (rows.Count == 0)
                return string.Empty;

            var overlays = GetOverlays(sessionId, observerNpcId);
            var sb = new StringBuilder();

            foreach (var row in rows)
            {
                string message = row.MessageText;
                if (row.Role.Equals("player", StringComparison.OrdinalIgnoreCase) &&
                    overlays.TryGetValue(row.Id, out var perceived))
                {
                    message = perceived;
                }

                sb.AppendLine(
                    row.Role.Equals("system", StringComparison.OrdinalIgnoreCase)
                        ? $"[SYSTEM] {message}"
                        : $"{row.Speaker}: {message}");
            }

            return sb.ToString().TrimEnd();
        }

        public static bool HasAnyOverlay(long sessionId, int observerNpcId)
        {
            EnsureSchema();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1
                FROM ConversationMessagePerception
                WHERE SessionId=$session AND ObserverNpcId=$npc
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$session", sessionId);
            cmd.Parameters.AddWithValue("$npc", observerNpcId);
            return cmd.ExecuteScalar() != null;
        }

        private static Dictionary<long, string> GetOverlays(long sessionId, int observerNpcId)
        {
            EnsureSchema();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT MessageId,PerceivedText
                FROM ConversationMessagePerception
                WHERE SessionId=$session AND ObserverNpcId=$npc;
                """;
            cmd.Parameters.AddWithValue("$session", sessionId);
            cmd.Parameters.AddWithValue("$npc", observerNpcId);

            var map = new Dictionary<long, string>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                map[r.GetInt64(0)] = r.GetString(1);
            return map;
        }

        private static void EnsureSchema()
        {
            var parent = System.IO.Path.GetDirectoryName(DbPath);
            if (!string.IsNullOrWhiteSpace(parent))
                System.IO.Directory.CreateDirectory(parent);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS ConversationMessagePerception(
                    MessageId INTEGER NOT NULL,
                    SessionId INTEGER NOT NULL,
                    ObserverNpcId INTEGER NOT NULL,
                    PerceivedText TEXT NOT NULL,
                    SourceEventKey TEXT NULL,
                    CreatedUtc TEXT NOT NULL,
                    PRIMARY KEY(MessageId,ObserverNpcId)
                );

                CREATE INDEX IF NOT EXISTS IX_ConversationMessagePerception_SessionNpc
                    ON ConversationMessagePerception(SessionId,ObserverNpcId,MessageId);
                """;
            cmd.ExecuteNonQuery();
        }
    }
}
