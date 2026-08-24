using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace ProjectEve.Data;

public static class ProjectEveDatabaseSetup
{
    public static string ProjectEveDataRoot => @"D:\ProjectEveData";

    public static string DatabaseRoot => Path.Combine(ProjectEveDataRoot, "Database");

    public static string NpcRoot => Path.Combine(ProjectEveDataRoot, "NPC");

    public static string MainDatabasePath => Path.Combine(DatabaseRoot, "project_eve.db");

    public static string HistoryDatabasePath => Path.Combine(DatabaseRoot, "project_eve_history.db");

    public static void EnsureAll()
    {
        EnsureFolders();
        EnsureMainDatabase();
        EnsureHistoryDatabase();

        Environment.SetEnvironmentVariable("EVE_DB_PATH", MainDatabasePath);
        Environment.SetEnvironmentVariable("EVE_HISTORY_DB_PATH", HistoryDatabasePath);
    }

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(ProjectEveDataRoot);
        Directory.CreateDirectory(DatabaseRoot);
        Directory.CreateDirectory(NpcRoot);

        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Queue"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Temp"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Logs"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Trash"));
    }

    public static void EnsureNpcFolders(int npcId, string npcName)
    {
        var folder = GetNpcFolderPath(npcId, npcName);

        Directory.CreateDirectory(folder);

        Directory.CreateDirectory(Path.Combine(folder, "Pictures"));
        Directory.CreateDirectory(Path.Combine(folder, "Pictures", "Reference"));
        Directory.CreateDirectory(Path.Combine(folder, "Pictures", "Profile"));
        Directory.CreateDirectory(Path.Combine(folder, "Pictures", "Contact"));
        Directory.CreateDirectory(Path.Combine(folder, "Pictures", "Social"));
        Directory.CreateDirectory(Path.Combine(folder, "Pictures", "Archive"));

        Directory.CreateDirectory(Path.Combine(folder, "Voice"));
        Directory.CreateDirectory(Path.Combine(folder, "Voice", "Reference"));
        Directory.CreateDirectory(Path.Combine(folder, "Voice", "Tests"));
        Directory.CreateDirectory(Path.Combine(folder, "Voice", "Approved"));
        Directory.CreateDirectory(Path.Combine(folder, "Voice", "Presets"));
        Directory.CreateDirectory(Path.Combine(folder, "Voice", "Training"));
        Directory.CreateDirectory(Path.Combine(folder, "Voice", "Exports"));

        Directory.CreateDirectory(Path.Combine(folder, "VoiceMessages"));
        Directory.CreateDirectory(Path.Combine(folder, "VoiceMessages", "Inbox"));
        Directory.CreateDirectory(Path.Combine(folder, "VoiceMessages", "Sent"));
        Directory.CreateDirectory(Path.Combine(folder, "VoiceMessages", "Temp"));

        Directory.CreateDirectory(Path.Combine(folder, "Comfy"));
        Directory.CreateDirectory(Path.Combine(folder, "Comfy", "Prompts"));
        Directory.CreateDirectory(Path.Combine(folder, "Comfy", "Workflows"));
        Directory.CreateDirectory(Path.Combine(folder, "Comfy", "Output"));
        Directory.CreateDirectory(Path.Combine(folder, "Comfy", "Metadata"));

        Directory.CreateDirectory(Path.Combine(folder, "Story"));
        Directory.CreateDirectory(Path.Combine(folder, "Story", "Narrations"));
        Directory.CreateDirectory(Path.Combine(folder, "Story", "Chapters"));
        Directory.CreateDirectory(Path.Combine(folder, "Story", "Summaries"));

        Directory.CreateDirectory(Path.Combine(folder, "Traits"));
        Directory.CreateDirectory(Path.Combine(folder, "History"));
        Directory.CreateDirectory(Path.Combine(folder, "Notes"));
    }

    public static string GetNpcFolderPath(int npcId, string npcName)
    {
        return Path.Combine(NpcRoot, GetNpcFolderName(npcId, npcName));
    }

    public static string GetNpcFolderName(int npcId, string npcName)
    {
        var cleanName = Slug(npcName);

        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = "unknown";

        return $"npc_{npcId:D6}_{cleanName}";
    }

    private static string Slug(string value)
    {
        value = value.Trim().ToLowerInvariant();
        value = Regex.Replace(value, @"[^a-z0-9]+", "_");
        value = Regex.Replace(value, @"_+", "_");
        return value.Trim('_');
    }

    private static void EnsureMainDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={MainDatabasePath}");
        connection.Open();

        Execute(connection, """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS Characters
            (
                Id INTEGER PRIMARY KEY,

                NpcKey TEXT NOT NULL DEFAULT '',
                FolderName TEXT NOT NULL DEFAULT '',
                FolderPath TEXT NOT NULL DEFAULT '',

                Name TEXT NOT NULL DEFAULT '',
                Age INTEGER NOT NULL DEFAULT 0,
                Gender TEXT NOT NULL DEFAULT '',
                Occupation TEXT NOT NULL DEFAULT '',
                Location TEXT NOT NULL DEFAULT '',

                Status TEXT NOT NULL DEFAULT 'Draft',
                Tier INTEGER NOT NULL DEFAULT 4,

                Goal TEXT NOT NULL DEFAULT '',
                Need TEXT NOT NULL DEFAULT '',
                Fear TEXT NOT NULL DEFAULT '',
                Want TEXT NOT NULL DEFAULT '',
                PersonalityContext TEXT NOT NULL DEFAULT '',
                Hometown TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',

                BackstoryShort TEXT NOT NULL DEFAULT '',
                BackstoryLong TEXT NOT NULL DEFAULT '',
                PersonalitySummary TEXT NOT NULL DEFAULT '',
                SpeakingStyle TEXT NOT NULL DEFAULT '',

                CurrentReferenceImagePath TEXT NOT NULL DEFAULT '',
                CurrentProfileImagePath TEXT NOT NULL DEFAULT '',
                CurrentContactImagePath TEXT NOT NULL DEFAULT '',
                CurrentVoiceReferencePath TEXT NOT NULL DEFAULT '',
                CurrentVoicePresetId TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcAppearanceProfiles
            (
                NpcId INTEGER PRIMARY KEY,

                HairColor TEXT NOT NULL DEFAULT '',
                HairLength TEXT NOT NULL DEFAULT '',
                HairStyle TEXT NOT NULL DEFAULT '',
                EyeColor TEXT NOT NULL DEFAULT '',
                SkinTone TEXT NOT NULL DEFAULT '',
                FaceShape TEXT NOT NULL DEFAULT '',
                FacialFeatures TEXT NOT NULL DEFAULT '',
                BodyType TEXT NOT NULL DEFAULT '',
                Height TEXT NOT NULL DEFAULT '',
                WeightFrame TEXT NOT NULL DEFAULT '',
                DistinctiveFeatures TEXT NOT NULL DEFAULT '',
                DefaultClothingStyle TEXT NOT NULL DEFAULT '',
                DefaultExpression TEXT NOT NULL DEFAULT '',

                Notes TEXT NOT NULL DEFAULT '',

                FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcTraitValues
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                MainGroup TEXT NOT NULL DEFAULT '',
                SubGroup TEXT NOT NULL DEFAULT '',
                SubSubGroup TEXT NOT NULL DEFAULT '',

                TraitId TEXT NOT NULL DEFAULT '',
                TraitName TEXT NOT NULL DEFAULT '',

                IsEnabled INTEGER NOT NULL DEFAULT 1,
                StartingValue INTEGER NOT NULL DEFAULT 50,
                CurrentValue INTEGER NOT NULL DEFAULT 50,

                Notes TEXT NOT NULL DEFAULT '',

                FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcRelationships
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                TargetNpcId INTEGER NULL,
                TargetName TEXT NOT NULL DEFAULT '',

                RelationshipType TEXT NOT NULL DEFAULT '',
                Trust INTEGER NOT NULL DEFAULT 0,
                Respect INTEGER NOT NULL DEFAULT 0,
                Affection INTEGER NOT NULL DEFAULT 0,
                Attraction INTEGER NOT NULL DEFAULT 0,
                Tension INTEGER NOT NULL DEFAULT 0,

                Notes TEXT NOT NULL DEFAULT '',

                FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcMediaAssets
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                Purpose TEXT NOT NULL DEFAULT '',
                ImageType TEXT NOT NULL DEFAULT '',

                FilePath TEXT NOT NULL DEFAULT '',
                RelativePath TEXT NOT NULL DEFAULT '',

                Prompt TEXT NOT NULL DEFAULT '',
                NegativePrompt TEXT NOT NULL DEFAULT '',
                Seed INTEGER NOT NULL DEFAULT 0,

                ModelName TEXT NOT NULL DEFAULT '',
                WorkflowName TEXT NOT NULL DEFAULT '',
                Sampler TEXT NOT NULL DEFAULT '',
                Steps INTEGER NOT NULL DEFAULT 0,
                Cfg REAL NOT NULL DEFAULT 0,
                Width INTEGER NOT NULL DEFAULT 0,
                Height INTEGER NOT NULL DEFAULT 0,

                IsReference INTEGER NOT NULL DEFAULT 0,
                ForceWhiteBackground INTEGER NOT NULL DEFAULT 0,
                IsCurrent INTEGER NOT NULL DEFAULT 0,

                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcVoiceProfiles
            (
                NpcId INTEGER PRIMARY KEY,

                ReferenceAudioPath TEXT NOT NULL DEFAULT '',
                CurrentPresetId TEXT NOT NULL DEFAULT '',
                VoiceStatus TEXT NOT NULL DEFAULT 'Draft',
                Notes TEXT NOT NULL DEFAULT '',

                FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcVoicePresets
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                PresetName TEXT NOT NULL DEFAULT '',
                Emotion TEXT NOT NULL DEFAULT '',

                ReferenceAudioPath TEXT NOT NULL DEFAULT '',
                OutputSamplePath TEXT NOT NULL DEFAULT '',

                Exaggeration REAL NOT NULL DEFAULT 0.45,
                CfgWeight REAL NOT NULL DEFAULT 0.50,
                Temperature REAL NOT NULL DEFAULT 0.80,
                Speed REAL NOT NULL DEFAULT 1.00,
                Pitch REAL NOT NULL DEFAULT 0.00,

                IsApproved INTEGER NOT NULL DEFAULT 0,
                IsCurrent INTEGER NOT NULL DEFAULT 0,

                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcBuildRevisions
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                RevisionType TEXT NOT NULL DEFAULT '',
                Title TEXT NOT NULL DEFAULT '',
                Details TEXT NOT NULL DEFAULT '',

                OldValue TEXT NOT NULL DEFAULT '',
                NewValue TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
            );
            """);

        Execute(connection, """
            CREATE INDEX IF NOT EXISTS IX_Characters_Name ON Characters(Name);
            CREATE INDEX IF NOT EXISTS IX_Characters_Status ON Characters(Status);
            CREATE INDEX IF NOT EXISTS IX_NpcTraitValues_NpcId ON NpcTraitValues(NpcId);
            CREATE INDEX IF NOT EXISTS IX_NpcMediaAssets_NpcId ON NpcMediaAssets(NpcId);
            CREATE INDEX IF NOT EXISTS IX_NpcVoicePresets_NpcId ON NpcVoicePresets(NpcId);
            """);
    }

    private static void EnsureHistoryDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={HistoryDatabasePath}");
        connection.Open();

        Execute(connection, """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcGameplayEvents
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                EventType TEXT NOT NULL DEFAULT '',
                Title TEXT NOT NULL DEFAULT '',
                Details TEXT NOT NULL DEFAULT '',

                GameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcMemories
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                MemoryText TEXT NOT NULL DEFAULT '',
                MemoryType TEXT NOT NULL DEFAULT '',
                Importance INTEGER NOT NULL DEFAULT 1,

                GameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcConversationHistory
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                Speaker TEXT NOT NULL DEFAULT '',
                Text TEXT NOT NULL DEFAULT '',
                Emotion TEXT NOT NULL DEFAULT '',

                GameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcRelationshipHistory
            (
                Id TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                TargetNpcId INTEGER NULL,
                TargetName TEXT NOT NULL DEFAULT '',

                ChangeType TEXT NOT NULL DEFAULT '',
                OldValue TEXT NOT NULL DEFAULT '',
                NewValue TEXT NOT NULL DEFAULT '',
                Reason TEXT NOT NULL DEFAULT '',

                GameTime TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(connection, """
            CREATE INDEX IF NOT EXISTS IX_NpcGameplayEvents_NpcId ON NpcGameplayEvents(NpcId);
            CREATE INDEX IF NOT EXISTS IX_NpcMemories_NpcId ON NpcMemories(NpcId);
            CREATE INDEX IF NOT EXISTS IX_NpcConversationHistory_NpcId ON NpcConversationHistory(NpcId);
            CREATE INDEX IF NOT EXISTS IX_NpcRelationshipHistory_NpcId ON NpcRelationshipHistory(NpcId);
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}