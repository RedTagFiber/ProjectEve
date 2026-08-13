using Microsoft.Data.Sqlite;
using ProjectEve.Characters.Base;
using ProjectEve.Money;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Bridge between the richer Project Eve job/workplace JSON and the existing
    /// SimCharacter + JobProfile + CharacterRepository system.
    ///
    /// Key rule: jobs are assigned from REAL workplace slots. No slot = no job.
    /// </summary>
    public static class SmallTownEmploymentSystem
    {
        private static readonly object Sync = new();
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private static JobMasterRoot? _jobMaster;
        private static TownWorkplaceRoot? _town;

        public static string JobMasterPath =>
            Environment.GetEnvironmentVariable("EVE_JOB_MASTER")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "World", "Ohio", "project_eve_all_jobs_master.json");

        public static string WorkplacePath =>
            Environment.GetEnvironmentVariable("EVE_TOWN_WORKPLACES")
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "World", "Ohio", "project_eve_town_workplaces.json");

        private static string DbPath => DatabaseInitializer.DbPath;
        private static string ConnStr => $"Data Source={DbPath}";

        public static void Initialize()
        {
            lock (Sync)
            {
                LoadData();
                EnsureTables();
                SyncSlotsFromJson();
            }
        }

        public static void Reload()
        {
            lock (Sync)
            {
                _jobMaster = null;
                _town = null;
                Initialize();
            }
        }

        private static void LoadData()
        {
            if (_jobMaster == null)
            {
                if (!File.Exists(JobMasterPath))
                    throw new FileNotFoundException("Project Eve job master not found.", JobMasterPath);

                _jobMaster = JsonSerializer.Deserialize<JobMasterRoot>(
                    File.ReadAllText(JobMasterPath), JsonOpts)
                    ?? throw new InvalidDataException("Could not deserialize job master.");
            }

            if (_town == null)
            {
                if (!File.Exists(WorkplacePath))
                    throw new FileNotFoundException("Project Eve workplace registry not found.", WorkplacePath);

                _town = JsonSerializer.Deserialize<TownWorkplaceRoot>(
                    File.ReadAllText(WorkplacePath), JsonOpts)
                    ?? throw new InvalidDataException("Could not deserialize workplace registry.");
            }
        }

        private static void EnsureTables()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS WorkplaceJobSlots (
                    JobSlotId TEXT PRIMARY KEY,
                    WorkplaceId TEXT NOT NULL,
                    WorkplaceName TEXT NOT NULL,
                    JobId TEXT NOT NULL,
                    JobName TEXT NOT NULL,
                    SlotNumber INTEGER NOT NULL,
                    OccupantNpcId INTEGER,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_workplace_slot_occupant
                    ON WorkplaceJobSlots(OccupantNpcId)
                    WHERE OccupantNpcId IS NOT NULL;

                CREATE INDEX IF NOT EXISTS ix_workplace_slots_open
                    ON WorkplaceJobSlots(WorkplaceId, JobId, OccupantNpcId);

                CREATE TABLE IF NOT EXISTS NpcEmployment (
                    NpcId INTEGER PRIMARY KEY,
                    WorkplaceId TEXT NOT NULL,
                    WorkplaceName TEXT NOT NULL,
                    JobId TEXT NOT NULL,
                    JobSlotId TEXT NOT NULL UNIQUE,
                    HireDate TEXT NOT NULL,
                    ScheduledStart TEXT,
                    ScheduledEnd TEXT,
                    WorkDaysJson TEXT,
                    AttendancePolicyId TEXT,
                    AttendancePoints REAL NOT NULL DEFAULT 0,
                    CallOffsRolling12Months INTEGER NOT NULL DEFAULT 0,
                    LateArrivalsRolling12Months INTEGER NOT NULL DEFAULT 0,
                    NoCallNoShowsRolling12Months INTEGER NOT NULL DEFAULT 0,
                    LastCallOffDate TEXT,
                    LastCallOffReason TEXT,
                    CurrentDiscipline TEXT NOT NULL DEFAULT 'none',
                    HealthPlanId TEXT,
                    HealthEnrolled INTEGER NOT NULL DEFAULT 0,
                    CoversSpouse INTEGER NOT NULL DEFAULT 0,
                    CoversChildren INTEGER NOT NULL DEFAULT 0,
                    NextPayReview TEXT,
                    CurrentHourlyRate REAL NOT NULL DEFAULT 0,
                    CurrentAnnualSalary REAL NOT NULL DEFAULT 0,
                    JobSatisfaction INTEGER NOT NULL DEFAULT 50,
                    LookingForAnotherJob INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE TABLE IF NOT EXISTS NpcWorkAbsence (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL,
                    WorkDate TEXT NOT NULL,
                    Kind TEXT NOT NULL,
                    Reason TEXT,
                    PointCost REAL NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UNIQUE(NpcId, WorkDate, Kind),
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );

                CREATE TABLE IF NOT EXISTS NpcPayHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NpcId INTEGER NOT NULL,
                    ChangedAt TEXT NOT NULL,
                    OldHourlyRate REAL,
                    NewHourlyRate REAL,
                    OldAnnualSalary REAL,
                    NewAnnualSalary REAL,
                    Reason TEXT,
                    FOREIGN KEY (NpcId) REFERENCES Characters(Id)
                );
                """;
            cmd.ExecuteNonQuery();
        }

        private static void SyncSlotsFromJson()
        {
            LoadData();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            foreach (var workplace in _town!.Workplaces)
            {
                foreach (var pos in workplace.Positions)
                {
                    for (int i = 1; i <= pos.Slots; i++)
                    {
                        string slotId = $"{workplace.WorkplaceId}:{pos.JobId}:{i:000}";
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            INSERT OR IGNORE INTO WorkplaceJobSlots
                            (JobSlotId, WorkplaceId, WorkplaceName, JobId, JobName, SlotNumber, OccupantNpcId, CreatedAt, UpdatedAt)
                            VALUES ($slot, $wid, $wname, $jid, $jname, $num, NULL, $now, $now);
                            """;
                        cmd.Parameters.AddWithValue("$slot", slotId);
                        cmd.Parameters.AddWithValue("$wid", workplace.WorkplaceId);
                        cmd.Parameters.AddWithValue("$wname", workplace.Name);
                        cmd.Parameters.AddWithValue("$jid", pos.JobId);
                        cmd.Parameters.AddWithValue("$jname", pos.JobName);
                        cmd.Parameters.AddWithValue("$num", i);
                        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            tx.Commit();
        }

        /// <summary>
        /// Assigns one real vacant job slot and copies the resulting work facts into the
        /// NPC's existing ProjectEve.Money.JobProfile. Returns false if no compatible slot exists.
        /// </summary>
        public static bool AssignOpenJob(
            SimCharacter npc,
            string? preferredCategory = null,
            Random? rng = null)
        {
            if (npc == null) return false;
            rng ??= new Random();
            Initialize();

            var jobs = _jobMaster!.JobDefinitions.ToDictionary(
                j => j.Id, StringComparer.OrdinalIgnoreCase);

            var workplaceById = _town!.Workplaces.ToDictionary(
                w => w.WorkplaceId, StringComparer.OrdinalIgnoreCase);

            var openSlots = ReadOpenSlots();

            var candidates = new List<JobCandidate>();
            foreach (var slot in openSlots)
            {
                if (!jobs.TryGetValue(slot.JobId, out var job)) continue;
                if (!workplaceById.TryGetValue(slot.WorkplaceId, out var workplace)) continue;
                if (npc.Age < job.MinimumAge) continue;

                if (!string.IsNullOrWhiteSpace(preferredCategory) &&
                    !string.Equals(job.Category, preferredCategory, StringComparison.OrdinalIgnoreCase))
                    continue;

                candidates.Add(new JobCandidate(slot, workplace, job));
            }

            // If a preferred category had no opening, let the NPC compete for the townwide pool.
            if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(preferredCategory))
            {
                foreach (var slot in openSlots)
                {
                    if (!jobs.TryGetValue(slot.JobId, out var job)) continue;
                    if (!workplaceById.TryGetValue(slot.WorkplaceId, out var workplace)) continue;
                    if (npc.Age < job.MinimumAge) continue;
                    candidates.Add(new JobCandidate(slot, workplace, job));
                }
            }

            if (candidates.Count == 0) return false;

            var chosen = WeightedPick(candidates, rng);
            ApplyEmploymentToNpc(npc, chosen, rng);
            OccupySlotAndSaveEmployment(npc, chosen);
            CharacterRepository.SaveJob(npc);
            return true;
        }

        private static JobCandidate WeightedPick(List<JobCandidate> candidates, Random rng)
        {
            int total = candidates.Sum(c => Math.Max(1, c.Job.SelectionWeight));
            int roll = rng.Next(total);
            int acc = 0;

            foreach (var c in candidates)
            {
                acc += Math.Max(1, c.Job.SelectionWeight);
                if (roll < acc) return c;
            }

            return candidates[^1];
        }

        private static List<SlotRow> ReadOpenSlots()
        {
            var list = new List<SlotRow>();
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT JobSlotId, WorkplaceId, WorkplaceName, JobId, JobName
                FROM WorkplaceJobSlots
                WHERE OccupantNpcId IS NULL;
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new SlotRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
            return list;
        }

        private static void ApplyEmploymentToNpc(SimCharacter npc, JobCandidate c, Random rng)
        {
            npc.Job ??= new JobProfile();
            npc.Occupation = c.Job.JobName;

            var j = npc.Job;
            j.JobName = c.Job.JobName;
            j.Employer = c.Workplace.Name;
            j.JobType = c.Job.EmploymentTypes.FirstOrDefault() ?? "full_time";
            j.IndustryPath = c.Job.Category;
            j.Department = c.Job.Category;
            j.TitleLevel = LevelName(c.Job.CareerLevel);
            j.WorkLocationMode = "onsite";

            var shift = ResolveShift(c.Job, rng);
            j.StartHour = ParseHour(shift.Start, 9);
            j.EndHour = ParseHour(shift.End, 17);
            j.ShiftType = c.Job.Schedule.ScheduleType ?? "fixed";
            j.WorkDays = ResolveWorkDays(shift, c.Job.Schedule, rng);
            j.WeeklyHours = RandomDecimal(
                rng,
                Math.Max(1, c.Job.Schedule.WeeklyHoursMin),
                Math.Max(c.Job.Schedule.WeeklyHoursMin, c.Job.Schedule.WeeklyHoursMax));

            if (c.Workplace.OutsideTown)
                j.CommuteMinutesOneWay = rng.Next(15, 46);
            else
                j.CommuteMinutesOneWay = rng.Next(2, 26);

            if (string.Equals(c.Job.Pay.Type, "salary", StringComparison.OrdinalIgnoreCase))
            {
                j.IsSalaried = true;
                j.AnnualSalary = RandomDecimal(
                    rng,
                    c.Job.Pay.StartingSalaryMin,
                    Math.Max(c.Job.Pay.StartingSalaryMin, c.Job.Pay.StartingSalaryMax));
                j.HourlyRate = 0;
            }
            else
            {
                j.IsSalaried = false;
                j.HourlyRate = RandomDecimal(
                    rng,
                    c.Job.Pay.StartingMin,
                    Math.Max(c.Job.Pay.StartingMin, c.Job.Pay.StartingMax));
                j.AnnualSalary = 0;
            }

            j.OvertimeRateMultiplier = c.Job.Pay.OvertimeEligible ? 1.5m : 0m;
            j.TypicalOvertimeHoursPerWeek = c.Job.Schedule.OvertimeAvailable ? rng.Next(0, 6) : 0;

            j.StressLoad = c.Job.WorkDemand.Stress;
            j.SocialDemand = c.Job.WorkDemand.Social;
            j.PhysicalDemand = c.Job.WorkDemand.Physical;
            j.CognitiveDemand = c.Job.WorkDemand.Cognitive;
            j.BurnoutAccum = rng.Next(0, Math.Max(5, c.Job.WorkDemand.BurnoutPotential / 3));

            string planId = c.Workplace.DefaultBenefitPlan ?? c.Job.BenefitPlanOptions.FirstOrDefault() ?? "none";
            var plan = FindBenefitPlan(planId);

            j.HasInsurance = plan?.HealthInsurance ?? false;
            j.InsuranceProvider = planId;
            j.InsurancePremium = (decimal)(plan?.EmployeeMonthlyPremium ?? 0);
            j.CoveragePercent = CoverageFromQuality(plan?.InsuranceQuality);
            j.HasRetirementMatch = plan?.RetirementPlan ?? false;
            j.RetirementMatchPercent = (decimal)(plan?.RetirementMatchPercent ?? 0);
            j.HasPaidTimeOff = (plan?.PaidSickDays ?? 0) > 0 || (plan?.VacationDays ?? 0) > 0;
            j.SickDaysPerYear = plan?.PaidSickDays ?? 0;
            j.VacationDaysPerYear = plan?.VacationDays ?? 0;

            j.BossName = "";
            j.BossRelationship = "neutral";
            j.TeamClimate = "cordial";
            j.HireDate = DateTime.Now.AddDays(-rng.Next(30, 3650));
            j.DaysWorked = Math.Max(1, (int)(DateTime.Now - j.HireDate).TotalDays);
        }

        private static void OccupySlotAndSaveEmployment(SimCharacter npc, JobCandidate c)
        {
            var shift = ResolveShift(c.Job, new Random(npc.Id ^ Environment.TickCount));
            string[] workDays = npc.Job.WorkDays ?? Array.Empty<string>();
            string planId = c.Workplace.DefaultBenefitPlan ?? c.Job.BenefitPlanOptions.FirstOrDefault() ?? "none";

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var slot = conn.CreateCommand())
            {
                slot.Transaction = tx;
                slot.CommandText = """
                    UPDATE WorkplaceJobSlots
                    SET OccupantNpcId=$npc, UpdatedAt=$now
                    WHERE JobSlotId=$slot AND OccupantNpcId IS NULL;
                    """;
                slot.Parameters.AddWithValue("$npc", npc.Id);
                slot.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                slot.Parameters.AddWithValue("$slot", c.Slot.JobSlotId);

                if (slot.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Job slot was already occupied.");
            }

            using (var emp = conn.CreateCommand())
            {
                emp.Transaction = tx;
                emp.CommandText = """
                    INSERT INTO NpcEmployment
                    (NpcId, WorkplaceId, WorkplaceName, JobId, JobSlotId, HireDate,
                     ScheduledStart, ScheduledEnd, WorkDaysJson, AttendancePolicyId,
                     HealthPlanId, HealthEnrolled, NextPayReview,
                     CurrentHourlyRate, CurrentAnnualSalary)
                    VALUES
                    ($npc, $wid, $wname, $jid, $slot, $hire,
                     $start, $end, $days, $attendance,
                     $plan, $health, $review, $hourly, $annual)
                    ON CONFLICT(NpcId) DO UPDATE SET
                     WorkplaceId=$wid, WorkplaceName=$wname, JobId=$jid, JobSlotId=$slot,
                     HireDate=$hire, ScheduledStart=$start, ScheduledEnd=$end,
                     WorkDaysJson=$days, AttendancePolicyId=$attendance,
                     HealthPlanId=$plan, HealthEnrolled=$health, NextPayReview=$review,
                     CurrentHourlyRate=$hourly, CurrentAnnualSalary=$annual;
                    """;
                emp.Parameters.AddWithValue("$npc", npc.Id);
                emp.Parameters.AddWithValue("$wid", c.Workplace.WorkplaceId);
                emp.Parameters.AddWithValue("$wname", c.Workplace.Name);
                emp.Parameters.AddWithValue("$jid", c.Job.Id);
                emp.Parameters.AddWithValue("$slot", c.Slot.JobSlotId);
                emp.Parameters.AddWithValue("$hire", npc.Job.HireDate.ToString("o"));
                emp.Parameters.AddWithValue("$start", (object?)shift.Start ?? DBNull.Value);
                emp.Parameters.AddWithValue("$end", (object?)shift.End ?? DBNull.Value);
                emp.Parameters.AddWithValue("$days", JsonSerializer.Serialize(workDays));
                emp.Parameters.AddWithValue("$attendance", c.Workplace.AttendancePolicy ?? c.Job.AttendancePolicy ?? "standard");
                emp.Parameters.AddWithValue("$plan", planId);
                emp.Parameters.AddWithValue("$health", npc.Job.HasInsurance ? 1 : 0);
                emp.Parameters.AddWithValue("$review", npc.Job.HireDate.AddMonths(Math.Max(1, c.Job.Pay.AnnualReviewMonths)).ToString("o"));
                emp.Parameters.AddWithValue("$hourly", (double)npc.Job.HourlyRate);
                emp.Parameters.AddWithValue("$annual", (double)npc.Job.AnnualSalary);
                emp.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>
        /// Exact availability check. Uses NpcEmployment so 6:00-14:30 remains exact
        /// even though legacy JobProfile only stores integer hours.
        /// </summary>
        public static bool IsNpcWorking(int npcId, DateTime localTime)
        {
            Initialize();
            var e = GetEmployment(npcId);
            if (e == null) return false;

            if (HasAbsence(npcId, localTime.Date))
                return false;

            if (!e.WorkDays.Contains(localTime.ToString("ddd"), StringComparer.OrdinalIgnoreCase))
                return false;

            if (!TimeOnly.TryParse(e.Start, out var start) ||
                !TimeOnly.TryParse(e.End, out var end))
                return false;

            var now = TimeOnly.FromDateTime(localTime);

            if (start == end) return true;
            if (start < end) return now >= start && now < end;
            return now >= start || now < end;
        }

        public static EmploymentSnapshot? GetEmployment(int npcId)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT WorkplaceId, WorkplaceName, JobId, JobSlotId,
                       ScheduledStart, ScheduledEnd, WorkDaysJson,
                       AttendancePolicyId, AttendancePoints, CurrentDiscipline
                FROM NpcEmployment WHERE NpcId=$id;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            string[] days;
            try { days = JsonSerializer.Deserialize<string[]>(reader.IsDBNull(6) ? "[]" : reader.GetString(6)) ?? Array.Empty<string>(); }
            catch { days = Array.Empty<string>(); }

            return new EmploymentSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                days,
                reader.IsDBNull(7) ? "standard" : reader.GetString(7),
                reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                reader.IsDBNull(9) ? "none" : reader.GetString(9));
        }

        public static void RecordCallOff(
            SimCharacter npc,
            DateTime workDate,
            string reason,
            bool lateNotice = false,
            bool noCallNoShow = false)
        {
            if (npc == null) return;
            Initialize();

            var e = GetEmployment(npc.Id);
            if (e == null) return;

            var policy = FindAttendancePolicy(e.AttendancePolicyId);
            double pointCost = noCallNoShow
                ? policy?.Events.NoCallNoShow ?? 3.0
                : lateNotice
                    ? policy?.Events.CallOffLateNotice ?? 1.5
                    : policy?.Events.CallOffNormal ?? 1.0;

            string kind = noCallNoShow ? "no_call_no_show" : "call_off";

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var absence = conn.CreateCommand())
            {
                absence.Transaction = tx;
                absence.CommandText = """
                    INSERT OR IGNORE INTO NpcWorkAbsence
                    (NpcId, WorkDate, Kind, Reason, PointCost, CreatedAt)
                    VALUES ($id, $date, $kind, $reason, $points, $now);
                    """;
                absence.Parameters.AddWithValue("$id", npc.Id);
                absence.Parameters.AddWithValue("$date", workDate.Date.ToString("yyyy-MM-dd"));
                absence.Parameters.AddWithValue("$kind", kind);
                absence.Parameters.AddWithValue("$reason", reason ?? "");
                absence.Parameters.AddWithValue("$points", pointCost);
                absence.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));

                if (absence.ExecuteNonQuery() == 0)
                {
                    tx.Rollback();
                    return; // already recorded
                }
            }

            double newPoints = e.AttendancePoints + pointCost;
            string discipline = DisciplineFor(policy, newPoints);

            using (var update = conn.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = """
                    UPDATE NpcEmployment SET
                        AttendancePoints=$points,
                        CallOffsRolling12Months=CallOffsRolling12Months + $call,
                        NoCallNoShowsRolling12Months=NoCallNoShowsRolling12Months + $ncns,
                        LastCallOffDate=$date,
                        LastCallOffReason=$reason,
                        CurrentDiscipline=$discipline
                    WHERE NpcId=$id;
                    """;
                update.Parameters.AddWithValue("$points", newPoints);
                update.Parameters.AddWithValue("$call", noCallNoShow ? 0 : 1);
                update.Parameters.AddWithValue("$ncns", noCallNoShow ? 1 : 0);
                update.Parameters.AddWithValue("$date", workDate.Date.ToString("yyyy-MM-dd"));
                update.Parameters.AddWithValue("$reason", reason ?? "");
                update.Parameters.AddWithValue("$discipline", discipline);
                update.Parameters.AddWithValue("$id", npc.Id);
                update.ExecuteNonQuery();
            }

            tx.Commit();

            double fireAt = policy?.Discipline.TerminationEligibleAt ?? 7.0;
            if (newPoints >= fireAt)
                TerminateEmployment(npc, "attendance_limit");
        }

        public static void TerminateEmployment(SimCharacter npc, string reason)
        {
            if (npc == null) return;
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var tx = conn.BeginTransaction();

            string? slotId = null;
            using (var get = conn.CreateCommand())
            {
                get.Transaction = tx;
                get.CommandText = "SELECT JobSlotId FROM NpcEmployment WHERE NpcId=$id";
                get.Parameters.AddWithValue("$id", npc.Id);
                slotId = get.ExecuteScalar() as string;
            }

            if (!string.IsNullOrWhiteSpace(slotId))
            {
                using var free = conn.CreateCommand();
                free.Transaction = tx;
                free.CommandText = """
                    UPDATE WorkplaceJobSlots
                    SET OccupantNpcId=NULL, UpdatedAt=$now
                    WHERE JobSlotId=$slot;
                    """;
                free.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                free.Parameters.AddWithValue("$slot", slotId);
                free.ExecuteNonQuery();
            }

            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM NpcEmployment WHERE NpcId=$id";
                del.Parameters.AddWithValue("$id", npc.Id);
                del.ExecuteNonQuery();
            }

            tx.Commit();

            npc.Job.JobType = "unemployed";
            npc.Job.JobName = "";
            npc.Job.Employer = "";
            npc.Occupation = "Unemployed";
            npc.PersonalityContext += $" Employment ended: {reason}.";
            CharacterRepository.SaveJob(npc);
        }

        private static bool HasAbsence(int npcId, DateTime date)
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM NpcWorkAbsence WHERE NpcId=$id AND WorkDate=$date LIMIT 1";
            cmd.Parameters.AddWithValue("$id", npcId);
            cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            return cmd.ExecuteScalar() != null;
        }

        private static AttendancePolicy? FindAttendancePolicy(string id)
        {
            if (_jobMaster == null) LoadData();
            if (_jobMaster!.AttendancePolicies.TryGetValue(id, out var p))
                return p;
            if (_jobMaster.AttendancePolicies.TryGetValue("standard", out var s))
                return s;
            return null;
        }

        private static BenefitPlan? FindBenefitPlan(string id)
        {
            if (_jobMaster == null) LoadData();
            if (_jobMaster!.BenefitPlans.TryGetValue(id, out var p))
                return p;
            return null;
        }

        private static string DisciplineFor(AttendancePolicy? p, double points)
        {
            if (p == null) return points >= 7 ? "termination" : "none";
            if (points >= p.Discipline.TerminationEligibleAt) return "termination";
            if (points >= p.Discipline.FinalWarningAt) return "final_warning";
            if (points >= p.Discipline.WrittenWarningAt) return "written_warning";
            if (points >= p.Discipline.CoachingAt) return "coaching";
            return "none";
        }

        private static ShiftTemplate ResolveShift(JobDefinition job, Random rng)
        {
            if (_jobMaster == null) LoadData();

            string? id = job.Schedule.PrimaryShiftTemplate;
            if (string.IsNullOrWhiteSpace(id))
                id = job.Schedule.AllowedShiftTemplates.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(id) &&
                _jobMaster!.ShiftTemplates.TryGetValue(id!, out var shift))
                return shift;

            return new ShiftTemplate { Start = "09:00", End = "17:00" };
        }

        private static string[] ResolveWorkDays(ShiftTemplate shift, JobScheduleDefinition schedule, Random rng)
        {
            if (shift.Days.ValueKind == JsonValueKind.Array)
            {
                return shift.Days.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? "")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
            }

            // Variable schedule: build a stable weekly set from expected hours.
            int targetDays = Math.Clamp(
                (int)Math.Round(((schedule.WeeklyHoursMin + schedule.WeeklyHoursMax) / 2.0) / 8.0),
                2, 6);

            string[] all = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            return all.OrderBy(_ => rng.Next()).Take(targetDays).ToArray();
        }

        private static int ParseHour(string? value, int fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (TimeOnly.TryParse(value, out var t)) return t.Hour;
            return fallback;
        }

        private static decimal RandomDecimal(Random rng, double min, double max)
        {
            if (max < min) (min, max) = (max, min);
            if (Math.Abs(max - min) < 0.0001) return (decimal)min;
            return Math.Round((decimal)(min + rng.NextDouble() * (max - min)), 2);
        }

        private static string LevelName(int level) => level switch
        {
            <= 1 => "entry",
            2 => "mid",
            3 => "senior",
            4 => "manager",
            _ => "owner"
        };

        private static decimal CoverageFromQuality(string? q) => (q ?? "").ToLowerInvariant() switch
        {
            "excellent" => 90,
            "good" => 85,
            "average" => 80,
            "fair" => 70,
            _ => 0
        };

        // ---------------- DTOs ----------------

        public record EmploymentSnapshot(
            string WorkplaceId,
            string WorkplaceName,
            string JobId,
            string JobSlotId,
            string Start,
            string End,
            string[] WorkDays,
            string AttendancePolicyId,
            double AttendancePoints,
            string Discipline);

        private record SlotRow(
            string JobSlotId,
            string WorkplaceId,
            string WorkplaceName,
            string JobId,
            string JobName);

        private record JobCandidate(
            SlotRow Slot,
            WorkplaceDefinition Workplace,
            JobDefinition Job);

        public class JobMasterRoot
        {
            public Dictionary<string, ShiftTemplate> ShiftTemplates { get; set; } = new();
            public Dictionary<string, AttendancePolicy> AttendancePolicies { get; set; } = new();
            public Dictionary<string, BenefitPlan> BenefitPlans { get; set; } = new();
            public List<JobDefinition> JobDefinitions { get; set; } = new();
        }

        public class JobDefinition
        {
            public string Id { get; set; } = "";
            public string JobName { get; set; } = "";
            public string Category { get; set; } = "";
            public int CareerLevel { get; set; }
            public int SelectionWeight { get; set; } = 1;
            public List<string> EmploymentTypes { get; set; } = new();
            public int MinimumAge { get; set; } = 18;
            public JobPayDefinition Pay { get; set; } = new();
            public JobScheduleDefinition Schedule { get; set; } = new();
            public string AttendancePolicy { get; set; } = "standard";
            public List<string> BenefitPlanOptions { get; set; } = new();
            public WorkDemandDefinition WorkDemand { get; set; } = new();
        }

        public class JobPayDefinition
        {
            public string Type { get; set; } = "hourly";
            public double StartingMin { get; set; }
            public double StartingMax { get; set; }
            public double PositionMax { get; set; }
            public double StartingSalaryMin { get; set; }
            public double StartingSalaryMax { get; set; }
            public double PositionMaxSalary { get; set; }
            public int AnnualReviewMonths { get; set; } = 12;
            public bool OvertimeEligible { get; set; } = true;
        }

        public class JobScheduleDefinition
        {
            public List<string> AllowedShiftTemplates { get; set; } = new();
            public string? PrimaryShiftTemplate { get; set; }
            public double WeeklyHoursMin { get; set; } = 40;
            public double WeeklyHoursMax { get; set; } = 40;
            public string? ScheduleType { get; set; }
            public bool OvertimeAvailable { get; set; }
        }

        public class WorkDemandDefinition
        {
            public int Stress { get; set; }
            public int Social { get; set; }
            public int Physical { get; set; }
            public int Cognitive { get; set; }
            public int BurnoutPotential { get; set; }
        }

        public class ShiftTemplate
        {
            public string? Start { get; set; }
            public string? End { get; set; }
            public JsonElement Days { get; set; }
        }

        public class BenefitPlan
        {
            public bool HealthInsurance { get; set; }
            public string? InsuranceQuality { get; set; }
            public double EmployeeMonthlyPremium { get; set; }
            public bool RetirementPlan { get; set; }
            public double RetirementMatchPercent { get; set; }
            public int PaidSickDays { get; set; }
            public int VacationDays { get; set; }
        }

        public class AttendancePolicy
        {
            public AttendanceEvents Events { get; set; } = new();
            public AttendanceDiscipline Discipline { get; set; } = new();
        }

        public class AttendanceEvents
        {
            public double CallOffNormal { get; set; } = 1.0;
            public double CallOffLateNotice { get; set; } = 1.5;
            public double NoCallNoShow { get; set; } = 3.0;
        }

        public class AttendanceDiscipline
        {
            public double CoachingAt { get; set; } = 3;
            public double WrittenWarningAt { get; set; } = 5;
            public double FinalWarningAt { get; set; } = 6;
            public double TerminationEligibleAt { get; set; } = 7;
        }

        public class TownWorkplaceRoot
        {
            public List<WorkplaceDefinition> Workplaces { get; set; } = new();
        }

        public class WorkplaceDefinition
        {
            public string WorkplaceId { get; set; } = "";
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public string District { get; set; } = "";
            public bool OutsideTown { get; set; }
            public string? DefaultBenefitPlan { get; set; }
            public string? AttendancePolicy { get; set; }
            public List<WorkplacePosition> Positions { get; set; } = new();
        }

        public class WorkplacePosition
        {
            public string JobId { get; set; } = "";
            public string JobName { get; set; } = "";
            public string Category { get; set; } = "";
            public int Slots { get; set; }
        }
    }
}
