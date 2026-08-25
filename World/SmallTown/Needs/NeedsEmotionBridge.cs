using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Characters.Traits;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// NEEDS -> FAST TRAITS -> EMOTIONAL PROFILE
    ///
    /// Uses Project Eve's CURRENT trait system directly:
    ///     NpcTraits.Get / Set / Adjust
    ///     TraitEngine.ApplyTag
    ///
    /// No reflection.
    /// No duplicate emotion database.
    /// Fast traits remain gameplay truth.
    /// </summary>
    public static class NeedsEmotionBridge
    {
        public static EmotionBridgeResult Sync(
            SimCharacter npc,
            EmotionalProfile? emotionalProfile,
            DateTime gameTime,
            string sourceEventId = "")
        {
            var result = new EmotionBridgeResult
            {
                NpcId = npc.Id,
                GameTime = gameTime,
                SourceEventId = sourceEventId ?? ""
            };

            if (npc.Traits == null)
            {
                result.Messages.Add("NpcTraits missing.");
                return result;
            }

            var needs = NeedsSystem.GetState(npc.Id);

            if (needs == null)
            {
                NeedsSystem.EnsureNpc(npc.Id, gameTime);
                needs = NeedsSystem.GetState(npc.Id);
            }

            if (needs == null)
            {
                result.Messages.Add("Needs state unavailable.");
                return result;
            }

            ApplyNeedsToFastTraits(
                npc,
                needs,
                result);

            if (emotionalProfile != null)
            {
                emotionalProfile.SyncFromFast(npc.Traits);

                // NeedsSystem owns physical energy.
                int desiredEnergy =
                    Math.Clamp(
                        (int)Math.Round(needs.Energy),
                        0,
                        100);

                int energyDelta =
                    desiredEnergy - emotionalProfile.Energy;

                if (energyDelta != 0)
                    emotionalProfile.AddEnergy(energyDelta);

                result.ProfileUpdated = true;
                result.DominantMood = emotionalProfile.Mood;
            }

            result.Success = true;
            return result;
        }

        public static EmotionBridgeResult Sync(
            SimCharacter npc,
            Func<SimCharacter, EmotionalProfile?> profileAccessor,
            DateTime gameTime)
        {
            return Sync(
                npc,
                profileAccessor(npc),
                gameTime);
        }

        public static EmotionBridgeResult ApplyHumanEventAndSync(
            SimCharacter npc,
            EmotionalProfile? emotionalProfile,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime)
        {
            NeedsSystem.ApplyEvent(
                npc.Id,
                decision.EventId,
                gameTime);

            return Sync(
                npc,
                emotionalProfile,
                gameTime,
                decision.EventId);
        }

        /// <summary>
        /// Project current body/life pressure into FAST traits only.
        ///
        /// Mid traits are NOT directly changed here.
        /// Slow traits are NEVER changed here.
        ///
        /// Repeated behavior/history may later confirm Mid traits through
        /// the existing mid.*.test / confirm / violate machinery.
        /// </summary>
        private static void ApplyNeedsToFastTraits(
            SimCharacter npc,
            NeedsSystem.NeedState needs,
            EmotionBridgeResult result)
        {
            // --------------------------------------------------------
            // STRESS
            // --------------------------------------------------------
            if (needs.Stress >= 60)
            {
                result.CurrentReasonType = "Need.Stress.High";
                result.CurrentReason = "Stress is above the high-pressure threshold.";
                double pressure =
                    (needs.Stress - 60.0) / 40.0;

                ApplyFast(
                    npc,
                    "trait.tension",
                    2f + (float)(pressure * 6.0),
                    5,
                    result);

                ApplyFast(
                    npc,
                    "trait.anxiety",
                    1f + (float)(pressure * 5.0),
                    5,
                    result);

                ApplyFast(
                    npc,
                    "trait.fear",
                    (float)(pressure * 2.0),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.patience",
                    -(1f + (float)(pressure * 4.0)),
                    5,
                    result);
            }
            else if (needs.Stress <= 30)
            {
                result.CurrentReasonType = "Need.Stress.Low";
                result.CurrentReason = "Stress is in the calm/recovery range.";
                double calm =
                    (30.0 - needs.Stress) / 30.0;

                ApplyFast(
                    npc,
                    "trait.tension",
                    -(1f + (float)(calm * 3.0)),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.anxiety",
                    -(1f + (float)(calm * 2.0)),
                    4,
                    result);
            }

            // --------------------------------------------------------
            // ENERGY
            // --------------------------------------------------------
            if (needs.Energy <= 35)
            {
                result.CurrentReasonType = "Need.Energy.Low";
                result.CurrentReason = "Low physical energy is applying fatigue pressure.";
                double fatigue =
                    (35.0 - needs.Energy) / 35.0;

                ApplyFast(
                    npc,
                    "trait.patience",
                    -(1f + (float)(fatigue * 3.0)),
                    5,
                    result);

                ApplyFast(
                    npc,
                    "trait.tension",
                    (float)(fatigue * 2.0),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.playfulness",
                    -(1f + (float)(fatigue * 4.0)),
                    5,
                    result);
            }

            // --------------------------------------------------------
            // SLEEP DEBT
            // --------------------------------------------------------
            if (needs.SleepDebt >= 60)
            {
                result.CurrentReasonType = "Need.SleepDebt.High";
                result.CurrentReason = "High sleep debt is applying emotional pressure.";
                double debt =
                    (needs.SleepDebt - 60.0) / 40.0;

                ApplyFast(
                    npc,
                    "trait.anxiety",
                    (float)(debt * 2.0),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.tension",
                    1f + (float)(debt * 3.0),
                    5,
                    result);

                ApplyFast(
                    npc,
                    "trait.patience",
                    -(1f + (float)(debt * 3.0)),
                    5,
                    result);
            }

            // --------------------------------------------------------
            // SOCIAL SATISFACTION
            // --------------------------------------------------------
            if (needs.Social <= 30)
            {
                result.CurrentReasonType = "Need.Social.Low";
                result.CurrentReason = "Low social satisfaction is increasing isolation pressure.";
                double lonely =
                    (30.0 - needs.Social) / 30.0;

                ApplyFast(
                    npc,
                    "trait.loneliness",
                    2f + (float)(lonely * 7.0),
                    6,
                    result);

                ApplyFast(
                    npc,
                    "trait.hurt",
                    (float)(lonely * 2.0),
                    4,
                    result);
            }
            else if (needs.Social >= 70)
            {
                result.CurrentReasonType = "Need.Social.High";
                result.CurrentReason = "Strong social satisfaction is reducing isolation pressure.";
                double connected =
                    (needs.Social - 70.0) / 30.0;

                ApplyFast(
                    npc,
                    "trait.loneliness",
                    -(2f + (float)(connected * 5.0)),
                    5,
                    result);

                ApplyFast(
                    npc,
                    "trait.hope",
                    (float)(connected * 2.0),
                    4,
                    result);
            }

            // --------------------------------------------------------
            // FUN / BOREDOM
            // --------------------------------------------------------
            if (needs.Fun <= 30)
            {
                result.CurrentReasonType = "Need.Fun.Low";
                result.CurrentReason = "Low fun satisfaction is creating boredom pressure.";
                double bored =
                    (30.0 - needs.Fun) / 30.0;

                ApplyFast(
                    npc,
                    "trait.playfulness",
                    -(1f + (float)(bored * 3.0)),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.tension",
                    (float)bored,
                    3,
                    result);
            }
            else if (needs.Fun >= 70)
            {
                result.CurrentReasonType = "Need.Fun.High";
                result.CurrentReason = "High fun satisfaction is supporting playfulness and hope.";
                double fulfilled =
                    (needs.Fun - 70.0) / 30.0;

                ApplyFast(
                    npc,
                    "trait.playfulness",
                    1f + (float)(fulfilled * 3.0),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.hope",
                    (float)(fulfilled * 2.0),
                    3,
                    result);
            }

            // --------------------------------------------------------
            // COMFORT
            // --------------------------------------------------------
            if (needs.Comfort <= 25)
            {
                result.CurrentReasonType = "Need.Comfort.Low";
                result.CurrentReason = "Low physical comfort is increasing emotional pressure.";
                double discomfort =
                    (25.0 - needs.Comfort) / 25.0;

                ApplyFast(
                    npc,
                    "trait.tension",
                    1f + (float)(discomfort * 3.0),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.anxiety",
                    (float)(discomfort * 2.0),
                    4,
                    result);
            }
            else if (needs.Comfort >= 75)
            {
                result.CurrentReasonType = "Need.Comfort.High";
                result.CurrentReason = "High physical comfort is reducing tension.";
                double comfortable =
                    (needs.Comfort - 75.0) / 25.0;

                ApplyFast(
                    npc,
                    "trait.tension",
                    -(1f + (float)(comfortable * 2.0)),
                    4,
                    result);
            }

            // --------------------------------------------------------
            // HUNGER
            // --------------------------------------------------------
            if (needs.Hunger >= 60)
            {
                result.CurrentReasonType = "Need.Hunger.High";
                result.CurrentReason = "High hunger is reducing patience and increasing tension.";
                double hunger =
                    (needs.Hunger - 60.0) / 40.0;

                ApplyFast(
                    npc,
                    "trait.patience",
                    -(1f + (float)(hunger * 2.0)),
                    4,
                    result);

                ApplyFast(
                    npc,
                    "trait.tension",
                    (float)(hunger * 1.5),
                    3,
                    result);
            }

            // --------------------------------------------------------
            // HYGIENE
            // --------------------------------------------------------
            if (needs.Hygiene <= 25)
            {
                result.CurrentReasonType = "Need.Hygiene.Low";
                result.CurrentReason = "Low hygiene is increasing self-conscious defensive pressure.";
                double hygienePressure =
                    (25.0 - needs.Hygiene) / 25.0;

                ApplyFast(
                    npc,
                    "trait.guard",
                    (float)(hygienePressure * 2.0),
                    3,
                    result);

                ApplyFast(
                    npc,
                    "trait.shame",
                    (float)hygienePressure,
                    3,
                    result);
            }
        }

        /// <summary>
        /// Use the real TraitEngine so Project Eve's existing intensity scaling,
        /// extreme resistance and Mid/Slow rules stay centralized.
        /// </summary>
        private static void ApplyFast(
            SimCharacter npc,
            string traitId,
            float delta,
            int intensity,
            EmotionBridgeResult result)
        {
            if (Math.Abs(delta) < 0.01f || npc.Traits == null)
                return;

            // Safety: this bridge only touches Fast traits.
            if (!Array.Exists(
                TraitEngine.FastIds,
                x => string.Equals(
                    x,
                    traitId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                result.Messages.Add(
                    $"Skipped non-Fast trait: {traitId}");

                return;
            }

            float before =
                npc.Traits.Get(traitId);

            TraitEngine.ApplyTag(
                npc,
                traitId,
                delta,
                intensity);

            float after =
                npc.Traits.Get(traitId);

            if (Math.Abs(after - before) >= 0.001f)
            {
                // MAIN keeps the canonical current intensity.
                NpcTraitRepository.SaveOne(
                    npc.Id,
                    traitId,
                    after);

                // RELATIONSHIPS keeps the subjective reason/evidence trail.
                NpcTraitRepository.RecordCausalChange(
                    npc.Id,
                    traitId,
                    before,
                    after,
                    result.GameTime,
                    result.CurrentReasonType,
                    result.CurrentReason,
                    sourceType: string.IsNullOrWhiteSpace(result.SourceEventId)
                        ? "NeedsSystem"
                        : "HumanEvent+Needs",
                    sourceEventId: result.SourceEventId);

                if (result.TraitChanges.TryGetValue(traitId, out var existing))
                {
                    existing.After = after;

                    if (!string.IsNullOrWhiteSpace(result.CurrentReason) &&
                        !existing.Reasons.Contains(
                            result.CurrentReason,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        existing.Reasons.Add(result.CurrentReason);
                    }
                }
                else
                {
                    var change = new TraitChange
                    {
                        Before = before,
                        After = after
                    };

                    if (!string.IsNullOrWhiteSpace(result.CurrentReason))
                        change.Reasons.Add(result.CurrentReason);

                    result.TraitChanges[traitId] = change;
                }
            }
        }

        public sealed class EmotionBridgeResult
        {
            public bool Success { get; set; }
            public int NpcId { get; set; }
            public DateTime GameTime { get; set; }
            public string SourceEventId { get; set; } = "";

            internal string CurrentReasonType { get; set; } = "";
            internal string CurrentReason { get; set; } = "";

            public bool ProfileUpdated { get; set; }
            public string DominantMood { get; set; } = "";

            public Dictionary<string, TraitChange> TraitChanges { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public List<string> Messages { get; } =
                new();
        }

        public sealed class TraitChange
        {
            public float Before { get; set; }
            public float After { get; set; }
            public float Delta => After - Before;

            public List<string> Reasons { get; } = new();

            public override string ToString()
                => $"{Before:0.0} -> {After:0.0} ({Delta:+0.0;-0.0;0.0})";
        }
    }
}
