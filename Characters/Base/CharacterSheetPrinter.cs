using ProjectEve.Characters.Base;
using System;
using System.Linq;

namespace ProjectEve.Characters.Base
{
    public static class CharacterSheetPrinter
    {
        public static void Print(SimCharacter eve)
        {
            if (eve == null)
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
                Console.WriteLine($"{label,-16}: {value ?? ""}");
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine(" CHARACTER SHEET");
            Console.WriteLine("========================================");

            Section("IDENTITY");
            Line("Id", eve.Id.ToString());
            Line("Name", eve.Name);
            Line("Age", eve.Age.ToString());
            Line("Gender", eve.Gender);
            Line("Occupation", eve.Occupation);
            Line("Location", eve.Location);

            Section("DRIVES");
            Line("Goal", eve.Goal);
            Line("Need", eve.Need);
            Line("Fear", eve.Fear);
            Line("Want", eve.Want);

            Section("MONEY");
            if (eve.Money == null)
            {
                Line("Money", "none (MoneyProfile missing on character)");
            }
            else
            {
                Line("Cash", eve.Money.Cash.ToString("0.00"));
                Line("Bank", eve.Money.Bank.ToString("0.00"));
                Line("Debt", eve.Money.Debt.ToString("0.00"));
                Line("Available", eve.Money.Available.ToString("0.00"));
                Line("Pressure", eve.Money.PressureLabel());
                Line("StressBias", eve.Money.StressBias().ToString());
                Line("DesireFunding", eve.Money.DesireFundingBias().ToString());
            }

            Section("JOB");
            if (eve.Job == null || string.IsNullOrWhiteSpace(eve.Job.JobName))
            {
                Line("Job", "none");
            }
            else
            {
                Line("Title", eve.Job.JobName);
                Line("Employer", eve.Job.Employer);
                Line("Type", eve.Job.JobType);
                Line("Hours", $"{eve.Job.StartHour}:00 - {eve.Job.EndHour}:00");
                Line("Pay", $"${eve.Job.HourlyRate:0.00}/hr");
                Line("WeeklyHours", eve.Job.WeeklyHours.ToString("0"));
                Line("MonthlyIncome", eve.Job.MonthlyIncome.ToString("0.00"));
                Line("JobStress", eve.Job.StressLevel.ToString());
                Line("Burnout", eve.Job.BurnoutLevel.ToString());
            }

            Section("EMOTION");
            if (eve.Emotion == null)
            {
                Line("Emotion", "none");
            }
            else
            {
                Line("State", eve.Emotion.State.ToString());
                Line("Mood", eve.Emotion.Mood);
                Line("Intensity", eve.Emotion.Intensity.ToString("0.00"));
                Line("Desire", eve.Emotion.Desire.ToString());
                Line("Shame", eve.Emotion.Shame.ToString());
                Line("Resentment", eve.Emotion.Resentment.ToString());
                Line("Restlessness", eve.Emotion.Restlessness.ToString());
                Line("Stress", eve.Emotion.Stress.ToString());
                Line("Energy", eve.Emotion.Energy.ToString());
                Line("Affection", eve.Emotion.Affection.ToString());
            }

            Section("BRAIN");
            if (eve.Brain == null)
            {
                Line("Brain", "none");
            }
            else
            {
                Line("Mood", eve.Brain.Mood.ToString("0.00"));
                Line("Stress", eve.Brain.Stress.ToString("0.00"));
                Line("Energy", eve.Brain.Energy.ToString("0.00"));
                Line("Affection", eve.Brain.Affection.ToString("0.00"));
                Line("Attraction", eve.Brain.Attraction.ToString("0.00"));
                Line("Trust", eve.Brain.Trust.ToString("0.00"));
                Line("Tension", eve.Brain.Tension.ToString("0.00"));
                Line("LastThought", eve.Brain.LastThought ?? "");
                Line("PsyAction", eve.Brain.LastPsyAction ?? "");
                Line("PsyScore", eve.Brain.LastPsyScore.ToString());
            }

            Section("TRAITS");
            if (eve.Traits == null)
            {
                Line("Traits", "none");
            }
            else
            {
                var all = eve.Traits.GetAll()
                    .OrderByDescending(kv => Math.Abs(kv.Value - 50f))
                    .ToList();

                Console.WriteLine($"(showing {all.Count} traits, strongest deviation first)");
                foreach (var kv in all)
                    Console.WriteLine($"  {kv.Key,-40} {kv.Value,5:0}");
            }

            Section("RELATIONSHIPS");
            if (eve.Relationships == null || eve.Relationships.Count == 0)
            {
                Line("Relationships", "none");
            }
            else
            {
                foreach (var r in eve.Relationships)
                {
                    Console.WriteLine(
                        $"  {r.TargetName}: Trust {r.Trust}, Respect {r.Respect}, " +
                        $"Affection {r.Affection}, Attraction {r.Attraction}");
                }
            }

            Section("PERSONALITY CONTEXT");
            Console.WriteLine(eve.PersonalityContext ?? "");

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine(" Tip: type 'sheet' in chat to print this again.");
            Console.WriteLine("========================================");
            Console.WriteLine();
        }
    }
}