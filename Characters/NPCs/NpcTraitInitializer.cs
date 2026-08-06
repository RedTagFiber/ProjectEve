using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.Linq;

public static class NpcTraitInitializer
{
    private static readonly Random Rng = new();

    public static void GenerateBalancedTraits(NpcTraits traits)
    {
        traits.InitializeFromRegistry();

        var all = TraitRegistry.AllTraits
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .ToList();

        if (all.Count == 0)
            return;

        // Start everyone low-average so defaults aren't flat 50 forever
        foreach (var def in all)
            traits.Set(def.Id, Rng.Next(8, 28));

        // Group by category
        var byCategory = all
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "General" : t.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 1) Pick signature traits across different categories
        var signature = PickAcrossCategories(byCategory, take: 10);
        foreach (var t in signature)
            traits.Set(t.Id, Rng.Next(72, 93));

        // 2) Strong traits, still spread
        var strong = PickAcrossCategories(byCategory, take: 20, excludeIds: signature.Select(x => x.Id));
        foreach (var t in strong)
            traits.Set(t.Id, Rng.Next(55, 71));

        // 3) Average band
        var used = new HashSet<string>(signature.Concat(strong).Select(x => x.Id));
        var averagePool = all.Where(t => !used.Contains(t.Id)).OrderBy(_ => Rng.Next()).Take(40);
        foreach (var t in averagePool)
            traits.Set(t.Id, Rng.Next(34, 55));

        // 4) Soft opposing pulls so personality isn't one-note
        SoftBalanceOpposites(traits);
    }

    private static List<TraitRegistry.TraitDefinition> PickAcrossCategories(
        Dictionary<string, List<TraitRegistry.TraitDefinition>> byCategory,
        int take,
        IEnumerable<string>? excludeIds = null)
    {
        var exclude = new HashSet<string>(excludeIds ?? Enumerable.Empty<string>());
        var result = new List<TraitRegistry.TraitDefinition>();

        // round-robin categories
        var cats = byCategory.Keys.OrderBy(_ => Rng.Next()).ToList();
        int guard = 0;

        while (result.Count < take && guard < take * 20)
        {
            guard++;
            foreach (var cat in cats)
            {
                if (result.Count >= take) break;

                // max 2 signature/strong picks per category in this pass wave
                int already = result.Count(r => r.Category == cat);
                if (already >= 2) continue;

                var pick = byCategory[cat]
                    .Where(t => !exclude.Contains(t.Id) && result.All(r => r.Id != t.Id))
                    .OrderBy(_ => Rng.Next())
                    .FirstOrDefault();

                if (pick != null)
                    result.Add(pick);
            }

            // if categories exhausted on caps, loosen cap
            if (result.Count < take)
            {
                foreach (var cat in cats)
                {
                    if (result.Count >= take) break;
                    var pick = byCategory[cat]
                        .Where(t => !exclude.Contains(t.Id) && result.All(r => r.Id != t.Id))
                        .OrderBy(_ => Rng.Next())
                        .FirstOrDefault();
                    if (pick != null)
                        result.Add(pick);
                }
            }
        }

        return result;
    }

    private static void SoftBalanceOpposites(NpcTraits traits)
    {
        BalancePair(traits, "trait.introversion", "trait.extroversion");
        BalancePair(traits, "trait.optimism", "trait.pessimism");
        BalancePair(traits, "trait.confidence", "trait.insecurity");
        BalancePair(traits, "trait.dominance", "trait.submission");
        BalancePair(traits, "trait.sexualShame", "trait.sexualConfidence");
    }

    private static void BalancePair(NpcTraits traits, string a, string b)
    {
        float va = traits.Get(a);
        float vb = traits.Get(b);

        // if both high, push one down
        if (va > 65 && vb > 65)
        {
            if (Rng.Next(2) == 0)
                traits.Set(b, Rng.Next(20, 45));
            else
                traits.Set(a, Rng.Next(20, 45));
        }
    }
}