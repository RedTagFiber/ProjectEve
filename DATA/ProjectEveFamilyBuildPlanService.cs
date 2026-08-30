using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

public static class ProjectEveFamilyBuildPlanService
{
    public static void SetEveStarterPlan(int motherSiblings=0,int fatherSiblings=0,int brothers=0,int sisters=0,string siblingBirthPattern="Auto")
        => SetPlan(1,motherSiblings,fatherSiblings,brothers,sisters,siblingBirthPattern);

    public static void SetPlan(int rootNpcId,int motherSiblings,int fatherSiblings,int brothers,int sisters,string siblingBirthPattern)
    {
        ProjectEveHistoryGenerationFoundationSchema.Ensure();
        using var conn = new SqliteConnection($"Data Source={ProjectEveDatabaseSetup.MainDatabasePath}");
        conn.Open();

        using var exists = conn.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM Characters WHERE Id=$id;";
        exists.Parameters.AddWithValue("$id", rootNpcId);
        if (Convert.ToInt32(exists.ExecuteScalar()) == 0)
            throw new InvalidOperationException($"NPC {rootNpcId} does not exist.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO NpcFamilyBuildPlans
        (RootNpcId,CreateMother,MotherSiblingCount,CreateFather,FatherSiblingCount,BrotherCount,SisterCount,SiblingBirthPattern,
         CreateMaternalGrandmother,CreateMaternalGrandfather,CreatePaternalGrandmother,CreatePaternalGrandfather,
         GenerateAuntsUncles,GenerateCousins,GenerateSpousesInLaws,ReuseExistingTownNpcForSpouses,ExtendedFamilyDepth,
         GenerateSharedFamilyHistory,GenerateIndividualMemories,GenerateFullNpcProfiles,Status,UpdatedRealAt)
        VALUES($id,1,$ms,1,$fs,$b,$s,$pattern,1,1,1,1,1,1,1,1,'Deep',1,1,1,'Draft',CURRENT_TIMESTAMP)
        ON CONFLICT(RootNpcId) DO UPDATE SET
          MotherSiblingCount=excluded.MotherSiblingCount,FatherSiblingCount=excluded.FatherSiblingCount,
          BrotherCount=excluded.BrotherCount,SisterCount=excluded.SisterCount,SiblingBirthPattern=excluded.SiblingBirthPattern,
          Status='Draft',UpdatedRealAt=CURRENT_TIMESTAMP;
        """;
        cmd.Parameters.AddWithValue("$id", rootNpcId);
        cmd.Parameters.AddWithValue("$ms", Math.Max(0,motherSiblings));
        cmd.Parameters.AddWithValue("$fs", Math.Max(0,fatherSiblings));
        cmd.Parameters.AddWithValue("$b", Math.Max(0,brothers));
        cmd.Parameters.AddWithValue("$s", Math.Max(0,sisters));
        cmd.Parameters.AddWithValue("$pattern", string.IsNullOrWhiteSpace(siblingBirthPattern) ? "Auto" : siblingBirthPattern.Trim());
        cmd.ExecuteNonQuery();
        Console.WriteLine($"Family build plan saved for NPC {rootNpcId}. AI generation has NOT run yet.");
    }
}
