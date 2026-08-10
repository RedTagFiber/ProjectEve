using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Rolls a balanced Fast + Mid + Slow bag for an NPC.
/// Does not use old TraitRegistry fill-all.
/// </summary>
public static class NpcTraitInitializer
{
    private static readonly Random Rng = new();

    /// <summary>
    /// Preferred path: TraitJson mid/slow + Fast scatter.
    /// </summary>
    public static void GenerateBalancedTraits(NpcTraits traits)
    {
        if (traits == null)
            return;

        try
        {
            TraitJsonLoader.ApplyRolledLayers(traits, Rng);
            SoftBalanceFastOpposites(traits);
        }
        catch
        {
            // JSON missing / loader fail → Fast only
            GenerateFastOnly(traits);
        }
    }

    /// <summary>
    /// Fast 20 only, with a few highs and soft opposites.
    /// </summary>
    public static void GenerateFastOnly(NpcTraits traits)
    {
        if (traits == null)
            return;

        var fast = TraitJsonLoader.BuildFastDefaults(42f);
        var keys = fast.Keys.ToList();

        foreach (var k in keys)
            fast[k] = Clamp(fast[k] + Rng.Next(-6, 7));

        // Signature highs (3–5)
        Shuffle(keys);
        int sig = Rng.Next(3, 6);
        for (int i = 0; i < sig && i < keys.Count; i++)
            fast[keys[i]] = Rng.Next(70, 92);

        // A few lows
        for (int i = sig; i < sig + 3 && i < keys.Count; i++)
            fast[keys[i]] = Rng.Next(15, 32);

        traits.InitializeFromLayers(fast, null, null);
        SoftBalanceFastOpposites(traits);
    }

    /// <summary>
    /// Explicit layers when factory already picked mid/slow ids.
    /// </summary>
    public static void ApplyLayers(
        NpcTraits traits,
        IDictionary<string, float> fast,
        IDictionary<string, float>? mid = null,
        IDictionary<string, float>? slow = null)
    {
        if (traits == null || fast == null)
            return;

        traits.InitializeFromLayers(fast, mid, slow);
        SoftBalanceFastOpposites(traits);
    }

    // ------------------------------------------------------------------
    // Fast opposition soft-balance (same ladder, not old personality pairs)
    // ------------------------------------------------------------------
    private static void SoftBalanceFastOpposites(NpcTraits traits)
    {
        // Guard vs openness
        BalancePair(traits, "trait.guard", "trait.openness");
        // Fear/anxiety vs hope
        BalancePair(traits, "trait.fear", "trait.hope");
        BalancePair(traits, "trait.anxiety", "trait.hope");
        // Hurt vs affection (can coexist, but both extreme is rare)
        BalancePair(traits, "trait.hurt", "trait.affection");
        // Anger vs patience
        BalancePair(traits, "trait.anger", "trait.patience");
        // Shame vs pride
        BalancePair(traits, "trait.shame", "trait.pride");
    }

    private static void BalancePair(NpcTraits traits, string a, string b)
    {
        float va = traits.Get(a);
        float vb = traits.Get(b);

        if (va > 68 && vb > 68)
        {
            if (Rng.Next(2) == 0)
                traits.Set(b, Rng.Next(22, 48));
            else
                traits.Set(a, Rng.Next(22, 48));
        }
    }

    private static float Clamp(float v) => Math.Clamp(v, 0f, 100f);

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}