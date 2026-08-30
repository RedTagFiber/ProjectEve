namespace ProjectEve.NpcStudio.Models;

public sealed class CanonicalEducationRecord
{
    public string EducationRecordId { get; set; } = "";
    public int NpcId { get; set; }

    public string EducationType { get; set; } = "";
    public string InstitutionId { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string DegreeOrCredential { get; set; } = "";
    public string FieldOfStudy { get; set; } = "";

    public string StartGameTime { get; set; } = "";
    public string EndGameTime { get; set; } = "";

    public int? StartAge { get; set; }
    public int? EndAge { get; set; }

    public string Status { get; set; } = "Completed";
    public double? Gpa { get; set; }
    public string Honors { get; set; } = "";

    public string StartEventId { get; set; } = "";
    public string CompletionEventId { get; set; } = "";
    public string WithdrawalEventId { get; set; } = "";

    public string Notes { get; set; } = "";
}

public sealed class CanonicalProfessionalProfile
{
    public int NpcId { get; set; }

    public string PrimaryRoleId { get; set; } = "";
    public string CareerField { get; set; } = "";

    public double YearsExperience { get; set; }
    public string TrainingLevel { get; set; } = "";
    public string LicenseStanding { get; set; } = "";

    public int Burnout { get; set; }
    public int Motivation { get; set; } = 50;
    public int CurrentPerformance { get; set; } = 50;
    public int ProfessionalReputation { get; set; } = 50;

    public bool IsActive { get; set; } = true;
    public string Notes { get; set; } = "";
}

public sealed class CanonicalProfessionalQualification
{
    public string QualificationId { get; set; } = "";
    public int NpcId { get; set; }

    public string RoleId { get; set; } = "";
    public string QualificationType { get; set; } = "";
    public string Name { get; set; } = "";

    public string IssuerInstitutionId { get; set; } = "";
    public string IssuerName { get; set; } = "";
    public string CredentialNumber { get; set; } = "";

    public string Status { get; set; } = "Active";
    public string IssuedGameTime { get; set; } = "";
    public string ExpiresGameTime { get; set; } = "";

    public string ObtainedEventId { get; set; } = "";
    public string RenewedEventId { get; set; } = "";
    public string SuspendedEventId { get; set; } = "";
    public string RevokedEventId { get; set; } = "";

    public string Notes { get; set; } = "";
}

public sealed class CanonicalProfessionalCompetency
{
    public int NpcId { get; set; }

    public string RoleId { get; set; } = "";
    public string CompetencyId { get; set; } = "";
    public string CompetencyName { get; set; } = "";

    public int CurrentValue { get; set; } = 50;
    public int SetPointValue { get; set; } = 50;
    public int Confidence { get; set; } = 50;

    public string ExperienceLevel { get; set; } = "";

    public string SourceEducationRecordId { get; set; } = "";
    public string SourceQualificationId { get; set; } = "";
    public string LastChangeEventId { get; set; } = "";

    public string Notes { get; set; } = "";
}

public sealed class CanonicalProfessionalBundle
{
    public int NpcId { get; set; }
    public string NpcName { get; set; } = "";

    public List<CanonicalEducationRecord> Education { get; set; } = new();
    public CanonicalProfessionalProfile ProfessionalProfile { get; set; } = new();
    public List<CanonicalProfessionalQualification> Qualifications { get; set; } = new();
    public List<CanonicalProfessionalCompetency> Competencies { get; set; } = new();
}
