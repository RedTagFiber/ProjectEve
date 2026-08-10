using System;

namespace ProjectEve.Money
{
    /// <summary>
    /// Factual job state for an NPC or player.
    /// Attitudes/ambition live in slow.life.work_ambition + work_subs.
    /// This class is schedule, money, benefits, and demand — not personality.
    /// </summary>
    public class JobProfile
    {
        // ============================================================
        // IDENTITY
        // ============================================================
        public string JobName { get; set; } = "";
        public string Employer { get; set; } = "";
        /// <summary>e.g. full_time, part_time, gig, contract, seasonal, unemployed</summary>
        public string JobType { get; set; } = "full_time";
        /// <summary>e.g. trades, healthcare, office, retail, service, education, factory, public, logistics</summary>
        public string IndustryPath { get; set; } = "";
        public string Department { get; set; } = "";
        public string TitleLevel { get; set; } = ""; // entry, mid, senior, lead, manager, owner

        // ============================================================
        // SCHEDULE
        // ============================================================
        public int StartHour { get; set; } = 9;
        public int EndHour { get; set; } = 17;
        /// <summary>days, nights, swing, rotating, salary_unbounded</summary>
        public string ShiftType { get; set; } = "days";
        public bool IsNightShift =>
            string.Equals(ShiftType, "nights", StringComparison.OrdinalIgnoreCase)
            || StartHour >= 20 || StartHour < 6;

        /// <summary>office, hybrid, remote</summary>
        public string WorkLocationMode { get; set; } = "office";
        public double CommuteMinutesOneWay { get; set; } = 15;
        public string[] WorkDays { get; set; } = { "Mon", "Tue", "Wed", "Thu", "Fri" };

        // ============================================================
        // PAY
        // ============================================================
        public decimal HourlyRate { get; set; }
        public decimal WeeklyHours { get; set; } = 40;
        public bool IsSalaried { get; set; }
        public decimal AnnualSalary { get; set; }

        public decimal MonthlyIncome =>
            IsSalaried && AnnualSalary > 0
                ? AnnualSalary / 12m
                : HourlyRate * WeeklyHours * 4.333m;

        public decimal OvertimeRateMultiplier { get; set; } = 1.5m;
        public decimal TypicalOvertimeHoursPerWeek { get; set; }

        // ============================================================
        // BILLS / STRESS HOOKS (soft — MoneyProfile is source of truth for cash)
        // ============================================================
        public decimal MonthlyBillsHint { get; set; }
        public decimal SavingsRateHint { get; set; }
        public decimal FinancialPressureHint =>
            Math.Max(0, MonthlyBillsHint - MonthlyIncome);

        // ============================================================
        // BENEFITS
        // ============================================================
        public bool HasInsurance { get; set; }
        public string InsuranceProvider { get; set; } = "";
        public decimal InsurancePremium { get; set; }
        public decimal CoveragePercent { get; set; } = 80;

        public bool HasRetirementMatch { get; set; }
        public decimal RetirementMatchPercent { get; set; }
        public bool HasPaidTimeOff { get; set; } = true;
        public int VacationDaysPerYear { get; set; } = 10;
        public int VacationDaysUsed { get; set; }
        public int SickDaysPerYear { get; set; } = 5;
        public int SickDaysUsed { get; set; }
        public int VacationDaysRemaining => Math.Max(0, VacationDaysPerYear - VacationDaysUsed);
        public int SickDaysRemaining => Math.Max(0, SickDaysPerYear - SickDaysUsed);

        // ============================================================
        // DEMAND (0–100 factual load — not Fast emotion)
        // ============================================================
        public int StressLoad { get; set; }          // role pressure
        public int SocialDemand { get; set; }        // customers / team
        public int PhysicalDemand { get; set; }      // body load
        public int CognitiveDemand { get; set; }     // focus / decisions
        public int BurnoutAccum { get; set; }        // rises with bad weeks
        public bool IsBurnedOut => BurnoutAccum >= 70;

        // ============================================================
        // TENURE
        // ============================================================
        public DateTime HireDate { get; set; } = DateTime.Now;
        public int DaysWorked { get; set; }
        public int WeeksWorked => DaysWorked / 7;
        public int MonthsWorked => DaysWorked / 30;
        public int YearsWorked => DaysWorked / 365;
        public DateTime? PreviousJobEnd { get; set; }
        public string PreviousEmployer { get; set; } = "";

        // ============================================================
        // BOSS / TEAM (light facts for packet — attitudes in work_subs)
        // ============================================================
        public string BossName { get; set; } = "";
        public string BossRelationship { get; set; } = "neutral"; // toxic, tense, neutral, good, mentor
        public string TeamClimate { get; set; } = "cordial";      // isolated, cordial, friends, drama

        // ============================================================
        // FLAGS
        // ============================================================
        public bool IsUnemployed =>
            string.Equals(JobType, "unemployed", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(JobName);

        public bool IsStudent { get; set; }
        public bool IsRetired { get; set; }
        public bool HasSecondJob { get; set; }
        public string SecondJobName { get; set; } = "";

        // ============================================================
        // HELPERS
        // ============================================================
        public bool IsWorkingAt(DateTime localTime)
        {
            if (IsUnemployed || IsRetired) return false;
            var day = localTime.ToString("ddd");
            if (WorkDays != null && WorkDays.Length > 0)
            {
                bool dayMatch = false;
                foreach (var d in WorkDays)
                {
                    if (string.Equals(d, day, StringComparison.OrdinalIgnoreCase))
                    {
                        dayMatch = true;
                        break;
                    }
                }
                if (!dayMatch) return false;
            }

            int hour = localTime.Hour;
            if (StartHour == EndHour) return true;
            if (StartHour < EndHour)
                return hour >= StartHour && hour < EndHour;
            // overnight shift
            return hour >= StartHour || hour < EndHour;
        }

        public string SummaryLine()
        {
            if (IsRetired) return "Retired";
            if (IsUnemployed) return "Unemployed";
            if (IsStudent && string.IsNullOrWhiteSpace(JobName)) return "Student";
            string money = IsSalaried
                ? $"${AnnualSalary:0}/yr"
                : $"${HourlyRate:0.00}/hr";
            return $"{JobName} @ {Employer} ({money})";
        }

        public void TickBurnout(int delta)
        {
            BurnoutAccum = Math.Clamp(BurnoutAccum + delta, 0, 100);
        }

        public void RecoverBurnout(int amount)
        {
            BurnoutAccum = Math.Clamp(BurnoutAccum - Math.Abs(amount), 0, 100);
        }
    }
}