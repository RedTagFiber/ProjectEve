using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

public static class ProjectEveSupportNpcScaffoldPlanner
{
    private sealed record SlotTemplate(string Category, string RoleType, int Count, string InstitutionType = "", string Subject = "", int? GradeMin = null, int? GradeMax = null, double ReuseWeight = 1.0, string Notes = "");

    public static void CreateOrRefreshPlan(int targetCount = 400)
    {
        ProjectEveHistoryGenerationFoundationSchema.Ensure();
        using var conn = new SqliteConnection($"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();

        using (var cleanup = conn.CreateCommand())
        {
            cleanup.CommandText = "DELETE FROM SupportNpcScaffoldSlots WHERE AssignedNpcId IS NULL AND IsFamilySlot=0 AND Status='Open';";
            cleanup.ExecuteNonQuery();
        }

        var planned = 0;
        foreach (var t in BuildTemplates())
        {
            for (var i = 1; i <= t.Count && planned < targetCount; i++)
            {
                planned++;
                InsertSlot(conn, planned, t);
            }
        }

        while (planned < targetCount)
        {
            planned++;
            InsertSlot(conn, planned, new SlotTemplate("Town", "General town resident", 1, ReuseWeight: 0.75, Notes: "Flexible non-family support NPC."));
        }

        Console.WriteLine($"Support NPC scaffold plan ready: {planned} open non-family slots.");
        Console.WriteLine("No Characters, family links, or TRUE HISTORY were created.");
        PrintSummary(conn);
    }

    public static void PrintSummary()
    {
        ProjectEveHistoryGenerationFoundationSchema.Ensure();
        using var conn = new SqliteConnection($"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();
        PrintSummary(conn);
    }

    private static List<SlotTemplate> BuildTemplates() =>
    [
        new("School", "Elementary classroom teacher", 30, "School", "Elementary general", 0, 6, 2.2, "Reusable K-6 teacher pool."),
        new("School", "Middle/high English teacher", 10, "School", "English", 7, 12, 2.0),
        new("School", "Middle/high Math teacher", 10, "School", "Math", 7, 12, 2.0),
        new("School", "Middle/high Science teacher", 10, "School", "Science", 7, 12, 2.0),
        new("School", "Middle/high Social Studies teacher", 10, "School", "Social Studies", 7, 12, 2.0),
        new("School", "Elective teacher", 12, "School", "Art / Music / Shop / Technology / Language", 6, 12, 1.7),
        new("School", "Coach", 12, "School", "Athletics", 6, 12, 1.8),
        new("School", "Guidance counselor", 6, "School", "Counseling", 6, 12, 2.1),
        new("School", "Principal / administrator", 6, "School", "Administration", 0, 12, 2.4),
        new("School", "School office / support staff", 10, "School", "Support", 0, 12, 1.5),
        new("School", "Bus driver", 8, "School", "Transportation", 0, 12, 1.8),
        new("Community", "Pastor / clergy", 8, "Church", ReuseWeight: 2.3),
        new("Community", "Youth group leader", 12, "Church", "Youth", ReuseWeight: 2.0),
        new("Community", "Sunday school / children's group leader", 10, "Church", "Children", ReuseWeight: 1.8),
        new("Community", "Community mentor / volunteer", 12, "Community", ReuseWeight: 1.6),
        new("Community", "Librarian", 8, "Library", ReuseWeight: 2.0),
        new("Health", "Family doctor / physician", 8, "Clinic", "Primary care", ReuseWeight: 2.5),
        new("Health", "Nurse", 14, "Clinic", "Nursing", ReuseWeight: 1.8),
        new("Health", "Dentist / dental staff", 8, "Dental", ReuseWeight: 1.6),
        new("Health", "Pharmacist", 6, "Pharmacy", ReuseWeight: 2.0),
        new("Public Safety", "Police officer", 12, "Police", ReuseWeight: 1.7),
        new("Public Safety", "Firefighter / EMT", 12, "FireEMS", ReuseWeight: 1.7),
        new("Business", "Restaurant / diner worker", 18, "Business", "Food service", ReuseWeight: 1.5),
        new("Business", "Bartender", 10, "Business", "Bar", ReuseWeight: 2.2),
        new("Business", "Mechanic", 10, "Business", "Automotive", ReuseWeight: 1.8),
        new("Business", "Hairdresser / barber", 10, "Business", "Personal care", ReuseWeight: 2.0),
        new("Business", "Retail worker / cashier", 18, "Business", "Retail", ReuseWeight: 1.3),
        new("Business", "Local business owner", 12, "Business", "Owner", ReuseWeight: 2.0),
        new("Housing", "Landlord / property manager", 10, "Property", ReuseWeight: 1.7),
        new("Town", "Postal / delivery worker", 8, "TownService", ReuseWeight: 1.6),
        new("Town", "City / utility worker", 10, "TownService", ReuseWeight: 1.4),
        new("Social", "Childhood peer / classmate", 30, "School", "Peer", 0, 12, 1.2),
        new("Social", "Teen peer / classmate", 30, "School", "Peer", 7, 12, 1.3),
        new("Social", "Young-adult peer / coworker", 24, "Town", "Peer", ReuseWeight: 1.3)
    ];

    private static void InsertSlot(SqliteConnection conn, int ordinal, SlotTemplate t)
    {
        var key = $"support-{Slug(t.Category)}-{Slug(t.RoleType)}-{ordinal:D4}";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT OR IGNORE INTO SupportNpcScaffoldSlots
        (SlotKey,Category,RoleType,InstitutionType,SubjectOrSpecialty,GradeMin,GradeMax,ReuseWeight,IsFamilySlot,Status,Notes)
        VALUES($key,$category,$role,$institution,$subject,$gradeMin,$gradeMax,$reuse,0,'Open',$notes);
        """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$category", t.Category);
        cmd.Parameters.AddWithValue("$role", t.RoleType);
        cmd.Parameters.AddWithValue("$institution", t.InstitutionType);
        cmd.Parameters.AddWithValue("$subject", t.Subject);
        cmd.Parameters.AddWithValue("$gradeMin", (object?)t.GradeMin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gradeMax", (object?)t.GradeMax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$reuse", t.ReuseWeight);
        cmd.Parameters.AddWithValue("$notes", t.Notes);
        cmd.ExecuteNonQuery();
    }

    private static void PrintSummary(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Category,COUNT(*) FROM SupportNpcScaffoldSlots WHERE IsFamilySlot=0 GROUP BY Category ORDER BY Category;";
        using var r = cmd.ExecuteReader();
        Console.WriteLine("Scaffold by category:");
        while (r.Read()) Console.WriteLine($"  {r.GetString(0),-18} {r.GetInt32(1),4}");
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var s = new string(chars);
        while (s.Contains("--", StringComparison.Ordinal)) s = s.Replace("--", "-", StringComparison.Ordinal);
        return s.Trim('-');
    }
}
