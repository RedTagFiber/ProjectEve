using ProjectEve.AI.Brain;
using ProjectEve.Characters.Base;
using ProjectEve.Characters.Cognition;
using ProjectEve.Money;
using ProjectEve.Traits;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace ProjectEve.Characters.Characters
{
    /// <summary>
    /// Create / load NPCs. Traits via Fast/Mid/Slow. Jobs via town_jobs.json catalog.
    /// </summary>
    public static class CharacterFactory
    {
        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        static string DefaultJobsPath =>
            Environment.GetEnvironmentVariable("EVE_TOWN_JOBS")
            ?? Path.Combine(@"D:\ProjectEve\EveData\world\ohio", "town_jobs.json");

        static TownJobCatalog? _catalog;
        static readonly object CatalogLock = new();

        // ============================================================
        // LOAD
        // ============================================================
        public static SimCharacter? LoadCharacter(int npcId)
        {
            var npc = CharacterRepository.LoadCharacter(npcId);
            if (npc == null)
                return null;

            EnsureCore(npc);
            EnsureTraits(npc);
            EnsureCognition(npc, finalizeFromCurrentLife: true);
            return npc;
        }

        // ============================================================
        // CREATE (basic)
        // ============================================================
        public static SimCharacter Create(
            string name,
            int age,
            string? gender = null,
            string? location = null,
            string? occupation = null)
        {
            var npc = new SimCharacter(name, age)
            {
                Gender = gender ?? "Unknown",
                Location = location ?? "Bellefontaine / Sidney, Ohio area",
                Occupation = occupation ?? ""
            };

            EnsureCore(npc);
            EnsureTraits(npc);

            // Generate the stable IQ/cognitive baseline now.
            // If CreateWithJob is used, job/education context is finalized after ApplyJob.
            EnsureCognition(
                npc,
                finalizeFromCurrentLife: !string.IsNullOrWhiteSpace(occupation));

            return npc;
        }

        /// <summary>
        /// Create NPC and apply a full job from the town catalog.
        /// lane: shop | crew | art | school | casual | any/null
        /// jobId: optional exact catalog id (e.g. "firefighter")
        /// </summary>
        public static SimCharacter CreateWithJob(
            string name,
            int age,
            string? gender = null,
            string? location = null,
            string? lane = null,
            string? jobId = null,
            Random? rng = null)
        {
            rng ??= new Random();
            var npc = Create(name, age, gender, location, occupation: null);

            var job = ResolveJob(lane, jobId, rng);
            if (job != null)
                ApplyJob(npc, job, rng);

            return npc;
        }

        // ============================================================
        // CORE / TRAITS
        // ============================================================
        public static void EnsureCore(SimCharacter npc)
        {
            if (npc == null) return;

            npc.Brain ??= new Brain();
            npc.Brain.Owner = npc;
            npc.Money ??= new MoneyProfile();
            npc.Job ??= new JobProfile();
            npc.Cognition ??= new CognitiveProfile();
            npc.Traits ??= new NpcTraits();
            npc.Relationships ??= new List<ProjectEve.Relationships.Relationship>();
        }

        public static void EnsureTraits(SimCharacter npc)
        {
            if (npc == null) return;

            npc.Traits ??= new NpcTraits();
            if (npc.Traits.GetAll().Count > 0)
                return;

            try
            {
                NpcTraitInitializer.GenerateBalancedTraits(npc.Traits);
            }
            catch
            {
                try { TraitJsonLoader.ApplyRolledLayers(npc.Traits); }
                catch { npc.Traits.InitializeFastDefaults(); }
            }
        }

        /// <summary>
        /// Ensure a character has a stable cognitive profile.
        /// Existing loaded profiles are never rerolled.
        /// </summary>
        public static void EnsureCognition(
            SimCharacter npc,
            Random? rng = null,
            bool finalizeFromCurrentLife = false)
        {
            if (npc == null) return;
            rng ??= Random.Shared;

            npc.Cognition ??= new CognitiveProfile();
            bool wasMissing = !npc.Cognition.IsGenerated;

            CognitiveProfileGenerator.EnsureGenerated(npc, rng);

            if (finalizeFromCurrentLife &&
                (wasMissing || !npc.Cognition.LifeContextFinalized))
            {
                CognitiveProfileGenerator.FinalizeLifeContext(
                    npc,
                    rng,
                    allowEducationUpgrade: true);
            }
            else
            {
                CognitiveProfileGenerator.RefreshForCurrentLife(npc, rng);
            }

            // Only persisted NPCs can be saved here.
            if (npc.Id > 0 && wasMissing)
            {
                try { CharacterRepository.SaveCognition(npc); }
                catch { }
            }
        }

        public static void RerollTraits(SimCharacter npc)
        {
            if (npc == null) return;

            npc.Traits ??= new NpcTraits();
            try
            {
                NpcTraitInitializer.GenerateBalancedTraits(npc.Traits);
            }
            catch
            {
                try { TraitJsonLoader.ApplyRolledLayers(npc.Traits); }
                catch { npc.Traits.InitializeFastDefaults(); }
            }

            // Traits can change speech tendencies, but never reroll IQ/education.
            try { CognitiveProfileGenerator.RefreshForCurrentLife(npc); }
            catch { }
        }

        // ============================================================
        // TOWN JOB CATALOG
        // ============================================================
        public static TownJobCatalog LoadJobCatalog(string? path = null)
        {
            lock (CatalogLock)
            {
                path ??= DefaultJobsPath;
                if (_catalog != null && string.IsNullOrEmpty(path))
                    return _catalog;

                if (!File.Exists(path))
                {
                    _catalog = new TownJobCatalog { Jobs = new List<TownJob>() };
                    return _catalog;
                }

                string json = File.ReadAllText(path);
                _catalog = JsonSerializer.Deserialize<TownJobCatalog>(json, JsonOpts)
                           ?? new TownJobCatalog { Jobs = new List<TownJob>() };
                _catalog.Jobs ??= new List<TownJob>();
                return _catalog;
            }
        }

        public static void ReloadJobCatalog(string? path = null)
        {
            lock (CatalogLock)
            {
                _catalog = null;
            }
            LoadJobCatalog(path);
        }

        public static TownJob? ResolveJob(string? lane, string? jobId, Random rng)
        {
            var cat = LoadJobCatalog();
            if (cat.Jobs == null || cat.Jobs.Count == 0)
                return null;

            IEnumerable<TownJob> pool = cat.Jobs;

            if (!string.IsNullOrWhiteSpace(jobId))
            {
                var exact = cat.Jobs.FirstOrDefault(j =>
                    string.Equals(j.Id, jobId, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
            }

            if (!string.IsNullOrWhiteSpace(lane) &&
                !string.Equals(lane, "any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(lane, "casual", StringComparison.OrdinalIgnoreCase))
            {
                var filtered = cat.Jobs
                    .Where(j => string.Equals(j.Lane, lane, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (filtered.Count > 0)
                    pool = filtered;
            }
            else if (string.Equals(lane, "casual", StringComparison.OrdinalIgnoreCase))
            {
                var filtered = cat.Jobs
                    .Where(j => string.Equals(j.Lane, "casual", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (filtered.Count > 0)
                    pool = filtered;
            }

            var list = pool.ToList();
            if (list.Count == 0) return null;

            // weighted pick
            int total = list.Sum(j => Math.Max(1, j.Weight));
            int roll = rng.Next(total);
            int acc = 0;
            foreach (var j in list)
            {
                acc += Math.Max(1, j.Weight);
                if (roll < acc) return j;
            }
            return list[list.Count - 1];
        }

        /// <summary>Copy full catalog job onto NPC JobProfile + money/drives defaults.</summary>
        public static void ApplyJob(SimCharacter npc, TownJob j, Random? rng = null)
        {
            if (npc == null || j == null) return;
            rng ??= new Random();
            EnsureCore(npc);

            npc.Occupation = !string.IsNullOrWhiteSpace(j.NpcDefaults?.Occupation)
                ? j.NpcDefaults!.Occupation!
                : j.JobName;

            var job = npc.Job!;
            job.JobName = j.JobName ?? "";
            job.Employer = j.Employer ?? "";
            job.JobType = string.IsNullOrWhiteSpace(j.JobType) ? "full_time" : j.JobType;
            job.IndustryPath = j.IndustryPath ?? "";
            job.Department = j.Department ?? "";
            job.TitleLevel = j.TitleLevel ?? "";

            job.StartHour = j.StartHour;
            job.EndHour = j.EndHour;
            job.ShiftType = string.IsNullOrWhiteSpace(j.ShiftType) ? "days" : j.ShiftType;
            job.WorkLocationMode = string.IsNullOrWhiteSpace(j.WorkLocationMode) ? "office" : j.WorkLocationMode;
            job.CommuteMinutesOneWay = j.CommuteMinutesOneWay;
            job.WorkDays = j.WorkDays is { Length: > 0 }
                ? j.WorkDays
                : new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };

            job.HourlyRate = (decimal)j.HourlyRate;
            job.WeeklyHours = (decimal)(j.WeeklyHours <= 0 ? 40 : j.WeeklyHours);
            job.IsSalaried = j.IsSalaried;
            job.AnnualSalary = (decimal)j.AnnualSalary;
            job.OvertimeRateMultiplier = j.OvertimeRateMultiplier <= 0 ? 1.5m : (decimal)j.OvertimeRateMultiplier;
            job.TypicalOvertimeHoursPerWeek = (decimal)j.TypicalOvertimeHoursPerWeek;

            job.MonthlyBillsHint = (decimal)j.MonthlyBillsHint;
            job.SavingsRateHint = (decimal)j.SavingsRateHint;

            job.HasInsurance = j.HasInsurance;
            job.InsuranceProvider = j.InsuranceProvider ?? "";
            job.InsurancePremium = (decimal)j.InsurancePremium;
            job.CoveragePercent = j.CoveragePercent <= 0 && j.HasInsurance ? 80 : (decimal)j.CoveragePercent;
            job.HasRetirementMatch = j.HasRetirementMatch;
            job.RetirementMatchPercent = (decimal)j.RetirementMatchPercent;
            job.HasPaidTimeOff = j.HasPaidTimeOff;
            job.VacationDaysPerYear = j.VacationDaysPerYear;
            job.SickDaysPerYear = j.SickDaysPerYear;

            job.StressLoad = j.StressLoad;
            job.SocialDemand = j.SocialDemand;
            job.PhysicalDemand = j.PhysicalDemand;
            job.CognitiveDemand = j.CognitiveDemand;
            job.BurnoutAccum = j.BurnoutAccum;

            job.BossName = j.BossName ?? "";
            job.BossRelationship = string.IsNullOrWhiteSpace(j.BossRelationship) ? "neutral" : j.BossRelationship;
            job.TeamClimate = string.IsNullOrWhiteSpace(j.TeamClimate) ? "cordial" : j.TeamClimate;

            job.IsStudent = j.IsStudent;
            job.IsRetired = j.IsRetired;
            job.HasSecondJob = j.HasSecondJob;
            job.SecondJobName = j.SecondJobName ?? "";
            job.HireDate = DateTime.Now.AddDays(-rng.Next(60, 2400));
            job.DaysWorked = Math.Max(1, (int)(DateTime.Now - job.HireDate).TotalDays);

            var d = j.NpcDefaults;
            if (d != null)
            {
                if (!string.IsNullOrWhiteSpace(d.Goal)) npc.Goal = d.Goal!;
                if (!string.IsNullOrWhiteSpace(d.Need)) npc.Need = d.Need!;
                if (!string.IsNullOrWhiteSpace(d.Fear)) npc.Fear = d.Fear!;
                if (!string.IsNullOrWhiteSpace(d.Want)) npc.Want = d.Want!;
                if (d.TierBias is > 0) npc.Tier = d.TierBias.Value;

                npc.Money ??= new MoneyProfile();
                npc.Money.Cash = RandBetween(rng, d.MoneyCash, 40, 120);
                npc.Money.Bank = RandBetween(rng, d.MoneyBank, 400, 4000);
                npc.Money.Debt = RandBetween(rng, d.MoneyDebt, 0, 5000);
            }

            // New NPCs use job education hints to finalize education/speech/domain knowledge.
            // Existing finalized NPCs do NOT magically gain a degree when jobs change.
            try
            {
                bool canFinalizeEducation = !npc.Cognition.LifeContextFinalized;
                CognitiveProfileGenerator.FinalizeLifeContext(
                    npc,
                    rng,
                    j.MinimumEducation,
                    j.TypicalEducation,
                    j.FieldOfStudy,
                    allowEducationUpgrade: canFinalizeEducation);
            }
            catch { }
        }

        static decimal RandBetween(Random rng, int[]? range, int fallbackMin, int fallbackMax)
        {
            int min = fallbackMin;
            int max = fallbackMax;
            if (range != null && range.Length >= 2)
            {
                min = range[0];
                max = Math.Max(range[0], range[1]);
            }
            if (max < min) (min, max) = (max, min);
            return rng.Next(min, max + 1);
        }
    }

    // ============================================================
    // DTOs for town_jobs.json
    // ============================================================
    public class TownJobCatalog
    {
        public string World { get; set; } = "ohio";
        public string Town { get; set; } = "";
        public List<TownJob> Jobs { get; set; } = new();
    }

    public class TownJob
    {
        public string Id { get; set; } = "";
        public string Lane { get; set; } = "casual";
        public int Weight { get; set; } = 10;

        public string JobName { get; set; } = "";
        public string Employer { get; set; } = "";
        public string JobType { get; set; } = "full_time";
        public string IndustryPath { get; set; } = "";
        public string Department { get; set; } = "";
        public string TitleLevel { get; set; } = "";

        // Optional cognition/education hints from town_jobs.json.
        // Existing JSON remains compatible when these are omitted.
        public string? MinimumEducation { get; set; }
        public string? TypicalEducation { get; set; }
        public string? FieldOfStudy { get; set; }

        public int StartHour { get; set; } = 9;
        public int EndHour { get; set; } = 17;
        public string ShiftType { get; set; } = "days";
        public string WorkLocationMode { get; set; } = "office";
        public double CommuteMinutesOneWay { get; set; } = 15;
        public string[]? WorkDays { get; set; }

        public double HourlyRate { get; set; }
        public double WeeklyHours { get; set; } = 40;
        public bool IsSalaried { get; set; }
        public double AnnualSalary { get; set; }
        public double OvertimeRateMultiplier { get; set; } = 1.5;
        public double TypicalOvertimeHoursPerWeek { get; set; }

        public double MonthlyBillsHint { get; set; }
        public double SavingsRateHint { get; set; }

        public bool HasInsurance { get; set; }
        public string? InsuranceProvider { get; set; }
        public double InsurancePremium { get; set; }
        public double CoveragePercent { get; set; } = 80;
        public bool HasRetirementMatch { get; set; }
        public double RetirementMatchPercent { get; set; }
        public bool HasPaidTimeOff { get; set; } = true;
        public int VacationDaysPerYear { get; set; } = 10;
        public int SickDaysPerYear { get; set; } = 5;

        public int StressLoad { get; set; }
        public int SocialDemand { get; set; }
        public int PhysicalDemand { get; set; }
        public int CognitiveDemand { get; set; }
        public int BurnoutAccum { get; set; }

        public string? BossName { get; set; }
        public string? BossRelationship { get; set; }
        public string? TeamClimate { get; set; }

        public bool IsStudent { get; set; }
        public bool IsRetired { get; set; }
        public bool HasSecondJob { get; set; }
        public string? SecondJobName { get; set; }

        public TownJobNpcDefaults? NpcDefaults { get; set; }
    }

    public class TownJobNpcDefaults
    {
        public string? Occupation { get; set; }
        public int? TierBias { get; set; }
        public int[]? MoneyCash { get; set; }
        public int[]? MoneyBank { get; set; }
        public int[]? MoneyDebt { get; set; }
        public string? Goal { get; set; }
        public string? Need { get; set; }
        public string? Fear { get; set; }
        public string? Want { get; set; }
    }
}