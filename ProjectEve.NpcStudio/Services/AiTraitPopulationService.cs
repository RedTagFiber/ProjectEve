using System.Net.Http.Json;
using System.Text.Json;
using ProjectEve.Characters.Traits;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Canon-aware AI selector for ProjectEve's authored trait catalogs.
///
/// Population rules:
/// - Mid target: exactly 10 active parent traits.
/// - Slow target: 15-20 active parent traits based on build tier.
/// - Existing/manual assignments are preserved.
/// - AI may only select ids from the authored Mid/Slow catalogs.
/// - Every candidate is passed through TraitCompatibilityService.
/// - SubSlow values are generated only for selected Slow parents and only when missing.
/// - This service is PREVIEW ONLY. A later integration step persists an approved preview.
/// </summary>
public sealed class AiTraitPopulationService
{
    public const int MidTarget = 10;
    public const int SlowMinimum = 15;
    public const int SlowMaximum = 20;

    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;
    private readonly TraitCompatibilityService _compatibility;

    public AiTraitPopulationService(
        HttpClient http,
        NpcStudioOptions options,
        TraitCompatibilityService compatibility)
    {
        _http = http;
        _options = options;
        _compatibility = compatibility;
    }

    public async Task<AiTraitPopulationPreview> BuildPreviewAsync(
        int npcId,
        int buildTier,
        string npcContext,
        bool fillMid = true,
        bool fillSlow = true,
        int npcAge = 0,
        CancellationToken cancellationToken = default)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        var preview = new AiTraitPopulationPreview
        {
            NpcId = npcId,
            MidTarget = MidTarget,
            SlowTarget = SlowTargetForTier(buildTier),
            RequestedMid = fillMid,
            RequestedSlow = fillSlow
        };

        var fast = FastTraitDossierService.Load(npcId);
        var mid = MidTraitDossierService.Load(npcId);
        var slow = SlowTraitDossierService.Load(npcId);

        preview.ExistingMidCount = mid.Active.Count;
        preview.ExistingSlowCount = slow.Active.Count;

        var neededMid = fillMid ? Math.Max(0, MidTarget - mid.Active.Count) : 0;
        var neededSlow = fillSlow ? Math.Max(0, preview.SlowTarget - slow.Active.Count) : 0;

        var compatibilityBase = new List<TraitCompatibilityItem>();

        compatibilityBase.AddRange(
            fast.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.CurrentValue,
                Source = "ExistingFast"
            }));

        compatibilityBase.AddRange(
            mid.Active.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.CurrentValue,
                Source = "ExistingMid"
            }));

        compatibilityBase.AddRange(
            slow.Active.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.CurrentValue,
                Source = "ExistingSlow"
            }));

        var existingCheck = _compatibility.Evaluate(compatibilityBase);
        preview.SoftTensions.AddRange(existingCheck.SoftTensions);

        if (!existingCheck.IsCompatible)
        {
            preview.HardConflicts.AddRange(existingCheck.HardConflicts);
            preview.Warnings.Add(
                "BLOCK: Existing canonical traits already contain a hard compatibility conflict. " +
                "AI will not add more traits until it is reviewed.");
            return preview;
        }

        if (neededMid > 0 || neededSlow > 0)
        {
            var midCandidates = SelectCandidateWindow(
                mid.Available
                    .Select(x => new CatalogCandidate(
                        x.TraitId,
                        x.Label,
                        x.StyleOptions))
                    .ToArray(),
                npcId,
                140);

            var slowCandidates = SelectBalancedSlowCandidateWindow(
                slow.Available
                    .Where(x => npcAge >= 18 || !IsKinkTrait(x.TraitId))
                    .Select(x => new CatalogCandidate(
                        x.TraitId,
                        x.Label,
                        x.StyleOptions))
                    .ToArray(),
                npcId + 7919);

            var parentPrompt = BuildParentPrompt(
                npcId,
                buildTier,
                npcContext,
                neededMid,
                neededSlow,
                midCandidates,
                slowCandidates,
                mid.Active.Select(x => x.TraitId),
                slow.Active.Select(x => x.TraitId));

            var raw = await GenerateAsync(
                parentPrompt,
                maxOutputTokens: 2600,
                cancellationToken);

            ParentTraitResponse? response = null;

            try
            {
                response = JsonSerializer.Deserialize<ParentTraitResponse>(
                    ExtractJson(raw),
                    JsonOptions());
            }
            catch (Exception ex)
            {
                preview.Warnings.Add(
                    "BLOCK: AI trait parent response was not valid JSON: " + ex.Message);
            }

            if (response is not null)
            {
                var validMid = midCandidates.ToDictionary(
                    x => x.TraitId,
                    StringComparer.OrdinalIgnoreCase);

                var validSlow = slowCandidates.ToDictionary(
                    x => x.TraitId,
                    StringComparer.OrdinalIgnoreCase);

                AcceptParentProposals(
                    "Mid",
                    response.Mid,
                    neededMid,
                    validMid,
                    compatibilityBase,
                    preview.MidSelections,
                    preview);

                FillMissingParentSlotsFromCatalog(
                    group: "Mid",
                    neededTotal: neededMid,
                    validCatalog: validMid,
                    compatibilityBase: compatibilityBase,
                    accepted: preview.MidSelections,
                    preview: preview,
                    seed: npcId + 11003);

                compatibilityBase.AddRange(
                    preview.MidSelections.Select(x => new TraitCompatibilityItem
                    {
                        TraitId = x.TraitId,
                        Value = x.Value,
                        Source = "AiMidPreview"
                    }));

                var existingKinkCount = slow.Active.Count(x => IsKinkTrait(x.TraitId));
                var existingSportsCount = slow.Active.Count(x => IsSportsTrait(x.TraitId));

                AcceptParentProposals(
                    "Slow",
                    response.Slow,
                    neededSlow,
                    validSlow,
                    compatibilityBase,
                    preview.SlowSelections,
                    preview,
                    maxAutomaticKinks: npcAge >= 18
                        ? Math.Max(0, 2 - existingKinkCount)
                        : 0,
                    maxAutomaticSports: Math.Max(0, 3 - existingSportsCount));

                // Local models sometimes return fewer valid Slow candidates than requested.
                // Finish the remaining slots from the authored catalog so the NPC reaches
                // the required minimum instead of blocking Save at 10/15 or 14/15.
                FillMissingParentSlotsFromCatalog(
                    group: "Slow",
                    neededTotal: neededSlow,
                    validCatalog: validSlow,
                    compatibilityBase: compatibilityBase,
                    accepted: preview.SlowSelections,
                    preview: preview,
                    seed: npcId + 17041,
                    maxAutomaticKinks: npcAge >= 18
                        ? Math.Max(0, 2 - existingKinkCount)
                        : 0,
                    maxAutomaticSports: Math.Max(0, 3 - existingSportsCount));
            }
        }

        preview.ResultingMidCount =
            preview.ExistingMidCount + preview.MidSelections.Count;

        preview.ResultingSlowCount =
            preview.ExistingSlowCount + preview.SlowSelections.Count;

        if (fillMid && preview.ResultingMidCount < MidTarget)
        {
            preview.Warnings.Add(
                $"BLOCK: Mid trait population is incomplete: " +
                $"{preview.ResultingMidCount}/{MidTarget}.");
        }

        if (fillSlow && preview.ResultingSlowCount < SlowMinimum)
        {
            preview.Warnings.Add(
                $"BLOCK: Slow trait population is incomplete: " +
                $"{preview.ResultingSlowCount}/{SlowMinimum} minimum.");
        }

        if (fillSlow && preview.ResultingSlowCount > SlowMaximum)
        {
            preview.Warnings.Add(
                $"REVIEW: Existing Slow canon already exceeds the normal maximum " +
                $"of {SlowMaximum}. Nothing was deleted.");
        }

        if (fillSlow &&
            !preview.Warnings.Any(x =>
                x.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase)))
        {
            await BuildMissingSubTraitPreviewAsync(
                npcId,
                npcContext,
                slow.Active.Select(x => x.TraitId)
                    .Concat(preview.SlowSelections.Select(x => x.TraitId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                preview,
                cancellationToken);
        }

        return preview;
    }

    public Task ApplyApprovedPreviewAsync(AiTraitPopulationPreview preview)
    {
        if (preview is null)
            throw new ArgumentNullException(nameof(preview));

        if (!preview.IsValid)
            throw new InvalidOperationException(
                "Trait preview is not valid and cannot be saved.");

        // Re-check the final parent-trait combination immediately before save.
        // Existing/manual canon always remains part of the compatibility gate.
        var fast = FastTraitDossierService.Load(preview.NpcId);
        var mid = MidTraitDossierService.Load(preview.NpcId);
        var slow = SlowTraitDossierService.Load(preview.NpcId);

        var finalTraits = new List<TraitCompatibilityItem>();

        finalTraits.AddRange(
            fast.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.CurrentValue,
                Source = "ExistingFast"
            }));

        finalTraits.AddRange(
            mid.Active.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.CurrentValue,
                Source = "ExistingMid"
            }));

        finalTraits.AddRange(
            slow.Active.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.CurrentValue,
                Source = "ExistingSlow"
            }));

        finalTraits.AddRange(
            preview.MidSelections.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.Value,
                Source = "ApprovedAiMid"
            }));

        finalTraits.AddRange(
            preview.SlowSelections.Select(x => new TraitCompatibilityItem
            {
                TraitId = x.TraitId,
                Value = x.Value,
                Source = "ApprovedAiSlow"
            }));

        var compatibility = _compatibility.Evaluate(finalTraits);

        if (!compatibility.IsCompatible)
        {
            throw new InvalidOperationException(
                "Trait compatibility changed before save: " +
                string.Join(" ", compatibility.HardConflicts));
        }

        // Canonical gateway only. No duplicate Traits table is created here.
        foreach (var trait in preview.MidSelections)
        {
            NpcTraitRepository.SaveMidDossierState(
                preview.NpcId,
                trait.TraitId,
                trait.TraitName,
                trait.Value,
                trait.Value,
                trait.Style);
        }

        foreach (var trait in preview.SlowSelections)
        {
            NpcTraitRepository.SaveSlowDossierState(
                preview.NpcId,
                trait.TraitId,
                trait.TraitName,
                trait.Value,
                trait.Value,
                trait.Style);
        }

        foreach (var sub in preview.SubTraitSelections)
        {
            NpcTraitRepository.SaveSubSlowValue(
                preview.NpcId,
                sub.ParentTraitId,
                sub.SubTraitId,
                sub.SubTraitName,
                sub.ValueType,
                sub.ValueText);
        }

        return Task.CompletedTask;
    }
    private void FillMissingParentSlotsFromCatalog(
        string group,
        int neededTotal,
        IReadOnlyDictionary<string, CatalogCandidate> validCatalog,
        List<TraitCompatibilityItem> compatibilityBase,
        List<AiTraitSelection> accepted,
        AiTraitPopulationPreview preview,
        int seed,
        int maxAutomaticKinks = int.MaxValue,
        int maxAutomaticSports = int.MaxValue)
    {
        if (neededTotal <= 0 || accepted.Count >= neededTotal)
            return;

        var ordered = validCatalog.Values
            .Where(x => !accepted.Any(a =>
                a.TraitId.Equals(
                    x.TraitId,
                    StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => FallbackCategoryPriority(x.TraitId))
            .ThenBy(x => StableScore(seed, x.TraitId))
            .ToArray();

        foreach (var definition in ordered)
        {
            if (accepted.Count >= neededTotal)
                break;

            if (IsKinkTrait(definition.TraitId))
            {
                var kinkCount = accepted.Count(x => IsKinkTrait(x.TraitId));
                if (kinkCount >= maxAutomaticKinks)
                    continue;
            }

            if (IsSportsTrait(definition.TraitId))
            {
                var sportsCount = accepted.Count(x => IsSportsTrait(x.TraitId));
                if (sportsCount >= maxAutomaticSports)
                    continue;
            }

            // A neutral-moderate fallback value is intentionally conservative.
            // It creates a usable canonical slot without inventing an extreme.
            var value = 55;

            var candidate = new TraitCompatibilityItem
            {
                TraitId = definition.TraitId,
                Value = value,
                Source = "CatalogFallback"
            };

            if (!_compatibility.CanAdd(
                    compatibilityBase.Concat(
                        accepted.Select(x => new TraitCompatibilityItem
                        {
                            TraitId = x.TraitId,
                            Value = x.Value,
                            Source = "AcceptedPreview"
                        })),
                    candidate,
                    out var result))
            {
                continue;
            }

            foreach (var tension in result.SoftTensions)
            {
                if (!preview.SoftTensions.Contains(
                        tension,
                        StringComparer.OrdinalIgnoreCase))
                {
                    preview.SoftTensions.Add(tension);
                }
            }

            accepted.Add(new AiTraitSelection
            {
                Group = group,
                TraitId = definition.TraitId,
                TraitName = definition.Label,
                Value = value,
                Style = definition.StyleOptions.FirstOrDefault() ?? ""
            });

            preview.Warnings.Add(
                $"REVIEW: Added authored catalog fallback {group} trait " +
                $"'{definition.TraitId}' because AI returned too few valid candidates.");
        }
    }

    private static int FallbackCategoryPriority(string traitId)
    {
        var category = SlowCategory(traitId);

        return category switch
        {
            "Life" => 0,
            "Music" => 1,
            "Movies" => 2,
            "TV" => 3,
            "Other" => 4,
            "Sports" => 5,
            "Kinks" => 6,
            _ => 9
        };
    }
    private void AcceptParentProposals(
        string group,
        IEnumerable<AiParentTraitChoice>? proposals,
        int needed,
        IReadOnlyDictionary<string, CatalogCandidate> validCatalog,
        List<TraitCompatibilityItem> compatibilityBase,
        List<AiTraitSelection> accepted,
        AiTraitPopulationPreview preview,
        int maxAutomaticKinks = int.MaxValue,
        int maxAutomaticSports = int.MaxValue)
    {
        if (needed <= 0)
            return;

        foreach (var proposal in proposals ?? Array.Empty<AiParentTraitChoice>())
        {
            if (accepted.Count >= needed)
                break;

            var id = proposal.TraitId?.Trim() ?? "";
            if (id.Length == 0)
                continue;

            if (!validCatalog.TryGetValue(id, out var definition))
            {
                preview.Warnings.Add(
                    $"REVIEW: AI proposed non-catalog {group} trait '{id}', so it was ignored.");
                continue;
            }

            if (accepted.Any(x =>
                    x.TraitId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (IsKinkTrait(id))
            {
                var acceptedKinks = accepted.Count(x => IsKinkTrait(x.TraitId));

                if (acceptedKinks >= maxAutomaticKinks)
                {
                    preview.Warnings.Add(
                        $"REVIEW: AI proposed extra kink trait '{id}', but automatic kink selection is capped.");
                    continue;
                }
            }

            if (IsSportsTrait(id))
            {
                var acceptedSports = accepted.Count(x => IsSportsTrait(x.TraitId));

                if (acceptedSports >= maxAutomaticSports)
                {
                    preview.Warnings.Add(
                        $"REVIEW: AI proposed extra sports trait '{id}', but automatic sports selection is capped for diversity.");
                    continue;
                }
            }

            var value = Math.Clamp(proposal.Value, 0, 100);
            var style = CleanStyle(proposal.Style, definition.StyleOptions);

            var candidate = new TraitCompatibilityItem
            {
                TraitId = id,
                Value = value,
                Source = "AI"
            };

            if (!_compatibility.CanAdd(
                    compatibilityBase.Concat(
                        accepted.Select(x => new TraitCompatibilityItem
                        {
                            TraitId = x.TraitId,
                            Value = x.Value,
                            Source = "AcceptedPreview"
                        })),
                    candidate,
                    out var result))
            {
                preview.HardConflicts.AddRange(
                    result.HardConflicts.Where(x =>
                        !preview.HardConflicts.Contains(
                            x,
                            StringComparer.OrdinalIgnoreCase)));

                preview.Warnings.Add(
                    $"REVIEW: AI {group} trait '{id}' was rejected by compatibility rules.");
                continue;
            }

            foreach (var tension in result.SoftTensions)
            {
                if (!preview.SoftTensions.Contains(
                        tension,
                        StringComparer.OrdinalIgnoreCase))
                {
                    preview.SoftTensions.Add(tension);
                }
            }

            accepted.Add(new AiTraitSelection
            {
                Group = group,
                TraitId = id,
                TraitName = definition.Label,
                Value = value,
                Style = style
            });
        }
    }

    private async Task BuildMissingSubTraitPreviewAsync(
        int npcId,
        string npcContext,
        IReadOnlyList<string> selectedSlowIds,
        AiTraitPopulationPreview preview,
        CancellationToken cancellationToken)
    {
        var missing = new List<SubRequestDefinition>();

        foreach (var parentId in selectedSlowIds)
        {
            foreach (var sub in SubSlowDossierService.LoadForParent(npcId, parentId))
            {
                if (sub.HasExplicitValue)
                    continue;

                missing.Add(new SubRequestDefinition
                {
                    ParentTraitId = parentId,
                    SubTraitId = sub.SubTraitId,
                    Label = sub.Label,
                    ValueType = sub.ValueType,
                    DefaultValue = sub.DefaultValue,
                    PickCount = sub.PickCount,
                    MaxItems = sub.MaxItems,
                    Options = sub.Options
                        .Select(x => new OptionDefinition(x.Value, x.Label))
                        .Take(100)
                        .ToArray()
                });
            }
        }

        preview.RequiredMissingSubTraitCount = missing.Count;

        if (missing.Count == 0)
            return;

        // Keep a single request bounded. If there are unusually many subs,
        // batches prevent Ollama context overflow.
        foreach (var batch in missing.Chunk(40))
        {
            var prompt = BuildSubTraitPrompt(npcContext, batch);
            var raw = await GenerateAsync(
                prompt,
                maxOutputTokens: 2400,
                cancellationToken);

            SubTraitResponse? response = null;

            try
            {
                response = JsonSerializer.Deserialize<SubTraitResponse>(
                    ExtractJson(raw),
                    JsonOptions());
            }
            catch (Exception ex)
            {
                preview.Warnings.Add(
                    "REVIEW: AI sub-trait response was not valid JSON. Safe catalog fallbacks will be used where possible: " + ex.Message);
                continue;
            }

            var lookup = batch.ToDictionary(
                x => $"{x.ParentTraitId}|{x.SubTraitId}",
                StringComparer.OrdinalIgnoreCase);

            foreach (var value in response?.Values ?? new List<AiSubTraitChoice>())
            {
                var key =
                    $"{value.ParentTraitId?.Trim()}|{value.SubTraitId?.Trim()}";

                if (!lookup.TryGetValue(key, out var definition))
                    continue;

                var clean = ValidateSubValue(
                    definition,
                    value.Value ?? "");

                if (clean is null)
                {
                    preview.Warnings.Add(
                        $"REVIEW: Invalid AI value for sub-trait " +
                        $"'{definition.ParentTraitId} -> {definition.SubTraitId}' was ignored.");
                    continue;
                }

                if (preview.SubTraitSelections.Any(x =>
                        x.ParentTraitId.Equals(
                            definition.ParentTraitId,
                            StringComparison.OrdinalIgnoreCase) &&
                        x.SubTraitId.Equals(
                            definition.SubTraitId,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                preview.SubTraitSelections.Add(new AiSubTraitSelection
                {
                    ParentTraitId = definition.ParentTraitId,
                    SubTraitId = definition.SubTraitId,
                    SubTraitName = definition.Label,
                    ValueType = definition.ValueType,
                    ValueText = clean
                });
            }
        }

        // One local-model formatting miss should not disable the whole profile.
        // Complete unresolved typed fields from authored defaults/options when safe.
        var fallbackCount = 0;

        foreach (var definition in missing)
        {
            var alreadyFilled = preview.SubTraitSelections.Any(x =>
                x.ParentTraitId.Equals(
                    definition.ParentTraitId,
                    StringComparison.OrdinalIgnoreCase) &&
                x.SubTraitId.Equals(
                    definition.SubTraitId,
                    StringComparison.OrdinalIgnoreCase));

            if (alreadyFilled)
                continue;

            var fallback = BuildSafeSubTraitFallback(definition);
            if (fallback is null)
                continue;

            preview.SubTraitSelections.Add(new AiSubTraitSelection
            {
                ParentTraitId = definition.ParentTraitId,
                SubTraitId = definition.SubTraitId,
                SubTraitName = definition.Label,
                ValueType = definition.ValueType,
                ValueText = fallback
            });

            fallbackCount++;
        }

        if (fallbackCount > 0)
        {
            preview.Warnings.Add(
                $"REVIEW: {fallbackCount} missing sub-trait value(s) used authored catalog defaults/options after the AI response.");
        }

        if (preview.SubTraitSelections.Count < preview.RequiredMissingSubTraitCount)
        {
            var remaining =
                preview.RequiredMissingSubTraitCount -
                preview.SubTraitSelections.Count;

            preview.Warnings.Add(
                $"REVIEW: Sub-trait population is partial: " +
                $"{preview.SubTraitSelections.Count}/" +
                $"{preview.RequiredMissingSubTraitCount} missing values were filled. " +
                $"{remaining} unresolved sub-trait value(s) may be completed later.");
        }
    }

    private static bool IsKinkTrait(string? traitId) =>
        !string.IsNullOrWhiteSpace(traitId) &&
        traitId.Trim().StartsWith(
            "kink.",
            StringComparison.OrdinalIgnoreCase);

    private static string? BuildSafeSubTraitFallback(
        SubRequestDefinition definition)
    {
        var type = definition.ValueType.Trim().ToLowerInvariant();
        var authoredDefault = definition.DefaultValue?.Trim() ?? "";

        if (type == "bool")
        {
            if (authoredDefault.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "true";

            if (authoredDefault.Equals("false", StringComparison.OrdinalIgnoreCase))
                return "false";

            return "false";
        }

        if (type is "enum" or "catalog_ref")
        {
            if (authoredDefault.Length > 0)
            {
                var defaultMatch = definition.Options.FirstOrDefault(x =>
                    x.Value.Equals(
                        authoredDefault,
                        StringComparison.OrdinalIgnoreCase));

                if (defaultMatch is not null)
                    return defaultMatch.Value;
            }

            return definition.Options.FirstOrDefault()?.Value;
        }

        if (type == "string_list")
        {
            if (authoredDefault.Length > 0)
            {
                var validatedDefault = ValidateSubValue(
                    definition,
                    authoredDefault);

                if (validatedDefault is not null)
                    return validatedDefault;
            }

            if (definition.Options.Length == 0)
                return null;

            var take = definition.PickCount > 0
                ? definition.PickCount
                : definition.MaxItems > 0
                    ? Math.Min(definition.MaxItems, 1)
                    : 1;

            var values = definition.Options
                .Take(Math.Max(1, take))
                .Select(x => x.Value)
                .ToArray();

            return values.Length == 0
                ? null
                : SubSlowDossierService.SerializeMultiValue(values);
        }

        if (type == "string")
        {
            return authoredDefault.Length > 0
                ? authoredDefault
                : null;
        }

        return null;
    }
    private static string? ValidateSubValue(
        SubRequestDefinition definition,
        string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        var type = definition.ValueType.Trim().ToLowerInvariant();

        if (type == "bool")
        {
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "true";
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return "false";
            return null;
        }

        if (type is "enum" or "catalog_ref")
        {
            var match = definition.Options.FirstOrDefault(x =>
                x.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

            return match?.Value;
        }

        if (type == "string_list")
        {
            var values = SubSlowDossierService.ParseMultiValue(value).ToList();

            if (values.Count == 0)
                return null;

            if (definition.Options.Length > 0)
            {
                var allowed = definition.Options
                    .Select(x => x.Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                values = values.Where(allowed.Contains).ToList();
            }

            if (definition.PickCount > 0)
            {
                if (values.Count < definition.PickCount)
                    return null;

                values = values.Take(definition.PickCount).ToList();
            }
            else if (definition.MaxItems > 0)
            {
                values = values.Take(definition.MaxItems).ToList();
            }

            return values.Count == 0
                ? null
                : SubSlowDossierService.SerializeMultiValue(values);
        }

        if (type == "string")
            return value;

        return null;
    }

    private static string BuildParentPrompt(
        int npcId,
        int buildTier,
        string npcContext,
        int neededMid,
        int neededSlow,
        IReadOnlyList<CatalogCandidate> midCandidates,
        IReadOnlyList<CatalogCandidate> slowCandidates,
        IEnumerable<string> existingMid,
        IEnumerable<string> existingSlow)
    {
        string CatalogText(IEnumerable<CatalogCandidate> values) =>
            string.Join(
                "\n",
                values.Select(x =>
                    $"- {x.TraitId} | {x.Label} | styles: " +
                    $"{(x.StyleOptions.Count == 0 ? "(none)" : string.Join(", ", x.StyleOptions))}"));

        return $$"""
        You are Project Eve's canonical TRAIT SELECTOR.

        NPC ID: {{npcId}}
        BUILD TIER: {{Math.Clamp(buildTier, 1, 5)}}

        NPC CONTEXT:
        {{npcContext}}

        HARD RULES:
        - You may select ONLY traitId values listed in the candidate catalogs below.
        - Do not invent names or ids.
        - Existing traits are locked and must not be repeated.
        - We need {{neededMid}} new Mid traits. Return up to {{Math.Min(neededMid + 4, midCandidates.Count)}} ranked Mid candidates so validation has reserves.
        - We need {{neededSlow}} new Slow traits. Return up to {{Math.Min(neededSlow + 10, slowCandidates.Count)}} ranked Slow candidates so validation has reserves.
        - Values are integers 0-100.
        - Avoid contradictory personality selections.
        - SLOW DIVERSITY: do not let one interest category dominate.
        - Prefer a mix of Life, Music, Movies, TV, and other everyday preferences.
        - At most 3 Sports traits total from this AI pass.
        - At most 2 Kink traits total from this AI pass, and only when they genuinely fit.
        - Life traits should be strongly represented when available.
        - Prefer a coherent, human personality with nuance rather than random extremes.
        - Use an allowed style when styles are provided; otherwise style may be blank.
        - Return JSON only.

        EXISTING MID IDS:
        {{string.Join(", ", existingMid)}}

        EXISTING SLOW IDS:
        {{string.Join(", ", existingSlow)}}

        MID CANDIDATES:
        {{CatalogText(midCandidates)}}

        SLOW CANDIDATES:
        {{CatalogText(slowCandidates)}}

        RETURN:
        {
          "mid": [
            { "traitId": "mid.example", "value": 65, "style": "" }
          ],
          "slow": [
            { "traitId": "slow.example", "value": 65, "style": "" }
          ]
        }
        """;
    }

    private static string BuildSubTraitPrompt(
        string npcContext,
        IReadOnlyList<SubRequestDefinition> definitions)
    {
        var lines = definitions.Select(x =>
        {
            var options = x.Options.Length == 0
                ? "(free text / no fixed option catalog)"
                : string.Join(", ", x.Options.Select(o => $"{o.Value}={o.Label}"));

            return
                $"- parent={x.ParentTraitId}; sub={x.SubTraitId}; label={x.Label}; " +
                $"type={x.ValueType}; default={x.DefaultValue}; " +
                $"pickCount={x.PickCount}; maxItems={x.MaxItems}; options={options}";
        });

        return $$"""
        You are Project Eve's SUB-TRAIT SELECTOR.

        NPC CONTEXT:
        {{npcContext}}

        Fill every requested sub-trait with a plausible value that matches the
        selected parent trait and the NPC's overall personality.

        HARD RULES:
        - Return one value for EVERY requested parent/sub pair.
        - Never invent a parentTraitId or subTraitId.
        - bool: value must be "true" or "false".
        - enum/catalog_ref: value must be one exact option id from options.
        - string_list: value must be a JSON string containing a JSON array,
          for example "[\"rock\",\"jazz\"]". Respect pickCount/maxItems.
        - string: concise free text.
        - Prefer consistency with the full personality.
        - Return JSON only.

        REQUESTED SUB-TRAITS:
        {{string.Join("\n", lines)}}

        RETURN:
        {
          "values": [
            {
              "parentTraitId": "slow.example",
              "subTraitId": "sub.example",
              "value": "true"
            }
          ]
        }
        """;
    }

    private async Task<string> GenerateAsync(
        string prompt,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        using var healthCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        healthCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var health = await _http.GetAsync(
                new Uri(new Uri(_options.OllamaBaseUrl), "/api/tags"),
                healthCts.Token);

            health.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (
            ex is HttpRequestException or OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Could not reach Ollama at {_options.OllamaBaseUrl}. " +
                "Make sure Ollama is running.",
                ex);
        }

        var request = new
        {
            model = _options.OllamaModel,
            prompt,
            stream = false,
            format = "json",
            keep_alive = "30m",
            options = new
            {
                temperature = 0.45,
                num_ctx = 8192,
                num_predict = maxOutputTokens,
                repeat_penalty = 1.05
            }
        };

        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(180));

        using var response = await _http.PostAsJsonAsync(
            new Uri(new Uri(_options.OllamaBaseUrl), "/api/generate"),
            request,
            cts.Token);

        response.EnsureSuccessStatusCode();

        using var stream =
            await response.Content.ReadAsStreamAsync(cts.Token);

        using var doc = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cts.Token);

        return doc.RootElement.TryGetProperty("response", out var value)
            ? value.GetString() ?? "{}"
            : "{}";
    }

    private static int SlowTargetForTier(int buildTier) =>
        Math.Clamp(buildTier, 1, 5) switch
        {
            1 => 20,
            2 => 18,
            3 => 17,
            4 => 16,
            _ => 15
        };

    private static bool IsSportsTrait(string? traitId) =>
        !string.IsNullOrWhiteSpace(traitId) &&
        traitId.Trim().StartsWith(
            "slow.sports.",
            StringComparison.OrdinalIgnoreCase);

    private static string SlowCategory(string traitId)
    {
        var id = traitId?.Trim() ?? "";

        if (id.StartsWith("slow.life.", StringComparison.OrdinalIgnoreCase)) return "Life";
        if (id.StartsWith("slow.music.", StringComparison.OrdinalIgnoreCase)) return "Music";
        if (id.StartsWith("slow.movies.", StringComparison.OrdinalIgnoreCase)) return "Movies";
        if (id.StartsWith("slow.tv.", StringComparison.OrdinalIgnoreCase)) return "TV";
        if (id.StartsWith("slow.sports.", StringComparison.OrdinalIgnoreCase)) return "Sports";
        if (IsKinkTrait(id)) return "Kinks";
        return "Other";
    }

    private static IReadOnlyList<CatalogCandidate> SelectBalancedSlowCandidateWindow(
        IReadOnlyList<CatalogCandidate> all,
        int seed)
    {
        var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Life"] = 70,
            ["Music"] = 32,
            ["Movies"] = 32,
            ["TV"] = 32,
            ["Sports"] = 24,
            ["Kinks"] = 10,
            ["Other"] = 30
        };

        var selected = new List<CatalogCandidate>();

        foreach (var group in all.GroupBy(x => SlowCategory(x.TraitId)))
        {
            var limit = limits.TryGetValue(group.Key, out var configured)
                ? configured
                : 24;

            selected.AddRange(
                group.OrderBy(x => StableScore(seed, x.TraitId))
                    .Take(limit));
        }

        return selected
            .DistinctBy(x => x.TraitId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => SlowCategory(x.TraitId))
            .ThenBy(x => x.Label)
            .ToArray();
    }
    private static IReadOnlyList<CatalogCandidate> SelectCandidateWindow(
        IReadOnlyList<CatalogCandidate> all,
        int seed,
        int max)
    {
        if (all.Count <= max)
            return all.OrderBy(x => x.Label).ToArray();

        // Stable NPC-specific window: every NPC gets broad catalog diversity,
        // while requests stay small enough for a local model context window.
        return all
            .OrderBy(x => StableScore(seed, x.TraitId))
            .Take(max)
            .OrderBy(x => x.Label)
            .ToArray();
    }

    private static int StableScore(int seed, string text)
    {
        unchecked
        {
            int hash = seed * 397;
            foreach (var c in text)
                hash = (hash * 31) + char.ToUpperInvariant(c);
            return hash;
        }
    }

    private static string CleanStyle(
        string? proposed,
        IReadOnlyList<string> allowed)
    {
        var value = proposed?.Trim() ?? "";

        if (value.Length == 0 || allowed.Count == 0)
            return value;

        var match = allowed.FirstOrDefault(x =>
            x.Equals(value, StringComparison.OrdinalIgnoreCase));

        return match ?? "";
    }

    private static string ExtractJson(string raw)
    {
        var value = raw?.Trim() ?? "{}";

        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = value.IndexOf('\n');
            if (firstNewLine >= 0)
                value = value[(firstNewLine + 1)..];

            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                value = value[..lastFence];
        }

        var firstBrace = value.IndexOf('{');
        var lastBrace = value.LastIndexOf('}');

        return firstBrace >= 0 && lastBrace >= firstBrace
            ? value[firstBrace..(lastBrace + 1)]
            : "{}";
    }

    private static JsonSerializerOptions JsonOptions() =>
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private sealed record CatalogCandidate(
        string TraitId,
        string Label,
        IReadOnlyList<string> StyleOptions);

    private sealed class ParentTraitResponse
    {
        public List<AiParentTraitChoice> Mid { get; set; } = new();
        public List<AiParentTraitChoice> Slow { get; set; } = new();
    }

    private sealed class AiParentTraitChoice
    {
        public string TraitId { get; set; } = "";
        public int Value { get; set; } = 50;
        public string Style { get; set; } = "";
    }

    private sealed class SubTraitResponse
    {
        public List<AiSubTraitChoice> Values { get; set; } = new();
    }

    private sealed class AiSubTraitChoice
    {
        public string ParentTraitId { get; set; } = "";
        public string SubTraitId { get; set; } = "";
        public string Value { get; set; } = "";
    }

    private sealed class SubRequestDefinition
    {
        public string ParentTraitId { get; init; } = "";
        public string SubTraitId { get; init; } = "";
        public string Label { get; init; } = "";
        public string ValueType { get; init; } = "";
        public string DefaultValue { get; init; } = "";
        public int PickCount { get; init; }
        public int MaxItems { get; init; }
        public OptionDefinition[] Options { get; init; } = Array.Empty<OptionDefinition>();
    }

    private sealed record OptionDefinition(string Value, string Label);
}

public sealed class AiTraitPopulationPreview
{
    public int NpcId { get; init; }
    public int MidTarget { get; init; }
    public int SlowTarget { get; init; }

    public bool RequestedMid { get; init; } = true;
    public bool RequestedSlow { get; init; } = true;

    public int ExistingMidCount { get; set; }
    public int ExistingSlowCount { get; set; }
    public int ResultingMidCount { get; set; }
    public int ResultingSlowCount { get; set; }

    public int RequiredMissingSubTraitCount { get; set; }

    public List<AiTraitSelection> MidSelections { get; } = new();
    public List<AiTraitSelection> SlowSelections { get; } = new();
    public List<AiSubTraitSelection> SubTraitSelections { get; } = new();

    public List<string> HardConflicts { get; } = new();
    public List<string> SoftTensions { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool IsValid =>
        Warnings.All(x =>
            !x.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase)) &&
        HardConflicts.Count == 0 &&
        (!RequestedMid || ResultingMidCount >= MidTarget) &&
        (!RequestedSlow ||
            (ResultingSlowCount >= AiTraitPopulationService.SlowMinimum &&
             ResultingSlowCount <= AiTraitPopulationService.SlowMaximum)) &&
        true;
}

public sealed class AiTraitSelection
{
    public string Group { get; init; } = "";
    public string TraitId { get; init; } = "";
    public string TraitName { get; init; } = "";
    public int Value { get; init; }
    public string Style { get; init; } = "";
}

public sealed class AiSubTraitSelection
{
    public string ParentTraitId { get; init; } = "";
    public string SubTraitId { get; init; } = "";
    public string SubTraitName { get; init; } = "";
    public string ValueType { get; init; } = "";
    public string ValueText { get; init; } = "";
}