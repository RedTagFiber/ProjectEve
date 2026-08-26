using System.Text.Json;
using ProjectEve.Traits;

namespace ProjectEve.Characters.Traits;

/// <summary>
/// Typed detail layer beneath parent Slow traits.
///
/// Supported types:
/// - bool
/// - enum
/// - catalog_ref
/// - string_list
/// - string
///
/// Blank EditValue means "no explicit authored override".
/// Multi-value fields are persisted as compact JSON string arrays.
/// </summary>
public static class SubSlowDossierService
{
    private static readonly StringComparer IdComparer =
        StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<SubSlowDossierItem> LoadForParent(
        int npcId,
        string parentTraitId)
    {
        if (npcId <= 0 || string.IsNullOrWhiteSpace(parentTraitId))
            return Array.Empty<SubSlowDossierItem>();

        var definitions = LoadDefinitions()
            .Where(x => x.ParentTraitIds.Contains(parentTraitId, IdComparer))
            .OrderBy(x => TypeOrder(x.ValueType))
            .ThenBy(x => x.Label)
            .ToArray();

        var persisted = NpcTraitRepository
            .LoadSubSlowValues(npcId)
            .Where(x => string.Equals(
                x.ParentTraitId,
                parentTraitId,
                StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                x => x.SubTraitId,
                IdComparer);

        return definitions
            .Select(definition =>
            {
                persisted.TryGetValue(definition.SubTraitId, out var row);

                return new SubSlowDossierItem
                {
                    ParentTraitId = parentTraitId,
                    SubTraitId = definition.SubTraitId,
                    Label = definition.Label,
                    ValueType = definition.ValueType,
                    DefaultValue = definition.DefaultValue,
                    Options = definition.Options,
                    PickCount = definition.PickCount,
                    MaxItems = definition.MaxItems,
                    Source = definition.Source,
                    EditValue = row?.ValueText ?? "",
                    HasExplicitValue = row is not null
                };
            })
            .ToArray();
    }

    public static int CountSupportedForParent(string parentTraitId)
    {
        if (string.IsNullOrWhiteSpace(parentTraitId))
            return 0;

        return LoadDefinitions().Count(x =>
            x.ParentTraitIds.Contains(parentTraitId, IdComparer));
    }

    public static IReadOnlyList<string> ParseMultiValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(value);
            if (parsed is not null)
            {
                return parsed
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(IdComparer)
                    .ToArray();
            }
        }
        catch
        {
            // Backward/hand-authored fallback below.
        }

        return value
            .Split(
                new[] { ',', ';', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(IdComparer)
            .ToArray();
    }

    public static string SerializeMultiValue(IEnumerable<string> values)
    {
        var clean = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(IdComparer)
            .ToArray();

        return clean.Length == 0
            ? ""
            : JsonSerializer.Serialize(clean);
    }

    private static IReadOnlyList<SubSlowDefinition> LoadDefinitions()
    {
        string root = TraitJsonLoader.ResolveDefaultRoot();
        string subRoot = Path.Combine(root, "SubSlowTraits");

        if (!Directory.Exists(subRoot))
            return Array.Empty<SubSlowDefinition>();

        var catalogs = LoadCatalogs(subRoot);
        var definitions = new List<SubSlowDefinition>();

        foreach (string file in Directory.EnumerateFiles(
                     subRoot,
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

                var parents = ReadParents(json);

                if (parents.Count == 0 ||
                    !json.TryGetProperty("subs", out var subsProp) ||
                    subsProp.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var sub in subsProp.EnumerateArray())
                {
                    if (sub.ValueKind != JsonValueKind.Object)
                        continue;

                    string id = GetString(sub, "id");
                    string type = GetString(sub, "type");

                    if (string.IsNullOrWhiteSpace(id) ||
                        string.IsNullOrWhiteSpace(type))
                    {
                        continue;
                    }

                    string label = GetString(sub, "label");
                    if (string.IsNullOrWhiteSpace(label))
                        label = PrettyName(id);

                    string defaultValue = ReadDefault(sub);
                    int pickCount = GetInt(sub, "pickCount");
                    int maxItems = GetInt(sub, "maxItems");
                    string source = GetString(sub, "source");

                    var options = new List<SubSlowOption>();

                    if (string.Equals(type, "bool", StringComparison.OrdinalIgnoreCase))
                    {
                        options.Add(new SubSlowOption("true", "Yes"));
                        options.Add(new SubSlowOption("false", "No"));
                    }
                    else if (string.Equals(type, "enum", StringComparison.OrdinalIgnoreCase))
                    {
                        if (sub.TryGetProperty("values", out var valuesProp) &&
                            valuesProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var option in valuesProp.EnumerateArray())
                            {
                                if (option.ValueKind != JsonValueKind.String)
                                    continue;

                                string value = option.GetString()?.Trim() ?? "";
                                if (!string.IsNullOrWhiteSpace(value))
                                    options.Add(new SubSlowOption(value, DisplayLabel(value)));
                            }
                        }
                    }
                    else if (string.Equals(type, "catalog_ref", StringComparison.OrdinalIgnoreCase))
                    {
                        if (sub.TryGetProperty("catalogs", out var catalogProp) &&
                            catalogProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var catalogName in catalogProp.EnumerateArray())
                            {
                                if (catalogName.ValueKind != JsonValueKind.String)
                                    continue;

                                string catalog = catalogName.GetString()?.Trim() ?? "";
                                if (string.IsNullOrWhiteSpace(catalog))
                                    continue;

                                if (catalogs.TryGetValue(catalog, out var catalogOptions))
                                    options.AddRange(catalogOptions);
                            }
                        }
                    }
                    else if (string.Equals(type, "string_list", StringComparison.OrdinalIgnoreCase))
                    {
                        options.AddRange(LoadSourceOptions(subRoot, source, catalogs));
                    }
                    else if (!string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
                    {
                        // Unknown future types stay out of the editor until deliberately supported.
                        continue;
                    }

                    definitions.Add(new SubSlowDefinition
                    {
                        ParentTraitIds = parents
                            .Distinct(IdComparer)
                            .ToArray(),
                        SubTraitId = id,
                        Label = label,
                        ValueType = type,
                        DefaultValue = defaultValue,
                        PickCount = pickCount,
                        MaxItems = maxItems,
                        Source = source,
                        Options = options
                            .GroupBy(x => x.Value, IdComparer)
                            .Select(x => x.First())
                            .OrderBy(x => x.Label)
                            .ToArray()
                    });
                }
            }
            catch
            {
                // One bad catalog file must not take down NpcStudio.
            }
        }

        return definitions
            .GroupBy(
                x => string.Join(
                    "|",
                    x.ParentTraitIds.OrderBy(y => y, IdComparer)) + "::" + x.SubTraitId,
                IdComparer)
            .Select(x => x.First())
            .ToArray();
    }

    private static Dictionary<string, IReadOnlyList<SubSlowOption>> LoadCatalogs(
        string subRoot)
    {
        var result = new Dictionary<string, IReadOnlyList<SubSlowOption>>(
            IdComparer);

        foreach (string file in Directory.EnumerateFiles(
                     subRoot,
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
                string catalog = GetString(json, "catalog");

                if (string.IsNullOrWhiteSpace(catalog))
                    continue;

                var options = ExtractCatalogOptions(json);

                if (options.Count > 0)
                    result[catalog] = options;
            }
            catch
            {
                // Ignore malformed optional catalogs.
            }
        }

        return result;
    }

    private static IReadOnlyList<SubSlowOption> ExtractCatalogOptions(JsonElement json)
    {
        var options = new List<SubSlowOption>();

        if (json.TryGetProperty("entries", out var entries) &&
            entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    string entryValue = entry.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(entryValue))
                        options.Add(new SubSlowOption(entryValue, entryValue));

                    continue;
                }

                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                string value = GetString(entry, "id");
                string label = GetString(entry, "label");

                if (string.IsNullOrWhiteSpace(value))
                    value = label;

                if (string.IsNullOrWhiteSpace(label))
                    label = DisplayLabel(value);

                if (!string.IsNullOrWhiteSpace(value))
                    options.Add(new SubSlowOption(value, label));
            }
        }

        // Artist seed catalog is grouped by genre instead of entries[].
        if (json.TryGetProperty("byGenre", out var byGenre) &&
            byGenre.ValueKind == JsonValueKind.Object)
        {
            foreach (var genre in byGenre.EnumerateObject())
            {
                if (genre.Value.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var artist in genre.Value.EnumerateArray())
                {
                    if (artist.ValueKind != JsonValueKind.String)
                        continue;

                    string value = artist.GetString()?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(value))
                        options.Add(new SubSlowOption(value, value));
                }
            }
        }

        return options
            .GroupBy(x => x.Value, IdComparer)
            .Select(x => x.First())
            .OrderBy(x => x.Label)
            .ToArray();
    }

    private static IReadOnlyList<SubSlowOption> LoadSourceOptions(
        string subRoot,
        string source,
        IReadOnlyDictionary<string, IReadOnlyList<SubSlowOption>> catalogs)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Array.Empty<SubSlowOption>();

        string normalizedSource = source
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        string sourceRoot = Path.Combine(subRoot, normalizedSource);

        var options = new List<SubSlowOption>();

        if (Directory.Exists(sourceRoot))
        {
            foreach (string file in Directory.EnumerateFiles(
                         sourceRoot,
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

                    options.AddRange(ExtractCatalogOptions(doc.RootElement));
                }
                catch
                {
                    // Optional seed list; skip bad file.
                }
            }
        }

        // Music/Artists currently resolves through artist_seeds catalog.
        if (options.Count == 0 &&
            source.Replace('\\', '/').EndsWith(
                "Music/Artists",
                StringComparison.OrdinalIgnoreCase) &&
            catalogs.TryGetValue("artist_seeds", out var artistOptions))
        {
            options.AddRange(artistOptions);
        }

        return options
            .GroupBy(x => x.Value, IdComparer)
            .Select(x => x.First())
            .OrderBy(x => x.Label)
            .ToArray();
    }

    private static List<string> ReadParents(JsonElement json)
    {
        var parents = new List<string>();

        if (json.TryGetProperty("parent", out var parentProp) &&
            parentProp.ValueKind == JsonValueKind.String)
        {
            string parent = parentProp.GetString()?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(parent))
                parents.Add(parent);
        }

        if (json.TryGetProperty("appliesToParents", out var parentsProp) &&
            parentsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in parentsProp.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                    continue;

                string parent = entry.GetString()?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(parent))
                    parents.Add(parent);
            }
        }

        return parents;
    }

    private static string GetString(JsonElement node, string key)
    {
        if (!node.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return value.GetString()?.Trim() ?? "";
    }

    private static int GetInt(JsonElement node, string key)
    {
        if (!node.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int result))
        {
            return 0;
        }

        return result;
    }

    private static string ReadDefault(JsonElement node)
    {
        if (!node.TryGetProperty("default", out var value))
            return "";

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    private static int TypeOrder(string type) =>
        type.ToLowerInvariant() switch
        {
            "bool" => 0,
            "enum" => 1,
            "catalog_ref" => 2,
            "string_list" => 3,
            "string" => 4,
            _ => 99
        };

    private static string PrettyName(string id)
    {
        string value = id;

        if (value.StartsWith("sub.", StringComparison.OrdinalIgnoreCase))
            value = value[4..];

        return DisplayLabel(value.Replace(".", " "));
    }

    public static string DisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        string text = value
            .Replace("_", " ")
            .Trim();

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            return "Yes";

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            return "No";

        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}

public sealed class SubSlowDefinition
{
    public IReadOnlyList<string> ParentTraitIds { get; init; } =
        Array.Empty<string>();

    public string SubTraitId { get; init; } = "";
    public string Label { get; init; } = "";
    public string ValueType { get; init; } = "";
    public string DefaultValue { get; init; } = "";
    public int PickCount { get; init; }
    public int MaxItems { get; init; }
    public string Source { get; init; } = "";

    public IReadOnlyList<SubSlowOption> Options { get; init; } =
        Array.Empty<SubSlowOption>();
}

public sealed class SubSlowDossierItem
{
    public string ParentTraitId { get; init; } = "";
    public string SubTraitId { get; init; } = "";
    public string Label { get; init; } = "";
    public string ValueType { get; init; } = "";
    public string DefaultValue { get; init; } = "";
    public int PickCount { get; init; }
    public int MaxItems { get; init; }
    public string Source { get; init; } = "";

    public IReadOnlyList<SubSlowOption> Options { get; init; } =
        Array.Empty<SubSlowOption>();

    /// <summary>
    /// Scalar: raw string.
    /// Multi-value: compact JSON string array.
    /// Blank: definition default / no explicit authored override.
    /// </summary>
    public string EditValue { get; set; } = "";

    public bool HasExplicitValue { get; set; }
}

public sealed record SubSlowOption(string Value, string Label);
