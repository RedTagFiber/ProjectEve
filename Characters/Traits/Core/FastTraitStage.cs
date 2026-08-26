using System;
using System.Collections.Generic;

namespace ProjectEve.Traits
{
    /// <summary>
    /// Canonical derived Fast20 stage.
    ///
    /// This is NOT persisted. CurrentValue remains canonical truth.
    /// Stage and Wildcard are always derived from the current 0-100 value.
    /// </summary>
    public sealed record FastTraitStage(
        int Min,
        int Max,
        int Stage,
        string Label,
        bool IsWildcard);

    public static class FastTraitStageRules
    {
        private static readonly IReadOnlyList<FastTraitStage> _all =
            new[]
            {
                new FastTraitStage(0, 20, 1, "Stage 1", false),
                new FastTraitStage(21, 40, 2, "Stage 2", false),
                new FastTraitStage(41, 60, 3, "Stage 3", false),
                new FastTraitStage(61, 80, 4, "Stage 4", false),
                new FastTraitStage(81, 94, 5, "Stage 5", false),
                new FastTraitStage(95, 100, 6, "Wildcard", true)
            };

        public static IReadOnlyList<FastTraitStage> All => _all;

        public static FastTraitStage Get(float value)
        {
            float clamped = Math.Clamp(value, 0f, 100f);

            foreach (var stage in _all)
            {
                if (clamped >= stage.Min && clamped <= stage.Max)
                    return stage;
            }

            // Math.Clamp guarantees this should never be reached.
            return _all[^1];
        }

        public static bool IsWildcard(float value)
        {
            return Get(value).IsWildcard;
        }
    }
}
