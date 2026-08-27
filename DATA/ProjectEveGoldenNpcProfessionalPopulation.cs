using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Golden NPC 2B-3: formation + professional canon for Eve Sinclair.
///
/// This patch is intentionally conservative:
/// - Eve's current job as coffee shop manager is established canon.
/// - Her professional profile/competencies are derived from that role.
/// - No college degree, GPA, license number, or certification is invented.
/// - Education/qualification rows record only defensible generic formation.
/// </summary>
public static class ProjectEveGoldenNpcProfessionalPopulation
{
    private const int EveId = 1;
    private const string RoleId = "coffee-shop-manager";

    public static void PopulateEveProfessional()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);
        using var transaction = connection.BeginTransaction();

        ValidateEve(connection, transaction);
        PopulateEducation(connection, transaction);
        PopulateProfessionalProfile(connection, transaction);
        PopulateQualifications(connection, transaction);
        PopulateCompetencies(connection, transaction);

        transaction.Commit();

        Console.WriteLine();
        Console.WriteLine("Golden NPC 2B-3 populated for Eve Sinclair.");
        Console.WriteLine("  Education / formation baseline");
        Console.WriteLine("  Professional profile");
        Console.WriteLine("  Role qualification baseline");
        Console.WriteLine("  Professional competencies");
        Console.WriteLine();
        Console.WriteLine("No unsupported college degree, GPA, license number,");
        Console.WriteLine("or external certification was invented.");
    }

    private static void ValidateEve(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Name, Occupation
            FROM Characters
            WHERE Id = $npcId;
            """;
        command.Parameters.AddWithValue("$npcId", EveId);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            throw new InvalidOperationException("Eve Sinclair (NpcId=1) is missing.");

        string name = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
        string occupation = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();

        if (!string.Equals(name, "Eve Sinclair", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Expected NpcId=1 to be Eve Sinclair, but found '{name}'.");

        if (!occupation.Contains("coffee", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Eve's occupation no longer appears to be coffee-related ('{occupation}'). " +
                "Population aborted to avoid writing mismatched professional canon.");
    }

    private static void PopulateEducation(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcEducationRecords
            (
                EducationRecordId,
                NpcId,
                EducationType,
                InstitutionId,
                InstitutionName,
                ProgramName,
                DegreeOrCredential,
                FieldOfStudy,
                StartGameTime,
                EndGameTime,
                StartAge,
                EndAge,
                Status,
                Gpa,
                Honors,
                StartEventId,
                CompletionEventId,
                WithdrawalEventId,
                Notes,
                CreatedRealAt,
                UpdatedRealAt
            )
            VALUES
            (
                'eve-education-general-secondary',
                $npcId,
                'GeneralEducation',
                '',
                '',
                'General secondary education',
                '',
                '',
                '',
                '',
                NULL,
                NULL,
                'Completed',
                NULL,
                '',
                '',
                '',
                '',
                'Baseline formation record only. No school name, graduation year, GPA, degree, or honors are asserted.',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(EducationRecordId) DO UPDATE SET
                NpcId = excluded.NpcId,
                EducationType = excluded.EducationType,
                ProgramName = excluded.ProgramName,
                Status = excluded.Status,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$npcId", EveId);
        command.ExecuteNonQuery();
    }

    private static void PopulateProfessionalProfile(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcProfessionalProfiles
            (
                NpcId,
                PrimaryRoleId,
                CareerField,
                YearsExperience,
                TrainingLevel,
                LicenseStanding,
                Burnout,
                Motivation,
                CurrentPerformance,
                ProfessionalReputation,
                IsActive,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $npcId,
                $roleId,
                'Hospitality / Coffee Shop Operations',
                0,
                'Experienced on-the-job manager',
                'Not applicable / no known license requirement recorded',
                25,
                80,
                85,
                90,
                1,
                'Eve is established canonically as a trusted coffee shop manager. Numeric values are conservative gameplay baselines reflecting competence, motivation, and strong local reputation rather than external credentials.',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                PrimaryRoleId = excluded.PrimaryRoleId,
                CareerField = excluded.CareerField,
                TrainingLevel = excluded.TrainingLevel,
                LicenseStanding = excluded.LicenseStanding,
                Burnout = excluded.Burnout,
                Motivation = excluded.Motivation,
                CurrentPerformance = excluded.CurrentPerformance,
                ProfessionalReputation = excluded.ProfessionalReputation,
                IsActive = excluded.IsActive,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$npcId", EveId);
        command.Parameters.AddWithValue("$roleId", RoleId);
        command.ExecuteNonQuery();
    }

    private static void PopulateQualifications(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcProfessionalQualifications
            (
                QualificationId,
                NpcId,
                RoleId,
                QualificationType,
                Name,
                IssuerInstitutionId,
                IssuerName,
                CredentialNumber,
                Status,
                IssuedGameTime,
                ExpiresGameTime,
                ObtainedEventId,
                RenewedEventId,
                SuspendedEventId,
                RevokedEventId,
                Notes,
                CreatedRealAt,
                UpdatedRealAt
            )
            VALUES
            (
                'eve-qualification-coffee-manager-experience',
                $npcId,
                $roleId,
                'Experience',
                'Practical coffee-shop management experience',
                '',
                '',
                '',
                'Active',
                '',
                '',
                '',
                '',
                '',
                '',
                'This is an experience-based qualification, not an external certificate or license.',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(QualificationId) DO UPDATE SET
                NpcId = excluded.NpcId,
                RoleId = excluded.RoleId,
                QualificationType = excluded.QualificationType,
                Name = excluded.Name,
                Status = excluded.Status,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$npcId", EveId);
        command.Parameters.AddWithValue("$roleId", RoleId);
        command.ExecuteNonQuery();
    }

    private static void PopulateCompetencies(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        UpsertCompetency(
            connection, transaction,
            "customer-relations",
            "Customer relations",
            90, 85, 90,
            "Advanced",
            "Her established warmth, memory for personal details, and trusted place in the community strongly support this competency.");

        UpsertCompetency(
            connection, transaction,
            "staff-coordination",
            "Staff coordination",
            80, 80, 80,
            "Experienced",
            "Manager role supports day-to-day staff coordination and delegation.");

        UpsertCompetency(
            connection, transaction,
            "coffee-shop-operations",
            "Coffee shop operations",
            85, 85, 85,
            "Experienced",
            "Core role competency for an active coffee shop manager.");

        UpsertCompetency(
            connection, transaction,
            "conflict-deescalation",
            "Conflict de-escalation",
            80, 80, 85,
            "Experienced",
            "Consistent with Eve's patient, attentive, listen-first interpersonal style.");

        UpsertCompetency(
            connection, transaction,
            "community-relationship-management",
            "Community relationship management",
            90, 85, 90,
            "Advanced",
            "Eve is deeply embedded in Bellefontaine community life and is widely trusted.");

        UpsertCompetency(
            connection, transaction,
            "shift-prioritization",
            "Shift prioritization",
            80, 80, 80,
            "Experienced",
            "Operational management requires triage, prioritization, and practical decision-making.");
    }

    private static void UpsertCompetency(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string competencyId,
        string competencyName,
        int currentValue,
        int setPointValue,
        int confidence,
        string experienceLevel,
        string notes)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO NpcProfessionalCompetencies
            (
                NpcId,
                RoleId,
                CompetencyId,
                CompetencyName,
                CurrentValue,
                SetPointValue,
                Confidence,
                ExperienceLevel,
                SourceEducationRecordId,
                SourceQualificationId,
                LastChangeEventId,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $npcId,
                $roleId,
                $competencyId,
                $competencyName,
                $currentValue,
                $setPointValue,
                $confidence,
                $experienceLevel,
                '',
                'eve-qualification-coffee-manager-experience',
                '',
                $notes,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId, RoleId, CompetencyId) DO UPDATE SET
                CompetencyName = excluded.CompetencyName,
                CurrentValue = excluded.CurrentValue,
                SetPointValue = excluded.SetPointValue,
                Confidence = excluded.Confidence,
                ExperienceLevel = excluded.ExperienceLevel,
                SourceQualificationId = excluded.SourceQualificationId,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        command.Parameters.AddWithValue("$npcId", EveId);
        command.Parameters.AddWithValue("$roleId", RoleId);
        command.Parameters.AddWithValue("$competencyId", competencyId);
        command.Parameters.AddWithValue("$competencyName", competencyName);
        command.Parameters.AddWithValue("$currentValue", currentValue);
        command.Parameters.AddWithValue("$setPointValue", setPointValue);
        command.Parameters.AddWithValue("$confidence", confidence);
        command.Parameters.AddWithValue("$experienceLevel", experienceLevel);
        command.Parameters.AddWithValue("$notes", notes);

        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();

        return connection;
    }
}
