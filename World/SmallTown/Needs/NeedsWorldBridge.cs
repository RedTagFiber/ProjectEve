using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Connects NeedsSystem to ActivityPlanner, WorldActivityEngine and HumanEventEngine.
    /// </summary>
    public static class NeedsWorldBridge
    {
        /// <summary>
        /// Call from the same fast world loop as ActivityPlanner (~5 game minutes).
        ///
        /// The NPC's current world activity drives need changes automatically.
        /// </summary>
        public static NeedsTickResult TickPopulation(
            DateTime gameTime,
            IEnumerable<SimCharacter> npcs)
        {
            NeedsSystem.Initialize();

            var result = new NeedsTickResult
            {
                GameTime = gameTime
            };

            foreach (var npc in npcs)
            {
                if (npc == null || npc.Tier >= 5)
                    continue;

                string activity =
                    WorldActivityEngine.GetState(npc.Id)?.Activity
                    ?? ActivityPlanner.GetCurrentPlan(npc.Id, gameTime)?.ActivityId
                    ?? "idle";

                NeedsSystem.TickNpc(
                    npc,
                    gameTime,
                    activity);

                result.NpcsUpdated++;
            }

            return result;
        }

        /// <summary>
        /// Build planner context with need tags already included.
        ///
        /// The caller can then add real friend/family/date/appointment facts.
        /// </summary>
        public static ActivityPlanner.PlannerContext BuildPlannerContext(
            SimCharacter npc,
            Action<ActivityPlanner.PlannerContext>? addWorldFacts = null)
        {
            var ctx =
                NeedsSystem.AddToPlannerContext(
                    npc);

            addWorldFacts?.Invoke(ctx);

            return ctx;
        }

        /// <summary>
        /// Add current needs to an existing HumanEvent context.
        /// </summary>
        public static HumanEventEngine.HumanEventContext AddHumanEventNeeds(
            SimCharacter npc,
            HumanEventEngine.HumanEventContext context)
        {
            return NeedsSystem.AddToHumanEventContext(
                npc,
                context);
        }

        /// <summary>
        /// Call after a HumanEvent actually happened.
        ///
        /// Only events listed in needs_system.json eventEffects change needs.
        /// Unknown events are harmless no-ops.
        /// </summary>
        public static NeedsSystem.NeedState ApplyHumanEvent(
            int npcId,
            HumanEventEngine.HumanEventDecision decision,
            DateTime gameTime)
        {
            return NeedsSystem.ApplyEvent(
                npcId,
                decision.EventId,
                gameTime);
        }

        public sealed class NeedsTickResult
        {
            public DateTime GameTime { get; set; }
            public int NpcsUpdated { get; set; }
        }
    }
}
