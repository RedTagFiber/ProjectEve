using ProjectEve.Traits;

namespace ProjectEve.Characters.Traits;

/// <summary>
/// Canonical read model for Fast20 dossier/UI/AI context.
///
/// Combines:
/// - MAIN: persisted CurrentValue + original StartingValue
/// - live NpcTraits: runtime set-point + active expression style when available
/// - RELATIONSHIPS: active causal reasons + last meaningful trait change
///
/// It creates no new persistence and owns no duplicate trait truth.
/// </summary>
public static class FastTraitDossierService
{
    private static readonly string[] Fast20 =
    {
        "trait.anger",
        "trait.anxiety",
        "trait.fear",
        "trait.shame",
        "trait.guilt",
        "trait.hurt",
        "trait.jealousy",
        "trait.resentment",
        "trait.trust",
        "trait.affection",
        "trait.desire",
        "trait.attraction",
        "trait.tension",
        "trait.playfulness",
        "trait.pride",
        "trait.patience",
        "trait.guard",
        "trait.openness",
        "trait.loneliness",
        "trait.hope"
    };

    public static IReadOnlyList<FastTraitDossierItem> Load(
        int npcId,
        NpcTraits? liveTraits = null,
        int maxReasonsPerTrait = 8)
    {
        var persisted = NpcTraitRepository
            .LoadValueRecords(npcId)
            .ToDictionary(
                x => x.TraitId,
                StringComparer.OrdinalIgnoreCase);

        var styleOptions = TraitJsonLoader.LoadFastStyleOptions();

        var result = new List<FastTraitDossierItem>(Fast20.Length);

        foreach (string traitId in Fast20)
        {
            persisted.TryGetValue(traitId, out var row);

            float current = liveTraits != null && liveTraits.Has(traitId)
                ? liveTraits.Get(traitId)
                : row?.CurrentValue ?? 50f;

            float baseline = liveTraits != null && liveTraits.Has(traitId)
                ? liveTraits.GetSetPoint(traitId)
                : row?.SetPointValue ?? row?.StartingValue ?? current;

            string style =
                liveTraits?.GetStyle(traitId) ??
                row?.ExpressionStyle ??
                "";

            IReadOnlyList<string> allowedStyles =
                styleOptions.TryGetValue(traitId, out var options)
                    ? options
                    : Array.Empty<string>();

            var reasons = NpcTraitRepository
                .LoadActiveReasons(
                    npcId,
                    traitId,
                    Math.Clamp(maxReasonsPerTrait, 1, 50))
                .Select(ToReason)
                .ToList();

            var lastChange = NpcTraitRepository
                .LoadLastMeaningfulChange(npcId, traitId);

            result.Add(new FastTraitDossierItem
            {
                NpcId = npcId,
                TraitId = traitId,
                TraitName = CleanName(row?.TraitName, traitId),
                CurrentValue = current,
                Baseline = baseline,
                Style = style,
                StyleOptions = allowedStyles,
                ActiveReasons = reasons,
                LastChange = lastChange == null
                    ? null
                    : ToLastChange(lastChange)
            });
        }

        return result;
    }

    public static FastTraitDossierItem? LoadOne(
        int npcId,
        string traitId,
        NpcTraits? liveTraits = null,
        int maxReasons = 12)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            return null;

        return Load(npcId, liveTraits, maxReasons)
            .FirstOrDefault(
                x => string.Equals(
                    x.TraitId,
                    traitId,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static FastTraitReasonItem ToReason(
        NpcTraitRepository.TraitReasonRecord row)
    {
        return new FastTraitReasonItem
        {
            Id = row.Id,
            ReasonType = row.ReasonType,
            Reason = row.Reason,
            Impact = row.Impact,
            SourceType = row.SourceType,
            SourceEventId = row.SourceEventId,
            SourceMemoryId = row.SourceMemoryId,
            TargetCharacterId = row.TargetCharacterId,
            Confidence = row.Confidence,
            GameTime = row.GameTime,
            UpdatedRealAt = row.UpdatedRealAt
        };
    }

    private static FastTraitLastChange ToLastChange(
        NpcTraitRepository.TraitChangeRecord row)
    {
        return new FastTraitLastChange
        {
            Id = row.Id,
            BeforeValue = row.BeforeValue,
            AfterValue = row.AfterValue,
            Delta = row.Delta,
            ReasonType = row.ReasonType,
            Reason = row.Reason,
            SourceType = row.SourceType,
            SourceEventId = row.SourceEventId,
            SourceMemoryId = row.SourceMemoryId,
            TargetCharacterId = row.TargetCharacterId,
            GameTime = row.GameTime,
            CreatedRealAt = row.CreatedRealAt
        };
    }

    private static string CleanName(
        string? persistedName,
        string traitId)
    {
        if (!string.IsNullOrWhiteSpace(persistedName))
            return persistedName;

        string value = traitId;

        if (value.StartsWith(
            "trait.",
            StringComparison.OrdinalIgnoreCase))
        {
            value = value["trait.".Length..];
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

public sealed class FastTraitDossierItem
{
    public int NpcId { get; init; }
    public string TraitId { get; init; } = "";
    public string TraitName { get; init; } = "";

    public float CurrentValue { get; set; }
    public float Baseline { get; set; }
    public float DeviationFromBaseline => CurrentValue - Baseline;

    public FastTraitStage StageInfo =>
        FastTraitStageRules.Get(CurrentValue);

    public int Stage => StageInfo.Stage;
    public string StageLabel => StageInfo.Label;
    public int StageMin => StageInfo.Min;
    public int StageMax => StageInfo.Max;
    public bool IsWildcard => StageInfo.IsWildcard;

    public string Style { get; set; } = "";

    public IReadOnlyList<string> StyleOptions { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<FastTraitReasonItem> ActiveReasons { get; init; } =
        Array.Empty<FastTraitReasonItem>();

    public FastTraitLastChange? LastChange { get; init; }
}

public sealed class FastTraitReasonItem
{
    public string Id { get; init; } = "";
    public string ReasonType { get; init; } = "";
    public string Reason { get; init; } = "";
    public float Impact { get; init; }
    public string SourceType { get; init; } = "";
    public string SourceEventId { get; init; } = "";
    public string SourceMemoryId { get; init; } = "";
    public int TargetCharacterId { get; init; } = -1;
    public int Confidence { get; init; } = 100;
    public string GameTime { get; init; } = "";
    public string UpdatedRealAt { get; init; } = "";
}

public sealed class FastTraitLastChange
{
    public string Id { get; init; } = "";
    public float BeforeValue { get; init; }
    public float AfterValue { get; init; }
    public float Delta { get; init; }
    public string ReasonType { get; init; } = "";
    public string Reason { get; init; } = "";
    public string SourceType { get; init; } = "";
    public string SourceEventId { get; init; } = "";
    public string SourceMemoryId { get; init; } = "";
    public int TargetCharacterId { get; init; } = -1;
    public string GameTime { get; init; } = "";
    public string CreatedRealAt { get; init; } = "";
}
