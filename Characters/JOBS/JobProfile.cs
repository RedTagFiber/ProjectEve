public class JobProfile
{
    // ============================
    // BASIC JOB INFO
    // ============================
    public string JobName { get; set; } = "";
    public string Employer { get; set; } = "";
    public string JobType { get; set; } = ""; // Retail, Nurse, Programmer, etc.

    // ============================
    // WORK SCHEDULE
    // ============================
    public int StartHour { get; set; }   // 0–23
    public int EndHour { get; set; }     // 0–23
    public bool IsNightShift => StartHour >= 20 || StartHour < 6;

    // ============================
    // PAY & FINANCES
    // ============================
    public decimal HourlyRate { get; set; }
    public decimal WeeklyHours { get; set; }
    public decimal MonthlyIncome => HourlyRate * WeeklyHours * 4;

    public decimal MonthlyBills { get; set; }
    public decimal SavingsRate { get; set; } // percent
    public decimal FinancialStress => Math.Max(0, MonthlyBills - MonthlyIncome);

    // ============================
    // HEALTHCARE
    // ============================
    public bool HasInsurance { get; set; }
    public string InsuranceProvider { get; set; } = "";
    public decimal InsurancePremium { get; set; }
    public decimal CoveragePercent { get; set; } // 0–100

    // ============================
    // VACATION / PTO
    // ============================
    public int VacationDaysPerYear { get; set; }
    public int VacationDaysUsed { get; set; }
    public int VacationDaysRemaining => VacationDaysPerYear - VacationDaysUsed;

    // ============================
    // JOB STRESS FACTORS
    // ============================
    public int StressLevel { get; set; }       // 0–100
    public int SocialLevel { get; set; }       // 0–100
    public int PhysicalDemand { get; set; }    // 0–100

    // ============================
    // BURNOUT
    // ============================
    public int BurnoutLevel { get; set; }      // 0–100
    public bool IsBurnedOut => BurnoutLevel >= 70;

    // ============================
    // WORK HISTORY (NEW SECTION)
    // ============================
    public int DaysWorked { get; set; }
    public int WeeksWorked => DaysWorked / 7;
    public int MonthsWorked => DaysWorked / 30;
    public int YearsWorked => DaysWorked / 365;

    // Optional: track exact hire date
    public DateTime HireDate { get; set; } = DateTime.Now;
}
