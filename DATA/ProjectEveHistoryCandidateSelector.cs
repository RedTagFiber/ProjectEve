using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

public static class ProjectEveHistoryCandidateSelector
{
    public sealed record Candidate(int SlotId,string SlotKey,string Category,string RoleType,string SubjectOrSpecialty,int? GradeMin,int? GradeMax,int? ActiveStartYear,int? ActiveEndYear,double ReuseWeight,int? AssignedNpcId);

    public static IReadOnlyList<Candidate> Find(string category,int? year=null,int? grade=null,int limit=25)
    {
        ProjectEveHistoryGenerationFoundationSchema.Ensure();
        using var conn = new SqliteConnection($"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        var filters = new List<string>{"IsFamilySlot=0","Status<>'Retired'"};
        if(!string.IsNullOrWhiteSpace(category)){filters.Add("Category=$category");cmd.Parameters.AddWithValue("$category",category.Trim());}
        if(year is not null){filters.Add("(ActiveStartYear IS NULL OR ActiveStartYear<=$year)");filters.Add("(ActiveEndYear IS NULL OR ActiveEndYear>=$year)");cmd.Parameters.AddWithValue("$year",year.Value);}
        if(grade is not null){filters.Add("(GradeMin IS NULL OR GradeMin<=$grade)");filters.Add("(GradeMax IS NULL OR GradeMax>=$grade)");cmd.Parameters.AddWithValue("$grade",grade.Value);}
        cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,200));
        cmd.CommandText=$"""
        SELECT SlotId,SlotKey,Category,RoleType,SubjectOrSpecialty,GradeMin,GradeMax,ActiveStartYear,ActiveEndYear,ReuseWeight,AssignedNpcId
        FROM SupportNpcScaffoldSlots WHERE {string.Join(" AND ",filters)}
        ORDER BY CASE WHEN AssignedNpcId IS NOT NULL THEN 0 ELSE 1 END,ReuseWeight DESC,SlotId LIMIT $limit;
        """;
        var list=new List<Candidate>();
        using var r=cmd.ExecuteReader();
        while(r.Read()) list.Add(new Candidate(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetInt32(5),r.IsDBNull(6)?null:r.GetInt32(6),r.IsDBNull(7)?null:r.GetInt32(7),r.IsDBNull(8)?null:r.GetInt32(8),r.GetDouble(9),r.IsDBNull(10)?null:r.GetInt32(10)));
        return list;
    }
}
