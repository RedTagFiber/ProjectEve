using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

public static class ProjectEveHistoryGenerationFoundationSchema
{
    public static void Ensure()
    {
        Directory.CreateDirectory(ProjectEveDatabaseSetup.DatabaseRoot);

        using var conn = new SqliteConnection($"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        PRAGMA foreign_keys=ON;

        CREATE TABLE IF NOT EXISTS SupportNpcScaffoldSlots
        (
            SlotId INTEGER PRIMARY KEY AUTOINCREMENT,
            SlotKey TEXT NOT NULL UNIQUE,
            Category TEXT NOT NULL DEFAULT '',
            RoleType TEXT NOT NULL DEFAULT '',
            InstitutionType TEXT NOT NULL DEFAULT '',
            InstitutionId TEXT NOT NULL DEFAULT '',
            SubjectOrSpecialty TEXT NOT NULL DEFAULT '',
            GradeMin INTEGER NULL,
            GradeMax INTEGER NULL,
            ActiveStartYear INTEGER NULL,
            ActiveEndYear INTEGER NULL,
            PreferredAgeMin INTEGER NULL,
            PreferredAgeMax INTEGER NULL,
            SexPreference TEXT NOT NULL DEFAULT '',
            LocationId TEXT NOT NULL DEFAULT '',
            ReuseWeight REAL NOT NULL DEFAULT 1.0,
            IsFamilySlot INTEGER NOT NULL DEFAULT 0,
            AssignedNpcId INTEGER NULL,
            Status TEXT NOT NULL DEFAULT 'Open',
            Notes TEXT NOT NULL DEFAULT '',
            CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (AssignedNpcId) REFERENCES Characters(Id) ON DELETE SET NULL
        );

        CREATE INDEX IF NOT EXISTS IX_SupportNpcScaffoldSlots_Category
            ON SupportNpcScaffoldSlots(Category, RoleType);
        CREATE INDEX IF NOT EXISTS IX_SupportNpcScaffoldSlots_Years
            ON SupportNpcScaffoldSlots(ActiveStartYear, ActiveEndYear);

        CREATE TABLE IF NOT EXISTS NpcLifeBuildProfiles
        (
            NpcId INTEGER PRIMARY KEY,
            DesiredDepthTier INTEGER NOT NULL DEFAULT 4,
            BuildMode TEXT NOT NULL DEFAULT 'Scaffold',
            CharacterDirection TEXT NOT NULL DEFAULT '',
            HistoryDepth TEXT NOT NULL DEFAULT '',
            FamilyStatus TEXT NOT NULL DEFAULT 'NotStarted',
            HistoryStatus TEXT NOT NULL DEFAULT 'NotStarted',
            SubjectiveStatus TEXT NOT NULL DEFAULT 'NotStarted',
            PresentLifeStatus TEXT NOT NULL DEFAULT 'NotStarted',
            PhotoStatus TEXT NOT NULL DEFAULT 'NotStarted',
            VoiceStatus TEXT NOT NULL DEFAULT 'NotStarted',
            OverallPercent REAL NOT NULL DEFAULT 0,
            LockedForCanon INTEGER NOT NULL DEFAULT 0,
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcFamilyBuildPlans
        (
            RootNpcId INTEGER PRIMARY KEY,
            CreateMother INTEGER NOT NULL DEFAULT 1,
            MotherSiblingCount INTEGER NOT NULL DEFAULT 0,
            CreateFather INTEGER NOT NULL DEFAULT 1,
            FatherSiblingCount INTEGER NOT NULL DEFAULT 0,
            BrotherCount INTEGER NOT NULL DEFAULT 0,
            SisterCount INTEGER NOT NULL DEFAULT 0,
            SiblingBirthPattern TEXT NOT NULL DEFAULT 'Auto',
            CreateMaternalGrandmother INTEGER NOT NULL DEFAULT 1,
            CreateMaternalGrandfather INTEGER NOT NULL DEFAULT 1,
            CreatePaternalGrandmother INTEGER NOT NULL DEFAULT 1,
            CreatePaternalGrandfather INTEGER NOT NULL DEFAULT 1,
            GenerateAuntsUncles INTEGER NOT NULL DEFAULT 1,
            GenerateCousins INTEGER NOT NULL DEFAULT 1,
            GenerateSpousesInLaws INTEGER NOT NULL DEFAULT 1,
            ReuseExistingTownNpcForSpouses INTEGER NOT NULL DEFAULT 1,
            ExtendedFamilyDepth TEXT NOT NULL DEFAULT 'Deep',
            GenerateSharedFamilyHistory INTEGER NOT NULL DEFAULT 1,
            GenerateIndividualMemories INTEGER NOT NULL DEFAULT 1,
            GenerateFullNpcProfiles INTEGER NOT NULL DEFAULT 1,
            Status TEXT NOT NULL DEFAULT 'Draft',
            Notes TEXT NOT NULL DEFAULT '',
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (RootNpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS NpcRelationshipImportance
        (
            NpcId INTEGER NOT NULL,
            RelatedNpcId INTEGER NOT NULL,
            RelationshipKind TEXT NOT NULL DEFAULT '',
            CurrentImportanceTier INTEGER NOT NULL DEFAULT 5,
            HistoricalImportanceTier INTEGER NOT NULL DEFAULT 5,
            SharedEventCount INTEGER NOT NULL DEFAULT 0,
            MajorSharedEventCount INTEGER NOT NULL DEFAULT 0,
            EmotionalImpact REAL NOT NULL DEFAULT 0,
            CausalImpact REAL NOT NULL DEFAULT 0,
            CurrentContact REAL NOT NULL DEFAULT 0,
            RelationshipSpanYears REAL NOT NULL DEFAULT 0,
            HouseholdOverlap REAL NOT NULL DEFAULT 0,
            Confidence REAL NOT NULL DEFAULT 0,
            Reason TEXT NOT NULL DEFAULT '',
            LastCalculatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (NpcId, RelatedNpcId),
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE,
            FOREIGN KEY (RelatedNpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS IX_NpcRelationshipImportance_Tier
            ON NpcRelationshipImportance(NpcId, CurrentImportanceTier);

        CREATE TABLE IF NOT EXISTS NpcHistoryBuildSessions
        (
            BuildSessionId TEXT PRIMARY KEY,
            RootNpcId INTEGER NOT NULL,
            Stage TEXT NOT NULL DEFAULT '',
            Status TEXT NOT NULL DEFAULT 'Draft',
            PromptText TEXT NOT NULL DEFAULT '',
            DraftJson TEXT NOT NULL DEFAULT '',
            ApprovedJson TEXT NOT NULL DEFAULT '',
            StartedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (RootNpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );
        """;
        cmd.ExecuteNonQuery();
    }
}
