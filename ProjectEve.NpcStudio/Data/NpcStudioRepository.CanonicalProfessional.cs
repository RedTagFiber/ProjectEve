using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    public Task<CanonicalProfessionalBundle?> GetCanonicalProfessionalBundleAsync(int npcId)
    {
        using var conn = Open();

        var name = CanonicalScalarString(
            conn,
            "SELECT IFNULL(Name, '') FROM Characters WHERE Id = $npcId;",
            npcId);

        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult<CanonicalProfessionalBundle?>(null);

        var bundle = new CanonicalProfessionalBundle
        {
            NpcId = npcId,
            NpcName = name,
            Education = GetCanonicalEducation(conn, npcId),
            ProfessionalProfile = GetCanonicalProfessionalProfile(conn, npcId),
            Qualifications = GetCanonicalQualifications(conn, npcId),
            Competencies = GetCanonicalCompetencies(conn, npcId)
        };

        return Task.FromResult<CanonicalProfessionalBundle?>(bundle);
    }

    public Task SaveCanonicalEducationAsync(CanonicalEducationRecord item)
    {
        using var conn = Open();

        if (string.IsNullOrWhiteSpace(item.EducationRecordId))
            item.EducationRecordId = "edu-" + Guid.NewGuid().ToString("N");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcEducationRecords
            (
                EducationRecordId, NpcId,
                EducationType, InstitutionId, InstitutionName,
                ProgramName, DegreeOrCredential, FieldOfStudy,
                StartGameTime, EndGameTime, StartAge, EndAge,
                Status, Gpa, Honors,
                StartEventId, CompletionEventId, WithdrawalEventId,
                Notes, CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $id, $npcId,
                $educationType, $institutionId, $institutionName,
                $programName, $degree, $field,
                $startGameTime, $endGameTime, $startAge, $endAge,
                $status, $gpa, $honors,
                $startEventId, $completionEventId, $withdrawalEventId,
                $notes, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            )
            ON CONFLICT(EducationRecordId) DO UPDATE SET
                NpcId = excluded.NpcId,
                EducationType = excluded.EducationType,
                InstitutionId = excluded.InstitutionId,
                InstitutionName = excluded.InstitutionName,
                ProgramName = excluded.ProgramName,
                DegreeOrCredential = excluded.DegreeOrCredential,
                FieldOfStudy = excluded.FieldOfStudy,
                StartGameTime = excluded.StartGameTime,
                EndGameTime = excluded.EndGameTime,
                StartAge = excluded.StartAge,
                EndAge = excluded.EndAge,
                Status = excluded.Status,
                Gpa = excluded.Gpa,
                Honors = excluded.Honors,
                StartEventId = excluded.StartEventId,
                CompletionEventId = excluded.CompletionEventId,
                WithdrawalEventId = excluded.WithdrawalEventId,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", item.EducationRecordId);
        cmd.Parameters.AddWithValue("$npcId", item.NpcId);
        cmd.Parameters.AddWithValue("$educationType", item.EducationType ?? "");
        cmd.Parameters.AddWithValue("$institutionId", item.InstitutionId ?? "");
        cmd.Parameters.AddWithValue("$institutionName", item.InstitutionName ?? "");
        cmd.Parameters.AddWithValue("$programName", item.ProgramName ?? "");
        cmd.Parameters.AddWithValue("$degree", item.DegreeOrCredential ?? "");
        cmd.Parameters.AddWithValue("$field", item.FieldOfStudy ?? "");
        cmd.Parameters.AddWithValue("$startGameTime", item.StartGameTime ?? "");
        cmd.Parameters.AddWithValue("$endGameTime", item.EndGameTime ?? "");
        cmd.Parameters.AddWithValue("$startAge", (object?)item.StartAge ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$endAge", (object?)item.EndAge ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", item.Status ?? "");
        cmd.Parameters.AddWithValue("$gpa", (object?)item.Gpa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$honors", item.Honors ?? "");
        cmd.Parameters.AddWithValue("$startEventId", item.StartEventId ?? "");
        cmd.Parameters.AddWithValue("$completionEventId", item.CompletionEventId ?? "");
        cmd.Parameters.AddWithValue("$withdrawalEventId", item.WithdrawalEventId ?? "");
        cmd.Parameters.AddWithValue("$notes", item.Notes ?? "");

        cmd.ExecuteNonQuery();

        AddRevision(
            conn,
            item.NpcId,
            "Canonical Education",
            "Canonical education record saved",
            $"EducationRecordId={item.EducationRecordId}; Type={item.EducationType}; Status={item.Status}.");

        return Task.CompletedTask;
    }

    public Task SaveCanonicalProfessionalProfileAsync(CanonicalProfessionalProfile item)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcProfessionalProfiles
            (
                NpcId, PrimaryRoleId, CareerField,
                YearsExperience, TrainingLevel, LicenseStanding,
                Burnout, Motivation, CurrentPerformance, ProfessionalReputation,
                IsActive, Notes, UpdatedRealAt
            )
            VALUES
            (
                $npcId, $primaryRoleId, $careerField,
                $yearsExperience, $trainingLevel, $licenseStanding,
                $burnout, $motivation, $performance, $reputation,
                $isActive, $notes, CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                PrimaryRoleId = excluded.PrimaryRoleId,
                CareerField = excluded.CareerField,
                YearsExperience = excluded.YearsExperience,
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

        cmd.Parameters.AddWithValue("$npcId", item.NpcId);
        cmd.Parameters.AddWithValue("$primaryRoleId", item.PrimaryRoleId ?? "");
        cmd.Parameters.AddWithValue("$careerField", item.CareerField ?? "");
        cmd.Parameters.AddWithValue("$yearsExperience", Math.Max(0, item.YearsExperience));
        cmd.Parameters.AddWithValue("$trainingLevel", item.TrainingLevel ?? "");
        cmd.Parameters.AddWithValue("$licenseStanding", item.LicenseStanding ?? "");
        cmd.Parameters.AddWithValue("$burnout", Math.Clamp(item.Burnout, 0, 100));
        cmd.Parameters.AddWithValue("$motivation", Math.Clamp(item.Motivation, 0, 100));
        cmd.Parameters.AddWithValue("$performance", Math.Clamp(item.CurrentPerformance, 0, 100));
        cmd.Parameters.AddWithValue("$reputation", Math.Clamp(item.ProfessionalReputation, 0, 100));
        cmd.Parameters.AddWithValue("$isActive", item.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", item.Notes ?? "");

        cmd.ExecuteNonQuery();

        AddRevision(
            conn,
            item.NpcId,
            "Canonical Professional",
            "Canonical professional profile saved",
            $"Role={item.PrimaryRoleId}; CareerField={item.CareerField}.");

        return Task.CompletedTask;
    }

    public Task SaveCanonicalQualificationAsync(CanonicalProfessionalQualification item)
    {
        using var conn = Open();

        if (string.IsNullOrWhiteSpace(item.QualificationId))
            item.QualificationId = "qual-" + Guid.NewGuid().ToString("N");

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcProfessionalQualifications
            (
                QualificationId, NpcId,
                RoleId, QualificationType, Name,
                IssuerInstitutionId, IssuerName, CredentialNumber,
                Status, IssuedGameTime, ExpiresGameTime,
                ObtainedEventId, RenewedEventId, SuspendedEventId, RevokedEventId,
                Notes, CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $id, $npcId,
                $roleId, $type, $name,
                $issuerInstitutionId, $issuerName, $credentialNumber,
                $status, $issued, $expires,
                $obtainedEventId, $renewedEventId, $suspendedEventId, $revokedEventId,
                $notes, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            )
            ON CONFLICT(QualificationId) DO UPDATE SET
                NpcId = excluded.NpcId,
                RoleId = excluded.RoleId,
                QualificationType = excluded.QualificationType,
                Name = excluded.Name,
                IssuerInstitutionId = excluded.IssuerInstitutionId,
                IssuerName = excluded.IssuerName,
                CredentialNumber = excluded.CredentialNumber,
                Status = excluded.Status,
                IssuedGameTime = excluded.IssuedGameTime,
                ExpiresGameTime = excluded.ExpiresGameTime,
                ObtainedEventId = excluded.ObtainedEventId,
                RenewedEventId = excluded.RenewedEventId,
                SuspendedEventId = excluded.SuspendedEventId,
                RevokedEventId = excluded.RevokedEventId,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", item.QualificationId);
        cmd.Parameters.AddWithValue("$npcId", item.NpcId);
        cmd.Parameters.AddWithValue("$roleId", item.RoleId ?? "");
        cmd.Parameters.AddWithValue("$type", item.QualificationType ?? "");
        cmd.Parameters.AddWithValue("$name", item.Name ?? "");
        cmd.Parameters.AddWithValue("$issuerInstitutionId", item.IssuerInstitutionId ?? "");
        cmd.Parameters.AddWithValue("$issuerName", item.IssuerName ?? "");
        cmd.Parameters.AddWithValue("$credentialNumber", item.CredentialNumber ?? "");
        cmd.Parameters.AddWithValue("$status", item.Status ?? "");
        cmd.Parameters.AddWithValue("$issued", item.IssuedGameTime ?? "");
        cmd.Parameters.AddWithValue("$expires", item.ExpiresGameTime ?? "");
        cmd.Parameters.AddWithValue("$obtainedEventId", item.ObtainedEventId ?? "");
        cmd.Parameters.AddWithValue("$renewedEventId", item.RenewedEventId ?? "");
        cmd.Parameters.AddWithValue("$suspendedEventId", item.SuspendedEventId ?? "");
        cmd.Parameters.AddWithValue("$revokedEventId", item.RevokedEventId ?? "");
        cmd.Parameters.AddWithValue("$notes", item.Notes ?? "");

        cmd.ExecuteNonQuery();

        AddRevision(
            conn,
            item.NpcId,
            "Canonical Qualification",
            "Canonical qualification saved",
            $"QualificationId={item.QualificationId}; Name={item.Name}.");

        return Task.CompletedTask;
    }

    public Task SaveCanonicalCompetencyAsync(CanonicalProfessionalCompetency item)
    {
        if (string.IsNullOrWhiteSpace(item.RoleId))
            throw new InvalidOperationException("RoleId is required for a competency.");

        if (string.IsNullOrWhiteSpace(item.CompetencyId))
            throw new InvalidOperationException("CompetencyId is required for a competency.");

        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcProfessionalCompetencies
            (
                NpcId, RoleId, CompetencyId, CompetencyName,
                CurrentValue, SetPointValue, Confidence, ExperienceLevel,
                SourceEducationRecordId, SourceQualificationId, LastChangeEventId,
                Notes, UpdatedRealAt
            )
            VALUES
            (
                $npcId, $roleId, $competencyId, $competencyName,
                $currentValue, $setPointValue, $confidence, $experienceLevel,
                $sourceEducation, $sourceQualification, $lastChangeEventId,
                $notes, CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId, RoleId, CompetencyId) DO UPDATE SET
                CompetencyName = excluded.CompetencyName,
                CurrentValue = excluded.CurrentValue,
                SetPointValue = excluded.SetPointValue,
                Confidence = excluded.Confidence,
                ExperienceLevel = excluded.ExperienceLevel,
                SourceEducationRecordId = excluded.SourceEducationRecordId,
                SourceQualificationId = excluded.SourceQualificationId,
                LastChangeEventId = excluded.LastChangeEventId,
                Notes = excluded.Notes,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$npcId", item.NpcId);
        cmd.Parameters.AddWithValue("$roleId", item.RoleId);
        cmd.Parameters.AddWithValue("$competencyId", item.CompetencyId);
        cmd.Parameters.AddWithValue("$competencyName", item.CompetencyName ?? "");
        cmd.Parameters.AddWithValue("$currentValue", Math.Clamp(item.CurrentValue, 0, 100));
        cmd.Parameters.AddWithValue("$setPointValue", Math.Clamp(item.SetPointValue, 0, 100));
        cmd.Parameters.AddWithValue("$confidence", Math.Clamp(item.Confidence, 0, 100));
        cmd.Parameters.AddWithValue("$experienceLevel", item.ExperienceLevel ?? "");
        cmd.Parameters.AddWithValue("$sourceEducation", item.SourceEducationRecordId ?? "");
        cmd.Parameters.AddWithValue("$sourceQualification", item.SourceQualificationId ?? "");
        cmd.Parameters.AddWithValue("$lastChangeEventId", item.LastChangeEventId ?? "");
        cmd.Parameters.AddWithValue("$notes", item.Notes ?? "");

        cmd.ExecuteNonQuery();

        AddRevision(
            conn,
            item.NpcId,
            "Canonical Competency",
            "Canonical competency saved",
            $"Role={item.RoleId}; CompetencyId={item.CompetencyId}; Value={item.CurrentValue}.");

        return Task.CompletedTask;
    }

    private static List<CanonicalEducationRecord> GetCanonicalEducation(
        SqliteConnection conn,
        int npcId)
    {
        var list = new List<CanonicalEducationRecord>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT *
            FROM NpcEducationRecords
            WHERE NpcId = $npcId
            ORDER BY
                CASE WHEN StartAge IS NULL THEN 1 ELSE 0 END,
                StartAge,
                CreatedRealAt;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            list.Add(new CanonicalEducationRecord
            {
                EducationRecordId = CReadString(r, "EducationRecordId"),
                NpcId = npcId,
                EducationType = CReadString(r, "EducationType"),
                InstitutionId = CReadString(r, "InstitutionId"),
                InstitutionName = CReadString(r, "InstitutionName"),
                ProgramName = CReadString(r, "ProgramName"),
                DegreeOrCredential = CReadString(r, "DegreeOrCredential"),
                FieldOfStudy = CReadString(r, "FieldOfStudy"),
                StartGameTime = CReadString(r, "StartGameTime"),
                EndGameTime = CReadString(r, "EndGameTime"),
                StartAge = CReadNullableInt(r, "StartAge"),
                EndAge = CReadNullableInt(r, "EndAge"),
                Status = CReadString(r, "Status"),
                Gpa = CReadNullableDouble(r, "Gpa"),
                Honors = CReadString(r, "Honors"),
                StartEventId = CReadString(r, "StartEventId"),
                CompletionEventId = CReadString(r, "CompletionEventId"),
                WithdrawalEventId = CReadString(r, "WithdrawalEventId"),
                Notes = CReadString(r, "Notes")
            });
        }

        return list;
    }

    private static CanonicalProfessionalProfile GetCanonicalProfessionalProfile(
        SqliteConnection conn,
        int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT *
            FROM NpcProfessionalProfiles
            WHERE NpcId = $npcId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return new CanonicalProfessionalProfile { NpcId = npcId };

        return new CanonicalProfessionalProfile
        {
            NpcId = npcId,
            PrimaryRoleId = CReadString(r, "PrimaryRoleId"),
            CareerField = CReadString(r, "CareerField"),
            YearsExperience = CReadDouble(r, "YearsExperience"),
            TrainingLevel = CReadString(r, "TrainingLevel"),
            LicenseStanding = CReadString(r, "LicenseStanding"),
            Burnout = CReadInt(r, "Burnout"),
            Motivation = CReadInt(r, "Motivation"),
            CurrentPerformance = CReadInt(r, "CurrentPerformance"),
            ProfessionalReputation = CReadInt(r, "ProfessionalReputation"),
            IsActive = CReadBool(r, "IsActive"),
            Notes = CReadString(r, "Notes")
        };
    }

    private static List<CanonicalProfessionalQualification> GetCanonicalQualifications(
        SqliteConnection conn,
        int npcId)
    {
        var list = new List<CanonicalProfessionalQualification>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT *
            FROM NpcProfessionalQualifications
            WHERE NpcId = $npcId
            ORDER BY QualificationType, Name, CreatedRealAt;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            list.Add(new CanonicalProfessionalQualification
            {
                QualificationId = CReadString(r, "QualificationId"),
                NpcId = npcId,
                RoleId = CReadString(r, "RoleId"),
                QualificationType = CReadString(r, "QualificationType"),
                Name = CReadString(r, "Name"),
                IssuerInstitutionId = CReadString(r, "IssuerInstitutionId"),
                IssuerName = CReadString(r, "IssuerName"),
                CredentialNumber = CReadString(r, "CredentialNumber"),
                Status = CReadString(r, "Status"),
                IssuedGameTime = CReadString(r, "IssuedGameTime"),
                ExpiresGameTime = CReadString(r, "ExpiresGameTime"),
                ObtainedEventId = CReadString(r, "ObtainedEventId"),
                RenewedEventId = CReadString(r, "RenewedEventId"),
                SuspendedEventId = CReadString(r, "SuspendedEventId"),
                RevokedEventId = CReadString(r, "RevokedEventId"),
                Notes = CReadString(r, "Notes")
            });
        }

        return list;
    }

    private static List<CanonicalProfessionalCompetency> GetCanonicalCompetencies(
        SqliteConnection conn,
        int npcId)
    {
        var list = new List<CanonicalProfessionalCompetency>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT *
            FROM NpcProfessionalCompetencies
            WHERE NpcId = $npcId
            ORDER BY RoleId, CompetencyName, CompetencyId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            list.Add(new CanonicalProfessionalCompetency
            {
                NpcId = npcId,
                RoleId = CReadString(r, "RoleId"),
                CompetencyId = CReadString(r, "CompetencyId"),
                CompetencyName = CReadString(r, "CompetencyName"),
                CurrentValue = CReadInt(r, "CurrentValue"),
                SetPointValue = CReadInt(r, "SetPointValue"),
                Confidence = CReadInt(r, "Confidence"),
                ExperienceLevel = CReadString(r, "ExperienceLevel"),
                SourceEducationRecordId = CReadString(r, "SourceEducationRecordId"),
                SourceQualificationId = CReadString(r, "SourceQualificationId"),
                LastChangeEventId = CReadString(r, "LastChangeEventId"),
                Notes = CReadString(r, "Notes")
            });
        }

        return list;
    }

    private static string CanonicalScalarString(
        SqliteConnection conn,
        string sql,
        int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static int COrdinal(SqliteDataReader r, string name)
        => r.GetOrdinal(name);

    private static string CReadString(SqliteDataReader r, string name)
    {
        var i = COrdinal(r, name);
        return r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";
    }

    private static int CReadInt(SqliteDataReader r, string name)
    {
        var i = COrdinal(r, name);
        return r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
    }

    private static int? CReadNullableInt(SqliteDataReader r, string name)
    {
        var i = COrdinal(r, name);
        return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    }

    private static double CReadDouble(SqliteDataReader r, string name)
    {
        var i = COrdinal(r, name);
        return r.IsDBNull(i) ? 0 : Convert.ToDouble(r.GetValue(i));
    }

    private static double? CReadNullableDouble(SqliteDataReader r, string name)
    {
        var i = COrdinal(r, name);
        return r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i));
    }

    private static bool CReadBool(SqliteDataReader r, string name)
    {
        var i = COrdinal(r, name);
        return !r.IsDBNull(i) && Convert.ToInt32(r.GetValue(i)) != 0;
    }
}
