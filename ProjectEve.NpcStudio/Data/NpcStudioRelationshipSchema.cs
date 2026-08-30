using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

/// <summary>
/// Owns the fresh canonical relationship-database foundation required by NPC Studio.
///
/// Objective world/history events are NOT stored here.
/// This database owns current directed relationship state plus subjective
/// reasons, memories and knowledge.
/// </summary>
public static class NpcStudioRelationshipSchema
{
    public static void Ensure(NpcStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var path = string.IsNullOrWhiteSpace(options.RelationshipsDbPath)
            ? @"D:\ProjectEveData\Database\project_eve_relationships.db"
            : options.RelationshipsDbPath;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        EnsureRelationshipStates(conn);
        EnsureRelationshipReasons(conn);
        EnsurePersonalMemories(conn);
        EnsureKnowledgeItems(conn);
        EnsureNpcTraitReasons(conn);
        EnsureNpcTraitChangeHistory(conn);
    }

    private static void EnsureRelationshipStates(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS RelationshipStates
            (
                RelationshipId TEXT PRIMARY KEY,
                SourceCharacterId INTEGER NOT NULL,
                TargetCharacterId INTEGER NULL,
                TargetName TEXT NOT NULL DEFAULT '',
                RelationshipType TEXT NOT NULL DEFAULT 'acquaintance',

                -- FamilyRole is compatibility/display cache only.
                -- Canonical objective kinship comes from FamilyParentChildLinks
                -- and FamilyUnionLinks and is resolved from the current NPC viewpoint.
                FamilyRole TEXT NOT NULL DEFAULT '',

                Love INTEGER NOT NULL DEFAULT 50,
                Trust INTEGER NOT NULL DEFAULT 50,
                Respect INTEGER NOT NULL DEFAULT 50,
                Loyalty INTEGER NOT NULL DEFAULT 50,

                Anger INTEGER NOT NULL DEFAULT 0,
                Resentment INTEGER NOT NULL DEFAULT 0,
                Fear INTEGER NOT NULL DEFAULT 0,
                Jealousy INTEGER NOT NULL DEFAULT 0,
                Attraction INTEGER NOT NULL DEFAULT 0,
                Tension INTEGER NOT NULL DEFAULT 0,

                Importance INTEGER NOT NULL DEFAULT 50,
                RelationshipTier INTEGER NOT NULL DEFAULT 0,

                KnownSinceYear INTEGER NULL,
                RelationshipStartedYear INTEGER NULL,
                RelationshipEndedYear INTEGER NULL,
                RelationshipLifeStage TEXT NOT NULL DEFAULT '',
                IsEstranged INTEGER NOT NULL DEFAULT 0,

                Notes TEXT NOT NULL DEFAULT '',
                UpdatedGameTime TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(conn, """
            CREATE INDEX IF NOT EXISTS IX_RelationshipStates_Source
            ON RelationshipStates(SourceCharacterId);

            CREATE INDEX IF NOT EXISTS IX_RelationshipStates_Target
            ON RelationshipStates(TargetCharacterId);

            CREATE INDEX IF NOT EXISTS IX_RelationshipStates_Source_Type
            ON RelationshipStates(SourceCharacterId, RelationshipType);

            CREATE INDEX IF NOT EXISTS IX_RelationshipStates_Source_Tier
            ON RelationshipStates(SourceCharacterId, RelationshipTier);
            """);
    }

    private static void EnsureRelationshipReasons(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS RelationshipReasons
            (
                Id TEXT PRIMARY KEY,
                RelationshipId TEXT NOT NULL,
                EventId TEXT NOT NULL DEFAULT '',
                ScoreName TEXT NOT NULL DEFAULT '',
                Delta INTEGER NOT NULL DEFAULT 0,
                Reason TEXT NOT NULL DEFAULT '',
                Interpretation TEXT NOT NULL DEFAULT '',
                IsStillActive INTEGER NOT NULL DEFAULT 1,
                Importance INTEGER NOT NULL DEFAULT 50,
                GameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_RelationshipReasons_Relationship
            ON RelationshipReasons(RelationshipId, IsStillActive);
            """);
    }

    private static void EnsurePersonalMemories(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS PersonalMemories
            (
                Id TEXT PRIMARY KEY,
                KnowerCharacterId INTEGER NOT NULL,
                SubjectCharacterId INTEGER NULL,
                EventId TEXT NOT NULL DEFAULT '',
                MemoryType TEXT NOT NULL DEFAULT 'General',
                MemoryText TEXT NOT NULL DEFAULT '',
                Interpretation TEXT NOT NULL DEFAULT '',
                EmotionalMeaning TEXT NOT NULL DEFAULT '',
                Importance INTEGER NOT NULL DEFAULT 50,
                Strength INTEGER NOT NULL DEFAULT 50,
                Confidence INTEGER NOT NULL DEFAULT 70,
                IsLockedPeak INTEGER NOT NULL DEFAULT 0,
                LearnedGameTime TEXT NOT NULL DEFAULT '',
                LastUpdatedGameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_PersonalMemories_Knower
            ON PersonalMemories(KnowerCharacterId, SubjectCharacterId);

            CREATE INDEX IF NOT EXISTS IX_PersonalMemories_Event
            ON PersonalMemories(EventId);
            """);
    }

    private static void EnsureKnowledgeItems(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS KnowledgeItems
            (
                Id TEXT PRIMARY KEY,
                KnowerCharacterId INTEGER NOT NULL,
                SubjectCharacterId INTEGER NULL,
                EventId TEXT NOT NULL DEFAULT '',
                KnowledgeType TEXT NOT NULL DEFAULT '',
                WhatTheyKnow TEXT NOT NULL DEFAULT '',
                HowTheyLearnedIt TEXT NOT NULL DEFAULT '',
                SourceCharacterId INTEGER NULL,
                Confidence INTEGER NOT NULL DEFAULT 50,
                IsRumor INTEGER NOT NULL DEFAULT 0,
                IsSecret INTEGER NOT NULL DEFAULT 0,
                IsFalseBelief INTEGER NOT NULL DEFAULT 0,
                LearnedGameTime TEXT NOT NULL DEFAULT '',
                LastUpdatedGameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_KnowledgeItems_Knower
            ON KnowledgeItems(KnowerCharacterId, SubjectCharacterId);

            CREATE INDEX IF NOT EXISTS IX_KnowledgeItems_Event
            ON KnowledgeItems(EventId);
            """);
    }

    private static void EnsureNpcTraitReasons(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcTraitReasons
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,
                TraitId TEXT NOT NULL,
                TargetCharacterId INTEGER NOT NULL DEFAULT -1,

                ReasonType TEXT NOT NULL DEFAULT '',
                Reason TEXT NOT NULL DEFAULT '',
                Impact REAL NOT NULL DEFAULT 0,

                SourceType TEXT NOT NULL DEFAULT '',
                SourceEventId TEXT NOT NULL DEFAULT '',
                SourceMemoryId TEXT NOT NULL DEFAULT '',

                Confidence INTEGER NOT NULL DEFAULT 100,
                IsActive INTEGER NOT NULL DEFAULT 1,

                GameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_NpcTraitReasons_NpcTrait
            ON NpcTraitReasons(NpcId, TraitId, IsActive);

            CREATE INDEX IF NOT EXISTS IX_NpcTraitReasons_Target
            ON NpcTraitReasons(TargetCharacterId);

            CREATE INDEX IF NOT EXISTS IX_NpcTraitReasons_Event
            ON NpcTraitReasons(SourceEventId);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcTraitReasons_CausalKey
            ON NpcTraitReasons
            (
                NpcId,
                TraitId,
                TargetCharacterId,
                ReasonType,
                Reason,
                SourceType,
                SourceEventId,
                SourceMemoryId
            );
            """);
    }
    private static void EnsureNpcTraitChangeHistory(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcTraitChangeHistory
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,
                TraitId TEXT NOT NULL,
                TargetCharacterId INTEGER NOT NULL DEFAULT -1,

                BeforeValue REAL NOT NULL DEFAULT 50,
                AfterValue REAL NOT NULL DEFAULT 50,
                Delta REAL NOT NULL DEFAULT 0,

                ReasonType TEXT NOT NULL DEFAULT '',
                Reason TEXT NOT NULL DEFAULT '',

                SourceType TEXT NOT NULL DEFAULT '',
                SourceEventId TEXT NOT NULL DEFAULT '',
                SourceMemoryId TEXT NOT NULL DEFAULT '',

                GameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_NpcTraitChangeHistory_NpcTrait
            ON NpcTraitChangeHistory(NpcId, TraitId, GameTime, CreatedRealAt);

            CREATE INDEX IF NOT EXISTS IX_NpcTraitChangeHistory_Target
            ON NpcTraitChangeHistory(TargetCharacterId);

            CREATE INDEX IF NOT EXISTS IX_NpcTraitChangeHistory_Event
            ON NpcTraitChangeHistory(SourceEventId);
            """);
    }
    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}


