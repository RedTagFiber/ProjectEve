using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Foundation schema for objective education and professional truth.
///
/// Current/structural records live in MAIN.
/// Objective events that created or changed these records remain in HISTORY
/// and are linked through EventId columns.
/// </summary>
public static class ProjectEveEducationProfessionalSchema
{
    public static void Ensure()
    {
        EnsureEducationRecords();
        EnsureProfessionalProfiles();
        EnsureProfessionalQualifications();
        EnsureProfessionalCompetencies();
    }

    private static void EnsureEducationRecords()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcEducationRecords
            (
                EducationRecordId TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                EducationType TEXT NOT NULL DEFAULT '',
                InstitutionId TEXT NOT NULL DEFAULT '',
                InstitutionName TEXT NOT NULL DEFAULT '',

                ProgramName TEXT NOT NULL DEFAULT '',
                DegreeOrCredential TEXT NOT NULL DEFAULT '',
                FieldOfStudy TEXT NOT NULL DEFAULT '',

                StartGameTime TEXT NOT NULL DEFAULT '',
                EndGameTime TEXT NOT NULL DEFAULT '',

                StartAge INTEGER NULL,
                EndAge INTEGER NULL,

                Status TEXT NOT NULL DEFAULT 'Completed',

                Gpa REAL NULL,
                Honors TEXT NOT NULL DEFAULT '',

                StartEventId TEXT NOT NULL DEFAULT '',
                CompletionEventId TEXT NOT NULL DEFAULT '',
                WithdrawalEventId TEXT NOT NULL DEFAULT '',

                Notes TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (NpcId)
                    REFERENCES Characters(Id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_NpcEducationRecords_Npc
                ON NpcEducationRecords(NpcId, StartAge, EndAge);

            CREATE INDEX IF NOT EXISTS IX_NpcEducationRecords_Institution
                ON NpcEducationRecords(InstitutionId, InstitutionName);
            """);
    }

    private static void EnsureProfessionalProfiles()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcProfessionalProfiles
            (
                NpcId INTEGER PRIMARY KEY,

                PrimaryRoleId TEXT NOT NULL DEFAULT '',
                CareerField TEXT NOT NULL DEFAULT '',

                YearsExperience REAL NOT NULL DEFAULT 0,
                TrainingLevel TEXT NOT NULL DEFAULT '',
                LicenseStanding TEXT NOT NULL DEFAULT '',

                Burnout INTEGER NOT NULL DEFAULT 0
                    CHECK (Burnout BETWEEN 0 AND 100),

                Motivation INTEGER NOT NULL DEFAULT 50
                    CHECK (Motivation BETWEEN 0 AND 100),

                CurrentPerformance INTEGER NOT NULL DEFAULT 50
                    CHECK (CurrentPerformance BETWEEN 0 AND 100),

                ProfessionalReputation INTEGER NOT NULL DEFAULT 50
                    CHECK (ProfessionalReputation BETWEEN 0 AND 100),

                IsActive INTEGER NOT NULL DEFAULT 1,

                Notes TEXT NOT NULL DEFAULT '',

                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (NpcId)
                    REFERENCES Characters(Id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_NpcProfessionalProfiles_Role
                ON NpcProfessionalProfiles(PrimaryRoleId, IsActive);
            """);
    }

    private static void EnsureProfessionalQualifications()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcProfessionalQualifications
            (
                QualificationId TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,

                RoleId TEXT NOT NULL DEFAULT '',
                QualificationType TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL DEFAULT '',

                IssuerInstitutionId TEXT NOT NULL DEFAULT '',
                IssuerName TEXT NOT NULL DEFAULT '',

                CredentialNumber TEXT NOT NULL DEFAULT '',

                Status TEXT NOT NULL DEFAULT 'Active',

                IssuedGameTime TEXT NOT NULL DEFAULT '',
                ExpiresGameTime TEXT NOT NULL DEFAULT '',

                ObtainedEventId TEXT NOT NULL DEFAULT '',
                RenewedEventId TEXT NOT NULL DEFAULT '',
                SuspendedEventId TEXT NOT NULL DEFAULT '',
                RevokedEventId TEXT NOT NULL DEFAULT '',

                Notes TEXT NOT NULL DEFAULT '',

                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                FOREIGN KEY (NpcId)
                    REFERENCES Characters(Id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_NpcProfessionalQualifications_Npc
                ON NpcProfessionalQualifications(NpcId, Status);

            CREATE INDEX IF NOT EXISTS IX_NpcProfessionalQualifications_Role
                ON NpcProfessionalQualifications(RoleId, QualificationType);
            """);
    }

    private static void EnsureProfessionalCompetencies()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS NpcProfessionalCompetencies
            (
                NpcId INTEGER NOT NULL,
                RoleId TEXT NOT NULL DEFAULT '',
                CompetencyId TEXT NOT NULL,

                CompetencyName TEXT NOT NULL DEFAULT '',

                CurrentValue INTEGER NOT NULL DEFAULT 50
                    CHECK (CurrentValue BETWEEN 0 AND 100),

                SetPointValue INTEGER NOT NULL DEFAULT 50
                    CHECK (SetPointValue BETWEEN 0 AND 100),

                Confidence INTEGER NOT NULL DEFAULT 50
                    CHECK (Confidence BETWEEN 0 AND 100),

                ExperienceLevel TEXT NOT NULL DEFAULT '',

                SourceEducationRecordId TEXT NOT NULL DEFAULT '',
                SourceQualificationId TEXT NOT NULL DEFAULT '',
                LastChangeEventId TEXT NOT NULL DEFAULT '',

                Notes TEXT NOT NULL DEFAULT '',

                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,

                PRIMARY KEY (NpcId, RoleId, CompetencyId),

                FOREIGN KEY (NpcId)
                    REFERENCES Characters(Id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_NpcProfessionalCompetencies_Npc
                ON NpcProfessionalCompetencies(NpcId, RoleId);

            CREATE INDEX IF NOT EXISTS IX_NpcProfessionalCompetencies_Competency
                ON NpcProfessionalCompetencies(CompetencyId, CurrentValue);
            """);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;");

        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
