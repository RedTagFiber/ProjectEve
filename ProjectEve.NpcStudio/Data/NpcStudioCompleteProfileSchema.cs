using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

/// <summary>
/// Canonical non-history profile foundation required by NPC Studio family creation.
///
/// This schema exists so a completely fresh ProjectEve database can be built by
/// NPC Studio without depending on old seed databases.
///
/// HISTORY / memories / canonical world events are deliberately NOT created here.
/// </summary>
public static class NpcStudioCompleteProfileSchema
{
    public static void Ensure(NpcStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Directory.CreateDirectory(Path.GetDirectoryName(options.MainDbPath)!);

        using var conn = new SqliteConnection($"Data Source={options.MainDbPath}");
        conn.Open();

        EnsureCharacterColumns(conn);
        EnsurePhysical(conn);
        EnsureTraitCompatibility(conn);
        EnsurePhone(conn);
        EnsureCanonicalPhoneCompatibility(conn);
        EnsureVehicles(conn);
        EnsureCanonicalVehicleCompatibility(conn);
        EnsureFinance(conn);
        EnsureEducationAndCareer(conn);
        EnsureFamilyBuildTracking(conn);
    }

    private static void EnsureCharacterColumns(SqliteConnection conn)
    {
        // Columns already used by current NPC Studio repository/builder code.
        EnsureColumn(conn, "Characters", "WorldId", "TEXT NOT NULL DEFAULT 'smalltown'");
        EnsureColumn(conn, "Characters", "Employer", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "CurrentLocationId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "HomeLocationId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "WorkLocationId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "BackstoryShort", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "PersonalitySummary", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "CreatedRealAt", "TEXT NOT NULL DEFAULT ''");

        // Current-life completeness flags.  These are NOT history.
        EnsureColumn(conn, "Characters", "LifeStage", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "IsDeceased", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsurePhysical(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcPhysicalProfiles
            (
                NpcId INTEGER PRIMARY KEY,
                HeightCm REAL NOT NULL DEFAULT 0,
                WeightKg REAL NOT NULL DEFAULT 0,
                BodyType TEXT NOT NULL DEFAULT '',
                HairColor TEXT NOT NULL DEFAULT '',
                HairStyle TEXT NOT NULL DEFAULT '',
                EyeColor TEXT NOT NULL DEFAULT '',
                SkinTone TEXT NOT NULL DEFAULT '',
                DefaultClothingStyle TEXT NOT NULL DEFAULT '',
                DistinguishingFeatures TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        // Compatibility with both names currently used by Studio code.
        EnsureColumn(conn, "NpcPhysicalProfiles", "DistinctiveFeatures", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "NpcPhysicalProfiles", "DistinguishingFeatures", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureTraitCompatibility(SqliteConnection conn)
    {
        if (!TableExists(conn, "NpcTraitValues"))
            return;

        // The runtime trait dossier reads these canonical columns directly.
        // Fresh NPC Studio databases must expose the same shape as the core trait system.
        EnsureColumn(conn, "NpcTraitValues", "SetPointValue", "REAL NOT NULL DEFAULT 50");
        EnsureColumn(conn, "NpcTraitValues", "ExpressionStyle", "TEXT NOT NULL DEFAULT ''");

        // Existing rows created by older Studio schema should inherit their
        // original starting value as the initial set-point.
        Execute(conn, """
            UPDATE NpcTraitValues
            SET SetPointValue =
                CASE
                    WHEN SetPointValue IS NULL OR SetPointValue = 50
                    THEN COALESCE(StartingValue, CurrentValue, 50)
                    ELSE SetPointValue
                END;
            """);
    }
    private static void EnsurePhone(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcPhones
            (
                PhoneId TEXT PRIMARY KEY,
                WorldId TEXT NOT NULL DEFAULT 'smalltown',
                NpcId INTEGER NOT NULL,
                PhoneNumber TEXT NOT NULL DEFAULT '',
                PhoneType TEXT NOT NULL DEFAULT 'Mobile',
                CarrierName TEXT NOT NULL DEFAULT '',
                DeviceMake TEXT NOT NULL DEFAULT '',
                DeviceModel TEXT NOT NULL DEFAULT '',
                DeviceLabel TEXT NOT NULL DEFAULT '',
                IsPrimary INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_NpcPhones_Npc
            ON NpcPhones(NpcId, IsPrimary, IsActive);
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcPhoneContacts
            (
                ContactId TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,
                ContactNpcId INTEGER NULL,
                DisplayName TEXT NOT NULL DEFAULT '',
                PhoneNumber TEXT NOT NULL DEFAULT '',
                RelationshipLabel TEXT NOT NULL DEFAULT '',
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                IsBlocked INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_NpcPhoneContacts_Npc
            ON NpcPhoneContacts(NpcId, IsFavorite, DisplayName);
            """);
    }

    private static void EnsureCanonicalPhoneCompatibility(SqliteConnection conn)
    {
        if (!TableExists(conn, "NpcPhones"))
            return;

        EnsureColumn(conn, "NpcPhones", "ActivatedGameTime", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "NpcPhones", "DeactivatedGameTime", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "NpcPhones", "ActivatedEventId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "NpcPhones", "DeactivatedEventId", "TEXT NOT NULL DEFAULT ''");

        Execute(conn, """
            CREATE INDEX IF NOT EXISTS IX_NpcPhones_Number
            ON NpcPhones(PhoneNumber);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcPhones_ActiveNumber
            ON NpcPhones(WorldId, PhoneNumber)
            WHERE IsActive = 1
              AND trim(PhoneNumber) <> '';

            CREATE UNIQUE INDEX IF NOT EXISTS UX_NpcPhones_OneActivePrimary
            ON NpcPhones(NpcId)
            WHERE IsActive = 1
              AND IsPrimary = 1;
            """);
    }
    private static void EnsureVehicles(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS Vehicles
            (
                VehicleId TEXT PRIMARY KEY,
                WorldId TEXT NOT NULL DEFAULT 'smalltown',
                RegisteredOwnerNpcId INTEGER NULL,
                PrimaryDriverNpcId INTEGER NULL,
                VehicleType TEXT NOT NULL DEFAULT 'Car',
                Make TEXT NOT NULL DEFAULT '',
                Model TEXT NOT NULL DEFAULT '',
                ModelYear INTEGER NULL,
                Color TEXT NOT NULL DEFAULT '',
                Vin TEXT NOT NULL DEFAULT '',
                PlateNumber TEXT NOT NULL DEFAULT '',
                PlateState TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Active',
                OdometerMiles REAL NULL,
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_Vehicles_Owner
            ON Vehicles(RegisteredOwnerNpcId);

            CREATE INDEX IF NOT EXISTS IX_Vehicles_Driver
            ON Vehicles(PrimaryDriverNpcId);
            """);
    }

    private static void EnsureCanonicalVehicleCompatibility(SqliteConnection conn)
    {
        if (!TableExists(conn, "Vehicles"))
            return;

        EnsureColumn(conn, "Vehicles", "CurrentLocationId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Vehicles", "AcquiredGameTime", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Vehicles", "DisposedGameTime", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Vehicles", "AcquisitionEventId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Vehicles", "DisposalEventId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Vehicles", "LastMajorEventId", "TEXT NOT NULL DEFAULT ''");

        Execute(conn, """
            CREATE INDEX IF NOT EXISTS IX_Vehicles_RegisteredOwner
            ON Vehicles(RegisteredOwnerNpcId, Status);

            CREATE INDEX IF NOT EXISTS IX_Vehicles_PrimaryDriver
            ON Vehicles(PrimaryDriverNpcId, Status);

            CREATE INDEX IF NOT EXISTS IX_Vehicles_CurrentLocation
            ON Vehicles(CurrentLocationId, Status);

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Vehicles_Vin
            ON Vehicles(Vin)
            WHERE trim(Vin) <> '';

            CREATE UNIQUE INDEX IF NOT EXISTS UX_Vehicles_ActivePlate
            ON Vehicles(PlateState, PlateNumber)
            WHERE Status = 'Active'
              AND trim(PlateNumber) <> '';
            """);
    }
    private static void EnsureFinance(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS FinancialAccounts
            (
                AccountId TEXT PRIMARY KEY,
                OwnerType TEXT NOT NULL DEFAULT 'NPC',
                OwnerId INTEGER NOT NULL,
                AccountType TEXT NOT NULL DEFAULT '',
                InstitutionName TEXT NOT NULL DEFAULT '',
                AccountName TEXT NOT NULL DEFAULT '',
                Balance REAL NOT NULL DEFAULT 0,
                CurrencyCode TEXT NOT NULL DEFAULT 'USD',
                IsPrimary INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Active',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_FinancialAccounts_Owner
            ON FinancialAccounts(OwnerType, OwnerId);
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS FinancialObligations
            (
                ObligationId TEXT PRIMARY KEY,
                OwnerNpcId INTEGER NOT NULL,
                ObligationType TEXT NOT NULL DEFAULT '',
                LenderName TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                OriginalAmount REAL NOT NULL DEFAULT 0,
                CurrentBalance REAL NOT NULL DEFAULT 0,
                MonthlyPayment REAL NOT NULL DEFAULT 0,
                InterestRate REAL NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Active',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_FinancialObligations_Owner
            ON FinancialObligations(OwnerNpcId, Status);
            """);
    }

    private static void EnsureEducationAndCareer(SqliteConnection conn)
    {
        // Exact canonical table/column names already used by
        // NpcStudioRepository.CanonicalProfessional.
        Execute(conn, """
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
                Status TEXT NOT NULL DEFAULT '',
                Gpa REAL NULL,
                Honors TEXT NOT NULL DEFAULT '',
                StartEventId TEXT NOT NULL DEFAULT '',
                CompletionEventId TEXT NOT NULL DEFAULT '',
                WithdrawalEventId TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_NpcEducationRecords_Npc
            ON NpcEducationRecords(NpcId);
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcProfessionalProfiles
            (
                NpcId INTEGER PRIMARY KEY,
                PrimaryRoleId TEXT NOT NULL DEFAULT '',
                CareerField TEXT NOT NULL DEFAULT '',
                YearsExperience INTEGER NOT NULL DEFAULT 0,
                TrainingLevel TEXT NOT NULL DEFAULT '',
                LicenseStanding TEXT NOT NULL DEFAULT '',
                Burnout INTEGER NOT NULL DEFAULT 0,
                Motivation INTEGER NOT NULL DEFAULT 50,
                CurrentPerformance INTEGER NOT NULL DEFAULT 50,
                ProfessionalReputation INTEGER NOT NULL DEFAULT 50,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Notes TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(conn, """
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
                Status TEXT NOT NULL DEFAULT '',
                IssuedGameTime TEXT NOT NULL DEFAULT '',
                ExpiresGameTime TEXT NOT NULL DEFAULT '',
                ObtainedEventId TEXT NOT NULL DEFAULT '',
                RenewedEventId TEXT NOT NULL DEFAULT '',
                SuspendedEventId TEXT NOT NULL DEFAULT '',
                RevokedEventId TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_NpcProfessionalQualifications_Npc
            ON NpcProfessionalQualifications(NpcId);
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcProfessionalCompetencies
            (
                NpcId INTEGER NOT NULL,
                RoleId TEXT NOT NULL,
                CompetencyId TEXT NOT NULL,
                CompetencyName TEXT NOT NULL DEFAULT '',
                CurrentValue INTEGER NOT NULL DEFAULT 50,
                SetPointValue INTEGER NOT NULL DEFAULT 50,
                Confidence INTEGER NOT NULL DEFAULT 50,
                ExperienceLevel TEXT NOT NULL DEFAULT '',
                SourceEducationRecordId TEXT NOT NULL DEFAULT '',
                SourceQualificationId TEXT NOT NULL DEFAULT '',
                LastChangeEventId TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (NpcId, RoleId, CompetencyId)
            );
            """);
    }

    private static void EnsureFamilyBuildTracking(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcFamilyBuildCompleteness
            (
                NpcId INTEGER PRIMARY KEY,
                IdentityStatus TEXT NOT NULL DEFAULT 'NotStarted',
                AppearanceStatus TEXT NOT NULL DEFAULT 'NotStarted',
                TraitsStatus TEXT NOT NULL DEFAULT 'NotStarted',
                CurrentLifeStatus TEXT NOT NULL DEFAULT 'NotStarted',
                EducationCareerStatus TEXT NOT NULL DEFAULT 'NotStarted',
                JobStatus TEXT NOT NULL DEFAULT 'NotStarted',
                FinanceStatus TEXT NOT NULL DEFAULT 'NotStarted',
                PhoneStatus TEXT NOT NULL DEFAULT 'NotStarted',
                VehicleStatus TEXT NOT NULL DEFAULT 'NotStarted',
                HomeStatus TEXT NOT NULL DEFAULT 'NotStarted',
                FamilyStructureStatus TEXT NOT NULL DEFAULT 'NotStarted',
                NonHistoryPercent INTEGER NOT NULL DEFAULT 0,
                HistoryStatus TEXT NOT NULL DEFAULT 'NOT_INCLUDED',
                Notes TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        if (!TableExists(conn, table))
            return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            if (string.Equals(
                    Convert.ToString(reader["name"]),
                    column,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void EnsureColumn(
        SqliteConnection conn,
        string table,
        string column,
        string definition)
    {
        if (ColumnExists(conn, table, column))
            return;

        Execute(conn, $"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};");
    }
}


