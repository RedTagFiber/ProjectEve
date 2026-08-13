using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// GOSSIP ENGINE — TELEPHONE GAME
    ///
    /// Information is never omniscient.
    /// A person knows a rumor only if:
    ///  - they originated it, or
    ///  - another person actually transmitted it to them.
    ///
    /// Each retelling can:
    ///  - preserve detail
    ///  - omit detail
    ///  - exaggerate
    ///  - soften
    ///  - misremember
    ///  - add speaker bias
    ///
    /// The SAME rumor keeps one RumorId while versions branch through retellings.
    /// </summary>
    public static class GossipEngine
    {
        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS GossipRumor (
                    RumorId INTEGER PRIMARY KEY AUTOINCREMENT,
                    OriginNpcId INTEGER,
                    SubjectNpcId INTEGER,
                    CreatedGameTime TEXT NOT NULL,
                    OriginalText TEXT NOT NULL,
                    SecretLevel INTEGER NOT NULL DEFAULT 0,
                    IsActive INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS GossipKnowledge (
                    RumorId INTEGER NOT NULL,
                    NpcId INTEGER NOT NULL,
                    CurrentVersionText TEXT NOT NULL,
                    Confidence REAL NOT NULL,
                    RetellDepth INTEGER NOT NULL,
                    SourceNpcId INTEGER,
                    LearnedGameTime TEXT NOT NULL,
                    PRIMARY KEY (RumorId, NpcId)
                );

                CREATE TABLE IF NOT EXISTS GossipTransmission (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RumorId INTEGER NOT NULL,
                    SpeakerNpcId INTEGER NOT NULL,
                    ListenerNpcId INTEGER NOT NULL,
                    BeforeText TEXT NOT NULL,
                    AfterText TEXT NOT NULL,
                    ConfidenceBefore REAL NOT NULL,
                    ConfidenceAfter REAL NOT NULL,
                    DistortionType TEXT NOT NULL,
                    RetellDepth INTEGER NOT NULL,
                    LocationId TEXT,
                    GameTime TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_gossip_knowledge_npc
                    ON GossipKnowledge(NpcId, RumorId);

                CREATE INDEX IF NOT EXISTS ix_gossip_transmission_rumor
                    ON GossipTransmission(RumorId, Id);
                """;
            cmd.ExecuteNonQuery();
        }

        public static long CreateRumor(
            int originNpcId,
            int? subjectNpcId,
            string text,
            DateTime gameTime,
            int secretLevel = 0)
        {
            Initialize();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            long rumorId;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO GossipRumor
                    (OriginNpcId, SubjectNpcId, CreatedGameTime, OriginalText, SecretLevel, IsActive)
                    VALUES ($origin,$subject,$time,$text,$secret,1);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("$origin", originNpcId);
                cmd.Parameters.AddWithValue("$subject", (object?)subjectNpcId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$time", gameTime.ToString("o"));
                cmd.Parameters.AddWithValue("$text", text);
                cmd.Parameters.AddWithValue("$secret", Math.Clamp(secretLevel, 0, 100));
                rumorId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            UpsertKnowledge(
                rumorId,
                originNpcId,
                text,
                1.0,
                0,
                null,
                gameTime);

            return rumorId;
        }

        public static GossipTransmissionResult TryTransmit(
            long rumorId,
            SimCharacter speaker,
            SimCharacter listener,
            DateTime gameTime,
            string? locationId = null,
            Random? rng = null)
        {
            Initialize();
            rng ??= Random.Shared;

            var knowledge = GetKnowledge(rumorId, speaker.Id);
            if (knowledge == null)
                return GossipTransmissionResult.Fail("Speaker does not know this rumor.");

            if (speaker.Id == listener.Id)
                return GossipTransmissionResult.Fail("Speaker and listener are the same person.");

            var rumor = GetRumor(rumorId);
            if (rumor == null || !rumor.IsActive)
                return GossipTransmissionResult.Fail("Rumor is missing or inactive.");

            double retellChance = 0.28;

            var rel = speaker.Relationships?.FirstOrDefault(r => r.TargetId == listener.Id);
            if (rel != null)
            {
                if (rel.Trust >= 70 || rel.Affection >= 70)
                    retellChance += 0.15;

                if (rumor.SecretLevel >= 70)
                    retellChance *= 0.55;
            }

            // Personality nudges. Unknown traits remain neutral.
            float sociability = TryTrait(speaker, "sociability");
            float secrecy = TryTrait(speaker, "secrecy");
            float honesty = TryTrait(speaker, "honesty");
            float manip = TryTrait(speaker, "manipulativeness");

            retellChance += (sociability - 50) * 0.003;
            retellChance -= (secrecy - 50) * 0.003;

            retellChance = Math.Clamp(retellChance, 0.01, 0.95);

            if (rng.NextDouble() > retellChance)
                return GossipTransmissionResult.Fail("Speaker chose not to retell.");

            int nextDepth = knowledge.RetellDepth + 1;
            double nextConfidence = Math.Clamp(
                knowledge.Confidence - 0.06,
                0.05,
                1.0);

            string distortion = ChooseDistortion(
                honesty,
                manip,
                secrecy,
                nextDepth,
                rng);

            string after = ApplyDistortion(
                knowledge.CurrentVersionText,
                distortion,
                rng);

            if (distortion != "preserved")
                nextConfidence = Math.Max(0.05, nextConfidence - 0.04);

            UpsertKnowledge(
                rumorId,
                listener.Id,
                after,
                nextConfidence,
                nextDepth,
                speaker.Id,
                gameTime);

            SaveTransmission(
                rumorId,
                speaker.Id,
                listener.Id,
                knowledge.CurrentVersionText,
                after,
                knowledge.Confidence,
                nextConfidence,
                distortion,
                nextDepth,
                locationId ?? speaker.Location,
                gameTime);

            return new GossipTransmissionResult
            {
                Success = true,
                RumorId = rumorId,
                SpeakerNpcId = speaker.Id,
                ListenerNpcId = listener.Id,
                BeforeText = knowledge.CurrentVersionText,
                AfterText = after,
                DistortionType = distortion,
                Confidence = nextConfidence,
                RetellDepth = nextDepth
            };
        }

        public static List<GossipKnowledgeState> GetKnownRumors(int npcId)
        {
            Initialize();
            var list = new List<GossipKnowledgeState>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT RumorId, CurrentVersionText, Confidence, RetellDepth,
                       SourceNpcId, LearnedGameTime
                FROM GossipKnowledge
                WHERE NpcId=$npc
                ORDER BY LearnedGameTime DESC;
                """;
            cmd.Parameters.AddWithValue("$npc", npcId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new GossipKnowledgeState
                {
                    RumorId = r.GetInt64(0),
                    NpcId = npcId,
                    CurrentVersionText = r.GetString(1),
                    Confidence = r.GetDouble(2),
                    RetellDepth = r.GetInt32(3),
                    SourceNpcId = r.IsDBNull(4) ? null : r.GetInt32(4),
                    LearnedGameTime = DateTime.TryParse(r.GetString(5), out var dt)
                        ? dt : DateTime.MinValue
                });
            }

            return list;
        }

        public static List<GossipTransmissionTrace> GetTelephoneChain(long rumorId)
        {
            Initialize();
            var list = new List<GossipTransmissionTrace>();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT SpeakerNpcId, ListenerNpcId, BeforeText, AfterText,
                       ConfidenceBefore, ConfidenceAfter, DistortionType,
                       RetellDepth, LocationId, GameTime
                FROM GossipTransmission
                WHERE RumorId=$rumor
                ORDER BY Id;
                """;
            cmd.Parameters.AddWithValue("$rumor", rumorId);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new GossipTransmissionTrace
                {
                    SpeakerNpcId = r.GetInt32(0),
                    ListenerNpcId = r.GetInt32(1),
                    BeforeText = r.GetString(2),
                    AfterText = r.GetString(3),
                    ConfidenceBefore = r.GetDouble(4),
                    ConfidenceAfter = r.GetDouble(5),
                    DistortionType = r.GetString(6),
                    RetellDepth = r.GetInt32(7),
                    LocationId = r.IsDBNull(8) ? "" : r.GetString(8),
                    GameTime = DateTime.TryParse(r.GetString(9), out var dt)
                        ? dt : DateTime.MinValue
                });
            }

            return list;
        }

        private static string ChooseDistortion(
            float honesty,
            float manipulativeness,
            float secrecy,
            int depth,
            Random rng)
        {
            double detailLoss = 0.18 + Math.Min(0.20, depth * 0.015);
            double exaggerate = 0.10 + Math.Max(0, manipulativeness - 50) * 0.002;
            double soften = 0.07 + Math.Max(0, honesty - 50) * 0.001;
            double misremember = 0.06 + Math.Min(0.12, depth * 0.01);
            double bias = 0.10 + Math.Max(0, manipulativeness - 50) * 0.0015;

            double roll = rng.NextDouble();
            double c = 0;

            c += detailLoss;
            if (roll < c) return "detail_lost";

            c += exaggerate;
            if (roll < c) return "exaggerated";

            c += soften;
            if (roll < c) return "softened";

            c += misremember;
            if (roll < c) return "misremembered";

            c += bias;
            if (roll < c) return "speaker_bias";

            return "preserved";
        }

        private static string ApplyDistortion(string text, string distortion, Random rng)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            switch (distortion)
            {
                case "detail_lost":
                    {
                        string[] parts = text.Split(
                            new[] { ',', ';', '.', '—', '-' },
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        return parts.Length > 1
                            ? parts[0].Trim() + "."
                            : text;
                    }

                case "exaggerated":
                    return text.TrimEnd('.', '!', '?') + " — and it was apparently worse than people first said.";

                case "softened":
                    return "From what I heard, " + text.TrimEnd('.', '!', '?') + ", but maybe it wasn't quite that bad.";

                case "misremembered":
                    return "I might have this a little wrong, but " + LowerFirst(text);

                case "speaker_bias":
                    return "The way I heard it, " + LowerFirst(text);

                default:
                    return text;
            }
        }

        private static string LowerFirst(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (text.Length == 1) return text.ToLowerInvariant();
            return char.ToLowerInvariant(text[0]) + text[1..];
        }

        private static float TryTrait(SimCharacter npc, string trait)
        {
            try
            {
                float v = npc.Traits?.Get(trait) ?? 0;
                return Math.Abs(v) < 0.001f ? 50 : Math.Clamp(v, 0, 100);
            }
            catch
            {
                return 50;
            }
        }

        private static GossipKnowledgeState? GetKnowledge(long rumorId, int npcId)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT CurrentVersionText, Confidence, RetellDepth,
                       SourceNpcId, LearnedGameTime
                FROM GossipKnowledge
                WHERE RumorId=$rumor AND NpcId=$npc;
                """;
            cmd.Parameters.AddWithValue("$rumor", rumorId);
            cmd.Parameters.AddWithValue("$npc", npcId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new GossipKnowledgeState
            {
                RumorId = rumorId,
                NpcId = npcId,
                CurrentVersionText = r.GetString(0),
                Confidence = r.GetDouble(1),
                RetellDepth = r.GetInt32(2),
                SourceNpcId = r.IsDBNull(3) ? null : r.GetInt32(3),
                LearnedGameTime = DateTime.TryParse(r.GetString(4), out var dt)
                    ? dt : DateTime.MinValue
            };
        }

        private static GossipRumorState? GetRumor(long rumorId)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT OriginNpcId, SubjectNpcId, CreatedGameTime,
                       OriginalText, SecretLevel, IsActive
                FROM GossipRumor
                WHERE RumorId=$id;
                """;
            cmd.Parameters.AddWithValue("$id", rumorId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new GossipRumorState
            {
                RumorId = rumorId,
                OriginNpcId = r.IsDBNull(0) ? null : r.GetInt32(0),
                SubjectNpcId = r.IsDBNull(1) ? null : r.GetInt32(1),
                CreatedGameTime = DateTime.TryParse(r.GetString(2), out var dt)
                    ? dt : DateTime.MinValue,
                OriginalText = r.GetString(3),
                SecretLevel = r.GetInt32(4),
                IsActive = r.GetInt32(5) != 0
            };
        }

        private static void UpsertKnowledge(
            long rumorId,
            int npcId,
            string text,
            double confidence,
            int depth,
            int? sourceNpcId,
            DateTime gameTime)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO GossipKnowledge
                (RumorId,NpcId,CurrentVersionText,Confidence,RetellDepth,SourceNpcId,LearnedGameTime)
                VALUES ($r,$n,$t,$c,$d,$s,$g)
                ON CONFLICT(RumorId,NpcId) DO UPDATE SET
                    CurrentVersionText=$t,
                    Confidence=MAX(GossipKnowledge.Confidence,$c),
                    RetellDepth=MIN(GossipKnowledge.RetellDepth,$d),
                    SourceNpcId=$s,
                    LearnedGameTime=$g;
                """;
            cmd.Parameters.AddWithValue("$r", rumorId);
            cmd.Parameters.AddWithValue("$n", npcId);
            cmd.Parameters.AddWithValue("$t", text);
            cmd.Parameters.AddWithValue("$c", confidence);
            cmd.Parameters.AddWithValue("$d", depth);
            cmd.Parameters.AddWithValue("$s", (object?)sourceNpcId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$g", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        private static void SaveTransmission(
            long rumorId,
            int speaker,
            int listener,
            string before,
            string after,
            double confidenceBefore,
            double confidenceAfter,
            string distortion,
            int depth,
            string? locationId,
            DateTime gameTime)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO GossipTransmission
                (RumorId,SpeakerNpcId,ListenerNpcId,BeforeText,AfterText,
                 ConfidenceBefore,ConfidenceAfter,DistortionType,RetellDepth,LocationId,GameTime)
                VALUES ($r,$s,$l,$b,$a,$cb,$ca,$d,$depth,$loc,$g);
                """;
            cmd.Parameters.AddWithValue("$r", rumorId);
            cmd.Parameters.AddWithValue("$s", speaker);
            cmd.Parameters.AddWithValue("$l", listener);
            cmd.Parameters.AddWithValue("$b", before);
            cmd.Parameters.AddWithValue("$a", after);
            cmd.Parameters.AddWithValue("$cb", confidenceBefore);
            cmd.Parameters.AddWithValue("$ca", confidenceAfter);
            cmd.Parameters.AddWithValue("$d", distortion);
            cmd.Parameters.AddWithValue("$depth", depth);
            cmd.Parameters.AddWithValue("$loc", locationId ?? "");
            cmd.Parameters.AddWithValue("$g", gameTime.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public sealed class GossipRumorState
        {
            public long RumorId { get; set; }
            public int? OriginNpcId { get; set; }
            public int? SubjectNpcId { get; set; }
            public DateTime CreatedGameTime { get; set; }
            public string OriginalText { get; set; } = "";
            public int SecretLevel { get; set; }
            public bool IsActive { get; set; }
        }

        public sealed class GossipKnowledgeState
        {
            public long RumorId { get; set; }
            public int NpcId { get; set; }
            public string CurrentVersionText { get; set; } = "";
            public double Confidence { get; set; }
            public int RetellDepth { get; set; }
            public int? SourceNpcId { get; set; }
            public DateTime LearnedGameTime { get; set; }
        }

        public sealed class GossipTransmissionTrace
        {
            public int SpeakerNpcId { get; set; }
            public int ListenerNpcId { get; set; }
            public string BeforeText { get; set; } = "";
            public string AfterText { get; set; } = "";
            public double ConfidenceBefore { get; set; }
            public double ConfidenceAfter { get; set; }
            public string DistortionType { get; set; } = "";
            public int RetellDepth { get; set; }
            public string LocationId { get; set; } = "";
            public DateTime GameTime { get; set; }
        }

        public sealed class GossipTransmissionResult
        {
            public bool Success { get; set; }
            public string Reason { get; set; } = "";
            public long RumorId { get; set; }
            public int SpeakerNpcId { get; set; }
            public int ListenerNpcId { get; set; }
            public string BeforeText { get; set; } = "";
            public string AfterText { get; set; } = "";
            public string DistortionType { get; set; } = "";
            public double Confidence { get; set; }
            public int RetellDepth { get; set; }

            public static GossipTransmissionResult Fail(string reason)
                => new() { Success = false, Reason = reason };
        }
    }
}
