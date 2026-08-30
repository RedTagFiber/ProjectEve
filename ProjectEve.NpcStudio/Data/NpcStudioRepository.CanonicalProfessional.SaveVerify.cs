using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    public async Task<string> SaveAndVerifyCanonicalProfessionalProfileAsync(
        CanonicalProfessionalProfile item)
    {
        await SaveCanonicalProfessionalProfileAsync(item);

        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT IFNULL(Notes, '')
            FROM NpcProfessionalProfiles
            WHERE NpcId = $npcId;
            """;

        cmd.Parameters.AddWithValue("$npcId", item.NpcId);

        var savedNotes = Convert.ToString(cmd.ExecuteScalar()) ?? "";

        if (!string.Equals(
            savedNotes,
            item.Notes ?? "",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SQLite write verification failed. " +
                $"Attempted Notes='{item.Notes}', " +
                $"but database read-back returned Notes='{savedNotes}'.");
        }

        return savedNotes;
    }
}
