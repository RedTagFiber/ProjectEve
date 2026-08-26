using System.Text.Json;
using ProjectEve.Traits;

namespace ProjectEve.Characters.Traits;

/// <summary>
/// Read model for authored Mid personality traits.
///
/// Important ownership rule:
/// - The JSON catalog describes available Mid personality vocabulary.
/// - NpcTraitValues contains which Mid traits are actually assigned to an NPC.
/// - Missing Mid traits are not materialized as default 50s.
/// </summary>
public static class MidTraitDossierService
{
    public static MidTraitDossier Load(int npcId)
    {
        var definitions = LoadDefinitions();

        var persisted = NpcTraitRepository
            .LoadValueRecords(npcId)
            .Where(x => x.TraitId.StartsWith(
                "mid.",
                StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                x => x.TraitId,
                StringComparer.OrdinalIgnoreCase);

        var active = new List<MidTraitDossierItem>();

        foreach (var definition in definitions.Values
                     .OrderBy(x => x.Label))
        {
            if (!persisted.TryGetValue(definition.TraitId, out var row))
                continue;

            active.Add(new MidTraitDossierItem
            {
                TraitId = definition.TraitId,
                TraitName = definition.Label,
                CurrentValue = row.CurrentValue,
                Baseline = row.SetPointValue,
                Style = row.ExpressionStyle,
                StyleOptions = definition.StyleOptions
            });
        }

        // Preserve any existing mid.* row even if its JSON definition is missing.
        foreach (var row in persisted.Values
                     .Where(x => !definitions.ContainsKey(x.TraitId))
                     .OrderBy(x => x.TraitName))
        {
            active.Add(new MidTraitDossierItem
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
            .OrderBy(x => x.Label)
            .ToArray();

        return new MidTraitDossier
        {
            Active = active,
            Available = available,
            TotalDefinitions = definitions.Count
        };
    }

    private static Dictionary<string, MidTraitDefinition> LoadDefinitions()
    {
        string root = TraitJsonLoader.ResolveDefaultRoot();
        string midRoot = Path.Combine(root, "MidTraits");

        var result = new Dictionary<string, MidTraitDefinition>(
            StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(midRoot))
            return result;

        foreach (string file in Directory.EnumerateFiles(
                     midRoot,
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

                if (!json.TryGetProperty("layer", out var layer) ||
                    !string.Equals(
                        layer.GetString(),
                        "mid",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!json.TryGetProperty("id", out var idProp))
                    continue;

                string traitId = idProp.GetString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(traitId))
                    continue;

                string label =
                    json.TryGetProperty("label", out var labelProp)
                        ? labelProp.GetString()?.Trim() ?? ""
                        : "";

                if (string.IsNullOrWhiteSpace(label))
                    label = PrettyName(traitId);

                var styles = new List<string>();

                if (json.TryGetProperty("styles", out var stylesProp))
                {
                    if (stylesProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in stylesProp.EnumerateObject())
                        {
                            if (!string.IsNullOrWhiteSpace(property.Name))
                                styles.Add(property.Name);
                        }
                    }
                    else if (stylesProp.ValueKind == JsonValueKind.Array)
                    {
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

                            string valueFromObject = "";

                            foreach (string key in new[] { "id", "name", "style", "key", "label" })
                            {
                                if (entry.TryGetProperty(key, out var candidate) &&
                                    candidate.ValueKind == JsonValueKind.String)
                                {
                                    valueFromObject = candidate.GetString()?.Trim() ?? "";
                                    if (!string.IsNullOrWhiteSpace(valueFromObject))
                                        break;
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(valueFromObject))
                                styles.Add(valueFromObject);
                        }
                    }
                }

                styles = styles
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                result[traitId] = new MidTraitDefinition
                {
                    TraitId = traitId,
                    Label = label,
                    StyleOptions = styles
                };
            }
            catch
            {
                // Invalid JSON remains the responsibility of the trait validator.
                // One bad definition should not take down NpcStudio.
            }
        }

        return result;
    }

    private static string PrettyName(string traitId)
    {
        string value = traitId;

        if (value.StartsWith("mid.", StringComparison.OrdinalIgnoreCase))
            value = value["mid.".Length..];

        value = value
            .Replace("_", " ")
            .Replace(".", " ")
            .Trim();

        if (string.IsNullOrWhiteSpace(value))
            return traitId;

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}

public sealed class MidTraitDossier
{
    public IReadOnlyList<MidTraitDossierItem> Active { get; init; } =
        Array.Empty<MidTraitDossierItem>();

    public IReadOnlyList<MidTraitDefinition> Available { get; init; } =
        Array.Empty<MidTraitDefinition>();

    public int TotalDefinitions { get; init; }
}

public sealed class MidTraitDefinition
{
    public string TraitId { get; init; } = "";
    public string Label { get; init; } = "";

    public IReadOnlyList<string> StyleOptions { get; init; } =
        Array.Empty<string>();
}

public sealed class MidTraitDossierItem
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
