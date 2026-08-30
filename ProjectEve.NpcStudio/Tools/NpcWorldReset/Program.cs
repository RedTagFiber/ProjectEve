using Microsoft.Data.Sqlite;

const string DbPath = @"D:\ProjectEveData\Database\project_eve.db";
const string NpcRoot = @"D:\ProjectEveData\NPC";
const string ComfyNpcRoot = @"D:\ProjectEveData\Comfy\Temp\ProjectEve";
const string BackupRoot = @"D:\ProjectEveData\Backups";

Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine("============================================================");
Console.WriteLine(" PROJECT EVE - PURGE ALL NPCs / CLEAN RESTART");
Console.WriteLine("============================================================");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine("This will permanently remove ALL NPC data and NPC files.");
Console.WriteLine("World/location data and the World Builder code are preserved.");
Console.WriteLine();
Console.WriteLine($"Database: {DbPath}");
Console.WriteLine($"NPC files: {NpcRoot}");
Console.WriteLine($"Comfy NPC temp: {ComfyNpcRoot}\\NPC_*");
Console.WriteLine();

if (!File.Exists(DbPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("STOP: project_eve.db was not found. Nothing was changed.");
    Console.ResetColor();
    return 2;
}

// Warning 1
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("WARNING 1 OF 3: Every NPC record will be removed.");
Console.ResetColor();
Console.Write("Type DELETE to continue: ");
if (!string.Equals(Console.ReadLine()?.Trim(), "DELETE", StringComparison.Ordinal))
{
    Console.WriteLine("Cancelled. Nothing changed.");
    return 0;
}

// Warning 2
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine();
Console.WriteLine("WARNING 2 OF 3: NPC images, voice files, prompts, relationships, memories,");
Console.WriteLine("traits, history, media records, and Comfy NPC temp outputs will be removed.");
Console.ResetColor();
Console.Write("Type NPC to continue: ");
if (!string.Equals(Console.ReadLine()?.Trim(), "NPC", StringComparison.Ordinal))
{
    Console.WriteLine("Cancelled. Nothing changed.");
    return 0;
}

// Warning 3
Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine();
Console.WriteLine("FINAL WARNING 3 OF 3: This is the clean restart.");
Console.WriteLine("A database backup will be created first, but the NPC asset folders themselves will be deleted.");
Console.ResetColor();
Console.Write("Type PURGE ALL NPCS exactly: ");
if (!string.Equals(Console.ReadLine()?.Trim(), "PURGE ALL NPCS", StringComparison.Ordinal))
{
    Console.WriteLine("Cancelled. Nothing changed.");
    return 0;
}

var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
var backupDir = Path.Combine(BackupRoot, $"NpcReset_{stamp}");
Directory.CreateDirectory(backupDir);
var dbBackup = Path.Combine(backupDir, "project_eve_before_npc_reset.db");
File.Copy(DbPath, dbBackup, overwrite: false);
Console.WriteLine();
Console.WriteLine($"Database backup created: {dbBackup}");

var npcTables = new[]
{
    "Appearance",
    "BrainState",
    "ConversationLog",
    "History",
    "JobProfile",
    "Memories",
    "MoneyProfile",
    "NameReactions",
    "NpcAppearanceProfiles",
    "NpcBodyProfile",
    "NpcBuildRevisions",
    "NpcCognitionProfile",
    "NpcCurrentLocation",
    "NpcImageGenerations",
    "NpcLocationVisits",
    "NpcMediaAssets",
    "NpcPromptGenerations",
    "NpcRelationships",
    "NpcStudioIdeas",
    "NpcTraitValues",
    "NpcVoicePresets",
    "NpcVoiceProfiles",
    "Relationships",
    "TraitControl",
    "Traits",
    "history_aliases",
    "history_beats",
    "history_event_tags",
    "history_facts",
    "history_participants",
    "history_peaks",
    "history_events",
    "Characters"
};

try
{
    var cs = new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();
    await using var connection = new SqliteConnection(cs);
    await connection.OpenAsync();

    await using (var pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA foreign_keys = OFF;";
        await pragma.ExecuteNonQueryAsync();
    }

    await using var tx = await connection.BeginTransactionAsync();
    try
    {
        foreach (var table in npcTables)
        {
            await using var exists = connection.CreateCommand();
            exists.Transaction = (SqliteTransaction)tx;
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
            exists.Parameters.AddWithValue("$name", table);
            var found = Convert.ToInt32(await exists.ExecuteScalarAsync()) > 0;
            if (!found) continue;

            await using var delete = connection.CreateCommand();
            delete.Transaction = (SqliteTransaction)tx;
            delete.CommandText = $"DELETE FROM [{table.Replace("]", "]]", StringComparison.Ordinal)}];";
            await delete.ExecuteNonQueryAsync();
        }

        // Reset AUTOINCREMENT counters where they exist.
        await using (var seq = connection.CreateCommand())
        {
            seq.Transaction = (SqliteTransaction)tx;
            seq.CommandText = "DELETE FROM sqlite_sequence WHERE name IN ('Characters','ConversationLog','History','Memories','NameReactions','NpcBuildRevisions','NpcImageGenerations','NpcLocationVisits','NpcMediaAssets','NpcPromptGenerations','NpcRelationships','NpcStudioIdeas','NpcTraitValues','NpcVoicePresets','Relationships');";
            try { await seq.ExecuteNonQueryAsync(); } catch { /* sqlite_sequence may not exist */ }
        }

        // Seed exactly one clean Core NPC: Eve Sinclair.
        await using (var seed = connection.CreateCommand())
        {
            seed.Transaction = (SqliteTransaction)tx;
            seed.CommandText = @"
INSERT INTO Characters
(
    Id, NpcKey, FolderName, FolderPath, Name, Age, Gender, Occupation, Location,
    Status, Tier, Goal, Need, Fear, Want, PersonalityContext, Hometown, Address,
    BackstoryShort, BackstoryLong, PersonalitySummary, SpeakingStyle,
    CurrentReferenceImagePath, CurrentProfileImagePath, CurrentContactImagePath,
    CurrentVoiceReferencePath, CurrentVoicePresetId, CreatedRealAt, UpdatedRealAt,
    Nickname, DisplayName, FirstName, LastName
)
VALUES
(
    1, 'NPC_000001', '000001_Eve_Sinclair', 'D:\ProjectEveData\NPC\000001_Eve_Sinclair',
    'Eve Sinclair', 25, 'Female', 'Sinclair Coffee - Manager', 'Bellefontaine, Ohio',
    'Core', 1,
    'Keep Sinclair Coffee and her life from falling apart.',
    'To feel known without feeling trapped.',
    'Being abandoned or becoming invisible.',
    'A life that feels real and chosen.',
    'The Heart of Bellefontaine.',
    'Bellefontaine, Ohio', '', '', '', '', '', '', '', '', '', '',
    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP,
    'Eve', 'Eve Sinclair', 'Eve', 'Sinclair'
);";
            await seed.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }

    await using (var pragmaOn = connection.CreateCommand())
    {
        pragmaOn.CommandText = "PRAGMA foreign_keys = ON;";
        await pragmaOn.ExecuteNonQueryAsync();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine("DATABASE RESET FAILED.");
    Console.WriteLine(ex.Message);
    Console.WriteLine($"Your backup is here: {dbBackup}");
    Console.ResetColor();
    return 3;
}

// Delete NPC asset folder only after the database transaction succeeded.
try
{
    if (Directory.Exists(NpcRoot))
        Directory.Delete(NpcRoot, recursive: true);
    Directory.CreateDirectory(NpcRoot);

    // Clear old Comfy temp outputs for every previous NPC, but preserve other Comfy data.
    if (Directory.Exists(ComfyNpcRoot))
    {
        foreach (var dir in Directory.EnumerateDirectories(ComfyNpcRoot, "NPC_*", SearchOption.TopDirectoryOnly))
            Directory.Delete(dir, recursive: true);
    }

    // Create Eve's clean case-file folder structure.
    var eveRoot = Path.Combine(NpcRoot, "000001_Eve_Sinclair");
    string[] dirs =
    {
        "dossier", "images", "voice", "relationships", "traits", "prompts", "comfy", "notes", "revisions", "exports", "temp",
        @"images\reference", @"images\profile", @"images\contact", @"images\in_person", @"images\social", @"images\rejected", @"images\thumbnails",
        @"voice\reference", @"voice\samples", @"voice\generated", @"voice\presets", @"voice\scripts",
        @"comfy\workflows", @"comfy\requests", @"comfy\outputs", @"comfy\metadata"
    };
    foreach (var dir in dirs)
        Directory.CreateDirectory(Path.Combine(eveRoot, dir));
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine();
    Console.WriteLine("Database reset succeeded, but one or more NPC folders could not be removed.");
    Console.WriteLine(ex.Message);
    Console.ResetColor();
    return 4;
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine(" CLEAN RESTART COMPLETE");
Console.WriteLine("============================================================");
Console.ResetColor();
Console.WriteLine("NPC count: 1");
Console.WriteLine("NPC #1: Eve Sinclair");
Console.WriteLine($"Fresh NPC folder: {Path.Combine(NpcRoot, "000001_Eve_Sinclair")}");
Console.WriteLine($"Safety backup: {dbBackup}");
Console.WriteLine();
Console.WriteLine("Next: open NPC Studio and build Eve completely before adding her brother, mother, and father.");
return 0;
