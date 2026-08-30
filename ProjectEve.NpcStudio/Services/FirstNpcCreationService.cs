using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class FirstNpcCreationService
{
    private readonly NpcStudioOptions _options;

    public FirstNpcCreationService(NpcStudioOptions options)
    {
        _options = options;
    }

    public int Create(FirstNpcCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var first = (request.FirstName ?? "").Trim();
        var last = (request.LastName ?? "").Trim();
        var gender = (request.Gender ?? "").Trim();
        var hometown = (request.Hometown ?? "").Trim();
        var occupation = (request.Occupation ?? "").Trim();

        if (string.IsNullOrWhiteSpace(first))
            throw new InvalidOperationException("First name is required.");

        if (string.IsNullOrWhiteSpace(last))
            throw new InvalidOperationException("Last name is required.");

        if (request.Age < 1 || request.Age > 110)
            throw new InvalidOperationException("Age must be between 1 and 110.");

        if (string.IsNullOrWhiteSpace(gender))
            throw new InvalidOperationException("Gender is required.");

        if (string.IsNullOrWhiteSpace(hometown))
            throw new InvalidOperationException("Hometown is required.");

        if (string.IsNullOrWhiteSpace(occupation))
            throw new InvalidOperationException(
                "Occupation is required. Use Student, Retired <job>, Homemaker, etc. when appropriate.");

        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        using (var count = conn.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM Characters;";
            var existing = Convert.ToInt32(count.ExecuteScalar() ?? 0);
            if (existing > 0)
                throw new InvalidOperationException(
                    "The First NPC screen is only available when the NPC database is empty.");
        }

        var id = 1;
        var fullName = $"{first} {last}".Trim();
        var tier = Math.Clamp(request.Tier, 1, 5);
        var lifeStage = LifeStage(request.Age);
        var npcKey = $"npc-{id:D4}-{Slug(first)}-{Slug(last)}";
        var folderName = $"{id:D4}_{SafeFolder(first)}_{SafeFolder(last)}";
        var folderPath = Path.Combine(_options.NpcRoot, folderName);

        Directory.CreateDirectory(_options.NpcRoot);
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(Path.Combine(folderPath, "media"));
        Directory.CreateDirectory(Path.Combine(folderPath, "notes"));

        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO Characters
                (
                    Id, WorldId, NpcKey, FolderName, FolderPath,
                    Name, DisplayName, FirstName, LastName,
                    Age, Gender, Occupation, Employer,
                    Location, Hometown, Status, Tier,
                    LifeStage, IsDeceased,
                    PersonalityContext, Goal, Need, Fear, Want,
                    CreatedRealAt, UpdatedRealAt
                )
                VALUES
                (
                    $id, 'smalltown', $key, $folderName, $folderPath,
                    $name, $name, $first, $last,
                    $age, $gender, $occupation, '',
                    $hometown, $hometown, 'Core', $tier,
                    $lifeStage, 0,
                    '', '', '', '', '',
                    CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );
                """;

            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$key", npcKey);
            cmd.Parameters.AddWithValue("$folderName", folderName);
            cmd.Parameters.AddWithValue("$folderPath", folderPath);
            cmd.Parameters.AddWithValue("$name", fullName);
            cmd.Parameters.AddWithValue("$first", first);
            cmd.Parameters.AddWithValue("$last", last);
            cmd.Parameters.AddWithValue("$age", request.Age);
            cmd.Parameters.AddWithValue("$gender", gender);
            cmd.Parameters.AddWithValue("$occupation", occupation);
            cmd.Parameters.AddWithValue("$hometown", hometown);
            cmd.Parameters.AddWithValue("$tier", tier);
            cmd.Parameters.AddWithValue("$lifeStage", lifeStage);
            cmd.ExecuteNonQuery();
        }

        using (var name = conn.CreateCommand())
        {
            name.Transaction = tx;
            name.CommandText = """
                INSERT INTO NpcNameProfiles
                (
                    NpcId, FirstName, MiddleName, CurrentLastName,
                    BirthLastName, PreferredName, Suffix, UpdatedRealAt
                )
                VALUES
                (
                    $id, $first, '', $last,
                    $last, $first, '', CURRENT_TIMESTAMP
                );
                """;
            name.Parameters.AddWithValue("$id", id);
            name.Parameters.AddWithValue("$first", first);
            name.Parameters.AddWithValue("$last", last);
            name.ExecuteNonQuery();
        }

        using (var physical = conn.CreateCommand())
        {
            physical.Transaction = tx;
            physical.CommandText = """
                INSERT OR IGNORE INTO NpcPhysicalProfiles
                (NpcId, Notes, UpdatedRealAt)
                VALUES
                ($id, 'Created by First NPC screen. Physical profile pending NPC Studio profile build.', CURRENT_TIMESTAMP);
                """;
            physical.Parameters.AddWithValue("$id", id);
            physical.ExecuteNonQuery();
        }

        using (var appearance = conn.CreateCommand())
        {
            appearance.Transaction = tx;
            appearance.CommandText = """
                INSERT OR IGNORE INTO NpcAppearanceProfiles
                (NpcId, AppearanceStatus, Notes, UpdatedRealAt)
                VALUES
                ($id, 'NotStarted', 'Created by First NPC screen.', CURRENT_TIMESTAMP);
                """;
            appearance.Parameters.AddWithValue("$id", id);
            appearance.ExecuteNonQuery();
        }

        using (var voice = conn.CreateCommand())
        {
            voice.Transaction = tx;
            voice.CommandText = """
                INSERT OR IGNORE INTO NpcVoiceProfiles
                (NpcId, VoiceStatus, Notes, UpdatedRealAt)
                VALUES
                ($id, 'NotStarted', 'Created by First NPC screen.', CURRENT_TIMESTAMP);
                """;
            voice.Parameters.AddWithValue("$id", id);
            voice.ExecuteNonQuery();
        }

        using (var provenance = conn.CreateCommand())
        {
            provenance.Transaction = tx;
            provenance.CommandText = """
                INSERT INTO NpcCreationProvenance
                (
                    NpcId, CreationSourceType, CreatedFromNpcId,
                    CreatedFromNpcName, OriginalRole, CreationBatchId,
                    BuildStatus, CreatedRealAt, UpdatedRealAt
                )
                VALUES
                (
                    $id, 'NpcStudioFirstNpc', NULL,
                    '', 'Root NPC', 'FIRST-NPC',
                    'IdentityCreated', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                );
                """;
            provenance.Parameters.AddWithValue("$id", id);
            provenance.ExecuteNonQuery();
        }

        using (var completeness = conn.CreateCommand())
        {
            completeness.Transaction = tx;
            completeness.CommandText = """
                INSERT OR REPLACE INTO NpcFamilyBuildCompleteness
                (
                    NpcId,
                    IdentityStatus, AppearanceStatus, TraitsStatus,
                    CurrentLifeStatus, EducationCareerStatus, JobStatus,
                    FinanceStatus, PhoneStatus, VehicleStatus, HomeStatus,
                    FamilyStructureStatus, NonHistoryPercent, HistoryStatus,
                    Notes, UpdatedRealAt
                )
                VALUES
                (
                    $id,
                    'Started', 'NotStarted', 'NotStarted',
                    'Started', 'NotStarted', 'Started',
                    'NotStarted', 'NotStarted', 'NotStarted', 'NotStarted',
                    'NotStarted', 10, 'NOT_INCLUDED',
                    'Root NPC created by NPC Studio. Full non-history build still required.',
                    CURRENT_TIMESTAMP
                );
                """;
            completeness.Parameters.AddWithValue("$id", id);
            completeness.ExecuteNonQuery();
        }

        tx.Commit();
        return id;
    }

    private static string LifeStage(int age) =>
        age <= 12 ? "Child" :
        age <= 17 ? "Teenager" :
        age <= 29 ? "Young Adult" :
        age <= 49 ? "Adult" :
        age <= 64 ? "Older Adult" :
        "Elder";

    private static string Slug(string value) =>
        new string((value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

    private static string SafeFolder(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string((value ?? "").Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }
}

public sealed class FirstNpcCreateRequest
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int Age { get; set; } = 25;
    public string Gender { get; set; } = "Female";
    public int Tier { get; set; } = 1;
    public string Hometown { get; set; } = "Bellefontaine, Ohio";
    public string Occupation { get; set; } = "";
}
