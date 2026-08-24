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
    /// Create / load NPCs.
    /// 
    /// Supports:
    /// 1. Basic CharacterFactory.Create(...)
    /// 2. Old simple town_jobs.json via CreateWithJob(...)
    /// 3. New workplace-slot system via CreateWithOpenJobSlot(...)
    /// 
    /// New world generation should use CreateWithOpenJobSlot so the 200 deep NPCs
    /// are assigned to real limited workplace slots instead of everyone getting the same lane job.
    /// </summary>
    public static class CharacterFactory
    {
        static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // Old/simple catalog support.
        static string DefaultJobsPath =>
            Environment.GetEnvironmentVariable("EVE_TOWN_JOBS")
            ?? Path.Combine(@"D:\ProjectEve\EveData\world\ohio", "town_jobs.json");

        // New master job catalog.
        static string DefaultJobMasterPath =>
            Environment.GetEnvironmentVariable("EVE_JOB_MASTER")
            ?? Path.Combine(@"D:\ProjectEve\EveData\world\ohio", "project_eve_all_jobs_master.json");

        // New workplace registry with exact slots.
        static string DefaultWorkplaceRegistryPath =>
            Environment.GetEnvironmentVariable("EVE_WORKPLACE_REGISTRY")
            ?? Path.Combine(@"D:\ProjectEve\EveData\world\ohio", "project_eve_workplace_registry.json");

        static TownJobCatalog? _simpleCatalog;
        static ProjectEveJobMaster? _jobMaster;
        static ProjectEveWorkplaceRegistry? _workplaceRegistry;

        static readonly object CatalogLock = new();
        static readonly object WorkplaceLock = new();

        // In-memory slot reservation for a single generation run.
        // Program.cs / the world seeder can also call ReserveJobSlot(...) if it loads existing assignments from DB.
        static readonly HashSet<string> ReservedJobSlotIds = new(StringComparer.OrdinalIgnoreCase);

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
        // CREATE BASIC
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

            EnsureCognition(
                npc,
                finalizeFromCurrentLife: !string.IsNullOrWhiteSpace(occupation));

            return npc;
        }

        // ============================================================
        // OLD SIMPLE TOWN JOB CATALOG SUPPORT
        // ============================================================

        /// <summary>
        /// Old/simple flow.
        /// Uses town_jobs.json only.
        /// Good for quick tests.
        /// 
        /// World seeding should use CreateWithOpenJobSlot instead.
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

        public static TownJobCatalog LoadJobCatalog(string? path = null)
        {
            lock (CatalogLock)
            {
                path ??= DefaultJobsPath;

                if (_simpleCatalog != null && string.IsNullOrWhiteSpace(path))
                    return _simpleCatalog;

                if (!File.Exists(path))
                {
                    _simpleCatalog = new TownJobCatalog
                    {
                        Jobs = new List<TownJob>()
                    };

                    return _simpleCatalog;
                }

                string json = File.ReadAllText(path);

                _simpleCatalog = JsonSerializer.Deserialize<TownJobCatalog>(json, JsonOpts)
                                 ?? new TownJobCatalog
                                 {
                                     Jobs = new List<TownJob>()
                                 };

                _simpleCatalog.Jobs ??= new List<TownJob>();

                return _simpleCatalog;
            }
        }

        public static void ReloadJobCatalog(string? path = null)
        {
            lock (CatalogLock)
            {
                _simpleCatalog = null;
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

                if (exact != null)
                    return exact;
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

            return PickWeighted(pool.ToList(), x => Math.Max(1, x.Weight), rng);
        }

        // ============================================================
        // NEW WORKPLACE SLOT SYSTEM
        // ============================================================

        /// <summary>
        /// New real world-generation flow.
        /// 
        /// This creates a character, picks a real open job slot from the workplace registry,
        /// applies the matching master job definition, and reserves the slot so the next NPC
        /// cannot use it in the same bake.
        /// 
        /// If the preferred lane/category is full, it automatically falls back to another
        /// available slot.
        /// </summary>
        public static SimCharacter CreateWithOpenJobSlot(
            string name,
            int age,
            string? gender = null,
            string? location = null,
            string? preferredLaneOrCategory = null,
            string? preferredJobId = null,
            Random? rng = null)
        {
            rng ??= new Random();

            var npc = Create(
                name,
                age,
                gender,
                location ?? "Bellefontaine / Sidney, Ohio area",
                occupation: null);

            var assignment = ResolveOpenJobSlot(
                preferredLaneOrCategory,
                preferredJobId,
                rng);

            if (assignment != null)
            {
                ApplyJobSlot(npc, assignment, rng);
            }
            else
            {
                ApplyNoEmploymentDefault(npc, rng);
            }

            return npc;
        }

        public static ProjectEveJobMaster LoadJobMaster(string? path = null)
        {
            lock (WorkplaceLock)
            {
                path ??= DefaultJobMasterPath;

                if (_jobMaster != null && string.IsNullOrWhiteSpace(path))
                    return _jobMaster;

                if (!File.Exists(path))
                {
                    _jobMaster = new ProjectEveJobMaster
                    {
                        JobDefinitions = new List<ProjectEveJobDefinition>()
                    };

                    return _jobMaster;
                }

                string json = File.ReadAllText(path);

                _jobMaster = JsonSerializer.Deserialize<ProjectEveJobMaster>(json, JsonOpts)
                             ?? new ProjectEveJobMaster
                             {
                                 JobDefinitions = new List<ProjectEveJobDefinition>()
                             };

                _jobMaster.JobDefinitions ??= new List<ProjectEveJobDefinition>();

                return _jobMaster;
            }
        }

        public static ProjectEveWorkplaceRegistry LoadWorkplaceRegistry(string? path = null)
        {
            lock (WorkplaceLock)
            {
                path ??= DefaultWorkplaceRegistryPath;

                if (_workplaceRegistry != null && string.IsNullOrWhiteSpace(path))
                    return _workplaceRegistry;

                if (!File.Exists(path))
                {
                    _workplaceRegistry = new ProjectEveWorkplaceRegistry
                    {
                        Workplaces = new List<ProjectEveWorkplace>()
                    };

                    return _workplaceRegistry;
                }

                string json = File.ReadAllText(path);

                _workplaceRegistry = JsonSerializer.Deserialize<ProjectEveWorkplaceRegistry>(json, JsonOpts)
                                     ?? new ProjectEveWorkplaceRegistry
                                     {
                                         Workplaces = new List<ProjectEveWorkplace>()
                                     };

                _workplaceRegistry.Workplaces ??= new List<ProjectEveWorkplace>();

                return _workplaceRegistry;
            }
        }

        public static void ReloadWorkplaceSystem()
        {
            lock (WorkplaceLock)
            {
                _jobMaster = null;
                _workplaceRegistry = null;
                ReservedJobSlotIds.Clear();
            }

            LoadJobMaster();
            LoadWorkplaceRegistry();
        }

        public static void ClearReservedJobSlots()
        {
            lock (WorkplaceLock)
            {
                ReservedJobSlotIds.Clear();
            }
        }

        public static void ReserveJobSlot(string jobSlotId)
        {
            if (string.IsNullOrWhiteSpace(jobSlotId))
                return;

            lock (WorkplaceLock)
            {
                ReservedJobSlotIds.Add(jobSlotId);
            }
        }

        public static bool IsJobSlotReserved(string jobSlotId)
        {
            if (string.IsNullOrWhiteSpace(jobSlotId))
                return false;

            lock (WorkplaceLock)
            {
                return ReservedJobSlotIds.Contains(jobSlotId);
            }
        }

        public static JobSlotAssignment? ResolveOpenJobSlot(
            string? preferredLaneOrCategory,
            string? preferredJobId,
            Random rng)
        {
            var master = LoadJobMaster();
            var registry = LoadWorkplaceRegistry();

            if (registry.Workplaces == null || registry.Workplaces.Count == 0)
                return null;

            var jobById = (master.JobDefinitions ?? new List<ProjectEveJobDefinition>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var openSlots = BuildOpenSlots(registry, jobById);

            if (openSlots.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredJobId))
            {
                var exact = openSlots
                    .Where(x => string.Equals(x.JobId, preferredJobId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (exact.Count > 0)
                    return ReserveAndReturn(exact, rng);
            }

            if (!string.IsNullOrWhiteSpace(preferredLaneOrCategory) &&
                !string.Equals(preferredLaneOrCategory, "any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(preferredLaneOrCategory, "casual", StringComparison.OrdinalIgnoreCase))
            {
                var preferred = openSlots
                    .Where(x =>
                        string.Equals(x.Category, preferredLaneOrCategory, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(CategoryToLane(x.Category), preferredLaneOrCategory, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (preferred.Count > 0)
                    return ReserveAndReturn(preferred, rng);
            }

            return ReserveAndReturn(openSlots, rng);
        }

        static List<JobSlotAssignment> BuildOpenSlots(
            ProjectEveWorkplaceRegistry registry,
            Dictionary<string, ProjectEveJobDefinition> jobById)
        {
            var results = new List<JobSlotAssignment>();

            foreach (var workplace in registry.Workplaces ?? new List<ProjectEveWorkplace>())
            {
                if (workplace.Positions == null || workplace.Positions.Count == 0)
                    continue;

                foreach (var position in workplace.Positions)
                {
                    if (string.IsNullOrWhiteSpace(position.JobId))
                        continue;

                    if (!jobById.TryGetValue(position.JobId, out var jobDefinition))
                    {
                        jobDefinition = new ProjectEveJobDefinition
                        {
                            Id = position.JobId ?? "",
                            JobName = string.IsNullOrWhiteSpace(position.JobName) ? position.JobId ?? "Local Worker" : position.JobName,
                            Category = position.Category ?? "",
                            CareerLevel = 1,
                            SelectionWeight = 10,
                            EmploymentTypes = new List<string> { "full_time" },
                            MinimumAge = 18,
                            Schedule = new ProjectEveJobSchedule
                            {
                                PrimaryShiftTemplate = "retail_day",
                                WeeklyHoursMin = 30,
                                WeeklyHoursMax = 40
                            },
                            Pay = new ProjectEveJobPay
                            {
                                Type = "hourly",
                                StartingMin = 13.0,
                                StartingMax = 22.0,
                                OvertimeEligible = true,
                                OvertimeMultiplier = 1.5
                            },
                            WorkDemand = new ProjectEveJobWorkDemand
                            {
                                Stress = 40,
                                Social = 40,
                                Physical = 35,
                                Cognitive = 35,
                                BurnoutPotential = 35
                            }
                        };
                    }

                    var slots = Math.Max(0, position.Slots);

                    for (int slotNumber = 1; slotNumber <= slots; slotNumber++)
                    {
                        string jobSlotId = BuildJobSlotId(
                            workplace.WorkplaceId,
                            position.JobId,
                            slotNumber);

                        if (ReservedJobSlotIds.Contains(jobSlotId))
                            continue;

                        results.Add(new JobSlotAssignment
                        {
                            JobSlotId = jobSlotId,
                            WorkplaceId = workplace.WorkplaceId ?? "",
                            WorkplaceName = workplace.Name ?? "",
                            WorkplaceType = workplace.Type ?? "",
                            District = workplace.District ?? "",
                            OutsideTown = workplace.OutsideTown,

                            JobId = position.JobId ?? "",
                            JobName = position.JobName ?? jobDefinition.JobName ?? "",
                            Category = position.Category ?? jobDefinition.Category ?? "",

                            WorkplaceDefaultBenefitPlan = workplace.DefaultBenefitPlan ?? "",
                            WorkplaceAttendancePolicy = workplace.AttendancePolicy ?? "",

                            JobDefinition = jobDefinition,
                            SlotNumber = slotNumber
                        });
                    }
                }
            }

            return results;
        }

        static JobSlotAssignment? ReserveAndReturn(List<JobSlotAssignment> openSlots, Random rng)
        {
            if (openSlots.Count == 0)
                return null;

            var picked = PickWeighted(
                openSlots,
                x => Math.Max(1, x.JobDefinition?.SelectionWeight ?? 1),
                rng);

            if (picked == null)
                return null;

            ReservedJobSlotIds.Add(picked.JobSlotId);

            return picked;
        }

        static string BuildJobSlotId(string? workplaceId, string? jobId, int slotNumber)
        {
            workplaceId = string.IsNullOrWhiteSpace(workplaceId) ? "WORKPLACE" : workplaceId.Trim();
            jobId = string.IsNullOrWhiteSpace(jobId) ? "JOB" : jobId.Trim();

            return $"{workplaceId}-{jobId}-{slotNumber:D4}";
        }

        static string CategoryToLane(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "casual";

            var c = category.Trim().ToLowerInvariant();

            if (c is "retail" or "grocery" or "restaurants" or "fastfood" or "personalservices")
                return "shop";

            if (c is "fireems" or "police" or "government" or "construction" or "utilities" or "warehouse" or "manufacturing" or "automotive" or "transportation" or "agriculture")
                return "crew";

            if (c is "education")
                return "school";

            if (c is "community" or "legal" or "realestate" or "insurance" or "banking" or "hotel" or "healthcare" or "eldercare" or "postalshipping")
                return "casual";

            return "casual";
        }

        // ============================================================
        // APPLY JOBS
        // ============================================================

        /// <summary>
        /// Applies old/simple TownJob DTO to an NPC.
        /// </summary>
        public static void ApplyJob(SimCharacter npc, TownJob j, Random? rng = null)
        {
            if (npc == null || j == null)
                return;

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

            TryFinalizeCognitionFromJob(
                npc,
                rng,
                j.MinimumEducation,
                j.TypicalEducation,
                j.FieldOfStudy);
        }

        /// <summary>
        /// Applies new workplace-slot assignment to an NPC.
        /// </summary>
        public static void ApplyJobSlot(SimCharacter npc, JobSlotAssignment assignment, Random? rng = null)
        {
            if (npc == null || assignment == null || assignment.JobDefinition == null)
                return;

            rng ??= new Random();

            EnsureCore(npc);

            var def = assignment.JobDefinition;
            var job = npc.Job!;

            npc.Occupation = string.IsNullOrWhiteSpace(def.JobName)
                ? assignment.JobName
                : def.JobName;

            job.JobName = string.IsNullOrWhiteSpace(def.JobName)
                ? assignment.JobName
                : def.JobName;

            job.Employer = assignment.WorkplaceName ?? "";
            job.JobType = PickEmploymentType(def, rng);
            job.IndustryPath = def.Category ?? "";
            job.Department = assignment.District ?? "";
            job.TitleLevel = def.CareerLevel <= 1
                ? "entry"
                : def.CareerLevel == 2
                    ? "mid"
                    : def.CareerLevel == 3
                        ? "senior"
                        : "lead";

            var shift = ResolveShift(def);

            job.StartHour = shift.StartHour;
            job.EndHour = shift.EndHour;
            job.ShiftType = shift.ShiftType;
            job.WorkLocationMode = assignment.OutsideTown ? "regional_commute" : "local";
            job.CommuteMinutesOneWay = assignment.OutsideTown
                ? rng.Next(15, 46)
                : rng.Next(2, 26);

            job.WorkDays = shift.WorkDays;

            ApplyPay(job, def, rng);

            job.StressLoad = def.WorkDemand?.Stress ?? 40;
            job.SocialDemand = def.WorkDemand?.Social ?? 40;
            job.PhysicalDemand = def.WorkDemand?.Physical ?? 40;
            job.CognitiveDemand = def.WorkDemand?.Cognitive ?? 40;
            job.BurnoutAccum = rng.Next(0, Math.Max(1, def.WorkDemand?.BurnoutPotential ?? 30));

            job.BossName = "";
            job.BossRelationship = "neutral";
            job.TeamClimate = BuildTeamClimate(def, assignment);

            ApplyBenefits(job, assignment, def);

            job.IsStudent = false;
            job.IsRetired = false;
            job.HasSecondJob = false;
            job.SecondJobName = "";

            job.HireDate = DateTime.Now.AddDays(-rng.Next(30, 3650));
            job.DaysWorked = Math.Max(1, (int)(DateTime.Now - job.HireDate).TotalDays);

            job.MonthlyBillsHint = EstimateMonthlyBills(def, rng);
            job.SavingsRateHint = EstimateSavingsRate(def);

            ApplyGenericDrivesFromJob(npc, def, assignment);

            TrySetJobSlotMetadata(job, assignment);

            TryFinalizeCognitionFromJob(
                npc,
                rng,
                def.Requirements?.Education,
                def.Requirements?.Education,
                def.Category);
        }

        static void ApplyNoEmploymentDefault(SimCharacter npc, Random rng)
        {
            EnsureCore(npc);

            var options = new[]
            {
                "Retired",
                "Full-time student",
                "Unemployed",
                "Stay-at-home parent",
                "Family caregiver",
                "Between jobs",
                "Not currently working"
            };

            var picked = options[rng.Next(options.Length)];

            npc.Occupation = picked;

            var job = npc.Job!;
            job.JobName = picked;
            job.Employer = "";
            job.JobType = picked.ToLowerInvariant().Replace(" ", "_");
            job.IndustryPath = "not_employed";
            job.Department = "";
            job.TitleLevel = "";

            job.StartHour = 9;
            job.EndHour = 17;
            job.ShiftType = "none";
            job.WorkLocationMode = "none";
            job.WorkDays = Array.Empty<string>();

            job.HourlyRate = 0;
            job.WeeklyHours = 0;
            job.IsSalaried = false;
            job.AnnualSalary = 0;

            job.HasInsurance = false;
            job.HasRetirementMatch = false;
            job.HasPaidTimeOff = false;

            job.StressLoad = picked == "Unemployed" || picked == "Between jobs" ? 55 : 25;
            job.SocialDemand = 20;
            job.PhysicalDemand = 10;
            job.CognitiveDemand = 20;
            job.BurnoutAccum = 0;

            job.IsStudent = picked == "Full-time student";
            job.IsRetired = picked == "Retired";

            npc.Money ??= new MoneyProfile();
            npc.Money.Cash = rng.Next(20, 250);
            npc.Money.Bank = rng.Next(100, 3000);
            npc.Money.Debt = rng.Next(0, 12000);

            if (string.IsNullOrWhiteSpace(npc.Goal))
                npc.Goal = "Keep life stable while figuring out what comes next.";

            if (string.IsNullOrWhiteSpace(npc.Need))
                npc.Need = "A sense of security.";

            if (string.IsNullOrWhiteSpace(npc.Fear))
                npc.Fear = "Running out of options.";

            if (string.IsNullOrWhiteSpace(npc.Want))
                npc.Want = "A life that feels less uncertain.";
        }

        // ============================================================
        // CORE / TRAITS / COGNITION
        // ============================================================

        public static void EnsureCore(SimCharacter npc)
        {
            if (npc == null)
                return;

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
            if (npc == null)
                return;

            npc.Traits ??= new NpcTraits();

            if (npc.Traits.GetAll().Count > 0)
                return;

            try
            {
                NpcTraitInitializer.GenerateBalancedTraits(npc.Traits);
            }
            catch
            {
                try
                {
                    TraitJsonLoader.ApplyRolledLayers(npc.Traits);
                }
                catch
                {
                    npc.Traits.InitializeFastDefaults();
                }
            }
        }

        public static void EnsureCognition(
            SimCharacter npc,
            Random? rng = null,
            bool finalizeFromCurrentLife = false)
        {
            if (npc == null)
                return;

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

            if (npc.Id > 0 && wasMissing)
            {
                try
                {
                    CharacterRepository.SaveCognition(npc);
                }
                catch
                {
                }
            }
        }

        public static void RerollTraits(SimCharacter npc)
        {
            if (npc == null)
                return;

            npc.Traits ??= new NpcTraits();

            try
            {
                NpcTraitInitializer.GenerateBalancedTraits(npc.Traits);
            }
            catch
            {
                try
                {
                    TraitJsonLoader.ApplyRolledLayers(npc.Traits);
                }
                catch
                {
                    npc.Traits.InitializeFastDefaults();
                }
            }

            try
            {
                CognitiveProfileGenerator.RefreshForCurrentLife(npc);
            }
            catch
            {
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        static T? PickWeighted<T>(List<T> list, Func<T, int> weightFunc, Random rng)
        {
            if (list.Count == 0)
                return default;

            int total = list.Sum(x => Math.Max(1, weightFunc(x)));

            if (total <= 0)
                return list[rng.Next(list.Count)];

            int roll = rng.Next(total);
            int acc = 0;

            foreach (var item in list)
            {
                acc += Math.Max(1, weightFunc(item));

                if (roll < acc)
                    return item;
            }

            return list[list.Count - 1];
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

            if (max < min)
                (min, max) = (max, min);

            return rng.Next(min, max + 1);
        }

        static string PickEmploymentType(ProjectEveJobDefinition def, Random rng)
        {
            if (def.EmploymentTypes == null || def.EmploymentTypes.Count == 0)
                return "full_time";

            return def.EmploymentTypes[rng.Next(def.EmploymentTypes.Count)];
        }

        static ResolvedShift ResolveShift(ProjectEveJobDefinition def)
        {
            var primary = def.Schedule?.PrimaryShiftTemplate ?? "";

            return primary switch
            {
                "retail_open" => new ResolvedShift(6, 14, "opening", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "retail_day" => new ResolvedShift(9, 17, "days", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "retail_close" => new ResolvedShift(14, 23, "closing", new[] { "Wed", "Thu", "Fri", "Sat", "Sun" }),
                "grocery_early" => new ResolvedShift(5, 13, "early", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "restaurant_open" => new ResolvedShift(6, 14, "opening", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "restaurant_mid" => new ResolvedShift(10, 18, "mid", new[] { "Tue", "Wed", "Thu", "Fri", "Sat" }),
                "restaurant_evening" => new ResolvedShift(15, 23, "evening", new[] { "Wed", "Thu", "Fri", "Sat", "Sun" }),
                "bar_close" => new ResolvedShift(17, 2, "late", new[] { "Wed", "Thu", "Fri", "Sat", "Sun" }),
                "factory_first" => new ResolvedShift(6, 14, "first", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "factory_second" => new ResolvedShift(14, 22, "second", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "factory_third" => new ResolvedShift(22, 6, "third", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "hospital_day_12" => new ResolvedShift(7, 19, "12-hour day", new[] { "rotating" }),
                "hospital_night_12" => new ResolvedShift(19, 7, "12-hour night", new[] { "rotating" }),
                "school_day" => new ResolvedShift(7, 15, "school day", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "construction_day" => new ResolvedShift(7, 15, "construction day", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" }),
                "public_safety_12_day" => new ResolvedShift(7, 19, "12-hour day", new[] { "rotating" }),
                "public_safety_12_night" => new ResolvedShift(19, 7, "12-hour night", new[] { "rotating" }),
                "postal_early" => new ResolvedShift(6, 14, "early", new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }),
                _ => new ResolvedShift(8, 17, "days", new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
            };
        }

        static void ApplyPay(JobProfile job, ProjectEveJobDefinition def, Random rng)
        {
            if (def.Pay == null)
            {
                job.HourlyRate = 14;
                job.WeeklyHours = 40;
                job.IsSalaried = false;
                job.AnnualSalary = 0;
                return;
            }

            var pay = def.Pay;

            bool salary =
                string.Equals(pay.Type, "salary", StringComparison.OrdinalIgnoreCase) ||
                pay.StartingSalaryMin > 0 ||
                pay.StartingSalaryMax > 0;

            if (salary)
            {
                var min = pay.StartingSalaryMin > 0 ? pay.StartingSalaryMin : 40000;
                var max = pay.StartingSalaryMax > 0 ? pay.StartingSalaryMax : Math.Max(min, 52000);

                job.IsSalaried = true;
                job.AnnualSalary = (decimal)RandomDouble(rng, min, max);
                job.HourlyRate = 0;
                job.WeeklyHours = def.Schedule?.WeeklyHoursMax > 0
                    ? def.Schedule.WeeklyHoursMax
                    : 40;
            }
            else
            {
                var min = pay.StartingMin > 0 ? pay.StartingMin : 11.5;
                var max = pay.StartingMax > 0 ? pay.StartingMax : Math.Max(min, 16.5);

                job.IsSalaried = false;
                job.HourlyRate = (decimal)RandomDouble(rng, min, max);
                job.AnnualSalary = 0;

                var minHours = def.Schedule?.WeeklyHoursMin > 0 ? def.Schedule.WeeklyHoursMin : 25;
                var maxHours = def.Schedule?.WeeklyHoursMax > 0 ? def.Schedule.WeeklyHoursMax : 40;

                if (maxHours < minHours)
                    maxHours = minHours;

                job.WeeklyHours = rng.Next(minHours, maxHours + 1);
            }

            job.OvertimeRateMultiplier = pay.OvertimeMultiplier > 0
                ? (decimal)pay.OvertimeMultiplier
                : 1.5m;
        }

        static double RandomDouble(Random rng, double min, double max)
        {
            if (max < min)
                (min, max) = (max, min);

            return min + rng.NextDouble() * (max - min);
        }

        static string BuildTeamClimate(ProjectEveJobDefinition def, JobSlotAssignment assignment)
        {
            var stress = def.WorkDemand?.Stress ?? 40;
            var social = def.WorkDemand?.Social ?? 40;

            if (assignment.Category.Equals("fireEms", StringComparison.OrdinalIgnoreCase))
                return "crew-tight";

            if (stress >= 70)
                return "high-pressure";

            if (social >= 70)
                return "social and customer-facing";

            return "cordial";
        }

        static void ApplyBenefits(JobProfile job, JobSlotAssignment assignment, ProjectEveJobDefinition def)
        {
            var benefitPlan = assignment.WorkplaceDefaultBenefitPlan;

            job.HasInsurance = !string.IsNullOrWhiteSpace(benefitPlan) &&
                               !benefitPlan.Equals("none", StringComparison.OrdinalIgnoreCase);

            job.InsuranceProvider = job.HasInsurance
                ? benefitPlan
                : "";

            job.InsurancePremium = job.HasInsurance ? 140 : 0;
            job.CoveragePercent = job.HasInsurance ? 80 : 0;

            job.HasRetirementMatch = job.HasInsurance;
            job.RetirementMatchPercent = job.HasInsurance ? 3 : 0;

            job.HasPaidTimeOff = job.HasInsurance || def.Schedule?.WeeklyHoursMax >= 30;
            job.VacationDaysPerYear = job.HasPaidTimeOff ? 10 : 0;
            job.SickDaysPerYear = job.HasPaidTimeOff ? 5 : 2;
        }

        static decimal EstimateMonthlyBills(ProjectEveJobDefinition def, Random rng)
        {
            var baseBills = 1200;

            if (def.CareerLevel >= 3)
                baseBills += 400;

            if (def.CareerLevel >= 4)
                baseBills += 600;

            return baseBills + rng.Next(-200, 401);
        }

        static decimal EstimateSavingsRate(ProjectEveJobDefinition def)
        {
            return def.CareerLevel switch
            {
                <= 1 => 0.03m,
                2 => 0.06m,
                3 => 0.08m,
                _ => 0.10m
            };
        }

        static void ApplyGenericDrivesFromJob(
            SimCharacter npc,
            ProjectEveJobDefinition def,
            JobSlotAssignment assignment)
        {
            if (string.IsNullOrWhiteSpace(npc.Goal))
                npc.Goal = $"Keep steady work at {assignment.WorkplaceName}.";

            if (string.IsNullOrWhiteSpace(npc.Need))
                npc.Need = "Reliable income and people who feel familiar.";

            if (string.IsNullOrWhiteSpace(npc.Fear))
                npc.Fear = "Losing stability or being stuck with no way forward.";

            if (string.IsNullOrWhiteSpace(npc.Want))
                npc.Want = "A life that feels steady and worth waking up for.";

            npc.PersonalityContext =
                AppendLine(
                    npc.PersonalityContext,
                    $"Employment: {def.JobName} at {assignment.WorkplaceName}. " +
                    $"Job slot: {assignment.JobSlotId}. Category: {assignment.Category}.");
        }

        static string AppendLine(string existing, string line)
        {
            if (string.IsNullOrWhiteSpace(existing))
                return line;

            return existing.TrimEnd() + Environment.NewLine + line;
        }

        static void TrySetJobSlotMetadata(JobProfile job, JobSlotAssignment assignment)
        {
            TrySet(job, "WorkplaceId", assignment.WorkplaceId);
            TrySet(job, "WorkplaceName", assignment.WorkplaceName);
            TrySet(job, "WorkplaceType", assignment.WorkplaceType);
            TrySet(job, "JobId", assignment.JobId);
            TrySet(job, "JobSlotId", assignment.JobSlotId);
            TrySet(job, "JobCategory", assignment.Category);
            TrySet(job, "AttendancePolicy", assignment.WorkplaceAttendancePolicy);
            TrySet(job, "BenefitPlan", assignment.WorkplaceDefaultBenefitPlan);
        }

        static void TrySet(object target, string prop, object? value)
        {
            if (target == null || value == null)
                return;

            try
            {
                var p = target.GetType().GetProperty(prop);

                if (p == null || !p.CanWrite)
                    return;

                var dest = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

                if (dest == typeof(string))
                    p.SetValue(target, value.ToString());
                else if (dest == typeof(int) && int.TryParse(value.ToString(), out var i))
                    p.SetValue(target, i);
                else if (dest == typeof(double) && double.TryParse(value.ToString(), out var d))
                    p.SetValue(target, d);
                else if (dest == typeof(decimal) && decimal.TryParse(value.ToString(), out var m))
                    p.SetValue(target, m);
                else if (dest == typeof(bool) && bool.TryParse(value.ToString(), out var b))
                    p.SetValue(target, b);
                else
                    p.SetValue(target, Convert.ChangeType(value, dest));
            }
            catch
            {
            }
        }

        static void TryFinalizeCognitionFromJob(
            SimCharacter npc,
            Random rng,
            string? minimumEducation,
            string? typicalEducation,
            string? fieldOfStudy)
        {
            try
            {
                bool canFinalizeEducation = !npc.Cognition.LifeContextFinalized;

                CognitiveProfileGenerator.FinalizeLifeContext(
                    npc,
                    rng,
                    minimumEducation,
                    typicalEducation,
                    fieldOfStudy,
                    allowEducationUpgrade: canFinalizeEducation);
            }
            catch
            {
            }
        }


        public static void PrintWorkplaceSystemStatus()
        {
            var jobPath = DefaultJobMasterPath;
            var registryPath = DefaultWorkplaceRegistryPath;

            var master = LoadJobMaster();
            var registry = LoadWorkplaceRegistry();

            int jobCount = master.JobDefinitions?.Count ?? 0;
            int workplaceCount = registry.Workplaces?.Count ?? 0;
            int slotCount = 0;

            if (registry.Workplaces != null)
            {
                foreach (var workplace in registry.Workplaces)
                {
                    if (workplace.Positions == null)
                        continue;

                    foreach (var position in workplace.Positions)
                        slotCount += Math.Max(0, position.Slots);
                }
            }

            Console.WriteLine("Job master file:");
            Console.WriteLine("  " + jobPath);
            Console.WriteLine("  exists=" + File.Exists(jobPath));
            Console.WriteLine("Workplace registry file:");
            Console.WriteLine("  " + registryPath);
            Console.WriteLine("  exists=" + File.Exists(registryPath));
            Console.WriteLine("Workplace job system:");
            Console.WriteLine("  master jobs      = " + jobCount);
            Console.WriteLine("  workplaces       = " + workplaceCount);
            Console.WriteLine("  total job slots  = " + slotCount);

            if (workplaceCount > 0 && slotCount > 0 && jobCount == 0)
                Console.WriteLine("  note: master job file not loaded; using workplace positions with fallback pay/schedule defaults.");

            if (workplaceCount == 0 || slotCount == 0)
                Console.WriteLine("  WARNING: no workplace slots loaded, NPCs will fall back to not-employed defaults.");

            Console.WriteLine();
        }
    }

    // ============================================================
    // OLD SIMPLE DTOs FOR town_jobs.json
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

    // ============================================================
    // NEW DTOs FOR project_eve_all_jobs_master.json
    // ============================================================

    public class ProjectEveJobMaster
    {
        public List<string> Notes { get; set; } = new();
        public ProjectEveWorldInfo? World { get; set; }
        public ProjectEveEmploymentSystem? EmploymentSystem { get; set; }
        public List<ProjectEveJobDefinition> JobDefinitions { get; set; } = new();
    }

    public class ProjectEveWorldInfo
    {
        public string Id { get; set; } = "";
        public string TownStyle { get; set; } = "";
        public string Country { get; set; } = "";
        public string State { get; set; } = "";
        public int SimulationYear { get; set; }
    }

    public class ProjectEveEmploymentSystem
    {
        public bool RequireRealJobSlot { get; set; }
        public bool AllowUnemployment { get; set; }
        public bool AllowRetired { get; set; }
        public bool AllowStudent { get; set; }
        public bool AllowStayAtHomeParent { get; set; }
        public bool AllowOutsideTownEmployment { get; set; }
        public bool AllowSelfEmployment { get; set; }
        public bool AllowSecondJob { get; set; }
        public bool PromotionRequiresOpenSlot { get; set; }
    }

    public class ProjectEveJobDefinition
    {
        public string Id { get; set; } = "";
        public string JobName { get; set; } = "";
        public string Category { get; set; } = "";
        public int CareerLevel { get; set; }
        public int SelectionWeight { get; set; } = 10;
        public List<string> EmploymentTypes { get; set; } = new();
        public int MinimumAge { get; set; } = 16;

        public ProjectEveJobRequirements? Requirements { get; set; }
        public ProjectEveJobPay? Pay { get; set; }
        public ProjectEveJobSchedule? Schedule { get; set; }
        public ProjectEveJobWorkDemand? WorkDemand { get; set; }
        public ProjectEveJobSecurity? JobSecurity { get; set; }
        public ProjectEveNpcBakeHints? NpcBakeHints { get; set; }
    }

    public class ProjectEveJobRequirements
    {
        public string Education { get; set; } = "";
        public int ExperienceYears { get; set; }
        public List<string> Licenses { get; set; } = new();
        public List<string> Certifications { get; set; } = new();
        public bool BackgroundCheck { get; set; }
        public bool DrugScreen { get; set; }
        public bool DriversLicense { get; set; }
    }

    public class ProjectEveJobPay
    {
        public string Type { get; set; } = "hourly";

        public double StartingMin { get; set; }
        public double StartingMax { get; set; }
        public double PositionMax { get; set; }

        public double StartingSalaryMin { get; set; }
        public double StartingSalaryMax { get; set; }
        public double PositionMaxSalary { get; set; }

        public int AnnualReviewMonths { get; set; }
        public double NormalRaisePercentMin { get; set; }
        public double NormalRaisePercentMax { get; set; }
        public double ExcellentRaisePercentMin { get; set; }
        public double ExcellentRaisePercentMax { get; set; }

        public bool OvertimeEligible { get; set; }
        public double OvertimeMultiplier { get; set; } = 1.5;
    }

    public class ProjectEveJobSchedule
    {
        public List<string> AllowedShiftTemplates { get; set; } = new();
        public string PrimaryShiftTemplate { get; set; } = "";
        public int WeeklyHoursMin { get; set; }
        public int WeeklyHoursMax { get; set; }
        public string ScheduleType { get; set; } = "";
        public bool ShiftSwapAllowed { get; set; }
        public bool OvertimeAvailable { get; set; }
        public bool OvertimeMandatoryPossible { get; set; }
        public bool WeekendRequirement { get; set; }
        public bool HolidayRequirement { get; set; }
        public bool NightRequirement { get; set; }
    }

    public class ProjectEveJobWorkDemand
    {
        public int Stress { get; set; }
        public int Social { get; set; }
        public int Physical { get; set; }
        public int Cognitive { get; set; }
        public int Danger { get; set; }
        public int Boredom { get; set; }
        public int CustomerAbuse { get; set; }
        public int ManagementPressure { get; set; }
        public int ScheduleStress { get; set; }
        public int BurnoutPotential { get; set; }
    }

    public class ProjectEveJobSecurity
    {
        public int SecurityScore { get; set; }
        public int LayoffRisk { get; set; }
        public int TurnoverLevel { get; set; }
    }

    public class ProjectEveNpcBakeHints
    {
        public bool CanBeTier1 { get; set; }
        public bool CanBeTier2 { get; set; }
        public bool CanBeTier3 { get; set; }
        public bool CanBeTier4 { get; set; }
        public bool CanExistAsTier5History { get; set; }
        public bool UseRealWorkplaceSlot { get; set; }
    }

    // ============================================================
    // NEW DTOs FOR project_eve_workplace_registry.json
    // ============================================================

    public class ProjectEveWorkplaceRegistry
    {
        public ProjectEveWorkplaceWorld? World { get; set; }
        public string JobMasterFile { get; set; } = "";
        public ProjectEveGenerationRules? GenerationRules { get; set; }
        public ProjectEveDeepNpcPopulationTargets? DeepNpcPopulationTargets { get; set; }
        public ProjectEveTownEmploymentSummary? TownEmploymentSummary { get; set; }
        public List<ProjectEveWorkplace> Workplaces { get; set; } = new();
    }

    public class ProjectEveWorkplaceWorld
    {
        public string Id { get; set; } = "";
        public string TownStyle { get; set; } = "";
        public string State { get; set; } = "";
        public int TargetDeepNpcPopulation { get; set; }
    }

    public class ProjectEveGenerationRules
    {
        public bool RequireExistingJobSlot { get; set; }
        public bool OneNpcPerJobSlot { get; set; }
        public bool AllowVacancies { get; set; }
        public bool AllowRegionalCommute { get; set; }
        public bool PromotionRequiresVacantDestinationSlot { get; set; }
        public bool PreventUnlimitedPrestigeJobs { get; set; }
    }

    public class ProjectEveDeepNpcPopulationTargets
    {
        public int Employed { get; set; }
        public int NotEmployed { get; set; }
        public int Total { get; set; }
    }

    public class ProjectEveTownEmploymentSummary
    {
        public int WorkplaceCount { get; set; }
        public int LocalJobSlots { get; set; }
        public int RegionalSpecialtySlots { get; set; }
        public int TotalModeledJobSlots { get; set; }
        public int JobDefinitionsCovered { get; set; }
        public int JobDefinitionsAvailable { get; set; }
        public bool AllMasterJobsHaveAtLeastOneSlot { get; set; }
    }

    public class ProjectEveWorkplace
    {
        public string WorkplaceId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string District { get; set; } = "";
        public bool OutsideTown { get; set; }
        public string DefaultBenefitPlan { get; set; } = "";
        public string AttendancePolicy { get; set; } = "";
        public List<ProjectEveWorkplacePosition> Positions { get; set; } = new();
        public ProjectEveWorkplaceStaffing? Staffing { get; set; }
        public int BakedNpcTarget { get; set; }
        public int BackgroundNpcSlots { get; set; }
        public List<string> Notes { get; set; } = new();
    }

    public class ProjectEveWorkplacePosition
    {
        public string JobId { get; set; } = "";
        public string JobName { get; set; } = "";
        public string Category { get; set; } = "";
        public int Slots { get; set; }
        public int FilledAtWorldBake { get; set; }
        public int VacantAtWorldBake { get; set; }
    }

    public class ProjectEveWorkplaceStaffing
    {
        public int MinimumStaff { get; set; }
        public int IdealStaff { get; set; }
        public int MaximumStaff { get; set; }
        public int TotalJobSlots { get; set; }
    }

    public sealed class JobSlotAssignment
    {
        public string JobSlotId { get; set; } = "";
        public string WorkplaceId { get; set; } = "";
        public string WorkplaceName { get; set; } = "";
        public string WorkplaceType { get; set; } = "";
        public string District { get; set; } = "";
        public bool OutsideTown { get; set; }

        public string JobId { get; set; } = "";
        public string JobName { get; set; } = "";
        public string Category { get; set; } = "";

        public string WorkplaceDefaultBenefitPlan { get; set; } = "";
        public string WorkplaceAttendancePolicy { get; set; } = "";

        public int SlotNumber { get; set; }

        public ProjectEveJobDefinition? JobDefinition { get; set; }
    }

    readonly record struct ResolvedShift(
        int StartHour,
        int EndHour,
        string ShiftType,
        string[] WorkDays);
}