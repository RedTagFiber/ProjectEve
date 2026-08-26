using System.Text.Json;
using ProjectEve.Traits;

namespace ProjectEve.Characters.Traits;

/// <summary>
/// Read model for Slow traits / long-term preferences.
///
/// Catalog JSON defines available vocabulary.
/// NpcTraitValues defines which Slow traits are actually assigned.
/// Missing Slow traits remain missing and are never silently materialized at 50.
/// </summary>
public static class SlowTraitDossierService
{
    public static SlowTraitDossier Load(int npcId)
    {
        var definitions = LoadDefinitions();

        var persisted = NpcTraitRepository
            .LoadValueRecords(npcId)
            .Where(x =>
                x.TraitId.StartsWith(
                    "slow.",
                    StringComparison.OrdinalIgnoreCase) ||
                x.TraitId.StartsWith(
                    "kink.",
                    StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                x => x.TraitId,
                StringComparer.OrdinalIgnoreCase);

        var active = new List<SlowTraitDossierItem>();

        foreach (var definition in definitions.Values.OrderBy(x => x.Label))
        {
            if (!persisted.TryGetValue(definition.TraitId, out var row))
                continue;

            active.Add(new SlowTraitDossierItem
            {
                TraitId = definition.TraitId,
                TraitName = definition.Label,
                CurrentValue = row.CurrentValue,
                Baseline = row.SetPointValue,
                Style = row.ExpressionStyle,
                StyleOptions = definition.StyleOptions
            });
        }

        // Preserve canonical persisted Slow rows even when an older/manual trait
        // does not yet have a dedicated JSON catalog definition.
        foreach (var row in persisted.Values
                     .Where(x => !definitions.ContainsKey(x.TraitId))
                     .OrderBy(x => x.TraitName))
        {
            active.Add(new SlowTraitDossierItem
            {
                TraitId = row.TraitId,
                TraitName = string.IsNullOrWhiteSpace(row.TraitName)
                    ? PrettyName(row.TraitId)
                    : row.TraitName,
                CurrentValue = row.CurrentValue,
                Baseline = row.SetPointValue,
                Style = row.ExpressionStyle,
                StyleOptions = Array.Empty<string>(),
                DefinitionMissing = true
            });
        }

        var available = definitions.Values
            .Where(x => !persisted.ContainsKey(x.TraitId))
            .Where(x => !IsLegacySportsTeamAffinity(x.TraitId))
            .OrderBy(x => x.Label)
            .ToArray();

        return new SlowTraitDossier
        {
            Active = active,
            Available = available,
            TotalDefinitions = definitions.Count
        };
    }

    private static Dictionary<string, SlowTraitDefinition> LoadDefinitions()
    {
        string root = TraitJsonLoader.ResolveDefaultRoot();
        string slowRoot = Path.Combine(root, "SlowTraits");

        var result = new Dictionary<string, SlowTraitDefinition>(
            StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(slowRoot))
            return result;

        foreach (string file in Directory.EnumerateFiles(
                     slowRoot,
                     "*.json",
                     SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(
                    File.ReadAllText(file),
                    new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });

                JsonElement json = doc.RootElement;

                // Standard Slow trait JSON: one trait at the root.
                if (json.TryGetProperty("id", out var idProp) &&
                    idProp.ValueKind == JsonValueKind.String)
                {
                    AddDefinitionFromNode(
                        json,
                        result,
                        idProp.GetString()?.Trim() ?? "");
                }

                // Kink catalog JSON: many Slow-domain traits live inside kinks[].
                // These are top-level authored preferences even though their ids
                // intentionally use the kink.* namespace.
                if (json.TryGetProperty("kinks", out var kinksProp) &&
                    kinksProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var kink in kinksProp.EnumerateArray())
                    {
                        if (kink.ValueKind != JsonValueKind.Object ||
                            !kink.TryGetProperty("id", out var kinkIdProp) ||
                            kinkIdProp.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        AddDefinitionFromNode(
                            kink,
                            result,
                            kinkIdProp.GetString()?.Trim() ?? "");
                    }
                }
            }
            catch
            {
                // A bad catalog file should not take down NpcStudio.
            }
        }

        LoadMatrixDefinitions(root, result);

        return result;
    }

    private static void AddDefinitionFromNode(
        JsonElement node,
        Dictionary<string, SlowTraitDefinition> result,
        string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            return;

        string label = "";

        foreach (string key in new[] { "label", "name", "title" })
        {
            if (node.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                label = value.GetString()?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(label))
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(label))
            label = PrettyName(traitId);

        var styles = new List<string>();

        // Standard trait files use styles.
        if (node.TryGetProperty("styles", out var stylesProp))
            AddStyleValues(stylesProp, styles);

        // Kink catalog entries use styleOptions.
        if (node.TryGetProperty("styleOptions", out var styleOptionsProp))
            AddStyleValues(styleOptionsProp, styles);

        var definition = new SlowTraitDefinition
        {
            TraitId = traitId,
            Label = label,
            StyleOptions = styles
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

        // Dedicated definitions win. Duplicate catalog entries are ignored after
        // the first canonical occurrence so duplicate ids never create duplicate UI rows.
        if (!result.ContainsKey(traitId))
            result[traitId] = definition;
    }

    private static void AddStyleValues(
        JsonElement stylesProp,
        List<string> styles)
    {
        if (stylesProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in stylesProp.EnumerateObject())
            {
                if (!string.IsNullOrWhiteSpace(property.Name))
                    styles.Add(property.Name);
            }

            return;
        }

        if (stylesProp.ValueKind != JsonValueKind.Array)
            return;

        foreach (var entry in stylesProp.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                string value = entry.GetString()?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(value))
                    styles.Add(value);

                continue;
            }

            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            foreach (string key in new[] { "id", "name", "style", "key", "label" })
            {
                if (entry.TryGetProperty(key, out var candidate) &&
                    candidate.ValueKind == JsonValueKind.String)
                {
                    string value = candidate.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        styles.Add(value);
                        break;
                    }
                }
            }
        }
    }

    private static void LoadMatrixDefinitions(
        string root,
        Dictionary<string, SlowTraitDefinition> result)
    {
        string matrixPath = Path.Combine(
            root,
            "Matrix",
            "slow_matrix.json");

        if (!File.Exists(matrixPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(matrixPath),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

            VisitMatrixNode(doc.RootElement, result);
        }
        catch
        {
            // Dedicated Slow JSON remains usable even if the matrix is invalid.
        }
    }

    private static void VisitMatrixNode(
        JsonElement node,
        Dictionary<string, SlowTraitDefinition> result)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            string id = "";
            string layer = "";

            if (node.TryGetProperty("id", out var idProp) &&
                idProp.ValueKind == JsonValueKind.String)
            {
                id = idProp.GetString()?.Trim() ?? "";
            }

            if (node.TryGetProperty("layer", out var layerProp) &&
                layerProp.ValueKind == JsonValueKind.String)
            {
                layer = layerProp.GetString()?.Trim() ?? "";
            }

            if (!string.IsNullOrWhiteSpace(id) &&
                id.StartsWith("slow.", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(layer) ||
                 string.Equals(layer, "slow", StringComparison.OrdinalIgnoreCase)))
            {
                if (!result.ContainsKey(id))
                {
                    result[id] = new SlowTraitDefinition
                    {
                        TraitId = id,
                        Label = PrettyName(id),
                        StyleOptions = Array.Empty<string>()
                    };
                }
            }

            foreach (var property in node.EnumerateObject())
                VisitMatrixNode(property.Value, result);

            return;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
                VisitMatrixNode(item, result);
        }
    }

    private static bool IsLegacySportsTeamAffinity(string traitId) =>
        !string.IsNullOrWhiteSpace(traitId) &&
        traitId.StartsWith(
            "slow.sports.team.",
            StringComparison.OrdinalIgnoreCase);

    private static string PrettyName(string traitId)
    {
        string value = traitId;

        foreach (string prefix in new[] { "slow.", "kink." })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        value = value
            .Replace("_", " ")
            .Replace(".", " ")
            .Trim();

        if (string.IsNullOrWhiteSpace(value))
            return traitId;

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}

public sealed class SlowTraitDossier
{
    public IReadOnlyList<SlowTraitDossierItem> Active { get; init; } =
        Array.Empty<SlowTraitDossierItem>();

    public IReadOnlyList<SlowTraitDefinition> Available { get; init; } =
        Array.Empty<SlowTraitDefinition>();

    public int TotalDefinitions { get; init; }
}

public sealed class SlowTraitDefinition
{
    public string TraitId { get; init; } = "";
    public string Label { get; init; } = "";

    public IReadOnlyList<string> StyleOptions { get; init; } =
        Array.Empty<string>();
}

public sealed class SlowTraitDossierItem
{
    public string TraitId { get; init; } = "";
    public string TraitName { get; init; } = "";

    public float CurrentValue { get; set; }
    public float Baseline { get; set; }
    public float DeviationFromBaseline => CurrentValue - Baseline;

    public string Style { get; set; } = "";

    public IReadOnlyList<string> StyleOptions { get; init; } =
        Array.Empty<string>();

    public bool DefinitionMissing { get; init; }
}
