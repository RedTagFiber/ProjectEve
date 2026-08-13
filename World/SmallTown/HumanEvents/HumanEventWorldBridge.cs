using ProjectEve.Characters.Base;
using System;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Small adapter between Project Eve's live NPC/world state and HumanEventEngine.
    ///
    /// The bridge intentionally does NOT invent opportunities.
    /// The owning world systems add facts such as "in_store", "active_conflict",
    /// "patient_present", etc. Only real world state should create those tags.
    /// </summary>
    public static class HumanEventWorldBridge
    {
        public static HumanEventEngine.HumanEventContext CreateContext(
            SimCharacter actor,
            DateTime gameTime,
            int simulationTier,
            SimCharacter? target = null,
            string? locationId = null)
        {
            var ctx = new HumanEventEngine.HumanEventContext
            {
                SimulationTier = simulationTier,
                Target = target,
                LocationId = locationId ?? actor.Location,
                GameTime = gameTime
            };

            // Use the NPC's existing Project Eve JobProfile.
            try
            {
                if (actor.Job != null &&
                    !actor.Job.IsUnemployed &&
                    !actor.Job.IsRetired &&
                    !string.IsNullOrWhiteSpace(actor.Job.JobName))
                {
                    ctx.Tags.Add("has_job");

                    if (actor.Job.IsWorkingAt(gameTime))
                    {
                        ctx.IsOnDuty = true;
                        ctx.Tags.Add("on_duty");
                        ctx.Tags.Add("scheduled_to_work");
                    }
                }
            }
            catch
            {
                // Exact schedule can be supplied by SmallTownEmploymentSystem instead.
                // A failure here should never fabricate on-duty authority.
                ctx.IsOnDuty = false;
            }

            return ctx;
        }

        /// <summary>
        /// Convenience method for a deep-thinking pass.
        /// The caller should add real opportunity tags BEFORE calling Decide().
        /// </summary>
        public static HumanEventEngine.HumanEventDecision Decide(
            SimCharacter actor,
            DateTime gameTime,
            int simulationTier,
            Action<HumanEventEngine.HumanEventContext>? addWorldFacts = null,
            SimCharacter? target = null,
            string? locationId = null)
        {
            var ctx = CreateContext(
                actor,
                gameTime,
                simulationTier,
                target,
                locationId);

            addWorldFacts?.Invoke(ctx);

            return HumanEventEngine.Decide(actor, ctx);
        }
    }
}
