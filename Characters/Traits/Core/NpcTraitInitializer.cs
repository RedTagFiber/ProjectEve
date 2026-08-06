using ProjectEve.Traits;
using System;
using System.Collections.Generic;
namespace ProjectEve.Traits;

public static class NpcTraitInitializer
{
    private static readonly Random rng = new();

    public static List<NpcTrait> GenerateTraitsForNpc(int npcId)
    {
        var traits = new List<NpcTrait>();

        foreach (var def in TraitRegistry.AllTraits)
        {
            if (!int.TryParse(def.Id, out var parsedTraitId))
                throw new InvalidOperationException($"TraitDefinition.Id '{def.Id}' is not a valid integer.");

            traits.Add(new NpcTrait
            {
                NpcId = npcId,
                TraitId = parsedTraitId,
                Intensity = GenerateInitialIntensity(),
                Control = GenerateInitialControl()
            });
        }

        return traits;
    }

    // ============================================================
    // REALISTIC TRAIT DISTRIBUTION
    // ============================================================
    private static int GenerateInitialIntensity()
    {
        int roll = rng.Next(0, 100);

        if (roll < 60)
            return rng.Next(1, 11);      // 60% chance → LOW trait (1–10)

        if (roll < 90)
            return rng.Next(20, 41);     // 30% chance → MEDIUM trait (20–40)

        return rng.Next(60, 81);         // 10% chance → HIGH trait (60–80)
    }

    private static int GenerateInitialControl()
    {
        // Control is usually higher than intensity
        int roll = rng.Next(0, 100);

        if (roll < 50)
            return rng.Next(40, 71);     // 50% chance → medium control

        if (roll < 85)
            return rng.Next(20, 41);     // 35% chance → low control

        return rng.Next(70, 101);        // 15% chance → high control
    }
}
