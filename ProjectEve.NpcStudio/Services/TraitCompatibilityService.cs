using System.Text.Json;
using ProjectEve.Traits;
using ProjectEve.Traits.Matrix;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Central compatibility gate for authored and AI-selected NPC traits.
///
/// Rules:
/// - Explicit incompatibilities declared by trait JSON are hard conflicts.
/// - Matrix opposite-pairs are soft psychological tensions, not automatic bans.
/// - Existing/manual canon can be validated before AI additions are accepted.
/// - Parent/sub-trait validation can plug into this same service later.
/// </summary>
public sealed class TraitCompatibilityService
{
    private static readonly string[] ConflictKeys =
    {
        "conflicts",
        "incompatibleWith",
        "incompatible_with",
        "opposites",
        "mutuallyExclusiveWith",
        "mutually_exclusive_with"
    };

    private readonly object _sync = new();
    private bool _loaded;

    private readonly Dictionary<string, HashSet<string>> _hardConflicts =
        new(StringComparer.OrdinalIgnoreCase);

    public TraitCompatibilityResult Evaluate(
        IEnumerable<TraitCompatibilityItem> traits)
    {
        EnsureLoaded();

        var items = traits
            .Where(x => !string.IsNullOrWhiteSpace(x.TraitId))
            .Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId.Trim(),
                Value = Math.Clamp(x.Value, 0f, 100f),
                Source = x.Source?.Trim() ?? ""
            })
            .GroupBy(x => x.TraitId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Value).First())
            .ToList();

        var result = new TraitCompatibilityResult();

        var selected = items.ToDictionary(
            x => x.TraitId,
            StringComparer.OrdinalIgnoreCase);

        // Explicit JSON incompatibilities are hard blocks.
        foreach (var item in items)
        {
            if (!_hardConflicts.TryGetValue(item.TraitId, out var conflicts))
                continue;

            foreach (var otherId in conflicts)
            {
                if (!selected.ContainsKey(otherId))
                    continue;

                AddUnique(
                    result.HardConflicts,
                    $"{item.TraitId} conflicts with {otherId}.");
            }
        }

        // Existing ProjectEve opposite-pair matrix expresses psychological
        // tension. It is not always a logical impossibility.
        foreach (var pair in RelationshipMatrixLoader.OppositePairs)
        {
            if (string.IsNullOrWhiteSpace(pair.A) ||
                string.IsNullOrWhiteSpace(pair.B))
                continue;

            if (!selected.TryGetValue(pair.A, out var a) ||
                !selected.TryGetValue(pair.B, out var b))
                continue;

            var threshold = Math.Clamp(pair.RequiresMin, 0f, 100f);

            if (a.Value >= threshold && b.Value >= threshold)
            {
                AddUnique(
                    result.SoftTensions,
                    $"{pair.A} and {pair.B} are both strong " +
                    $"({a.Value:0}/{b.Value:0}); review for intentional nuance.");
            }
        }

        result.IsCompatible = result.HardConflicts.Count == 0;
        return result;
    }

    public bool CanAdd(
        IEnumerable<TraitCompatibilityItem> existing,
        TraitCompatibilityItem candidate,
        out TraitCompatibilityResult result)
    {
        var combined = existing.Concat(new[] { candidate });
        result = Evaluate(combined);
        return result.IsCompatible;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        lock (_sync)
        {
            if (_loaded)
                return;

            var root = TraitJsonLoader.ResolveDefaultRoot();
            var matrixFolder = Path.Combine(root, "Matrix");

            RelationshipMatrixLoader.Load(matrixFolder);

            LoadExplicitConflictMetadata(root);

            _loaded = true;
        }
    }

    private void LoadExplicitConflictMetadata(string root)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var file in Directory.EnumerateFiles(
                     root,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            // Matrix files have their own semantics and are loaded above.
            if (file.Contains(
                    $"{Path.DirectorySeparatorChar}Matrix{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(
                    File.ReadAllText(file),
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });

                VisitNode(document.RootElement);
            }
            catch
            {
                // Catalog validation is handled elsewhere. One malformed JSON
                // file must not make NPC Studio unavailable.
            }
        }
    }

    private void VisitNode(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var id = TryGetString(node, "id");

            if (!string.IsNullOrWhiteSpace(id))
            {
                foreach (var key in ConflictKeys)
                {
                    if (!node.TryGetProperty(key, out var conflicts))
                        continue;

                    foreach (var other in ReadStringValues(conflicts))
                        AddHardConflict(id, other);
                }
            }

            foreach (var property in node.EnumerateObject())
                VisitNode(property.Value);

            return;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                VisitNode(item);
        }
    }

    private void AddHardConflict(string a, string b)
    {
        a = a.Trim();
        b = b.Trim();

        if (a.Length == 0 ||
            b.Length == 0 ||
            a.Equals(b, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        AddDirection(a, b);
        AddDirection(b, a);
    }

    private void AddDirection(string from, string to)
    {
        if (!_hardConflicts.TryGetValue(from, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _hardConflicts[from] = set;
        }

        set.Add(to);
    }

    private static string TryGetString(JsonElement node, string key)
    {
        if (node.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim() ?? "";
        }

        return "";
    }

    private static IEnumerable<string> ReadStringValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim() ?? "";
            if (text.Length > 0)
                yield return text;

            yield break;
        }

        if (value.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString()?.Trim() ?? "";
                if (text.Length > 0)
                    yield return text;
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "id", "traitId", "trait", "target" })
                {
                    var text = TryGetString(item, key);
                    if (text.Length > 0)
                    {
                        yield return text;
                        break;
                    }
                }
            }
        }
    }

    private static void AddUnique(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            list.Add(value);
    }
}

public sealed class TraitCompatibilityItem
{
    public string TraitId { get; init; } = "";
    public float Value { get; init; } = 50f;
    public string Source { get; init; } = "";
}

public sealed class TraitCompatibilityResult
{
    public bool IsCompatible { get; set; } = true;
    public List<string> HardConflicts { get; } = new();
    public List<string> SoftTensions { get; } = new();

    public string Status =>
        IsCompatible
            ? HardConflicts.Count == 0
                ? "PASS"
                : "REVIEW"
            : "BLOCK";
}