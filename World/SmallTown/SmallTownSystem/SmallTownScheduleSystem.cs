using ProjectEve.Characters.Characters;
using ProjectEve.Worlds.SmallTown;

namespace ProjectEve.Worlds.SmallTownSystems
{
    public static class SmallTownScheduleSystem
    {
        private static readonly Random rng = new();

        // ============================================================
        // SHIFT DEFINITIONS
        // ============================================================

        private static readonly List<SmallTownShift> IndustrialShifts = new()
        {
            new SmallTownShift { Name = "Industrial Early", Start = "6:00 AM", End = "2:30 PM" },
            new SmallTownShift { Name = "Industrial Mid", Start = "3:00 PM", End = "11:00 PM" },
            new SmallTownShift { Name = "Industrial Night", Start = "11:00 PM", End = "7:00 AM" }
        };

        private static readonly List<SmallTownShift> EmergencyShifts = new()
        {
            new SmallTownShift { Name = "Emergency Early", Start = "6:00 AM", End = "2:00 PM" },
            new SmallTownShift { Name = "Emergency Mid", Start = "2:00 PM", End = "10:00 PM" },
            new SmallTownShift { Name = "Emergency Night", Start = "10:00 PM", End = "6:00 AM" }
        };

        private static readonly List<SmallTownShift> RetailShifts = new()
        {
            new SmallTownShift { Name = "Retail Morning", Start = "6:00 AM", End = "2:00 PM" },
            new SmallTownShift { Name = "Retail Evening", Start = "2:00 PM", End = "10:00 PM" }
        };

        private static readonly List<SmallTownShift> TeacherShifts = new()
        {
            new SmallTownShift { Name = "Teacher Standard", Start = "7:00 AM", End = "3:00 PM" },
            new SmallTownShift { Name = "Teacher Extended", Start = "8:00 AM", End = "4:00 PM" }
        };

        private static readonly List<SmallTownShift> OfficeShifts = new()
        {
            new SmallTownShift { Name = "Office Standard", Start = "9:00 AM", End = "5:00 PM" },
            new SmallTownShift { Name = "Office Early", Start = "7:00 AM", End = "3:00 PM" },
            new SmallTownShift { Name = "Office Late", Start = "11:00 AM", End = "7:00 PM" }
        };

        // ============================================================
        // DAY TYPE HELPERS
        // ============================================================

        private static bool IsWeekend(DayOfWeek day)
        {
            return day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
        }

        private static bool IsHolidayBreak()
        {
            return rng.NextDouble() < 0.05; // 5% chance
        }

        private static bool IsSpringBreak()
        {
            return rng.NextDouble() < 0.03; // 3% chance
        }

        private static bool IsSummerBreak()
        {
            return rng.NextDouble() < 0.10; // 10% chance
        }

        private static bool RandomDayOff()
        {
            return rng.NextDouble() < 0.20; // 20% chance
        }

        // ============================================================
        // MAIN SCHEDULE GENERATOR
        // ============================================================

        public static void GenerateDailySchedule(Town world)
        {
            foreach (var npc in world.Residents)
            {
                DayOfWeek today = (DayOfWeek)rng.Next(0, 7);

                bool weekend = IsWeekend(today);
                bool holidayBreak = IsHolidayBreak();
                bool springBreak = IsSpringBreak();
                bool summerBreak = IsSummerBreak();
                bool randomOff = RandomDayOff();

                // ============================================================
                // TEACHERS
                // ============================================================

                if (npc.Occupation == "Teacher")
                {
                    if (weekend || holidayBreak || springBreak || summerBreak)
                    {
                        npc.Schedule = new List<string>
                        {
                            "Day off (Teacher)",
                            "Sleep in",
                            "Relax at home",
                            "Plan lessons",
                            "Visit family",
                            "Enjoy free time"
                        };
                        continue;
                    }

                    var shift = TeacherShifts[rng.Next(TeacherShifts.Count)];

                    npc.Schedule = new List<string>
                    {
                        $"Wake up before {shift.Start}",
                        $"Teach classes from {shift.Start} to {shift.End}",
                        "Grade papers",
                        "Go home",
                        "Dinner",
                        "Relax",
                        "Sleep"
                    };

                    continue;
                }

                // ============================================================
                // OFFICE WORKERS
                // ============================================================

                if (npc.Occupation == "Office Worker")
                {
                    if (weekend || holidayBreak)
                    {
                        npc.Schedule = new List<string>
                        {
                            "Day off (Office Worker)",
                            "Sleep in",
                            "Relax",
                            "Run errands",
                            "Enjoy free time"
                        };
                        continue;
                    }

                    var shift = OfficeShifts[rng.Next(OfficeShifts.Count)];

                    npc.Schedule = new List<string>
                    {
                        $"Wake up before {shift.Start}",
                        $"Work from {shift.Start} to {shift.End}",
                        "Go home",
                        "Dinner",
                        "Relax",
                        "Sleep"
                    };

                    continue;
                }

                // ============================================================
                // FACTORY WORKERS
                // ============================================================

                if (npc.Occupation == "Factory Worker" ||
                    npc.Occupation == "Machine Operator" ||
                    npc.Occupation == "Welder" ||
                    npc.Occupation == "Forklift Driver" ||
                    npc.Occupation == "Assembly Line Worker")
                {
                    if (weekend || holidayBreak)
                    {
                        npc.Schedule = new List<string>
                        {
                            "Day off (Factory Worker)",
                            "Sleep in",
                            "Relax",
                            "Visit stores",
                            "Enjoy free time"
                        };
                        continue;
                    }

                    var shift = IndustrialShifts[rng.Next(IndustrialShifts.Count)];

                    npc.Schedule = new List<string>
                    {
                        $"Wake up before {shift.Start}",
                        $"Work from {shift.Start} to {shift.End}",
                        "Go home",
                        "Dinner",
                        "Relax",
                        "Sleep"
                    };

                    continue;
                }

                // ============================================================
                // RETAIL / FOOD / GAS
                // ============================================================

                if (npc.Occupation == "Cashier" ||
                    npc.Occupation == "Waiter" ||
                    npc.Occupation == "Cook" ||
                    npc.Occupation == "Dollar General Clerk" ||
                    npc.Occupation == "Gas Station Attendant")
                {
                    if (randomOff)
                    {
                        npc.Schedule = new List<string>
                        {
                            "Day off (Retail)",
                            "Sleep in",
                            "Relax",
                            "Visit friends",
                            "Enjoy free time"
                        };
                        continue;
                    }

                    var shift = RetailShifts[rng.Next(RetailShifts.Count)];

                    npc.Schedule = new List<string>
                    {
                        $"Wake up before {shift.Start}",
                        $"Work from {shift.Start} to {shift.End}",
                        "Go home",
                        "Dinner",
                        "Relax",
                        "Sleep"
                    };

                    continue;
                }

                // ============================================================
                // EMERGENCY SERVICES
                // ============================================================

                if (npc.Occupation == "Police Officer" ||
                    npc.Occupation == "Firefighter" ||
                    npc.Occupation == "EMT")
                {
                    var shift = EmergencyShifts[rng.Next(EmergencyShifts.Count)];

                    npc.Schedule = new List<string>
                    {
                        $"Wake up before {shift.Start}",
                        $"Emergency shift from {shift.Start} to {shift.End}",
                        "Go home",
                        "Dinner",
                        "Relax",
                        "Sleep"
                    };

                    continue;
                }

                // ============================================================
                // DEFAULT JOBS
                // ============================================================

                npc.Schedule = new List<string>
                {
                    "Wake up at 8:00 AM",
                    "Work 9:00 AM – 5:00 PM",
                    "Go home",
                    "Dinner",
                    "Relax",
                    "Sleep"
                };
            }
        }
    }
}
