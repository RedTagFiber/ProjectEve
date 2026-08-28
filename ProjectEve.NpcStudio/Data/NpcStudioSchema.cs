using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed class NpcStudioSchema
{
    private readonly NpcStudioOptions _options;

    public NpcStudioSchema(NpcStudioOptions options)
    {
        _options = options;
    }

    public void Ensure()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.MainDbPath)!);

        using var conn = new SqliteConnection("Data Source=" + _options.MainDbPath);
        conn.Open();

        EnsureBaseTables(conn);
        EnsureStudioColumns(conn);
        EnsureStudioTables(conn);
    }

    private static void EnsureBaseTables(SqliteConnection conn)
    {
        // Characters should already exist from the seeder, but Studio can create a safe shell if needed.
        Execute(conn, """
        CREATE TABLE IF NOT EXISTS Characters
        (
            Id INTEGER PRIMARY KEY,
            NpcKey TEXT,
            FolderName TEXT,
            FolderPath TEXT,
            Name TEXT NOT NULL DEFAULT '',
            Nickname TEXT,
            DirtyName TEXT,
            DarkName TEXT,
            DisplayName TEXT,
            FirstName TEXT,
            LastName TEXT,
            Age INTEGER NOT NULL DEFAULT 0,
            Gender TEXT,
            Occupation TEXT,
            Location TEXT,
            Status TEXT,
            Goal TEXT,
            Need TEXT,
            Fear TEXT,
            Want TEXT,
            PersonalityContext TEXT,
            Hometown TEXT,
            Address TEXT,
            Tier INTEGER NOT NULL DEFAULT 5,
            UpdatedRealAt TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcAppearanceProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            Notes TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcVoiceProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            VoiceStatus TEXT,
            Notes TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcTraitValues
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            MainGroup TEXT,
            SubGroup TEXT,
            SubSubGroup TEXT,
            TraitId TEXT,
            TraitName TEXT,
            IsEnabled INTEGER NOT NULL DEFAULT 1,
            StartingValue INTEGER NOT NULL DEFAULT 50,
            CurrentValue INTEGER NOT NULL DEFAULT 50,
            Notes TEXT
        );
        """);

        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcBuildRevisions
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            RevisionType TEXT,
            Title TEXT,
            Details TEXT,
            OldValue TEXT,
            NewValue TEXT,
            CreatedRealAt TEXT
        );
        """);
    }

    private static void EnsureStudioColumns(SqliteConnection conn)
    {
        // Character sheet fields used by Studio.
        EnsureColumn(conn, "Characters", "NpcKey", "TEXT");
        EnsureColumn(conn, "Characters", "Nickname", "TEXT");
        EnsureColumn(conn, "Characters", "DirtyName", "TEXT");
        EnsureColumn(conn, "Characters", "DarkName", "TEXT");
        EnsureColumn(conn, "Characters", "DisplayName", "TEXT");
        EnsureColumn(conn, "Characters", "FirstName", "TEXT");
        EnsureColumn(conn, "Characters", "LastName", "TEXT");
        EnsureColumn(conn, "Characters", "FolderName", "TEXT");
        EnsureColumn(conn, "Characters", "FolderPath", "TEXT");
        EnsureColumn(conn, "Characters", "Hometown", "TEXT");
        EnsureColumn(conn, "Characters", "Address", "TEXT");
        EnsureColumn(conn, "Characters", "Goal", "TEXT");
        EnsureColumn(conn, "Characters", "Need", "TEXT");
        EnsureColumn(conn, "Characters", "Fear", "TEXT");
        EnsureColumn(conn, "Characters", "Want", "TEXT");
        EnsureColumn(conn, "Characters", "PersonalityContext", "TEXT");
        EnsureColumn(conn, "Characters", "UpdatedRealAt", "TEXT");
        EnsureColumn(conn, "Characters", "HeightCm", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(conn, "Characters", "WeightKg", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(conn, "Characters", "IQ", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "Characters", "Archetype1", "TEXT");
        EnsureColumn(conn, "Characters", "Archetype2", "TEXT");
        EnsureColumn(conn, "Characters", "Archetype3", "TEXT");
        EnsureColumn(conn, "Characters", "PublicPersona", "TEXT");
        EnsureColumn(conn, "Characters", "PrivatePersona", "TEXT");
        EnsureColumn(conn, "Characters", "HiddenBehavior", "TEXT");
        EnsureColumn(conn, "Characters", "AiSummary", "TEXT");
        EnsureColumn(conn, "Characters", "StatusNotes", "TEXT");
        // Relationships are canonical in project_eve_relationships.db.
        // NPC Studio no longer creates or upgrades legacy MAIN relationship tables.

        // Appearance / Comfy prep.
        EnsureColumn(conn, "NpcAppearanceProfiles", "AppearanceStatus", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "BodyType", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "HeightText", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "HairColor", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "HairStyle", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "EyeColor", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "SkinTone", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "ClothingStyle", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "WorkClothes", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "CasualClothes", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "DistinguishingFeatures", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "ImagePrompt", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "NegativePrompt", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "ReferenceImagePath", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "ProfileImagePath", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "ContactImagePath", "TEXT");
        EnsureColumn(conn, "NpcAppearanceProfiles", "Approved", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "NpcAppearanceProfiles", "UpdatedRealAt", "TEXT");

        // Voice prep.
        EnsureColumn(conn, "NpcVoiceProfiles", "VoiceProvider", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "VoiceId", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "VoiceName", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "VoiceStyle", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "Accent", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "AgeTone", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "Energy", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "Warmth", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "Roughness", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "Pace", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "Pitch", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "ReferenceAudioPath", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "SampleText", "TEXT");
        EnsureColumn(conn, "NpcVoiceProfiles", "Approved", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "NpcVoiceProfiles", "UpdatedRealAt", "TEXT");
    }

    private static void EnsureStudioTables(SqliteConnection conn)
    {
        // Prompt history from Ollama.
        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcPromptGenerations
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            PromptType TEXT,
            SourceModel TEXT,
            InputJson TEXT,
            OutputText TEXT,
            PositivePrompt TEXT,
            NegativePrompt TEXT,
            Approved INTEGER NOT NULL DEFAULT 0,
            UsedForGeneration INTEGER NOT NULL DEFAULT 0,
            Notes TEXT,
            CreatedRealAt TEXT
        );
        """);

        // Comfy image generation history.
        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcImageGenerations
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            ImageType TEXT,
            PromptGenerationId TEXT,
            PositivePrompt TEXT,
            NegativePrompt TEXT,
            Seed TEXT,
            WorkflowName TEXT,
            Checkpoint TEXT,
            Width INTEGER NOT NULL DEFAULT 1024,
            Height INTEGER NOT NULL DEFAULT 1024,
            Steps INTEGER NOT NULL DEFAULT 30,
            Cfg REAL NOT NULL DEFAULT 7.0,
            Sampler TEXT,
            ImagePath TEXT,
            IsCurrent INTEGER NOT NULL DEFAULT 0,
            Approved INTEGER NOT NULL DEFAULT 0,
            Notes TEXT,
            CreatedRealAt TEXT
        );
        """);

        // Canonical authored life-history events. These are distinct from build
        // revisions: revisions track edits; history events describe the person.
        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcHistoryEvents
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            EventDate TEXT,
            AgeAtEvent INTEGER NOT NULL DEFAULT 0,
            EventType TEXT,
            Title TEXT,
            Details TEXT,
            Meaning TEXT,
            IsCanon INTEGER NOT NULL DEFAULT 1,
            CreatedRealAt TEXT
        );
        """);
        // Relationship reasons are canonical in project_eve_relationships.db.

        // Author-defined emotional triggers used by the Human Behavior Engine.
        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcEmotionTriggers
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            Emotion TEXT NOT NULL,
            TriggerText TEXT NOT NULL,
            Impact INTEGER NOT NULL DEFAULT 10,
            Reason TEXT,
            CalmedBy TEXT,
            MadeWorseBy TEXT,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedRealAt TEXT
        );
        """);

        // Creative ideas from the Prompt Engineer.
        Execute(conn, """
        CREATE TABLE IF NOT EXISTS NpcStudioIdeas
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            IdeaType TEXT,
            SourceModel TEXT,
            InputSummary TEXT,
            IdeaText TEXT,
            Approved INTEGER NOT NULL DEFAULT 0,
            Rejected INTEGER NOT NULL DEFAULT 0,
            AppliedToCharacter INTEGER NOT NULL DEFAULT 0,
            Notes TEXT,
            CreatedRealAt TEXT
        );
        """);
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection conn, string tableName, string columnName, string columnDefinition)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = check.ExecuteReader();

            while (reader.Read())
            {
                var existing = reader["name"]?.ToString() ?? "";
                if (existing.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
}
