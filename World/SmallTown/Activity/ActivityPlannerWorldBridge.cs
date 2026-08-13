using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Runs ActivityPlanner as the authoritative optional-life layer for the fast world loop.
    ///
    /// Call every ~5 game minutes.
    ///
    /// If an NPC already has an active plan, it simply applies that plan to
    /// NpcWorldActivity. If the plan ended, it builds the next one.
    /// </summary>
    public static class ActivityPlannerWorldBridge
    {
        public static ActivityTickResult Tick(
            DateTime gameTime,
            IEnumerable<SimCharacter> npcs,
            Func<SimCharacter, ActivityPlanner.PlannerContext>? contextBuilder = null,
            Random? rng = null)
        {
            ActivityPlanner.Initialize();
            rng ??= Random.Shared;

            var result = new ActivityTickResult
            {
                GameTime = gameTime
            };

            foreach (var npc in npcs)
            {
                if (npc == null || npc.Tier >= 5)
                    continue;

                var active = ActivityPlanner.GetCurrentPlan(
                    npc.Id,
                    gameTime);

                if (active == null)
                {
                    var ctx = contextBuilder?.Invoke(npc)
                              ?? new ActivityPlanner.PlannerContext();

                    active = ActivityPlanner.PlanNext(
                        npc,
                        gameTime,
                        ctx,
                        rng);

                    if (active.HasActivity)
                        result.NewPlans++;
                }

                if (active.HasActivity)
                {
                    ActivityPlanner.ApplyCurrentPlanToWorld(
                        npc.Id,
                        gameTime);

                    npc.Location = active.LocationId;
                    result.NpcsUpdated++;
                }
            }

            return result;
        }

        public sealed class ActivityTickResult
        {
            public DateTime GameTime { get; set; }
            public int NpcsUpdated { get; set; }
            public int NewPlans { get; set; }
        }
    }
}
