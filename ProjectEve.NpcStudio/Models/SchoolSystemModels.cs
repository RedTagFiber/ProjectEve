namespace ProjectEve.NpcStudio.Models;

public sealed class SchoolSystemMaster
{
    public SchoolDistrictMaster District { get; set; } = new();
    public List<SchoolMaster> Schools { get; set; } = new();
    public List<SchoolDepartmentMaster> Departments { get; set; } = new();
    public List<SchoolCourseMaster> Courses { get; set; } = new();
    public List<SchoolStaffRoleTemplateMaster> StaffRoleTemplates { get; set; } = new();
    public List<FictionalSchoolStaffAnchorMaster> FictionalAnchorStaff { get; set; } = new();
}

public sealed class SchoolDistrictMaster
{
    public string DistrictId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DistrictOfficeAddress { get; set; } = "";
    public string MascotName { get; set; } = "";
    public string TeamName { get; set; } = "";
    public string PrimaryColor { get; set; } = "";
    public string SecondaryColor { get; set; } = "";
    public string BrandingNote { get; set; } = "";
}

public sealed class SchoolMaster
{
    public string SchoolId { get; set; } = "";
    public string Name { get; set; } = "";
    public string GradeLow { get; set; } = "";
    public string GradeHigh { get; set; } = "";
    public string Address { get; set; } = "";
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public string BuildingType { get; set; } = "";
    public string MascotName { get; set; } = "";
}

public sealed class SchoolDepartmentMaster
{
    public string DepartmentId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> SchoolLevels { get; set; } = new();
}

public sealed class SchoolCourseMaster
{
    public string CourseCode { get; set; } = "";
    public string SchoolId { get; set; } = "";
    public string DepartmentId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> GradeLevels { get; set; } = new();
    public string CourseType { get; set; } = "";
}

public sealed class SchoolStaffRoleTemplateMaster
{
    public string SchoolId { get; set; } = "";
    public string DepartmentId { get; set; } = "";
    public string RoleTitle { get; set; } = "";
    public int TargetCount { get; set; }
    public int DefaultTier { get; set; } = 4;
}

public sealed class FictionalSchoolStaffAnchorMaster
{
    public string StaffKey { get; set; } = "";
    public string SuggestedName { get; set; } = "";
    public string SchoolId { get; set; } = "";
    public string DepartmentId { get; set; } = "";
    public string RoleTitle { get; set; } = "";
    public int Tier { get; set; } = 3;
    public List<string> TeachableCourseCodes { get; set; } = new();
    public string Notes { get; set; } = "";
}

public sealed record SchoolSystemSummary(
    int Schools,
    int Departments,
    int Courses,
    int StaffSlots,
    int AssignedStaff,
    int StudentYears,
    int CourseEnrollments);
