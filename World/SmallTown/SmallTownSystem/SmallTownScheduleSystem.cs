using ProjectEve.Characters.Base;
using ProjectEve.Worlds.SmallTown;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Replacement for the occupation-name switch schedule generator.
    /// All jobs now use the NPC's actual assigned employment schedule.
    ///
    /// Existing call SmallTownScheduleSystem.GenerateDailySchedule(world) still works.
    /// </summary>
    public static class SmallTownScheduleSystem
    {
        public static void GenerateDailySchedule(Town world, DateTime? localDateTime = null)
        {
            DateTime now = localDateTime ?? DateTime.Now;

            foreach (var npc in world.Residents)
                GenerateNpcDailySchedule(npc, now);
        }

        public static void GenerateNpcDailySchedule(SimCharacter npc, DateTime localDateTime)
        {
            npc.Schedule ??= new List<string>();
            npc.Schedule.Clear();

            if (npc.Job == null || npc.Job.IsRetired)
            {
                npc.Schedule.Add("No work shift today");
                npc.Schedule.Add("Personal / family time");
                return;
            }

            if (npc.Job.IsUnemployed)
            {
                npc.Schedule.Add("No job today");
                npc.Schedule.Add("Job search / errands / family time");
                return;
            }

            var employment = SmallTownEmploymentSystem.GetEmployment(npc.Id);

            if (employment == null)
            {
                // Legacy NPC fallback: preserve compatibility with existing seeded characters.
                BuildLegacyJobSchedule(npc, localDateTime);
                return;
            }

            bool scheduledDay = false;
            string day = localDateTime.ToString("ddd", CultureInfo.InvariantCulture);

            foreach (var d in employment.WorkDays)
            {
                if (string.Equals(d, day, StringComparison.OrdinalIgnoreCase))
                {
                    scheduledDay = true;
                    break;
                }
            }

            if (!scheduledDay)
            {
                npc.Schedule.Add($"Day off from {employment.WorkplaceName}");
                npc.Schedule.Add("Personal / family time");
                npc.Schedule.Add("Errands / social time");
                return;
            }

            bool workingRightNow = SmallTownEmploymentSystem.IsNpcWorking(npc.Id, localDateTime);

            npc.Schedule.Add($"Work day: {employment.WorkplaceName}");
            npc.Schedule.Add($"{npc.Job.JobName}: {employment.Start} - {employment.End}");

            if (workingRightNow)
                npc.Schedule.Add("CURRENT: at work");
            else
                npc.Schedule.Add("CURRENT: off the clock");

            npc.Schedule.Add("Commute home");
            npc.Schedule.Add("Personal / family time");
            npc.Schedule.Add("Sleep");
        }

        private static void BuildLegacyJobSchedule(SimCharacter npc, DateTime localDateTime)
        {
            string day = localDateTime.ToString("ddd", CultureInfo.InvariantCulture);
            bool worksToday = false;

            if (npc.Job.WorkDays != null)
            {
                foreach (var d in npc.Job.WorkDays)
                {
                    if (string.Equals(d, day, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(d, "varies", StringComparison.OrdinalIgnoreCase))
                    {
                        worksToday = true;
                        break;
                    }
                }
            }

            if (!worksToday)
            {
                npc.Schedule.Add("Day off");
                npc.Schedule.Add("Personal / family time");
                return;
            }

            npc.Schedule.Add($"Work: {npc.Job.StartHour:00}:00 - {npc.Job.EndHour:00}:00");
            npc.Schedule.Add(npc.Job.IsWorkingAt(localDateTime) ? "CURRENT: at work" : "CURRENT: off the clock");
            npc.Schedule.Add("Go home");
            npc.Schedule.Add("Personal / family time");
            npc.Schedule.Add("Sleep");
        }
    }
}
