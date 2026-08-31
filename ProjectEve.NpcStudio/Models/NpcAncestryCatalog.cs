namespace ProjectEve.NpcStudio.Models;

public static class NpcAncestryCatalog
{
    // ONE canonical vocabulary used by editor, Foundation, AI-facing context,
    // and family inheritance. Broad legacy values remain recognized but are
    // not considered specific enough to anchor biological inheritance.
    public static IReadOnlyList<string> Options { get; } = new[]
    {
        "",
        "White - English",
        "White - Irish",
        "White - German",
        "White - Italian",
        "White - Polish",
        "White - Scottish",
        "White - Norwegian",
        "White - French",
        "White - Spanish",
        "White - Portuguese",
        "Black - Jamaican",
        "Black - Haitian",
        "Black - Nigerian",
        "Black - Ghanaian",
        "Black - Ethiopian",
        "Black - African American",
        "Asian - Japanese",
        "Asian - Korean",
        "Asian - Chinese",
        "Asian - Filipino",
        "Asian - Vietnamese",
        "Asian - Thai",
        "Asian - Indian",
        "Asian - Pakistani",
        "Asian - Sri Lankan",
        "Pacific Islander - Samoan",
        "Pacific Islander - Tongan",
        "Pacific Islander - Native Hawaiian",
        "Pacific Islander - Fijian",
        "Pacific Islander - Chamorro",
        "Pacific Islander - Tahitian",
        "Hispanic / Latino - Mexican",
        "Hispanic / Latino - Puerto Rican",
        "Hispanic / Latino - Cuban",
        "Hispanic / Latino - Dominican",
        "Hispanic / Latino - Colombian",
        "Hispanic / Latino - Venezuelan",
        "Hispanic / Latino - Salvadoran",
        "Middle Eastern / North African - Lebanese",
        "Middle Eastern / North African - Egyptian",
        "Middle Eastern / North African - Syrian",
        "Middle Eastern / North African - Moroccan",
        "Middle Eastern / North African - Iranian",
        "Indigenous / Native - Cherokee",
        "Indigenous / Native - Navajo",
        "Indigenous / Native - Ojibwe",
        "Indigenous / Native - Lakota",
        "Indigenous / Native - Alaska Native",
        "Mixed / Multiracial",
        "Other / Self-described"
    };

    public static IReadOnlyList<string> FounderOptions { get; } =
        Options
            .Where(IsSpecific)
            .ToArray();

    public static bool IsSpecific(string? value)
    {
        var x = (value ?? "").Trim();

        if (string.IsNullOrWhiteSpace(x))
            return false;

        if (x.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
            x.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            x.Equals("Not Yet Determined", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Old editor vocabulary: useful broad category, but not detailed
        // enough to split biological ancestry outward from an anchor NPC.
        var broadLegacy = new[]
        {
            "White, non-Hispanic",
            "Black or African American, non-Hispanic",
            "Asian, non-Hispanic",
            "American Indian or Alaska Native, non-Hispanic",
            "Native Hawaiian or Other Pacific Islander, non-Hispanic",
            "Two or More Races, non-Hispanic",
            "Hispanic or Latino - White",
            "Hispanic or Latino - Black or African American",
            "Hispanic or Latino - Two or More Races",
            "Hispanic or Latino - Other race",
            "Middle Eastern / North African",
            "Mixed / Multiracial",
            "Other / Self-described",
            "White",
            "Black",
            "Black or African American",
            "Asian",
            "Pacific Islander",
            "Islander",
            "Hispanic",
            "Latino",
            "Hispanic or Latino",
            "Mixed",
            "Multiracial"
        };

        if (broadLegacy.Contains(x, StringComparer.OrdinalIgnoreCase))
            return false;

        // Detailed canonical values may appear in either authored form:
        //   White - Irish
        // or compact inherited/display form:
        //   White (Irish)
        //
        // Mixed inherited values begin "Mixed - " and may contain compact
        // parenthetical components.
        var hasCompactSpecific =
            x.Contains(" (", StringComparison.Ordinal) &&
            x.EndsWith(")", StringComparison.Ordinal);

        return x.Contains(" - ", StringComparison.Ordinal) ||
               x.StartsWith("Mixed - ", StringComparison.OrdinalIgnoreCase) ||
               hasCompactSpecific;
    }
}