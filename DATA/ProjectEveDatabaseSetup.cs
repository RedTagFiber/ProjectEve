using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace ProjectEve.Data;

/// <summary>
/// Canonical Project Eve database layout.
///
/// 1) project_eve.db               = current NPC/world-person truth
/// 2) project_eve_history.db       = objective world/game history
/// 3) project_eve_relationships.db = subjective relationship, memory and knowledge truth
/// 4) project_eve_locations.db     = location/room/scene truth
///
/// Binary assets remain on disk. SQL stores their paths, metadata and meaning.
/// </summary>
public static class ProjectEveDatabaseSetup
{
    public static string ProjectEveDataRoot => @"D:\ProjectEveData";
    public static string DatabaseRoot => Path.Combine(ProjectEveDataRoot, "Database");
    public static string NpcRoot => Path.Combine(ProjectEveDataRoot, "NPC");
    public static string LocationRoot => Path.Combine(ProjectEveDataRoot, "Locations");

    public static string MainDatabasePath => Path.Combine(DatabaseRoot, "project_eve.db");
    public static string HistoryDatabasePath => Path.Combine(DatabaseRoot, "project_eve_history.db");
    public static string RelationshipDatabasePath => Path.Combine(DatabaseRoot, "project_eve_relationships.db");
    public static string LocationDatabasePath => Path.Combine(DatabaseRoot, "project_eve_locations.db");

    public static void EnsureAll()
    {
        EnsureFolders();
        EnsureMainDatabase();
        EnsureMainCompatibilityColumns();
        EnsureHistoryDatabase();
        EnsureRelationshipDatabase();
        ProjectEvePersonFoundationSchema.Ensure();
        EnsureLocationDatabase();

                EnsureFinanceSchema();
        EnsureFamilyFriendWebSchema();

Environment.SetEnvironmentVariable("EVE_DB_PATH", MainDatabasePath);
        Environment.SetEnvironmentVariable("EVE_HISTORY_DB_PATH", HistoryDatabasePath);
        Environment.SetEnvironmentVariable("EVE_RELATIONSHIP_DB_PATH", RelationshipDatabasePath);
        Environment.SetEnvironmentVariable("EVE_LOCATION_DB_PATH", LocationDatabasePath);
    
            EnsureNpcWorldActivitySchema();
            EnsureScenePhysicalContactSchema();}

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(ProjectEveDataRoot);
        Directory.CreateDirectory(DatabaseRoot);
        Directory.CreateDirectory(NpcRoot);
        Directory.CreateDirectory(LocationRoot);

        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Queue"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Temp"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Logs"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Trash"));
        Directory.CreateDirectory(Path.Combine(ProjectEveDataRoot, "System", "Backups"));
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
        Directory.CreateDirectory(Path.Combine(folder, "Pictures", "Clothing"));
        Directory.CreateDirectory(Path.Combine(folder, "Pictures", "Archive"));

        Directory.CreateDirectory(Path.Combine(folder, "Video"));
        Directory.CreateDirectory(Path.Combine(folder, "Video", "Profile"));
        Directory.CreateDirectory(Path.Combine(folder, "Video", "Mood"));
        Directory.CreateDirectory(Path.Combine(folder, "Video", "Archive"));

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

        // These are export/debug folders only. SQL remains canonical truth.
        Directory.CreateDirectory(Path.Combine(folder, "Exports"));
        Directory.CreateDirectory(Path.Combine(folder, "Notes"));
    }

    public static string GetNpcFolderPath(int npcId, string npcName)
        => Path.Combine(NpcRoot, GetNpcFolderName(npcId, npcName));

    public static string GetNpcFolderName(int npcId, string npcName)
    {
        var cleanName = Slug(npcName);
        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = "unknown";

        return $"npc_{npcId:D6}_{cleanName}";
    }

    public static string GetLocationFolderPath(string locationId, string locationName)
    {
        var cleanId = Slug(locationId);
        var cleanName = Slug(locationName);
        if (string.IsNullOrWhiteSpace(cleanId)) cleanId = "location";
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "unknown";
        return Path.Combine(LocationRoot, $"{cleanId}_{cleanName}");
    }

    private static string Slug(string value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();
        value = Regex.Replace(value, @"[^a-z0-9]+", "_");
        value = Regex.Replace(value, @"_+", "_");
        return value.Trim('_');
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");
        return connection;
    }

    private static void EnsureMainDatabase()
    {
        using var connection = Open(MainDatabasePath);

        Execute(connection, """
        CREATE TABLE IF NOT EXISTS Characters
        (
            Id INTEGER PRIMARY KEY,
            WorldId TEXT NOT NULL DEFAULT 'smalltown',
            NpcKey TEXT NOT NULL DEFAULT '',
            FolderName TEXT NOT NULL DEFAULT '',
            FolderPath TEXT NOT NULL DEFAULT '',
            Name TEXT NOT NULL DEFAULT '',
            Nickname TEXT NOT NULL DEFAULT '',
            Age INTEGER NOT NULL DEFAULT 0,
            BirthYear INTEGER NULL,
            BirthMonth INTEGER NULL,
            BirthDay INTEGER NULL,
            BirthHour INTEGER NULL,
            Zodiac TEXT NOT NULL DEFAULT '',
            Gender TEXT NOT NULL DEFAULT '',
            Occupation TEXT NOT NULL DEFAULT '',
            Employer TEXT NOT NULL DEFAULT '',
            CurrentLocationId TEXT NOT NULL DEFAULT '',
            HomeLocationId TEXT NOT NULL DEFAULT '',
            WorkLocationId TEXT NOT NULL DEFAULT '',
            Location TEXT NOT NULL DEFAULT '',
            Hometown TEXT NOT NULL DEFAULT '',
            Address TEXT NOT NULL DEFAULT '',
            Status TEXT NOT NULL DEFAULT 'Draft',
            Tier INTEGER NOT NULL DEFAULT 4,
            Goal TEXT NOT NULL DEFAULT '',
            Need TEXT NOT NULL DEFAULT '',
            Fear TEXT NOT NULL DEFAULT '',
            Want TEXT NOT NULL DEFAULT '',
            PersonalityContext TEXT NOT NULL DEFAULT '',
            BackstoryShort TEXT NOT NULL DEFAULT '',
            BackstoryLong TEXT NOT NULL DEFAULT '',
            PersonalitySummary TEXT NOT NULL DEFAULT '',
            SpeakingStyle TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS NpcPhysicalProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            HeightCm REAL NULL,
            WeightKg REAL NULL,
            BodyType TEXT NOT NULL DEFAULT '',
            HairColor TEXT NOT NULL DEFAULT '',
            HairLength TEXT NOT NULL DEFAULT '',
            HairStyle TEXT NOT NULL DEFAULT '',
            EyeColor TEXT NOT NULL DEFAULT '',
            EyeStyle TEXT NOT NULL DEFAULT '',
            SkinTone TEXT NOT NULL DEFAULT '',
            FaceShape TEXT NOT NULL DEFAULT '',
            FacialFeatures TEXT NOT NULL DEFAULT '',
            DistinctiveFeatures TEXT NOT NULL DEFAULT '',
            Glasses TEXT NOT NULL DEFAULT '',
            ScarNotes TEXT NOT NULL DEFAULT '',
            Tattoos TEXT NOT NULL DEFAULT '',
            Piercings TEXT NOT NULL DEFAULT '',
            DefaultClothingStyle TEXT NOT NULL DEFAULT '',
            DefaultExpression TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcCognitionProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            IqScore INTEGER NULL,
            IntelligenceBand TEXT NOT NULL DEFAULT '',
            EducationLevel TEXT NOT NULL DEFAULT '',
            LearningStyle TEXT NOT NULL DEFAULT '',
            ProblemSolvingStyle TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcArchetypes
        (
            NpcId INTEGER PRIMARY KEY,
            PrimaryType TEXT NOT NULL DEFAULT '',
            SecondaryType TEXT NOT NULL DEFAULT '',
            TertiaryType TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcPersonas
        (
            NpcId INTEGER PRIMARY KEY,
            Energy INTEGER NOT NULL DEFAULT 50,
            PublicPersona TEXT NOT NULL DEFAULT '',
            PrivatePersona TEXT NOT NULL DEFAULT '',
            HiddenBehavior TEXT NOT NULL DEFAULT '',
            ReputationSummary TEXT NOT NULL DEFAULT '',
            PersonalitySnapshot TEXT NOT NULL DEFAULT '',
            AiDossierSummary TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );


        CREATE TABLE IF NOT EXISTS NpcSocialBehavior
        (
            NpcId INTEGER PRIMARY KEY,
            BookPostScore INTEGER NOT NULL DEFAULT 50 CHECK (BookPostScore BETWEEN 0 AND 100),
            GramPostScore INTEGER NOT NULL DEFAULT 50 CHECK (GramPostScore BETWEEN 0 AND 100),
            CommentScore INTEGER NOT NULL DEFAULT 50 CHECK (CommentScore BETWEEN 0 AND 100),
            TrollScore INTEGER NOT NULL DEFAULT 50 CHECK (TrollScore BETWEEN 0 AND 100),
            LastBookPostGameTime TEXT NOT NULL DEFAULT '',
            LastGramPostGameTime TEXT NOT NULL DEFAULT '',
            LastCommentGameTime TEXT NOT NULL DEFAULT '',
            LastTrollActionGameTime TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

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
            SetPointValue INTEGER NOT NULL DEFAULT 50,
            CurrentValue INTEGER NOT NULL DEFAULT 50,
            ExpressionStyle TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );


        CREATE TABLE IF NOT EXISTS NpcSubSlowValues
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            ParentTraitId TEXT NOT NULL DEFAULT '',
            SubTraitId TEXT NOT NULL DEFAULT '',
            SubTraitName TEXT NOT NULL DEFAULT '',
            ValueType TEXT NOT NULL DEFAULT '',
            ValueText TEXT NOT NULL DEFAULT '',
            IsEnabled INTEGER NOT NULL DEFAULT 1,
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE,
            UNIQUE (NpcId, ParentTraitId, SubTraitId)
        );

        CREATE TABLE IF NOT EXISTS NpcTraitControl
        (
        NpcId INTEGER NOT NULL,
        TraitId TEXT NOT NULL,
        Control INTEGER NOT NULL DEFAULT 50,
        LastUpdatedRealAt TEXT NOT NULL DEFAULT '',
        PRIMARY KEY (NpcId, TraitId),
        FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );
        CREATE TABLE IF NOT EXISTS NpcEmotionTriggers
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            TriggerName TEXT NOT NULL DEFAULT '',
            TriggerCategory TEXT NOT NULL DEFAULT '',
            AppliesToCharacterId INTEGER NULL,
            AppliesToRelationshipType TEXT NOT NULL DEFAULT '',
            AngerImpact INTEGER NOT NULL DEFAULT 0,
            JoyImpact INTEGER NOT NULL DEFAULT 0,
            SadnessImpact INTEGER NOT NULL DEFAULT 0,
            HurtImpact INTEGER NOT NULL DEFAULT 0,
            FearImpact INTEGER NOT NULL DEFAULT 0,
            JealousyImpact INTEGER NOT NULL DEFAULT 0,
            AttractionImpact INTEGER NOT NULL DEFAULT 0,
            StressImpact INTEGER NOT NULL DEFAULT 0,
            Reason TEXT NOT NULL DEFAULT '',
            CalmedBy TEXT NOT NULL DEFAULT '',
            MadeWorseBy TEXT NOT NULL DEFAULT '',
            DecayMinutes INTEGER NULL,
            IsEnabled INTEGER NOT NULL DEFAULT 1,
            Notes TEXT NOT NULL DEFAULT '',
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcHabitsAndInterests
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            ItemType TEXT NOT NULL DEFAULT '',
            Name TEXT NOT NULL DEFAULT '',
            Strength INTEGER NOT NULL DEFAULT 50,
            IsPublic INTEGER NOT NULL DEFAULT 1,
            Notes TEXT NOT NULL DEFAULT '',
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcMediaAssets
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            MediaKind TEXT NOT NULL DEFAULT 'Image',
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
            DurationSeconds REAL NOT NULL DEFAULT 0,
            IsReference INTEGER NOT NULL DEFAULT 0,
            ForceWhiteBackground INTEGER NOT NULL DEFAULT 0,
            IsApproved INTEGER NOT NULL DEFAULT 0,
            IsCurrent INTEGER NOT NULL DEFAULT 0,
            Notes TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcVoiceProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            VoiceName TEXT NOT NULL DEFAULT '',
            ReferenceAudioPath TEXT NOT NULL DEFAULT '',
            AlternateReferencePathsJson TEXT NOT NULL DEFAULT '[]',
            Accent TEXT NOT NULL DEFAULT '',
            SpeakingStyle TEXT NOT NULL DEFAULT '',
            Warmth INTEGER NOT NULL DEFAULT 50,
            Energy INTEGER NOT NULL DEFAULT 50,
            Pace INTEGER NOT NULL DEFAULT 50,
            Pitch INTEGER NOT NULL DEFAULT 50,
            EmotionalRange INTEGER NOT NULL DEFAULT 50,
            LiveEngine TEXT NOT NULL DEFAULT 'Chatterbox Turbo',
            PhoneEngine TEXT NOT NULL DEFAULT 'Chatterbox Turbo',
            InPersonEngine TEXT NOT NULL DEFAULT 'Chatterbox Turbo',
            RecordedEngine TEXT NOT NULL DEFAULT 'Chatterbox',
            VoiceStatus TEXT NOT NULL DEFAULT 'Draft',
            CurrentPresetId TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

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

        CREATE TABLE IF NOT EXISTS NpcBuildStatus
        (
            NpcId INTEGER PRIMARY KEY,
            CompletionPercent INTEGER NOT NULL DEFAULT 0,
            IdentityStatus TEXT NOT NULL DEFAULT 'Missing',
            AppearanceStatus TEXT NOT NULL DEFAULT 'Missing',
            TraitsStatus TEXT NOT NULL DEFAULT 'Missing',
            HistoryStatus TEXT NOT NULL DEFAULT 'Missing',
            RelationshipStatus TEXT NOT NULL DEFAULT 'Missing',
            VoiceStatus TEXT NOT NULL DEFAULT 'Missing',
            MediaStatus TEXT NOT NULL DEFAULT 'Missing',
            LastVerifiedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS IX_Characters_Name ON Characters(Name);
        CREATE INDEX IF NOT EXISTS IX_Characters_Status ON Characters(Status);
        CREATE INDEX IF NOT EXISTS IX_NpcSocialBehavior_Scores
            ON NpcSocialBehavior(BookPostScore, GramPostScore, CommentScore, TrollScore);
        CREATE INDEX IF NOT EXISTS IX_NpcTraitValues_NpcId ON NpcTraitValues(NpcId);
        CREATE INDEX IF NOT EXISTS IX_NpcSubSlowValues_NpcParent
            ON NpcSubSlowValues(NpcId, ParentTraitId, IsEnabled);
        CREATE INDEX IF NOT EXISTS IX_NpcEmotionTriggers_NpcId ON NpcEmotionTriggers(NpcId);
        CREATE INDEX IF NOT EXISTS IX_NpcMediaAssets_NpcId ON NpcMediaAssets(NpcId);
        CREATE INDEX IF NOT EXISTS IX_NpcMediaAssets_Current ON NpcMediaAssets(NpcId, Purpose, IsCurrent);
        CREATE INDEX IF NOT EXISTS IX_NpcVoicePresets_NpcId ON NpcVoicePresets(NpcId);
        """);
    }


    /// <summary>
    /// Adds canonical columns that older Project Eve databases may not have.
    /// CREATE TABLE IF NOT EXISTS does not retrofit columns into an existing table.
    /// </summary>
    private static void EnsureMainCompatibilityColumns()
    {
        using var connection = Open(MainDatabasePath);

        bool addedSetPoint = AddColumnIfMissing(
            connection,
            "NpcTraitValues",
            "SetPointValue",
            "INTEGER NOT NULL DEFAULT 50");

        AddColumnIfMissing(
            connection,
            "NpcTraitValues",
            "ExpressionStyle",
            "TEXT NOT NULL DEFAULT ''");

        // Legacy SQLite tables may also predate this timestamp column.
        AddColumnIfMissing(
            connection,
            "NpcTraitValues",
            "UpdatedRealAt",
            "TEXT NOT NULL DEFAULT ''");

        // The new set-point starts from the original persisted starting value.
        // Only do this on the migration run that actually added the column.
        if (addedSetPoint)
        {
            Execute(connection, """
                UPDATE NpcTraitValues
                SET SetPointValue = StartingValue;
                """);
        }
    }

    private static bool AddColumnIfMissing(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string sqlDefinition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({tableName});";

            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                string existing = reader.IsDBNull(1)
                    ? ""
                    : reader.GetString(1);

                if (string.Equals(
                    existing,
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqlDefinition};";
        alter.ExecuteNonQuery();

        return true;
    }

    private static void EnsureHistoryDatabase()
    {
        using var connection = Open(HistoryDatabasePath);

        Execute(connection, """
        CREATE TABLE IF NOT EXISTS WorldEvents
        (
            EventId TEXT PRIMARY KEY,
            WorldId TEXT NOT NULL DEFAULT 'smalltown',
            EventType TEXT NOT NULL DEFAULT '',
            Title TEXT NOT NULL DEFAULT '',
            Summary TEXT NOT NULL DEFAULT '',
            Details TEXT NOT NULL DEFAULT '',
            LocationId TEXT NOT NULL DEFAULT '',
            PlaceText TEXT NOT NULL DEFAULT '',
            Channel TEXT NOT NULL DEFAULT '',
            Status TEXT NOT NULL DEFAULT 'Closed',
            GameTime TEXT NOT NULL DEFAULT '',
            GameTimeEnd TEXT NOT NULL DEFAULT '',
            RealTime TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            RealTimeEnd TEXT NOT NULL DEFAULT '',
            Source TEXT NOT NULL DEFAULT '',
            Confidence INTEGER NOT NULL DEFAULT 100,
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS EventParticipants
        (
            EventId TEXT NOT NULL,
            CharacterId INTEGER NOT NULL,
            Role TEXT NOT NULL DEFAULT 'Present',
            PRIMARY KEY (EventId, CharacterId),
            FOREIGN KEY (EventId) REFERENCES WorldEvents(EventId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS EventFacts
        (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EventId TEXT NOT NULL,
            FactType TEXT NOT NULL DEFAULT 'Detail',
            FactText TEXT NOT NULL DEFAULT '',
            IsLocked INTEGER NOT NULL DEFAULT 0,
            FOREIGN KEY (EventId) REFERENCES WorldEvents(EventId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS ConversationTurns
        (
            Id TEXT PRIMARY KEY,
            EventId TEXT NOT NULL DEFAULT '',
            CharacterId INTEGER NULL,
            SpeakerType TEXT NOT NULL DEFAULT '',
            SpeakerName TEXT NOT NULL DEFAULT '',
            Channel TEXT NOT NULL DEFAULT '',
            Text TEXT NOT NULL DEFAULT '',
            ActionLine TEXT NOT NULL DEFAULT '',
            Emotion TEXT NOT NULL DEFAULT '',
            GameTime TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS Communications
        (
            Id TEXT PRIMARY KEY,
            EventId TEXT NOT NULL DEFAULT '',
            CommunicationType TEXT NOT NULL DEFAULT '',
            FromCharacterId INTEGER NULL,
            ToCharacterId INTEGER NULL,
            Text TEXT NOT NULL DEFAULT '',
            MediaPath TEXT NOT NULL DEFAULT '',
            GameTime TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS SceneActions
        (
            Id TEXT PRIMARY KEY,
            EventId TEXT NOT NULL DEFAULT '',
            CharacterId INTEGER NULL,
            ActionType TEXT NOT NULL DEFAULT '',
            ActionText TEXT NOT NULL DEFAULT '',
            LocationId TEXT NOT NULL DEFAULT '',
            GameTime TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS IX_WorldEvents_GameTime ON WorldEvents(GameTime);
        CREATE INDEX IF NOT EXISTS IX_WorldEvents_LocationId ON WorldEvents(LocationId);
        CREATE INDEX IF NOT EXISTS IX_EventParticipants_CharacterId ON EventParticipants(CharacterId);
        CREATE INDEX IF NOT EXISTS IX_ConversationTurns_EventId ON ConversationTurns(EventId);
        CREATE INDEX IF NOT EXISTS IX_Communications_EventId ON Communications(EventId);
        """);
    }

    private static void EnsureRelationshipDatabase()
    {
        using var connection = Open(RelationshipDatabasePath);

        Execute(connection, """
        CREATE TABLE IF NOT EXISTS RelationshipStates
        (
            RelationshipId TEXT PRIMARY KEY,
            SourceCharacterId INTEGER NOT NULL,
            TargetCharacterId INTEGER NULL,
            TargetName TEXT NOT NULL DEFAULT '',
            RelationshipType TEXT NOT NULL DEFAULT '',
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
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedGameTime TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

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
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (RelationshipId) REFERENCES RelationshipStates(RelationshipId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS PersonalMemories
        (
            Id TEXT PRIMARY KEY,
            KnowerCharacterId INTEGER NOT NULL,
            SubjectCharacterId INTEGER NULL,
            EventId TEXT NOT NULL DEFAULT '',
            MemoryType TEXT NOT NULL DEFAULT '',
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


        CREATE TABLE IF NOT EXISTS NpcTraitReasons
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            TraitId TEXT NOT NULL DEFAULT '',
            TargetCharacterId INTEGER NOT NULL DEFAULT -1,
            ReasonType TEXT NOT NULL DEFAULT '',
            Reason TEXT NOT NULL DEFAULT '',
            Impact REAL NOT NULL DEFAULT 0,
            SourceType TEXT NOT NULL DEFAULT '',
            SourceEventId TEXT NOT NULL DEFAULT '',
            SourceMemoryId TEXT NOT NULL DEFAULT '',
            Confidence INTEGER NOT NULL DEFAULT 100 CHECK (Confidence BETWEEN 0 AND 100),
            IsActive INTEGER NOT NULL DEFAULT 1,
            GameTime TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UNIQUE
            (
                NpcId,
                TraitId,
                TargetCharacterId,
                ReasonType,
                Reason,
                SourceType,
                SourceEventId,
                SourceMemoryId
            )
        );

        CREATE TABLE IF NOT EXISTS NpcTraitChangeHistory
        (
            Id TEXT PRIMARY KEY,
            NpcId INTEGER NOT NULL,
            TraitId TEXT NOT NULL DEFAULT '',
            TargetCharacterId INTEGER NOT NULL DEFAULT -1,
            BeforeValue REAL NOT NULL DEFAULT 0,
            AfterValue REAL NOT NULL DEFAULT 0,
            Delta REAL NOT NULL DEFAULT 0,
            ReasonType TEXT NOT NULL DEFAULT '',
            Reason TEXT NOT NULL DEFAULT '',
            SourceType TEXT NOT NULL DEFAULT '',
            SourceEventId TEXT NOT NULL DEFAULT '',
            SourceMemoryId TEXT NOT NULL DEFAULT '',
            GameTime TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS IX_NpcTraitReasons_NpcTrait
            ON NpcTraitReasons(NpcId, TraitId, IsActive, UpdatedRealAt);

        CREATE INDEX IF NOT EXISTS IX_NpcTraitChangeHistory_NpcTrait
            ON NpcTraitChangeHistory(NpcId, TraitId, GameTime);

        CREATE INDEX IF NOT EXISTS IX_RelationshipStates_Source ON RelationshipStates(SourceCharacterId);
        CREATE INDEX IF NOT EXISTS IX_RelationshipStates_Target ON RelationshipStates(TargetCharacterId);
        CREATE INDEX IF NOT EXISTS IX_RelationshipReasons_RelationshipId ON RelationshipReasons(RelationshipId);
        CREATE INDEX IF NOT EXISTS IX_PersonalMemories_Knower ON PersonalMemories(KnowerCharacterId);
        CREATE INDEX IF NOT EXISTS IX_KnowledgeItems_Knower ON KnowledgeItems(KnowerCharacterId);
        """);
    }

    private static void EnsureLocationDatabase()
    {
        using var connection = Open(LocationDatabasePath);

        Execute(connection, """
        CREATE TABLE IF NOT EXISTS LocationTemplates
        (
            TemplateId TEXT PRIMARY KEY,
            WorldId TEXT NOT NULL DEFAULT 'smalltown',
            Name TEXT NOT NULL DEFAULT '',
            TemplateType TEXT NOT NULL DEFAULT '',
            Category TEXT NOT NULL DEFAULT '',
            Description TEXT NOT NULL DEFAULT '',
            DefaultVibe TEXT NOT NULL DEFAULT '',
            IsResidential INTEGER NOT NULL DEFAULT 0,
            IsBusiness INTEGER NOT NULL DEFAULT 0,
            Notes TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS Locations
        (
            LocationId TEXT PRIMARY KEY,
            WorldId TEXT NOT NULL DEFAULT 'smalltown',
            TemplateId TEXT NOT NULL DEFAULT '',
            ParentLocationId TEXT NOT NULL DEFAULT '',
            Name TEXT NOT NULL DEFAULT '',
            LocationType TEXT NOT NULL DEFAULT '',
            LocationClass TEXT NOT NULL DEFAULT '',
            Address TEXT NOT NULL DEFAULT '',
            Region TEXT NOT NULL DEFAULT '',
            IsPublic INTEGER NOT NULL DEFAULT 1,
            IsBusiness INTEGER NOT NULL DEFAULT 0,
            IsResidential INTEGER NOT NULL DEFAULT 0,
            Vibe TEXT NOT NULL DEFAULT '',
            Condition TEXT NOT NULL DEFAULT '',
            CrowdStyle TEXT NOT NULL DEFAULT '',
            StoryPurpose TEXT NOT NULL DEFAULT '',
            Description TEXT NOT NULL DEFAULT '',
            AssetRootPath TEXT NOT NULL DEFAULT '',
            Notes TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS LocationAreas
        (
            AreaId TEXT PRIMARY KEY,
            LocationId TEXT NOT NULL,
            Name TEXT NOT NULL DEFAULT '',
            AreaType TEXT NOT NULL DEFAULT '',
            EnvironmentType TEXT NOT NULL DEFAULT 'Indoor',
            SortOrder INTEGER NOT NULL DEFAULT 0,
            StoryNotes TEXT NOT NULL DEFAULT '',
            AccessRules TEXT NOT NULL DEFAULT '',
            IsPrimary INTEGER NOT NULL DEFAULT 0,
            Notes TEXT NOT NULL DEFAULT '',
            FOREIGN KEY (LocationId) REFERENCES Locations(LocationId) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS LocationVisualAssets
        (
            Id TEXT PRIMARY KEY,
            LocationId TEXT NOT NULL,
            AreaId TEXT NOT NULL DEFAULT '',
            AssetType TEXT NOT NULL DEFAULT '',
            FilePath TEXT NOT NULL DEFAULT '',
            Prompt TEXT NOT NULL DEFAULT '',
            NegativePrompt TEXT NOT NULL DEFAULT '',
            ModelName TEXT NOT NULL DEFAULT '',
            WorkflowName TEXT NOT NULL DEFAULT '',
            Seed INTEGER NOT NULL DEFAULT 0,
            Season TEXT NOT NULL DEFAULT 'Any',
            Weather TEXT NOT NULL DEFAULT 'Clear',
            TimeOfDay TEXT NOT NULL DEFAULT 'Day',
            LightingState TEXT NOT NULL DEFAULT '',
            MoodState TEXT NOT NULL DEFAULT 'Normal',
            PanEnabled INTEGER NOT NULL DEFAULT 0,
            PanRange REAL NOT NULL DEFAULT 0,
            PanSeconds REAL NOT NULL DEFAULT 90,
            IsApproved INTEGER NOT NULL DEFAULT 0,
            IsCurrent INTEGER NOT NULL DEFAULT 0,
            Notes TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS LocationAudioAssets
        (
            Id TEXT PRIMARY KEY,
            LocationId TEXT NOT NULL,
            AreaId TEXT NOT NULL DEFAULT '',
            AudioType TEXT NOT NULL DEFAULT '',
            FilePath TEXT NOT NULL DEFAULT '',
            Volume REAL NOT NULL DEFAULT 1.0,
            MinDelaySeconds REAL NOT NULL DEFAULT 0,
            MaxDelaySeconds REAL NOT NULL DEFAULT 0,
            Season TEXT NOT NULL DEFAULT 'Any',
            Weather TEXT NOT NULL DEFAULT 'Any',
            TimeOfDay TEXT NOT NULL DEFAULT 'Any',
            IsLoop INTEGER NOT NULL DEFAULT 0,
            IsEnabled INTEGER NOT NULL DEFAULT 1,
            Notes TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS LocationMotionRegions
        (
            Id TEXT PRIMARY KEY,
            LocationId TEXT NOT NULL,
            AreaId TEXT NOT NULL DEFAULT '',
            Name TEXT NOT NULL DEFAULT '',
            MotionType TEXT NOT NULL DEFAULT '',
            MaskPath TEXT NOT NULL DEFAULT '',
            RegionDataJson TEXT NOT NULL DEFAULT '{}',
            Speed REAL NOT NULL DEFAULT 1.0,
            Intensity REAL NOT NULL DEFAULT 1.0,
            Direction TEXT NOT NULL DEFAULT '',
            WeatherLinked INTEGER NOT NULL DEFAULT 0,
            TimeLinked INTEGER NOT NULL DEFAULT 0,
            IsEnabled INTEGER NOT NULL DEFAULT 1,
            Notes TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS LocationSceneStates
        (
            Id TEXT PRIMARY KEY,
            LocationId TEXT NOT NULL,
            AreaId TEXT NOT NULL DEFAULT '',
            StateName TEXT NOT NULL DEFAULT '',
            TimeOfDay TEXT NOT NULL DEFAULT '',
            LightingState TEXT NOT NULL DEFAULT '',
            MoodState TEXT NOT NULL DEFAULT '',
            Weather TEXT NOT NULL DEFAULT '',
            InteriorBrightness REAL NOT NULL DEFAULT 1.0,
            WindowBrightness REAL NOT NULL DEFAULT 1.0,
            PracticalLightsJson TEXT NOT NULL DEFAULT '{}',
            AudioOverridesJson TEXT NOT NULL DEFAULT '{}',
            MotionOverridesJson TEXT NOT NULL DEFAULT '{}',
            Notes TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS LocationNpcLinks
        (
            Id TEXT PRIMARY KEY,
            LocationId TEXT NOT NULL,
            CharacterId INTEGER NOT NULL,
            LinkType TEXT NOT NULL DEFAULT '',
            IsPrimary INTEGER NOT NULL DEFAULT 0,
            Notes TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS LocationVisits
        (
            Id TEXT PRIMARY KEY,
            LocationId TEXT NOT NULL,
            CharacterId INTEGER NOT NULL,
            FirstVisitGameTime TEXT NOT NULL DEFAULT '',
            LastVisitGameTime TEXT NOT NULL DEFAULT '',
            VisitCount INTEGER NOT NULL DEFAULT 0,
            Notes TEXT NOT NULL DEFAULT ''
        );

        CREATE INDEX IF NOT EXISTS IX_Locations_Type ON Locations(LocationType);
        CREATE INDEX IF NOT EXISTS IX_LocationAreas_LocationId ON LocationAreas(LocationId);
        CREATE INDEX IF NOT EXISTS IX_LocationVisualAssets_LocationId ON LocationVisualAssets(LocationId);
        CREATE INDEX IF NOT EXISTS IX_LocationAudioAssets_LocationId ON LocationAudioAssets(LocationId);
        CREATE INDEX IF NOT EXISTS IX_LocationNpcLinks_CharacterId ON LocationNpcLinks(CharacterId);
        """);
    }

    private static void EnsureFinanceSchema()
    {
        using (var main = Open(MainDatabasePath))
        {
            Execute(main, """
                CREATE TABLE IF NOT EXISTS FinancialAccounts
                (
                    Id TEXT PRIMARY KEY,
                    OwnerType TEXT NOT NULL DEFAULT 'NPC',
                    OwnerId INTEGER NOT NULL,
                    AccountType TEXT NOT NULL DEFAULT '',
                    InstitutionName TEXT NOT NULL DEFAULT '',
                    AccountName TEXT NOT NULL DEFAULT '',
                    Currency TEXT NOT NULL DEFAULT 'USD',
                    Status TEXT NOT NULL DEFAULT 'Open',
                    CreditLimit REAL NOT NULL DEFAULT 0,
                    InterestRate REAL NOT NULL DEFAULT 0,
                    OpenedGameTime TEXT NOT NULL DEFAULT '',
                    CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS IX_FinancialAccounts_Owner
                    ON FinancialAccounts(OwnerType, OwnerId);

                CREATE TABLE IF NOT EXISTS FinancialObligations
                (
                    Id TEXT PRIMARY KEY,
                    OwnerNpcId INTEGER NOT NULL,
                    AccountId TEXT NOT NULL DEFAULT '',
                    PayeeName TEXT NOT NULL DEFAULT '',
                    ObligationType TEXT NOT NULL DEFAULT '',
                    Amount REAL NOT NULL DEFAULT 0,
                    Frequency TEXT NOT NULL DEFAULT '',
                    DueDay INTEGER NULL,
                    AutoPay INTEGER NOT NULL DEFAULT 0,
                    Status TEXT NOT NULL DEFAULT 'Active',
                    NextDueGameTime TEXT NOT NULL DEFAULT '',
                    CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS IX_FinancialObligations_Owner
                    ON FinancialObligations(OwnerNpcId);
                """);
        }

        using (var history = Open(HistoryDatabasePath))
        {
            Execute(history, """
                CREATE TABLE IF NOT EXISTS FinancialTransactions
                (
                    Id TEXT PRIMARY KEY,
                    AccountId TEXT NOT NULL,
                    OwnerType TEXT NOT NULL DEFAULT 'NPC',
                    OwnerId INTEGER NOT NULL,
                    TransferGroupId TEXT NOT NULL DEFAULT '',
                    TransactionType TEXT NOT NULL DEFAULT '',
                    Amount REAL NOT NULL DEFAULT 0,
                    CounterpartyAccountId TEXT NOT NULL DEFAULT '',
                    MerchantId INTEGER NULL,
                    LocationId INTEGER NULL,
                    Category TEXT NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    GameTime TEXT NOT NULL DEFAULT '',
                    RelatedEventId TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Posted',
                    CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS IX_FinancialTransactions_Account
                    ON FinancialTransactions(AccountId, CreatedRealAt);

                CREATE INDEX IF NOT EXISTS IX_FinancialTransactions_Owner
                    ON FinancialTransactions(OwnerType, OwnerId, CreatedRealAt);

                CREATE INDEX IF NOT EXISTS IX_FinancialTransactions_Transfer
                    ON FinancialTransactions(TransferGroupId);
                """);
        }
    }

    private static void EnsureFamilyFriendWebSchema()
    {
        using var conn = Open(RelationshipDatabasePath);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS FamilyFriendWeb
            (
                OwnerNpcId INTEGER NOT NULL,
                TargetNpcId INTEGER NOT NULL,
                WebTier INTEGER NOT NULL,
                RelationshipType TEXT NOT NULL DEFAULT '',
                IsHistoryOnly INTEGER NOT NULL DEFAULT 0,
                Source TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (OwnerNpcId, TargetNpcId)
            );

            CREATE INDEX IF NOT EXISTS IX_FamilyFriendWeb_OwnerTier
                ON FamilyFriendWeb(OwnerNpcId, WebTier);

            CREATE INDEX IF NOT EXISTS IX_FamilyFriendWeb_Target
                ON FamilyFriendWeb(TargetNpcId);

            CREATE TABLE IF NOT EXISTS HouseholdMembers
            (
                HouseholdId TEXT NOT NULL,
                NpcId INTEGER NOT NULL,
                HouseholdRole TEXT NOT NULL DEFAULT '',
                JoinedAt TEXT NOT NULL DEFAULT '',
                LeftAt TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (HouseholdId, NpcId)
            );

            CREATE INDEX IF NOT EXISTS IX_HouseholdMembers_Npc
                ON HouseholdMembers(NpcId);
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

        private static void EnsureNpcWorldActivitySchema()
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={MainDatabasePath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS NpcWorldActivity
                (
                    NpcId INTEGER PRIMARY KEY,
                    LocationId TEXT,
                    Activity TEXT,
                    ActivityStartGameTime TEXT,
                    LastWorldTickGameTime TEXT,
                    IsBusy INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE INDEX IF NOT EXISTS ix_world_activity_location
                    ON NpcWorldActivity(LocationId, Activity);
                """;

            cmd.ExecuteNonQuery();
        }

        private static void EnsureScenePhysicalContactSchema()
        {
            // ScenePhysicalContact remains in MAIN temporarily because the active
            // scene spatial/presence stack still shares MAIN scene state.
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={MainDatabasePath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS ScenePhysicalContact
                (
                    SceneId TEXT NOT NULL,
                    CharacterAKey TEXT NOT NULL,
                    CharacterBKey TEXT NOT NULL,
                    InitiatorCharacterKey TEXT NOT NULL,
                    ContactKind TEXT NOT NULL DEFAULT 'none',
                    State TEXT NOT NULL DEFAULT 'none',
                    ReactionState TEXT NOT NULL DEFAULT 'unknown',
                    StartedGameTime TEXT NOT NULL,
                    UpdatedGameTime TEXT NOT NULL,
                    UpdatedRealUtc TEXT NOT NULL,
                    PRIMARY KEY(SceneId,CharacterAKey,CharacterBKey)
                );

                CREATE INDEX IF NOT EXISTS IX_ScenePhysicalContact_Active
                    ON ScenePhysicalContact(SceneId,State,CharacterAKey,CharacterBKey);
                """;

            cmd.ExecuteNonQuery();
        }
}






