using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public static class NpcStudioSchoolSchema
{
    public static void Ensure(NpcStudioOptions options)
    {
        var path = @"D:\ProjectEveData\Database\project_eve_locations.db";

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        Exec(conn, """
            CREATE TABLE IF NOT EXISTS SchoolDistricts(
                DistrictId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                DistrictOfficeAddress TEXT NOT NULL DEFAULT '',
                MascotName TEXT NOT NULL DEFAULT '',
                TeamName TEXT NOT NULL DEFAULT '',
                PrimaryColor TEXT NOT NULL DEFAULT '',
                SecondaryColor TEXT NOT NULL DEFAULT '',
                BrandingNote TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS SchoolInstitutions(
                SchoolId TEXT PRIMARY KEY,
                DistrictId TEXT NOT NULL,
                Name TEXT NOT NULL,
                GradeLow TEXT NOT NULL DEFAULT '',
                GradeHigh TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                StartTime TEXT NOT NULL DEFAULT '',
                EndTime TEXT NOT NULL DEFAULT '',
                BuildingType TEXT NOT NULL DEFAULT '',
                MascotName TEXT NOT NULL DEFAULT '',
                IsActive INTEGER NOT NULL DEFAULT 1,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS SchoolDepartments(
                DepartmentId TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                SchoolLevels TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS SchoolCourses(
                CourseCode TEXT PRIMARY KEY,
                SchoolId TEXT NOT NULL,
                DepartmentId TEXT NOT NULL,
                Name TEXT NOT NULL,
                GradeLevels TEXT NOT NULL DEFAULT '',
                CourseType TEXT NOT NULL DEFAULT '',
                IsActive INTEGER NOT NULL DEFAULT 1,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS SchoolStaffSlots(
                StaffSlotId TEXT PRIMARY KEY,
                SchoolId TEXT NOT NULL,
                DepartmentId TEXT NOT NULL DEFAULT '',
                RoleTitle TEXT NOT NULL,
                SlotNumber INTEGER NOT NULL,
                DefaultTier INTEGER NOT NULL DEFAULT 4,
                AssignedNpcId INTEGER NULL,
                SuggestedName TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Open',
                Notes TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_SchoolStaffSlots_RoleSlot
            ON SchoolStaffSlots(SchoolId, DepartmentId, RoleTitle, SlotNumber);

            CREATE TABLE IF NOT EXISTS TeacherCourseCapabilities(
                CapabilityId TEXT PRIMARY KEY,
                StaffSlotId TEXT NOT NULL,
                CourseCode TEXT NOT NULL,
                Priority INTEGER NOT NULL DEFAULT 50,
                Notes TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_TeacherCourseCapabilities
            ON TeacherCourseCapabilities(StaffSlotId, CourseCode);

            CREATE TABLE IF NOT EXISTS StudentSchoolYears(
                SchoolYearRecordId TEXT PRIMARY KEY,
                NpcId INTEGER NOT NULL,
                SchoolId TEXT NOT NULL,
                AcademicYear TEXT NOT NULL DEFAULT '',
                GradeLevel TEXT NOT NULL,
                AgeStart INTEGER NULL,
                AgeEnd INTEGER NULL,
                OverallGpa REAL NULL,
                AcademicPerformance TEXT NOT NULL DEFAULT '',
                AttendanceSummary TEXT NOT NULL DEFAULT '',
                Activities TEXT NOT NULL DEFAULT '',
                Sports TEXT NOT NULL DEFAULT '',
                SocialNotes TEXT NOT NULL DEFAULT '',
                TeacherMentorNotes TEXT NOT NULL DEFAULT '',
                HistoryHooks TEXT NOT NULL DEFAULT '',
                CanonicalEventIds TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Draft',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_StudentSchoolYears_Npc
            ON StudentSchoolYears(NpcId, GradeLevel);

            CREATE TABLE IF NOT EXISTS StudentCourseEnrollments(
                EnrollmentId TEXT PRIMARY KEY,
                SchoolYearRecordId TEXT NOT NULL,
                NpcId INTEGER NOT NULL,
                CourseCode TEXT NOT NULL,
                TeacherNpcId INTEGER NULL,
                TeacherStaffSlotId TEXT NOT NULL DEFAULT '',
                FinalGrade TEXT NOT NULL DEFAULT '',
                NumericGrade REAL NULL,
                Semester TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                HistoryHooks TEXT NOT NULL DEFAULT '',
                CanonicalEventIds TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS IX_StudentCourseEnrollments_Npc
            ON StudentCourseEnrollments(NpcId, CourseCode);

            CREATE INDEX IF NOT EXISTS IX_StudentCourseEnrollments_Teacher
            ON StudentCourseEnrollments(TeacherNpcId, CourseCode);
            """);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

