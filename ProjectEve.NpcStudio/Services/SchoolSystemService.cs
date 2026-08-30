using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class SchoolSystemService
{
    private readonly NpcStudioOptions _options;
    private readonly string _masterPath =
        @"D:\ProjectEve\EveData\world\ohio\bellefontaine_school_system_master.json";

    public SchoolSystemService(NpcStudioOptions options)
    {
        _options = options;
    }

    public SchoolSystemMaster LoadMaster()
    {
        if (!File.Exists(_masterPath))
            throw new FileNotFoundException("Bellefontaine school master file not found.", _masterPath);

        var json = File.ReadAllText(_masterPath);
        return JsonSerializer.Deserialize<SchoolSystemMaster>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new SchoolSystemMaster();
    }

    public void SeedMaster()
    {
        var master = LoadMaster();
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        Exec(conn, tx, """
            INSERT INTO SchoolDistricts(
                DistrictId, Name, DistrictOfficeAddress, MascotName, TeamName,
                PrimaryColor, SecondaryColor, BrandingNote, UpdatedRealAt)
            VALUES($id,$name,$address,$mascot,$team,$primary,$secondary,$note,CURRENT_TIMESTAMP)
            ON CONFLICT(DistrictId) DO UPDATE SET
                Name=excluded.Name,
                DistrictOfficeAddress=excluded.DistrictOfficeAddress,
                MascotName=excluded.MascotName,
                TeamName=excluded.TeamName,
                PrimaryColor=excluded.PrimaryColor,
                SecondaryColor=excluded.SecondaryColor,
                BrandingNote=excluded.BrandingNote,
                UpdatedRealAt=CURRENT_TIMESTAMP;
            """,
            ("$id", master.District.DistrictId),
            ("$name", master.District.Name),
            ("$address", master.District.DistrictOfficeAddress),
            ("$mascot", master.District.MascotName),
            ("$team", master.District.TeamName),
            ("$primary", master.District.PrimaryColor),
            ("$secondary", master.District.SecondaryColor),
            ("$note", master.District.BrandingNote));

        foreach (var s in master.Schools)
        {
            Exec(conn, tx, """
                INSERT INTO SchoolInstitutions(
                    SchoolId,DistrictId,Name,GradeLow,GradeHigh,Address,StartTime,EndTime,
                    BuildingType,MascotName,IsActive,UpdatedRealAt)
                VALUES($id,$district,$name,$low,$high,$address,$start,$end,$type,$mascot,1,CURRENT_TIMESTAMP)
                ON CONFLICT(SchoolId) DO UPDATE SET
                    Name=excluded.Name, GradeLow=excluded.GradeLow, GradeHigh=excluded.GradeHigh,
                    Address=excluded.Address, StartTime=excluded.StartTime, EndTime=excluded.EndTime,
                    BuildingType=excluded.BuildingType, MascotName=excluded.MascotName,
                    UpdatedRealAt=CURRENT_TIMESTAMP;
                """,
                ("$id", s.SchoolId),("$district", master.District.DistrictId),("$name", s.Name),
                ("$low", s.GradeLow),("$high", s.GradeHigh),("$address", s.Address),
                ("$start", s.StartTime),("$end", s.EndTime),("$type", s.BuildingType),("$mascot", s.MascotName));
        }

        foreach (var d in master.Departments)
        {
            Exec(conn, tx, """
                INSERT INTO SchoolDepartments(DepartmentId,Name,SchoolLevels,UpdatedRealAt)
                VALUES($id,$name,$levels,CURRENT_TIMESTAMP)
                ON CONFLICT(DepartmentId) DO UPDATE SET
                    Name=excluded.Name, SchoolLevels=excluded.SchoolLevels, UpdatedRealAt=CURRENT_TIMESTAMP;
                """,
                ("$id", d.DepartmentId),("$name", d.Name),("$levels", string.Join("|", d.SchoolLevels)));
        }

        foreach (var c in master.Courses)
        {
            Exec(conn, tx, """
                INSERT INTO SchoolCourses(
                    CourseCode,SchoolId,DepartmentId,Name,GradeLevels,CourseType,IsActive,UpdatedRealAt)
                VALUES($code,$school,$dept,$name,$grades,$type,1,CURRENT_TIMESTAMP)
                ON CONFLICT(CourseCode) DO UPDATE SET
                    SchoolId=excluded.SchoolId, DepartmentId=excluded.DepartmentId, Name=excluded.Name,
                    GradeLevels=excluded.GradeLevels, CourseType=excluded.CourseType, UpdatedRealAt=CURRENT_TIMESTAMP;
                """,
                ("$code", c.CourseCode),("$school", c.SchoolId),("$dept", c.DepartmentId),
                ("$name", c.Name),("$grades", string.Join("|", c.GradeLevels)),("$type", c.CourseType));
        }

        foreach (var role in master.StaffRoleTemplates)
        {
            for (var i = 1; i <= role.TargetCount; i++)
            {
                var slot = $"{role.SchoolId}:{role.DepartmentId}:{Slug(role.RoleTitle)}:{i:D2}";
                Exec(conn, tx, """
                    INSERT INTO SchoolStaffSlots(
                        StaffSlotId,SchoolId,DepartmentId,RoleTitle,SlotNumber,DefaultTier,
                        AssignedNpcId,SuggestedName,Status,Notes,UpdatedRealAt)
                    VALUES($slot,$school,$dept,$title,$number,$tier,NULL,'','Open','',CURRENT_TIMESTAMP)
                    ON CONFLICT(StaffSlotId) DO UPDATE SET
                        DefaultTier=excluded.DefaultTier, UpdatedRealAt=CURRENT_TIMESTAMP;
                    """,
                    ("$slot", slot),("$school", role.SchoolId),("$dept", role.DepartmentId),
                    ("$title", role.RoleTitle),("$number", i),("$tier", role.DefaultTier));
            }
        }

        foreach (var anchor in master.FictionalAnchorStaff)
        {
            var slot = anchor.StaffKey;
            Exec(conn, tx, """
                INSERT INTO SchoolStaffSlots(
                    StaffSlotId,SchoolId,DepartmentId,RoleTitle,SlotNumber,DefaultTier,
                    AssignedNpcId,SuggestedName,Status,Notes,UpdatedRealAt)
                VALUES($slot,$school,$dept,$title,0,$tier,NULL,$name,'Proposed',$notes,CURRENT_TIMESTAMP)
                ON CONFLICT(StaffSlotId) DO UPDATE SET
                    SchoolId=excluded.SchoolId, DepartmentId=excluded.DepartmentId,
                    RoleTitle=excluded.RoleTitle, DefaultTier=excluded.DefaultTier,
                    SuggestedName=excluded.SuggestedName, Notes=excluded.Notes, UpdatedRealAt=CURRENT_TIMESTAMP;
                """,
                ("$slot", slot),("$school", anchor.SchoolId),("$dept", anchor.DepartmentId),
                ("$title", anchor.RoleTitle),("$tier", anchor.Tier),("$name", anchor.SuggestedName),("$notes", anchor.Notes));

            foreach (var code in anchor.TeachableCourseCodes)
            {
                Exec(conn, tx, """
                    INSERT OR IGNORE INTO TeacherCourseCapabilities(
                        CapabilityId,StaffSlotId,CourseCode,Priority,Notes,UpdatedRealAt)
                    VALUES($id,$slot,$course,100,'Fictional anchor teacher capability',CURRENT_TIMESTAMP);
                    """,
                    ("$id", $"{slot}:{code}"),("$slot", slot),("$course", code));
            }
        }

        tx.Commit();
    }

    public SchoolSystemSummary GetSummary()
    {
        using var conn = Open();
        return new SchoolSystemSummary(
            Count(conn, "SchoolInstitutions"),
            Count(conn, "SchoolDepartments"),
            Count(conn, "SchoolCourses"),
            Count(conn, "SchoolStaffSlots"),
            Scalar(conn, "SELECT COUNT(*) FROM SchoolStaffSlots WHERE AssignedNpcId IS NOT NULL;"),
            Count(conn, "StudentSchoolYears"),
            Count(conn, "StudentCourseEnrollments"));
    }


    public int GetOpenBhsStaffCount()
    {
        using var conn = Open();
        return Scalar(conn, "SELECT COUNT(*) FROM SchoolStaffSlots WHERE SchoolId='BHS' AND AssignedNpcId IS NULL;");
    }
    public IReadOnlyList<SchoolMaster> GetSchools() => LoadMaster().Schools;
    public IReadOnlyList<SchoolCourseMaster> GetCourses() => LoadMaster().Courses;
    public IReadOnlyList<SchoolStaffRoleTemplateMaster> GetRoleTemplates() => LoadMaster().StaffRoleTemplates;
    public IReadOnlyList<FictionalSchoolStaffAnchorMaster> GetAnchors() => LoadMaster().FictionalAnchorStaff;

    private SqliteConnection Open()
    {
        var path = @"D:\ProjectEveData\Database\project_eve_locations.db";
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        return conn;
    }

    private static int Count(SqliteConnection conn, string table)
        => Scalar(conn, $"SELECT COUNT(*) FROM [{table}];");

    private static int Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static string Slug(string s)
        => new string((s ?? "").ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name,value) in args)
            cmd.Parameters.AddWithValue(name, value ?? "");
        cmd.ExecuteNonQuery();
    }
}


