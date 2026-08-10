using ProjectEve.Characters.Base;
using System;
using System.Linq;

namespace ProjectEve.Characters.Base
{
    public static class CharacterSheetPrinter
    {
        public static void Print(SimCharacter c)
        {
            if (c == null)
            {
                Console.WriteLine("No character.");
                return;
            }

            void Section(string title)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("=== " + title + " ===");
                Console.ResetColor();
            }

            void Line(string label, string? value)
            {
                Console.WriteLine($"{label,-18}: {value ?? ""}");
            }

            float T(string id)
            {
                try { return c.Traits?.Get(id) ?? 0f; }
                catch { return 0f; }
            }

            string StyleOf(string id)
            {
                try
                {
                    var s = c.Traits?.GetStyle(id);
                    return string.IsNullOrWhiteSpace(s) ? "" : $" [{s}]";
                }
                catch { return ""; }
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine(" CHARACTER SHEET");
            Console.WriteLine("========================================");

            // ----------------------------------------------------------
            Section("IDENTITY");
            Line("Id", c.Id.ToString());
            Line("Name", c.Name);
            Line("Age", c.Age.ToString());
            Line("Gender", c.Gender);
            try
            {
                if (c.BirthDate.HasValue)
                    Line("BirthDate", c.BirthDate.Value.ToString("yyyy-MM-dd"));
            }
            catch { }

            try { Line("Zodiac", c.Zodiac); } catch { }
            try { Line("Zodiac", c.Zodiac); } catch { }
            Line("Occupation", c.Occupation);
            Line("Location", c.Location);
            try { Line("Hometown", c.Hometown); } catch { }
            try { Line("HomeAddress", c.HomeAddress); } catch { }
            try { Line("Tier", c.Tier.ToString()); } catch { }

            // ----------------------------------------------------------
            Section("APPEARANCE");
            try { Line("HeightCm", c.HeightCm.ToString()); } catch { }
            try { Line("Hair", $"{c.HairColor} / {c.HairStyle}"); } catch { }
            try { Line("Eyes", c.EyeColor); } catch { }
            try { Line("Skin", c.SkinTone); } catch { }
            try { Line("Body", c.BodyShape); } catch { }

            if (c.Appearance != null)
            {
                Line("Race", c.Appearance.Race);
                Line("Style", c.Appearance.Style);
                Line("Feature", c.Appearance.UniqueFeature);
                try { Line("Hair", $"{c.Appearance.HairColor} / {c.Appearance.HairStyle}"); } catch { }
                try { Line("Eyes", c.Appearance.EyeColor); } catch { }
                try { Line("Skin", c.Appearance.SkinTone); } catch { }
                try { Line("Body", c.Appearance.BodyType); } catch { }
            }

            // ----------------------------------------------------------
            Section("DRIVES");
            Line("Goal", c.Goal);
            Line("Need", c.Need);
            Line("Fear", c.Fear);
            Line("Want", c.Want);

            // ----------------------------------------------------------
            Section("MONEY");
            if (c.Money == null)
            {
                Line("Money", "none");
            }
            else
            {
                Line("Cash", c.Money.Cash.ToString("0.00"));
                Line("Bank", c.Money.Bank.ToString("0.00"));
                Line("Debt", c.Money.Debt.ToString("0.00"));
                try { Line("Available", c.Money.Available.ToString("0.00")); } catch { }
                try { Line("Pressure", c.Money.PressureLabel()); } catch { }
            }

            // ----------------------------------------------------------
            Section("JOB");
            if (c.Job == null || string.IsNullOrWhiteSpace(c.Job.JobName))
            {
                Line("Job", "none");
            }
            else
            {
                Line("Title", c.Job.JobName);
                Line("Employer", c.Job.Employer);
                Line("Type", c.Job.JobType);
                try { Line("Industry", c.Job.IndustryPath); } catch { }
                Line("Hours", $"{c.Job.StartHour}:00 - {c.Job.EndHour}:00");
                try { Line("Shift", c.Job.ShiftType); } catch { }
                try
                {
                    if (c.Job.IsSalaried)
                        Line("Pay", $"${c.Job.AnnualSalary:0}/yr");
                    else
                        Line("Pay", $"${c.Job.HourlyRate:0.00}/hr × {c.Job.WeeklyHours:0}h");
                }
                catch
                {
                    Line("Pay", $"${c.Job.HourlyRate:0.00}/hr");
                }
                Line("MonthlyIncome", c.Job.MonthlyIncome.ToString("0.00"));
                try { Line("StressLoad", c.Job.StressLoad.ToString()); } catch { }
                try { Line("SocialDemand", c.Job.SocialDemand.ToString()); } catch { }
                try { Line("PhysicalDemand", c.Job.PhysicalDemand.ToString()); } catch { }
                try { Line("Burnout", c.Job.BurnoutAccum.ToString()); } catch { }
                try { Line("Insurance", c.Job.HasInsurance ? "yes" : "no"); } catch { }
                try { Line("Boss", $"{c.Job.BossName} ({c.Job.BossRelationship})"); } catch { }
                try { Line("Team", c.Job.TeamClimate); } catch { }
                try { Line("Summary", c.Job.SummaryLine()); } catch { }
            }

            // ----------------------------------------------------------
            Section("FAST STATE (live)");
            if (c.Traits == null)
            {
                Line("Traits", "none");
            }
            else
            {
                void F(string label, string id) =>
                    Console.WriteLine($"{label,-18}: {T(id),5:0}{StyleOf(id)}");

                F("Anger", "trait.anger");
                F("Anxiety", "trait.anxiety");
                F("Fear", "trait.fear");
                F("Shame", "trait.shame");
                F("Guilt", "trait.guilt");
                F("Hurt", "trait.hurt");
                F("Jealousy", "trait.jealousy");
                F("Resentment", "trait.resentment");
                F("Trust", "trait.trust");
                F("Affection", "trait.affection");
                F("Desire", "trait.desire");
                F("Attraction", "trait.attraction");
                F("Tension", "trait.tension");
                F("Playfulness", "trait.playfulness");
                F("Pride", "trait.pride");
                F("Patience", "trait.patience");
                F("Guard", "trait.guard");
                F("Openness", "trait.openness");
                F("Loneliness", "trait.loneliness");
                F("Hope", "trait.hope");

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(c.Traits.BuildLlmSummary(12));
                Console.ResetColor();
            }

            // ----------------------------------------------------------
            Section("MID (character)");
            PrintPrefixed(c, "mid.");

            // ----------------------------------------------------------
            Section("SLOW (taste / life)");
            PrintPrefixed(c, "slow.", exclude: "slow.kink");

            // ----------------------------------------------------------
            Section("KINK / ADULT");
            PrintPrefixed(c, "slow.kink", min: 1f);
            if (T("slow.kink") < 50)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  (slow.kink parent < 50 — may not wake in play)");
                Console.ResetColor();
            }

            // ----------------------------------------------------------
            Section("BRAIN METERS");
            if (c.Brain == null)
            {
                Line("Brain", "none");
            }
            else
            {
                Line("Mood", c.Brain.Mood.ToString("0.00"));
                Line("Stress", c.Brain.Stress.ToString("0.00"));
                Line("Energy", c.Brain.Energy.ToString("0.00"));
                Line("Affection", c.Brain.Affection.ToString("0.00"));
                Line("Attraction", c.Brain.Attraction.ToString("0.00"));
                Line("Trust", c.Brain.Trust.ToString("0.00"));
                Line("Tension", c.Brain.Tension.ToString("0.00"));
                Line("LastThought", Trunc(c.Brain.LastThought, 120));
                Line("PsyAction", c.Brain.LastPsyAction ?? "");
                Line("PsyScore", c.Brain.LastPsyScore.ToString());
            }

            // ----------------------------------------------------------
            Section("RELATIONSHIPS");
            if (c.Relationships == null || c.Relationships.Count == 0)
            {
                Line("Relationships", "none");
            }
            else
            {
                foreach (var r in c.Relationships)
                {
                    string extra = "";
                    try { extra = $", Tension {r.Tension}"; } catch { }
                    Console.WriteLine(
                        $"  {r.TargetName}: Trust {r.Trust}, Respect {r.Respect}, " +
                        $"Affection {r.Affection}, Attraction {r.Attraction}{extra}");
                }
            }

            // ----------------------------------------------------------
            Section("PERSONALITY CONTEXT");
            Console.WriteLine(c.PersonalityContext ?? "");

            // ----------------------------------------------------------
            Section("ALL TRAITS");
            if (c.Traits == null)
            {
                Line("Traits", "none");
            }
            else
            {
                var all = c.Traits.GetAll()
                    .OrderByDescending(kv => kv.Value)
                    .ToList();

                Console.WriteLine($"(keys: {all.Count})");
                foreach (var kv in all)
                    Console.WriteLine($"  {kv.Key,-42} {kv.Value,5:0}{StyleOf(kv.Key)}");
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine(" Tip: sheet | traits | reroll");
            Console.WriteLine("========================================");
            Console.WriteLine();
        }

        static void PrintPrefixed(SimCharacter c, string prefix, string? exclude = null, float min = 0f)
        {
            if (c.Traits == null)
            {
                Console.WriteLine("  none");
                return;
            }

            var list = c.Traits.GetAll()
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Where(kv => exclude == null || !kv.Key.StartsWith(exclude, StringComparison.OrdinalIgnoreCase))
                .Where(kv => kv.Value >= min)
                .OrderByDescending(kv => kv.Value)
                .ToList();

            if (list.Count == 0)
            {
                Console.WriteLine("  (none)");
                return;
            }

            foreach (var kv in list)
            {
                string style = "";
                try
                {
                    var s = c.Traits.GetStyle(kv.Key);
                    if (!string.IsNullOrWhiteSpace(s))
                        style = $" [{s}]";
                }
                catch { }

                Console.WriteLine($"  {kv.Key,-42} {kv.Value,5:0}{style}");
            }
        }

        static string Trunc(string? s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
    }
}