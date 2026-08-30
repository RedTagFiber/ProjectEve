using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Preview-only relationship candidate engine.
///
/// This service NEVER writes relationship or NPC data. It searches existing NPCs,
/// applies hard eligibility rules, scores compatibility from canonical/scaffold
/// personal data and traits, and returns a build preview.
///
/// Tier 5 NPCs may receive suggestions for later deepening, but this service does
/// not mutate them.
/// </summary>
public sealed class RelationshipCandidateService
{
    private readonly NpcStudioOptions _options;

    public RelationshipCandidateService(NpcStudioOptions options)
    {
        _options = options;
    }

    public List<RelationshipCandidate> FindRomanceCandidates(int rootNpcId, int limit = 30)
    {
        using var main = new SqliteConnection("Data Source=" + _options.MainDbPath);
        main.Open();

        using var rel = new SqliteConnection("Data Source=" + _options.RelationshipsDbPath);
        rel.Open();

        var root = LoadNpc(main, rootNpcId);
        if (root is null)
            return new();

        var rootTraits = LoadTraits(main, rootNpcId);
        var rootWords = BuildPersonalWordSet(root);

        var result = new List<RelationshipCandidate>();

        using var cmd = main.CreateCommand();
        cmd.CommandText = """
        SELECT
            Id,
            IFNULL(Name,''),
            IFNULL(Age,0),
            IFNULL(Gender,''),
            IFNULL(Tier,5),
            IFNULL(Occupation,''),
            IFNULL(Location,''),
            IFNULL(PersonalityContext,''),
            IFNULL(PersonalitySummary,''),
            IFNULL(Goal,''),
            IFNULL(Need,''),
            IFNULL(Fear,''),
            IFNULL(Want,'')
        FROM Characters
        WHERE Id <> $root
          AND IFNULL(Status,'') <> 'Deleted'
        ORDER BY Tier, Id;
        """;
        cmd.Parameters.AddWithValue("$root", rootNpcId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var other = ReadNpc(r);
            var candidate = ScoreRomance(main, rel, root, other, rootTraits, rootWords);
            result.Add(candidate);
        }

        return result
            .OrderByDescending(x => x.Eligible)
            .ThenByDescending(x => x.CompatibilityScore)
            .ThenByDescending(x => x.CompatibilityConfidence)
            .ThenBy(x => x.Name)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
    }

    public RelationshipBuildPreview PreviewMarriage(int rootNpcId, int candidateNpcId)
    {
        using var main = new SqliteConnection("Data Source=" + _options.MainDbPath);
        main.Open();

        using var rel = new SqliteConnection("Data Source=" + _options.RelationshipsDbPath);
        rel.Open();

        var root = LoadNpc(main, rootNpcId);
        var other = LoadNpc(main, candidateNpcId);

        if (root is null || other is null)
            return new RelationshipBuildPreview
            {
                RootNpcId = rootNpcId,
                CandidateNpcId = candidateNpcId,
                Warnings = { "One or both NPCs could not be loaded." }
            };

        var candidate = FindRomanceCandidates(rootNpcId, 100)
            .FirstOrDefault(x => x.NpcId == candidateNpcId);

        var rootRole = SpouseRole(root.Gender);
        var otherRole = SpouseRole(other.Gender);

        var preview = new RelationshipBuildPreview
        {
            RootNpcId = rootNpcId,
            CandidateNpcId = candidateNpcId,
            RootName = root.Name,
            CandidateName = other.Name,
            ProposedRelationship = otherRole,
            ReverseRelationship = rootRole,
            CompatibilityScore = candidate?.CompatibilityScore ?? 0,
            CompatibilityConfidence = candidate?.CompatibilityConfidence ?? 0
        };

        preview.Writes.Add($"{root.Name} → {other.Name}: {otherRole}");
        preview.Writes.Add($"{other.Name} → {root.Name}: {rootRole}");
        preview.Writes.Add("Both links will use the canonical relationship database.");

        if (candidate is not null)
        {
            foreach (var c in candidate.Conflicts)
                preview.Warnings.Add(c);

            if (candidate.CanDeepenToFit)
                preview.ProposedTier5Updates.AddRange(candidate.DeepeningSuggestions);
        }

        if (IsActivelyMarried(rel, root.Id, candidateNpcId))
            preview.Warnings.Add($"{root.Name} already has an active spouse.");

        if (IsActivelyMarried(rel, other.Id, rootNpcId))
            preview.Warnings.Add($"{other.Name} already has an active spouse.");

        return preview;
    }

    private static RelationshipCandidate ScoreRomance(
        SqliteConnection main,
        SqliteConnection rel,
        NpcData root,
        NpcData other,
        Dictionary<string,int> rootTraits,
        HashSet<string> rootWords)
    {
        var c = new RelationshipCandidate
        {
            NpcId = other.Id,
            Name = other.Name,
            Age = other.Age,
            Gender = other.Gender,
            Tier = other.Tier,
            Occupation = other.Occupation,
            Location = other.Location,
            Eligible = true,
            Availability = "Available"
        };

        if (root.Age < 18 || other.Age < 18)
        {
            c.Eligible = false;
            c.Availability = "Blocked";
            c.Conflicts.Add("Romance builder only considers adult NPCs.");
            return c;
        }

        if (HasDirectFamilyLink(rel, root.Id, other.Id))
        {
            c.Eligible = false;
            c.Availability = "Related — blocked";
            c.Conflicts.Add("Direct canonical family relationship exists.");
            return c;
        }

        if (IsActivelyMarried(rel, other.Id, root.Id))
        {
            c.Eligible = false;
            c.Availability = "Married — unavailable";
            c.Conflicts.Add("Candidate already has an active spouse.");
        }

        var scoreParts = new List<int>();
        var confidenceParts = new List<int>();

        // Age/life-stage fit.
        var gap = Math.Abs(root.Age - other.Age);
        var ageScore = gap switch
        {
            <= 3 => 95,
            <= 6 => 88,
            <= 10 => 78,
            <= 15 => 62,
            _ => 45
        };
        scoreParts.Add(ageScore);
        confidenceParts.Add(100);

        if (gap <= 6) c.StrongMatches.Add($"Similar life stage (age gap {gap}).");
        else c.Differences.Add($"Age gap is {gap} years.");

        // Location/social plausibility.
        if (!string.IsNullOrWhiteSpace(root.Location) &&
            !string.IsNullOrWhiteSpace(other.Location) &&
            root.Location.Equals(other.Location, StringComparison.OrdinalIgnoreCase))
        {
            scoreParts.Add(92);
            confidenceParts.Add(90);
            c.StrongMatches.Add("Same current location.");
        }
        else
        {
            scoreParts.Add(58);
            confidenceParts.Add(60);
            c.Differences.Add("No exact current-location match.");
        }

        // Trait compatibility from traits that both NPCs actually have.
        var otherTraits = LoadTraits(main, other.Id);
        var common = rootTraits.Keys.Intersect(otherTraits.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        if (common.Count > 0)
        {
            var traitScores = common
                .Select(k => 100 - Math.Abs(rootTraits[k] - otherTraits[k]))
                .ToList();

            var traitScore = (int)Math.Round(traitScores.Average());
            scoreParts.Add(traitScore);
            confidenceParts.Add(Math.Min(100, 35 + common.Count * 10));

            if (traitScore >= 75)
                c.StrongMatches.Add($"{common.Count} shared saved traits are broadly compatible.");
            else
                c.Differences.Add($"{common.Count} shared traits show meaningful personality differences.");
        }
        else
        {
            confidenceParts.Add(20);
            if (other.Tier >= 5)
                c.DeepeningSuggestions.Add("Fill missing Tier 5 trait values using the established root NPC and candidate direction as context.");
        }

        // Personal-data similarity. This is deliberately soft, not clone-making.
        var otherWords = BuildPersonalWordSet(other);
        if (rootWords.Count > 0 && otherWords.Count > 0)
        {
            var overlap = rootWords.Intersect(otherWords, StringComparer.OrdinalIgnoreCase).Count();
            var union = rootWords.Union(otherWords, StringComparer.OrdinalIgnoreCase).Count();
            var jaccard = union == 0 ? 0 : (double)overlap / union;
            var personalScore = 55 + (int)Math.Round(jaccard * 40);
            scoreParts.Add(Math.Clamp(personalScore, 45, 95));
            confidenceParts.Add(Math.Min(90, 30 + Math.Min(rootWords.Count, otherWords.Count)));

            if (jaccard >= 0.12)
                c.StrongMatches.Add("Saved personal/personality language shows some compatible themes.");
        }
        else if (other.Tier >= 5)
        {
            confidenceParts.Add(15);
            c.DeepeningSuggestions.Add("Fill blank personality/personal fields without changing existing hard facts.");
        }

        // Occupation context adds plausibility but is not treated as destiny.
        if (!string.IsNullOrWhiteSpace(root.Occupation) && !string.IsNullOrWhiteSpace(other.Occupation))
        {
            scoreParts.Add(70);
            confidenceParts.Add(60);
            c.StrongMatches.Add($"Established careers: {root.Occupation} / {other.Occupation}.");
        }

        c.CompatibilityScore = scoreParts.Count == 0
            ? 50
            : (int)Math.Round(scoreParts.Average());

        c.CompatibilityConfidence = confidenceParts.Count == 0
            ? 20
            : (int)Math.Round(confidenceParts.Average());

        if (other.Tier >= 5)
        {
            if (string.IsNullOrWhiteSpace(other.PersonalityContext) &&
                string.IsNullOrWhiteSpace(other.PersonalitySummary))
                c.DeepeningSuggestions.Add("Generate a fuller Tier 5 personality direction.");

            if (string.IsNullOrWhiteSpace(other.Goal))
                c.DeepeningSuggestions.Add("Add a plausible personal goal.");

            if (string.IsNullOrWhiteSpace(other.Need))
                c.DeepeningSuggestions.Add("Add a plausible personal need.");

            if (string.IsNullOrWhiteSpace(other.Want))
                c.DeepeningSuggestions.Add("Add a plausible current want.");

            if (c.CompatibilityScore >= 55 && c.CompatibilityScore < 82)
                c.DeepeningSuggestions.Add("Allow small soft-trait adjustments toward compatibility; do not force a perfect match.");
        }

        if (!c.Eligible)
            c.CompatibilityScore = Math.Min(c.CompatibilityScore, 25);

        return c;
    }

    private static bool IsActivelyMarried(SqliteConnection rel, int npcId, int? exceptTargetId = null)
    {
        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
        SELECT COUNT(*)
        FROM RelationshipStates
        WHERE SourceCharacterId=$id
          AND lower(IFNULL(RelationshipType,'')) IN ('married','marriage','spouse','family')
          AND lower(IFNULL(FamilyRole,'')) IN ('wife','husband','spouse')
          AND ($exceptId IS NULL OR IFNULL(TargetCharacterId,-1) <> $exceptId);
        """;
        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$exceptId", (object?)exceptTargetId ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static bool HasDirectFamilyLink(SqliteConnection rel, int a, int b)
    {
        using var cmd = rel.CreateCommand();
        cmd.CommandText = """
        SELECT COUNT(*)
        FROM RelationshipStates
        WHERE SourceCharacterId=$a
          AND TargetCharacterId=$b
          AND
          (
              lower(IFNULL(RelationshipType,''))='family'
              OR lower(IFNULL(FamilyRole,'')) IN
                 ('mother','father','son','daughter','brother','sister','sibling',
                  'grandmother','grandfather','grandchild',
                  'maternal grandmother','maternal grandfather',
                  'paternal grandmother','paternal grandfather',
                  'stepmother','stepfather','stepson','stepdaughter','stepchild')
          );
        """;
        cmd.Parameters.AddWithValue("$a", a);
        cmd.Parameters.AddWithValue("$b", b);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static Dictionary<string,int> LoadTraits(SqliteConnection main, int npcId)
    {
        var result = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);

        using var cmd = main.CreateCommand();
        cmd.CommandText = """
        SELECT IFNULL(TraitName,''), IFNULL(CurrentValue,0)
        FROM NpcTraitValues
        WHERE NpcId=$id
          AND IFNULL(IsEnabled,1) <> 0
          AND TRIM(IFNULL(TraitName,'')) <> '';
        """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var name = r.GetString(0).Trim();
            if (!result.ContainsKey(name))
                result[name] = Convert.ToInt32(r.GetValue(1));
        }

        return result;
    }

    private static NpcData? LoadNpc(SqliteConnection main, int id)
    {
        using var cmd = main.CreateCommand();
        cmd.CommandText = """
        SELECT
            Id, IFNULL(Name,''), IFNULL(Age,0), IFNULL(Gender,''),
            IFNULL(Tier,5), IFNULL(Occupation,''), IFNULL(Location,''),
            IFNULL(PersonalityContext,''), IFNULL(PersonalitySummary,''),
            IFNULL(Goal,''), IFNULL(Need,''), IFNULL(Fear,''), IFNULL(Want,'')
        FROM Characters
        WHERE Id=$id;
        """;
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadNpc(r) : null;
    }

    private static NpcData ReadNpc(SqliteDataReader r)
        => new(
            r.GetInt32(0),
            r.GetString(1),
            Convert.ToInt32(r.GetValue(2)),
            r.GetString(3),
            Convert.ToInt32(r.GetValue(4)),
            r.GetString(5),
            r.GetString(6),
            r.GetString(7),
            r.GetString(8),
            r.GetString(9),
            r.GetString(10),
            r.GetString(11),
            r.GetString(12));

    private static HashSet<string> BuildPersonalWordSet(NpcData npc)
    {
        var text = string.Join(" ",
            npc.PersonalityContext,
            npc.PersonalitySummary,
            npc.Goal,
            npc.Need,
            npc.Fear,
            npc.Want);

        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the","and","that","with","this","from","have","has","their","they",
            "them","into","about","very","more","some","but","for","are","was",
            "were","will","would","could","should","person","npc"
        };

        return text
            .Split(new[] {' ','\t','\r','\n',',','.',';',';',':','-','/','(',')'},
                StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length >= 4 && !stop.Contains(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string SpouseRole(string gender)
        => gender.Equals("Female", StringComparison.OrdinalIgnoreCase) ? "Wife"
         : gender.Equals("Male", StringComparison.OrdinalIgnoreCase) ? "Husband"
         : "Spouse";

    private sealed record NpcData(
        int Id,
        string Name,
        int Age,
        string Gender,
        int Tier,
        string Occupation,
        string Location,
        string PersonalityContext,
        string PersonalitySummary,
        string Goal,
        string Need,
        string Fear,
        string Want);
}
