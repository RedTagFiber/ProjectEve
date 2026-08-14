using System;

namespace ProjectEve.Characters.NPCs.Body
{
    /// <summary>
    /// Lightweight current-body simulation. This owns physical state movement only.
    /// NeedsSystem can consume the resulting pressures; it should remain the owner of life-need decisions.
    /// </summary>
    public static class BodyStateService
    {
        public static void ApplyActivity(HumanBodyProfile body, string activityId, double intensity = 1.0)
        {
            if (body == null || string.IsNullOrWhiteSpace(activityId)) return;
            intensity = Math.Clamp(intensity, 0.0, 3.0);
            var s = body.State;

            switch (activityId.Trim().ToLowerInvariant())
            {
                case "walk":
                case "walking":
                    Add(s, sweat:8, heat:8, fatigue:3, thirst:5, hairMess:2, odor:2, clothes:-1, intensity);
                    break;

                case "brisk_walk":
                    Add(s, sweat:18, heat:15, fatigue:7, thirst:10, hairMess:4, odor:4, clothes:-3, intensity);
                    break;

                case "run":
                case "running":
                    Add(s, sweat:55, heat:35, fatigue:22, thirst:25, hairMess:20, odor:12, clothes:-18, intensity);
                    s.SweatWetness = Clamp(s.SweatWetness + 55 * intensity);
                    s.Breathlessness = Clamp(s.Breathlessness + 55 * intensity);
                    s.ClothingDampness = Clamp(s.ClothingDampness + 45 * intensity);
                    break;

                case "workout":
                case "weight_training":
                    Add(s, sweat:40, heat:25, fatigue:25, thirst:20, hairMess:12, odor:10, clothes:-15, intensity);
                    s.MuscleSoreness = Clamp(s.MuscleSoreness + 15 * intensity);
                    break;

                case "physical_labor":
                    Add(s, sweat:65, heat:25, fatigue:40, thirst:40, hairMess:25, odor:30, clothes:-50, intensity);
                    s.Dirt = Clamp(s.Dirt + 25 * intensity);
                    s.Grime = Clamp(s.Grime + 30 * intensity);
                    break;

                case "yard_work":
                    Add(s, sweat:55, heat:30, fatigue:25, thirst:30, hairMess:25, odor:20, clothes:-40, intensity);
                    s.Dirt = Clamp(s.Dirt + 35 * intensity);
                    s.Grime = Clamp(s.Grime + 20 * intensity);
                    break;

                case "dance":
                case "dancing":
                    Add(s, sweat:45, heat:25, fatigue:20, thirst:25, hairMess:15, odor:15, clothes:-20, intensity);
                    break;

                case "sex":
                case "adult_sexual_activity":
                    if (body.Identity.AgeYears < 18 || !body.AdultPrivate.Enabled) return;
                    Add(s, sweat:25, heat:20, fatigue:12, thirst:8, hairMess:12, odor:5, clothes:-8, intensity);
                    s.SexualAftercareCleanupNeed = Clamp(
                        s.SexualAftercareCleanupNeed +
                        CalculateAftercareCleanupPressure(body) * .65 * intensity);
                    break;
            }

            RecomputeCleanliness(body);
        }

        public static void Tick(HumanBodyProfile body, TimeSpan elapsed, bool resting = false)
        {
            if (body == null || elapsed <= TimeSpan.Zero) return;
            double h = elapsed.TotalHours;
            var s = body.State;

            s.BodyHeat = MoveToward(s.BodyHeat, 50, 30 * h);
            s.Breathlessness = Clamp(s.Breathlessness - 70 * h);
            s.SweatWetness = Clamp(s.SweatWetness - 35 * h);
            s.Sweat = Clamp(s.Sweat - 18 * h);
            s.ClothingDampness = Clamp(s.ClothingDampness - 20 * h);

            // Dry sweat does not make the person clean again.
            if (s.Sweat > 10 || s.SweatWetness > 10)
                s.BodyOdor = Clamp(s.BodyOdor + 1.5 * h);

            s.HairOiliness = Clamp(s.HairOiliness + 1.2 * h);
            s.Oiliness = Clamp(s.Oiliness + .8 * h);
            s.Hunger = Clamp(s.Hunger + 3.0 * h);
            s.Thirst = Clamp(s.Thirst + 4.0 * h);
            s.BladderNeed = Clamp(s.BladderNeed + 4.0 * h);

            if (resting)
                s.Fatigue = Clamp(s.Fatigue - 8 * h);
            else
                s.Fatigue = Clamp(s.Fatigue + .8 * h);

            s.MuscleSoreness = Clamp(s.MuscleSoreness - 2 * h);
            s.SexualAftercareCleanupNeed = Clamp(s.SexualAftercareCleanupNeed - 2 * h);

            RecomputeCleanliness(body);
        }

        public static void Shower(HumanBodyProfile body, bool washHair = true)
        {
            if (body == null) return;
            var s = body.State;

            s.Cleanliness = 98;
            s.Sweat = 0;
            s.SweatWetness = 0;
            s.BodyOdor = 0;
            s.Dirt = 0;
            s.Grime = 0;
            s.Oiliness = 5;
            s.SkinWetness = 35;
            s.SexualAftercareCleanupNeed = 0;

            if (washHair)
            {
                s.HairOiliness = 3;
                s.HairMess = 25;
                s.CurrentHairState = "wet_clean";
            }
        }

        public static double CalculateShowerNeed(HumanBodyProfile body)
        {
            if (body == null) return 0;
            var h = body.Hygiene;
            var s = body.State;

            double cleanlinessDeficit = Math.Max(0, h.ShowerTrigger - s.Cleanliness) * 1.4;
            double sweatPressure = s.Sweat * (1.15 - h.SweatTolerance / 120.0);
            double odorPressure = s.BodyOdor * (1.20 - h.OdorTolerance / 120.0);
            double grimePressure = (s.Dirt + s.Grime) * .35;
            double hairPressure = Math.Max(0, s.HairOiliness - h.OilyHairTolerance) * .5;
            double aftercare = s.SexualAftercareCleanupNeed * .7;

            return Clamp(cleanlinessDeficit + sweatPressure + odorPressure + grimePressure + hairPressure + aftercare);
        }

        public static string ShowerNeedBand(HumanBodyProfile body)
        {
            double n = CalculateShowerNeed(body);
            return n switch
            {
                < 25 => "none",
                < 45 => "noticeable",
                < 65 => "wants_cleanup",
                < 85 => "strong",
                _ => "urgent"
            };
        }

        public static string BuildNeedPrompt(HumanBodyProfile body)
        {
            string band = ShowerNeedBand(body);
            if (band == "none") return "";
            return band switch
            {
                "noticeable" => "BODY NEED: Could use a shower later; not urgent.",
                "wants_cleanup" => "BODY NEED: Feels sweaty/unclean enough to want to clean up when practical.",
                "strong" => "BODY NEED: Feels gross and strongly wants a shower/change of clothes.",
                _ => "BODY NEED: Clean-up/shower feels urgent and is difficult to ignore."
            };
        }

        private static double CalculateAftercareCleanupPressure(HumanBodyProfile b)
        {
            var a = b.AdultPrivate.Aftercare;
            return Clamp(
                a.PostSexCleanlinessPreference * .45 +
                (100 - a.FluidTolerance) * .25 +
                a.ImmediateShowerPreference * .30);
        }

        private static void RecomputeCleanliness(HumanBodyProfile body)
        {
            var s = body.State;
            double dirtiness =
                s.Sweat * .16 +
                s.BodyOdor * .28 +
                s.Dirt * .23 +
                s.Grime * .27 +
                s.Oiliness * .06;
            s.Cleanliness = Clamp(100 - dirtiness);
        }

        private static void Add(
            HumanBodyState s,
            double sweat, double heat, double fatigue, double thirst,
            double hairMess, double odor, double clothes, double intensity)
        {
            s.Sweat = Clamp(s.Sweat + sweat * intensity);
            s.BodyHeat = Clamp(s.BodyHeat + heat * intensity);
            s.Fatigue = Clamp(s.Fatigue + fatigue * intensity);
            s.Thirst = Clamp(s.Thirst + thirst * intensity);
            s.HairMess = Clamp(s.HairMess + hairMess * intensity);
            s.BodyOdor = Clamp(s.BodyOdor + odor * intensity);
            s.ClothingCleanliness = Clamp(s.ClothingCleanliness + clothes * intensity);
        }

        private static double MoveToward(double value, double target, double amount)
        {
            if (value < target) return Math.Min(target, value + amount);
            if (value > target) return Math.Max(target, value - amount);
            return value;
        }

        private static double Clamp(double v) => Math.Clamp(v, 0, 100);
    }
}
