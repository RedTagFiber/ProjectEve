using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class BhsStaffPersistenceService
{
    private readonly NpcStudioOptions _options;
    private readonly MediaStorageService _media;
    private readonly ComfyWorkflowService _comfy;

    public BhsStaffPersistenceService(
        NpcStudioOptions options,
        MediaStorageService media,
        ComfyWorkflowService comfy)
    {
        _options = options;
        _media = media;
        _comfy = comfy;
    }

    public static IReadOnlyList<string> OhioRaceEthnicityOptions { get; } = new[]
    {
        "White, non-Hispanic",
        "Black or African American, non-Hispanic",
        "Asian, non-Hispanic",
        "American Indian or Alaska Native, non-Hispanic",
        "Native Hawaiian or Other Pacific Islander, non-Hispanic",
        "Two or More Races, non-Hispanic",
        "Hispanic or Latino - White",
        "Hispanic or Latino - Black or African American",
        "Hispanic or Latino - Two or More Races",
        "Hispanic or Latino - Other race",
        "Other / Mixed / Self-described"
    };

    public async Task<BhsStaffSaveResult> SaveOneHouseholdAsync(
        BhsStaffDraft staff,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staff);

        if (string.IsNullOrWhiteSpace(staff.RaceEthnicity))
            throw new InvalidOperationException("Choose Race / Ethnicity before saving.");

        var batchId = $"BHS-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..39];
        var createdNpcIds = new List<int>();

        EnsureSaveSchema();

        using var main = new SqliteConnection($"Data Source={_options.MainDbPath}");
        main.Open();
        using var tx = main.BeginTransaction();

        var householdId = NextHouseholdId(main, tx);

        try
        {
            var staffId = CreateNpc(
                main, tx, batchId, householdId,
                staff.FirstName, staff.MiddleName, staff.LastName,
                staff.Age, staff.Gender, staff.RaceEthnicity, staff.Tier,
                staff.Occupation, "Bellefontaine High School",
                staff.Home.StreetOrArea, "Bellefontaine, Ohio",
                staff.Personality.Summary, staff.Personality.Goal,
                staff.Personality.Need, staff.Personality.Fear, staff.Want,
                staff.IQ, staff.Archetype1, staff.Archetype2, staff.Archetype3,
                staff.Personality.PublicPersona, staff.Personality.PrivatePersona, staff.HiddenBehavior,
                staff.Appearance, "GeneratedSchoolStaff", staff.RoleTitle);

            createdNpcIds.Add(staffId);
            WriteHousehold(main, tx, householdId, batchId, staff.Home);
            WriteHouseholdMember(main, tx, householdId, staffId, "Head", batchId);

            SaveAdultProfile(main, tx, staffId, batchId, staff);
            SaveEducation(main, tx, staffId, batchId, staff.Education);
            SaveQualifications(main, tx, staffId, batchId, staff.Certifications, staff.RoleTitle);
            SaveFinance(main, tx, staffId, batchId, staff.Banking, "Individual");
            SavePhone(main, tx, staffId, batchId, staff.Phone);
            SaveVehicle(main, tx, staffId, batchId, staff.Vehicle);
            SaveTraits(main, tx, staffId, batchId, staff.Personality.Traits);
            SaveCompleteness(main, tx, staffId, batchId, 90);

            int? spouseId = null;
            if (staff.Spouse.Include && !string.IsNullOrWhiteSpace(staff.Spouse.Name))
            {
                var spouseName = SplitName(staff.Spouse.Name);
                spouseId = CreateNpc(
                    main, tx, batchId, householdId,
                    spouseName.First, spouseName.Middle, spouseName.Last,
                    staff.Spouse.Age, staff.Spouse.Gender, staff.Spouse.RaceEthnicity,
                    Math.Clamp(staff.Spouse.Tier, 4, 5),
                    staff.Spouse.Occupation, staff.Spouse.Employer,
                    staff.Home.StreetOrArea, "Bellefontaine, Ohio",
                    staff.Spouse.PersonalitySummary, staff.Spouse.Goal,
                    staff.Spouse.Need, staff.Spouse.Fear,
                    string.IsNullOrWhiteSpace(staff.Spouse.Want) ? "A stable, fulfilling life with room for family and personal growth." : staff.Spouse.Want,
                    staff.Spouse.IQ <= 0 ? 105 : staff.Spouse.IQ,
                    string.IsNullOrWhiteSpace(staff.Spouse.Archetype1) ? "Caregiver" : staff.Spouse.Archetype1,
                    staff.Spouse.Archetype2, staff.Spouse.Archetype3,
                    staff.Spouse.PublicPersona, staff.Spouse.PrivatePersona, staff.Spouse.HiddenBehavior,
                    staff.Spouse.Appearance, "GeneratedSchoolStaffSpouse", "Spouse");

                createdNpcIds.Add(spouseId.Value);
                WriteHouseholdMember(main, tx, householdId, spouseId.Value, "Spouse", batchId);
                SaveEducation(main, tx, spouseId.Value, batchId, staff.Spouse.Education);
                SaveFinance(main, tx, spouseId.Value, batchId, staff.Spouse.Banking, "Individual");
                SavePhone(main, tx, spouseId.Value, batchId, staff.Spouse.Phone);
                SaveVehicle(main, tx, spouseId.Value, batchId, staff.Spouse.Vehicle);
                SaveTraits(main, tx, spouseId.Value, batchId, staff.Spouse.Traits);
                SaveCompleteness(main, tx, spouseId.Value, batchId, 85);
            }

            var childIds = new List<int>();
            foreach (var child in staff.Children)
            {
                var childName = SplitName(child.Name);
                var childId = CreateNpc(
                    main, tx, batchId, householdId,
                    childName.First, childName.Middle, childName.Last,
                    child.Age, child.Gender, child.RaceEthnicity,
                    5, child.Age >= 5 ? "Student" : "Child",
                    child.Age >= 14 ? "Bellefontaine High School" : "",
                    staff.Home.StreetOrArea, "Bellefontaine, Ohio",
                    child.PersonalitySummary, child.Goal, child.Need, child.Fear,
                    string.IsNullOrWhiteSpace(child.Want) ? "To feel capable, included, and free to discover who they are becoming." : child.Want,
                    child.IQ <= 0 ? 100 : child.IQ,
                    string.IsNullOrWhiteSpace(child.Archetype1) ? "Observer" : child.Archetype1,
                    child.Archetype2, child.Archetype3,
                    child.PublicPersona, child.PrivatePersona, child.HiddenBehavior,
                    child.Appearance, "GeneratedSchoolStaffChild", "Child");

                childIds.Add(childId);
                createdNpcIds.Add(childId);
                WriteHouseholdMember(main, tx, householdId, childId, "Child", batchId);
                SaveTraits(main, tx, childId, batchId, child.Traits);
                SavePhone(main, tx, childId, batchId, child.Phone);
                SaveCompleteness(main, tx, childId, batchId, 80);
            }

            SaveJointFinance(main, tx, householdId, batchId, staff);
            tx.Commit();

            SaveFamilyLinks(staffId, spouseId, childIds, batchId);
            AssignSchoolSlot(staffId, staff.RoleTitle, batchId);

            if (spouseId.HasValue && LooksLikeSchoolJob(staff.Spouse.Occupation))
                AssignBestSpouseSchoolSlot(spouseId.Value, staff.Spouse.Occupation, batchId);

            SaveChildSchoolRows(childIds, staff.Children, batchId);

            var portraitResults = new List<string>();
            portraitResults.Add(await QueuePortraitAsync(
                staffId, householdId, batchId, staff.FullName, staff.Age, staff.Gender,
                staff.RaceEthnicity, staff.Appearance, staff.RoleTitle, true, cancellationToken));

            if (spouseId.HasValue)
            {
                portraitResults.Add(await QueuePortraitAsync(
                    spouseId.Value, householdId, batchId, staff.Spouse.Name, staff.Spouse.Age,
                    staff.Spouse.Gender, staff.Spouse.RaceEthnicity, staff.Spouse.Appearance,
                    staff.Spouse.Occupation, LooksLikeSchoolJob(staff.Spouse.Occupation), cancellationToken));
            }

            for (var i = 0; i < childIds.Count; i++)
            {
                var child = staff.Children[i];
                portraitResults.Add(await QueuePortraitAsync(
                    childIds[i], householdId, batchId, child.Name, child.Age,
                    child.Gender, child.RaceEthnicity, child.Appearance,
                    "age-appropriate student profile", false, cancellationToken));
            }

            return new BhsStaffSaveResult
            {
                Success = true,
                BatchId = batchId,
                HouseholdId = householdId,
                StaffNpcId = staffId,
                CreatedNpcIds = createdNpcIds,
                Message = $"Saved {createdNpcIds.Count} NPC(s). Household {householdId}. " +
                          $"Comfy portrait jobs: {portraitResults.Count}."
            };
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }

    public BhsStaffDeleteResult DeleteGeneratedBatch(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            return new BhsStaffDeleteResult(false, "Missing batch id.");

        EnsureSaveSchema();

        var npcIds = new List<int>();
        long householdId = 0;

        using (var main = new SqliteConnection($"Data Source={_options.MainDbPath}"))
        {
            main.Open();

            using (var read = main.CreateCommand())
            {
                read.CommandText = """
                    SELECT NpcId FROM NpcCreationProvenance
                    WHERE CreationBatchId=$batch;
                    """;
                read.Parameters.AddWithValue("$batch", batchId);
                using var r = read.ExecuteReader();
                while (r.Read()) npcIds.Add(r.GetInt32(0));
            }

            using (var hh = main.CreateCommand())
            {
                hh.CommandText = "SELECT HouseholdId FROM GeneratedHouseholds WHERE GenerationBatchId=$batch LIMIT 1;";
                hh.Parameters.AddWithValue("$batch", batchId);
                householdId = Convert.ToInt64(hh.ExecuteScalar() ?? 0);
            }
        }

        if (npcIds.Count == 0)
            return new BhsStaffDeleteResult(false, "No generated NPCs found for that batch.");

        RemoveSchoolAssignments(batchId, npcIds);
        RemoveFamilyLinks(batchId, npcIds);

        using (var main = new SqliteConnection($"Data Source={_options.MainDbPath}"))
        {
            main.Open();
            using var tx = main.BeginTransaction();

            var joined = string.Join(",", npcIds);

            Exec(main, tx, $"DELETE FROM NpcImageGenerations WHERE NpcId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM NpcPromptGenerations WHERE NpcId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM NpcTraitValues WHERE NpcId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM NpcEducationRecords WHERE NpcId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM NpcProfessionalQualifications WHERE NpcId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM NpcProfessionalProfiles WHERE NpcId IN ({joined});");
            Exec(main, tx, $"DELETE FROM FinancialAccounts WHERE OwnerType='NPC' AND OwnerId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM FinancialObligations WHERE OwnerNpcId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM NpcPhones WHERE NpcId IN ({joined}) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM Vehicles WHERE (RegisteredOwnerNpcId IN ({joined}) OR PrimaryDriverNpcId IN ({joined})) AND Notes LIKE $batchLike;",
                ("$batchLike", "%" + batchId + "%"));
            Exec(main, tx, $"DELETE FROM NpcAppearanceProfiles WHERE NpcId IN ({joined});");
            Exec(main, tx, $"DELETE FROM NpcPhysicalProfiles WHERE NpcId IN ({joined});");
            Exec(main, tx, $"DELETE FROM NpcVoiceProfiles WHERE NpcId IN ({joined});");
            Exec(main, tx, $"DELETE FROM NpcFamilyBuildCompleteness WHERE NpcId IN ({joined});");
            Exec(main, tx, $"DELETE FROM NpcNameProfiles WHERE NpcId IN ({joined});");
            Exec(main, tx, $"DELETE FROM GeneratedHouseholdMembers WHERE GenerationBatchId=$batch;",
                ("$batch", batchId));
            Exec(main, tx, $"DELETE FROM GeneratedHouseholds WHERE GenerationBatchId=$batch;",
                ("$batch", batchId));
            Exec(main, tx, $"DELETE FROM NpcCreationProvenance WHERE CreationBatchId=$batch;",
                ("$batch", batchId));
            Exec(main, tx, $"DELETE FROM Characters WHERE Id IN ({joined});");

            tx.Commit();
        }

        if (householdId > 0)
        {
            var familyFolder = Path.Combine(MediaStorageService.FamiliesRoot, $"Household_{householdId:D6}");
            TryDeleteFolder(familyFolder);
        }

        foreach (var npcId in npcIds)
        {
            TryDeleteFolder(Path.Combine(MediaStorageService.SchoolSystemRoot, "Schools", "BHS", "staff", $"NPC_{npcId:D6}"));
            TryDeleteFolder(Path.Combine(MediaStorageService.SchoolSystemRoot, "Schools", "BHS", "students", $"NPC_{npcId:D6}"));
            TryDeleteNpcCaseFolder(npcId);
        }

        return new BhsStaffDeleteResult(true, $"Deleted generated test batch {batchId} with {npcIds.Count} NPC(s).");
    }

    private void EnsureSaveSchema()
    {
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        EnsureColumn(conn, "Characters", "RaceEthnicity", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Characters", "HouseholdId", "INTEGER NULL");
        EnsureColumn(conn, "NpcImageGenerations", "GenerationBatchId", "TEXT NOT NULL DEFAULT ''");

        Exec(conn, null, """
            CREATE TABLE IF NOT EXISTS GeneratedHouseholds
            (
                HouseholdId INTEGER PRIMARY KEY,
                GenerationBatchId TEXT NOT NULL UNIQUE,
                HouseholdType TEXT NOT NULL DEFAULT 'Family',
                StreetOrArea TEXT NOT NULL DEFAULT '',
                HousingType TEXT NOT NULL DEFAULT '',
                EstimatedMonthlyHousingCost REAL NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Active',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS GeneratedHouseholdMembers
            (
                HouseholdId INTEGER NOT NULL,
                NpcId INTEGER NOT NULL,
                MemberRole TEXT NOT NULL DEFAULT '',
                GenerationBatchId TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Active',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (HouseholdId, NpcId)
            );

            CREATE TABLE IF NOT EXISTS HouseholdFinancialAccounts
            (
                AccountId TEXT PRIMARY KEY,
                HouseholdId INTEGER NOT NULL,
                GenerationBatchId TEXT NOT NULL,
                AccountType TEXT NOT NULL DEFAULT '',
                OwnershipType TEXT NOT NULL DEFAULT 'Joint',
                Balance REAL NOT NULL DEFAULT 0,
                InstitutionName TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'Active',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);
    }

    private int CreateNpc(
        SqliteConnection conn, SqliteTransaction tx, string batchId, long householdId,
        string first, string middle, string last, int age, string gender, string race,
        int tier, string occupation, string employer, string address, string hometown,
        string personality, string goal, string need, string fear, string want,
        int iq, string archetype1, string archetype2, string archetype3,
        string publicPersona, string privatePersona, string hiddenBehavior,
        BhsStaffAppearanceDraft appearance,
        string sourceType, string originalRole)
    {
        first = (first ?? "").Trim();
        middle = (middle ?? "").Trim();
        last = (last ?? "").Trim();

        if (string.IsNullOrWhiteSpace(first))
            first = "Generated";
        if (string.IsNullOrWhiteSpace(last))
            last = "Resident";

        var id = NextCharacterId(conn, tx);
        var name = string.Join(" ", new[] { first, middle, last }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var folderName = $"{id:D4}_{SafeFolder(first)}_{SafeFolder(last)}";
        var folderPath = Path.Combine(_options.NpcRoot, folderName);

        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(Path.Combine(folderPath, "media"));
        Directory.CreateDirectory(Path.Combine(folderPath, "notes"));

        var heightCm = ParseHeightCm(appearance.HeightText);
        var weightKg = (double)appearance.WeightLb * 0.45359237;
        Exec(conn, tx, """
            INSERT INTO Characters
            (
                Id,WorldId,NpcKey,FolderName,FolderPath,Name,DisplayName,FirstName,LastName,
                Age,Gender,RaceEthnicity,Occupation,Employer,Location,Hometown,Address,
                Status,Tier,LifeStage,IsDeceased,PersonalityContext,PersonalitySummary,
                Goal,Need,Fear,Want,IQ,Archetype1,Archetype2,Archetype3,
                PublicPersona,PrivatePersona,HiddenBehavior,HeightCm,WeightKg,HouseholdId,
                CreatedRealAt,UpdatedRealAt
            )
            VALUES
            (
                $id,'smalltown',$key,$folder,$folderPath,$name,$first,$first,$last,
                $age,$gender,$race,$occupation,$employer,'Bellefontaine, Ohio',$hometown,$address,
                'Core',$tier,$lifeStage,0,$personality,$personality,
                $goal,$need,$fear,$want,$iq,$arch1,$arch2,$arch3,
                $public,$private,$hidden,$height,$weight,$household,
                CURRENT_TIMESTAMP,CURRENT_TIMESTAMP
            );
            """,
            ("$id",id),("$key",$"schoolgen-{id:D6}"),("$folder",folderName),("$folderPath",folderPath),
            ("$name",name),("$first",first),("$last",last),("$age",Math.Clamp(age,0,110)),
            ("$gender",gender ?? ""),("$race",race ?? ""),("$occupation",occupation ?? ""),
            ("$employer",employer ?? ""),("$hometown",hometown ?? "Bellefontaine, Ohio"),
            ("$address",address ?? ""),("$tier",Math.Clamp(tier,1,5)),("$lifeStage",LifeStage(age)),
            ("$personality",personality ?? ""),("$goal",goal ?? ""),("$need",need ?? ""),("$fear",fear ?? ""),("$want",want ?? ""),("$iq",Math.Clamp(iq,55,160)),("$arch1",archetype1 ?? ""),("$arch2",archetype2 ?? ""),("$arch3",archetype3 ?? ""),
            ("$public",publicPersona ?? ""),("$private",privatePersona ?? ""),("$hidden",hiddenBehavior ?? ""),("$height",heightCm),("$weight",weightKg),("$household",householdId));

        Exec(conn, tx, """
            INSERT INTO NpcNameProfiles
            (NpcId,FirstName,MiddleName,CurrentLastName,BirthLastName,PreferredName,Suffix,UpdatedRealAt)
            VALUES($id,$first,$middle,$last,$last,$first,'',CURRENT_TIMESTAMP);
            """,
            ("$id",id),("$first",first),("$middle",middle),("$last",last));

        Exec(conn, tx, """
            INSERT INTO NpcCreationProvenance
            (NpcId,CreationSourceType,CreatedFromNpcId,CreatedFromNpcName,OriginalRole,
             CreationBatchId,BuildStatus,CreatedRealAt,UpdatedRealAt)
            VALUES($id,$source,NULL,'',$role,$batch,'CompleteProfileTest',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
            """,
            ("$id",id),("$source",sourceType),("$role",originalRole ?? ""),("$batch",batchId));



        Exec(conn, tx, """
            INSERT INTO NpcPhysicalProfiles
            (NpcId,HeightCm,WeightKg,BodyType,HairColor,HairStyle,EyeColor,SkinTone,
             DefaultClothingStyle,DistinctiveFeatures,DistinguishingFeatures,Notes,UpdatedRealAt)
            VALUES($id,$height,$weight,$body,$hairColor,$hairStyle,$eyes,$skin,$clothing,$features,$features,$notes,CURRENT_TIMESTAMP)
            ON CONFLICT(NpcId) DO UPDATE SET
                HeightCm=excluded.HeightCm,WeightKg=excluded.WeightKg,BodyType=excluded.BodyType,
                HairColor=excluded.HairColor,HairStyle=excluded.HairStyle,EyeColor=excluded.EyeColor,
                SkinTone=excluded.SkinTone,DefaultClothingStyle=excluded.DefaultClothingStyle,
                DistinctiveFeatures=excluded.DistinctiveFeatures,DistinguishingFeatures=excluded.DistinguishingFeatures,
                Notes=excluded.Notes,UpdatedRealAt=CURRENT_TIMESTAMP;
            """,
            ("$id",id),("$height",heightCm),("$weight",weightKg),("$body",appearance.BodyType ?? ""),
            ("$hairColor",appearance.HairColor ?? ""),("$hairStyle",appearance.HairStyle ?? ""),
            ("$eyes",appearance.EyeColor ?? ""),("$skin",appearance.SkinTone ?? ""),
            ("$clothing",appearance.ClothingStyle ?? ""),("$features",appearance.DistinguishingFeatures ?? ""),
            ("$notes",$"Generated batch {batchId}. {appearance.AdultAnatomyNotes}"));

        Exec(conn, tx, """
            INSERT INTO NpcAppearanceProfiles
            (NpcId,AppearanceStatus,BodyType,HeightText,HairColor,HairStyle,EyeColor,SkinTone,
             ClothingStyle,WorkClothes,CasualClothes,DistinguishingFeatures,
             ImagePrompt,NegativePrompt,Approved,Notes,UpdatedRealAt)
            VALUES($id,'Profile Ready',$body,$height,$hairColor,$hairStyle,$eyes,$skin,
                   $clothing,$clothing,$clothing,$features,'','',0,$notes,CURRENT_TIMESTAMP)
            ON CONFLICT(NpcId) DO UPDATE SET
                AppearanceStatus='Profile Ready',BodyType=excluded.BodyType,HeightText=excluded.HeightText,
                HairColor=excluded.HairColor,HairStyle=excluded.HairStyle,EyeColor=excluded.EyeColor,
                SkinTone=excluded.SkinTone,ClothingStyle=excluded.ClothingStyle,
                WorkClothes=excluded.WorkClothes,CasualClothes=excluded.CasualClothes,
                DistinguishingFeatures=excluded.DistinguishingFeatures,Notes=excluded.Notes,
                UpdatedRealAt=CURRENT_TIMESTAMP;
            """,
            ("$id",id),("$body",appearance.BodyType ?? ""),("$height",appearance.HeightText ?? ""),
            ("$hairColor",appearance.HairColor ?? ""),("$hairStyle",appearance.HairStyle ?? ""),
            ("$eyes",appearance.EyeColor ?? ""),("$skin",appearance.SkinTone ?? ""),
            ("$clothing",appearance.ClothingStyle ?? ""),("$features",appearance.DistinguishingFeatures ?? ""),
            ("$notes",$"Generated batch {batchId}"));

        Exec(conn, tx, """
            INSERT OR IGNORE INTO NpcVoiceProfiles
            (NpcId,VoiceStatus,Notes,UpdatedRealAt)
            VALUES($id,'NotStarted',$notes,CURRENT_TIMESTAMP);
            """, ("$id",id),("$notes",$"Generated batch {batchId}"));

        return id;
    }

    private void SaveAdultProfile(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, BhsStaffDraft staff)
    {
        Exec(conn, tx, """
            INSERT INTO NpcProfessionalProfiles
            (NpcId,PrimaryRoleId,CareerField,YearsExperience,TrainingLevel,LicenseStanding,
             Burnout,Motivation,CurrentPerformance,ProfessionalReputation,IsActive,Notes,UpdatedRealAt)
            VALUES($id,$role,'Education',0,'Professional','Active',0,75,75,75,1,$notes,CURRENT_TIMESTAMP)
            ON CONFLICT(NpcId) DO UPDATE SET
                PrimaryRoleId=excluded.PrimaryRoleId,CareerField=excluded.CareerField,
                ProfessionalReputation=excluded.ProfessionalReputation,Notes=excluded.Notes,
                UpdatedRealAt=CURRENT_TIMESTAMP;
            """,
            ("$id",npcId),("$role",staff.RoleTitle),("$notes",
            $"Generated batch {batchId}. {staff.Professional.Reputation} Strengths: {string.Join(", ",staff.Professional.Strengths)}"));
    }

    private static void SaveEducation(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, IEnumerable<BhsStaffEducationDraft> items)
    {
        foreach (var item in items ?? Enumerable.Empty<BhsStaffEducationDraft>())
        {
            Exec(conn, tx, """
                INSERT INTO NpcEducationRecords
                (EducationRecordId,NpcId,EducationType,InstitutionId,InstitutionName,ProgramName,
                 DegreeOrCredential,FieldOfStudy,Status,Notes,CreatedRealAt,UpdatedRealAt)
                VALUES($id,$npc,$type,'',$institution,'',$credential,$field,$status,$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
                """,
                ("$id",$"bhs-edu-{Guid.NewGuid():N}"),("$npc",npcId),("$type",item.Level ?? ""),
                ("$institution",item.Institution ?? ""),("$credential",item.Credential ?? ""),
                ("$field",item.Field ?? ""),("$status",item.Status ?? "Completed"),
                ("$notes",$"Generated batch {batchId}"));
        }
    }

    private static void SaveQualifications(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, IEnumerable<string> certs, string role)
    {
        foreach (var cert in certs ?? Enumerable.Empty<string>())
        {
            Exec(conn, tx, """
                INSERT INTO NpcProfessionalQualifications
                (QualificationId,NpcId,RoleId,QualificationType,Name,Status,Notes,CreatedRealAt,UpdatedRealAt)
                VALUES($id,$npc,$role,'Certification',$name,'Active',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
                """,
                ("$id",$"bhs-qual-{Guid.NewGuid():N}"),("$npc",npcId),("$role",role ?? ""),
                ("$name",cert ?? ""),("$notes",$"Generated batch {batchId}"));
        }
    }

    private static void SaveFinance(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, BhsStaffBankingDraft banking, string ownership)
    {
        Exec(conn, tx, """
            INSERT INTO FinancialAccounts
            (AccountId,OwnerType,OwnerId,AccountType,InstitutionName,AccountName,Balance,CurrencyCode,IsPrimary,Status,Notes,CreatedRealAt,UpdatedRealAt)
            VALUES($id,'NPC',$npc,'Checking','Local Bank',$name,$balance,'USD',1,'Active',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
            """,
            ("$id",$"bhs-acct-{Guid.NewGuid():N}"),("$npc",npcId),("$name",$"{ownership} Checking"),
            ("$balance",banking.CheckingBalance),("$notes",$"Generated batch {batchId}; ownership={ownership}"));

        Exec(conn, tx, """
            INSERT INTO FinancialAccounts
            (AccountId,OwnerType,OwnerId,AccountType,InstitutionName,AccountName,Balance,CurrencyCode,IsPrimary,Status,Notes,CreatedRealAt,UpdatedRealAt)
            VALUES($id,'NPC',$npc,'Savings','Local Bank',$name,$balance,'USD',0,'Active',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
            """,
            ("$id",$"bhs-acct-{Guid.NewGuid():N}"),("$npc",npcId),("$name",$"{ownership} Savings"),
            ("$balance",banking.SavingsBalance),("$notes",$"Generated batch {batchId}; ownership={ownership}"));

        if (banking.MonthlyDebtPayments > 0 || !string.IsNullOrWhiteSpace(banking.DebtSummary))
        {
            Exec(conn, tx, """
                INSERT INTO FinancialObligations
                (ObligationId,OwnerNpcId,ObligationType,LenderName,Description,OriginalAmount,CurrentBalance,
                 MonthlyPayment,InterestRate,Status,Notes,CreatedRealAt,UpdatedRealAt)
                VALUES($id,$npc,'General Debt','',$desc,0,0,$monthly,0,'Active',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
                """,
                ("$id",$"bhs-debt-{Guid.NewGuid():N}"),("$npc",npcId),("$desc",banking.DebtSummary ?? ""),
                ("$monthly",banking.MonthlyDebtPayments),("$notes",$"Generated batch {batchId}"));
        }
    }

    private static void SaveJointFinance(SqliteConnection conn, SqliteTransaction tx, long householdId, string batchId, BhsStaffDraft staff)
    {
        if (!staff.Spouse.Include) return;

        var jointChecking = Math.Round(staff.Banking.CheckingBalance * 0.25m, 2);
        var jointSavings = Math.Round(staff.Banking.SavingsBalance * 0.25m, 2);

        Exec(conn, tx, """
            INSERT INTO HouseholdFinancialAccounts
            (AccountId,HouseholdId,GenerationBatchId,AccountType,OwnershipType,Balance,InstitutionName,Status,Notes)
            VALUES($id,$household,$batch,'Checking','Joint',$balance,'Local Bank','Active','Joint household checking; default split 50/50 if union is replaced.');
            """,
            ("$id",$"hh-acct-{Guid.NewGuid():N}"),("$household",householdId),("$batch",batchId),("$balance",jointChecking));

        Exec(conn, tx, """
            INSERT INTO HouseholdFinancialAccounts
            (AccountId,HouseholdId,GenerationBatchId,AccountType,OwnershipType,Balance,InstitutionName,Status,Notes)
            VALUES($id,$household,$batch,'Savings','Joint',$balance,'Local Bank','Active','Joint household savings; default split 50/50 if union is replaced.');
            """,
            ("$id",$"hh-acct-{Guid.NewGuid():N}"),("$household",householdId),("$batch",batchId),("$balance",jointSavings));
    }

    private static void SavePhone(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, BhsStaffPhoneDraft phone)
    {
        var number = GeneratePhoneNumber(conn);
        var parts = (phone.Device ?? "").Split(' ',2,StringSplitOptions.RemoveEmptyEntries);
        var make = parts.Length > 0 ? parts[0] : "";
        var model = parts.Length > 1 ? parts[1] : phone.Device ?? "";

        Exec(conn, tx, """
            INSERT INTO NpcPhones
            (PhoneId,WorldId,NpcId,PhoneNumber,PhoneType,CarrierName,DeviceMake,DeviceModel,DeviceLabel,
             IsPrimary,IsActive,Notes,CreatedRealAt,UpdatedRealAt)
            VALUES($id,'smalltown',$npc,$number,'Mobile',$carrier,$make,$model,'Primary',1,1,$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
            """,
            ("$id",$"bhs-phone-{Guid.NewGuid():N}"),("$npc",npcId),("$number",number),
            ("$carrier",phone.Carrier ?? ""),("$make",make),("$model",model),
            ("$notes",$"Generated batch {batchId}"));
    }

    private static void SaveVehicle(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, BhsStaffVehicleDraft vehicle)
    {
        if (vehicle.Year <= 0 && string.IsNullOrWhiteSpace(vehicle.Make) && string.IsNullOrWhiteSpace(vehicle.Model))
            return;

        Exec(conn, tx, """
            INSERT INTO Vehicles
            (VehicleId,WorldId,RegisteredOwnerNpcId,PrimaryDriverNpcId,VehicleType,Make,Model,ModelYear,
             Status,Notes,CreatedRealAt,UpdatedRealAt)
            VALUES($id,'smalltown',$npc,$npc,'Car',$make,$model,$year,'Active',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
            """,
            ("$id",$"bhs-vehicle-{Guid.NewGuid():N}"),("$npc",npcId),("$make",vehicle.Make ?? ""),
            ("$model",vehicle.Model ?? ""),("$year",vehicle.Year),
            ("$notes",$"Generated batch {batchId}. Financing: {vehicle.FinancingStatus}; payment {vehicle.MonthlyPayment:C}."));
    }

    private static void SaveTraits(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, IEnumerable<string> traits)
    {
        var i = 0;
        foreach (var trait in traits ?? Enumerable.Empty<string>())
        {
            var clean = (trait ?? "").Trim();
            if (clean.Length == 0) continue;
            i++;

            Exec(conn, tx, """
                INSERT INTO NpcTraitValues
                (Id,NpcId,MainGroup,SubGroup,SubSubGroup,TraitId,TraitName,IsEnabled,
                 StartingValue,CurrentValue,SetPointValue,ExpressionStyle,Notes,UpdatedRealAt)
                VALUES($id,$npc,'Personality','Generated','',$traitId,$name,1,65,65,65,'Natural',$notes,CURRENT_TIMESTAMP);
                """,
                ("$id",$"bhs-trait-{Guid.NewGuid():N}"),("$npc",npcId),("$traitId",$"bhs-{Slug(clean)}-{i}"),
                ("$name",clean),("$notes",$"Generated batch {batchId}"));
        }
    }

    private static void SaveCompleteness(SqliteConnection conn, SqliteTransaction tx, int npcId, string batchId, int percent)
    {
        Exec(conn, tx, """
            INSERT OR REPLACE INTO NpcFamilyBuildCompleteness
            (NpcId,IdentityStatus,AppearanceStatus,TraitsStatus,CurrentLifeStatus,EducationCareerStatus,
             JobStatus,FinanceStatus,PhoneStatus,VehicleStatus,HomeStatus,FamilyStructureStatus,
             NonHistoryPercent,HistoryStatus,Notes,UpdatedRealAt)
            VALUES($npc,'Complete','Complete','Complete','Complete','Complete','Complete',
                   'Complete','Complete','Complete','Complete','Complete',$percent,'NOT_INCLUDED',$notes,CURRENT_TIMESTAMP);
            """,
            ("$npc",npcId),("$percent",percent),("$notes",$"Generated batch {batchId}; TRUE HISTORY intentionally not generated."));
    }

    private static void WriteHousehold(SqliteConnection conn, SqliteTransaction tx, long householdId, string batchId, BhsStaffHomeDraft home)
    {
        Exec(conn, tx, """
            INSERT INTO GeneratedHouseholds
            (HouseholdId,GenerationBatchId,HouseholdType,StreetOrArea,HousingType,EstimatedMonthlyHousingCost,Status,Notes)
            VALUES($id,$batch,'Family',$street,$type,$cost,'Active',$notes);
            """,
            ("$id",householdId),("$batch",batchId),("$street",home.StreetOrArea ?? ""),
            ("$type",home.HousingType ?? ""),("$cost",home.EstimatedMonthlyHousingCost),
            ("$notes",home.Notes ?? ""));
    }

    private static void WriteHouseholdMember(SqliteConnection conn, SqliteTransaction tx, long householdId, int npcId, string role, string batchId)
    {
        Exec(conn, tx, """
            INSERT INTO GeneratedHouseholdMembers
            (HouseholdId,NpcId,MemberRole,GenerationBatchId,Status)
            VALUES($household,$npc,$role,$batch,'Active');
            """,
            ("$household",householdId),("$npc",npcId),("$role",role),("$batch",batchId));
    }

    private void SaveFamilyLinks(int staffId, int? spouseId, IReadOnlyList<int> childIds, string batchId)
    {
        var relPath = _options.RelationshipsDbPath;
        using var rel = new SqliteConnection($"Data Source={relPath}");
        rel.Open();
        using var tx = rel.BeginTransaction();

        if (spouseId.HasValue)
        {
            var p1 = Math.Min(staffId, spouseId.Value);
            var p2 = Math.Max(staffId, spouseId.Value);
            Exec(rel, tx, """
                INSERT INTO FamilyUnionLinks
                (Person1NpcId,Person2NpcId,UnionType,Status,StartGameDate,EndGameDate,Source,Notes,CreatedRealAt,UpdatedRealAt)
                VALUES($p1,$p2,'Marriage','Active','','','SchoolStaffGenerator',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
                """,
                ("$p1",p1),("$p2",p2),("$notes",$"Generated batch {batchId}"));
        }

        foreach (var child in childIds)
        {
            Exec(rel, tx, """
                INSERT INTO FamilyParentChildLinks
                (ParentNpcId,ChildNpcId,ParentKind,ParentSlot,FamilyLine,IsCurrent,StartGameDate,EndGameDate,Source,Notes,CreatedRealAt,UpdatedRealAt)
                VALUES($parent,$child,'Biological',$slot,'GeneratedHousehold',1,'','','SchoolStaffGenerator',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
                """,
                ("$parent",staffId),("$child",child),("$slot","Parent1"),("$notes",$"Generated batch {batchId}"));

            if (spouseId.HasValue)
            {
                Exec(rel, tx, """
                    INSERT INTO FamilyParentChildLinks
                    (ParentNpcId,ChildNpcId,ParentKind,ParentSlot,FamilyLine,IsCurrent,StartGameDate,EndGameDate,Source,Notes,CreatedRealAt,UpdatedRealAt)
                    VALUES($parent,$child,'Biological',$slot,'GeneratedHousehold',1,'','','SchoolStaffGenerator',$notes,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP);
                    """,
                    ("$parent",spouseId.Value),("$child",child),("$slot","Parent2"),("$notes",$"Generated batch {batchId}"));
            }
        }

        tx.Commit();
    }

    private void RemoveFamilyLinks(string batchId, IReadOnlyList<int> npcIds)
    {
        using var rel = new SqliteConnection($"Data Source={_options.RelationshipsDbPath}");
        rel.Open();
        using var tx = rel.BeginTransaction();
        var joined = string.Join(",", npcIds);

        Exec(rel, tx, $"DELETE FROM FamilyUnionLinks WHERE (Person1NpcId IN ({joined}) OR Person2NpcId IN ({joined})) AND Notes LIKE $batchLike;",
            ("$batchLike","%" + batchId + "%"));
        Exec(rel, tx, $"DELETE FROM FamilyParentChildLinks WHERE (ParentNpcId IN ({joined}) OR ChildNpcId IN ({joined})) AND Notes LIKE $batchLike;",
            ("$batchLike","%" + batchId + "%"));
        Exec(rel, tx, $"DELETE FROM RelationshipStates WHERE SourceCharacterId IN ({joined}) OR TargetCharacterId IN ({joined});");

        tx.Commit();
    }

    private void AssignSchoolSlot(int npcId, string roleTitle, string batchId)
    {
        using var conn = new SqliteConnection(@"Data Source=D:\ProjectEveData\Database\project_eve_locations.db");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT StaffSlotId
            FROM SchoolStaffSlots
            WHERE SchoolId='BHS'
              AND AssignedNpcId IS NULL
              AND lower(trim(RoleTitle)) = lower(trim($role))
            ORDER BY SlotNumber
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$role", roleTitle ?? "");
        var slot = Convert.ToString(cmd.ExecuteScalar()) ?? "";

        if (string.IsNullOrWhiteSpace(slot))
            return;

        using var update = conn.CreateCommand();
        update.CommandText = """
            UPDATE SchoolStaffSlots
            SET AssignedNpcId=$npc,Status='Filled',
                Notes=CASE WHEN trim(Notes)='' THEN $notes ELSE Notes || char(10) || $notes END,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE StaffSlotId=$slot;
            """;
        update.Parameters.AddWithValue("$npc",npcId);
        update.Parameters.AddWithValue("$notes",$"Generated batch {batchId}");
        update.Parameters.AddWithValue("$slot",slot);
        update.ExecuteNonQuery();
    }

    private void AssignBestSpouseSchoolSlot(int npcId, string occupation, string batchId)
    {
        using var conn = new SqliteConnection(@"Data Source=D:\ProjectEveData\Database\project_eve_locations.db");
        conn.Open();

        string rolePattern = occupation.Contains("teacher", StringComparison.OrdinalIgnoreCase) ? "%Teacher%" :
                             occupation.Contains("counsel", StringComparison.OrdinalIgnoreCase) ? "%Counsel%" :
                             occupation.Contains("nurse", StringComparison.OrdinalIgnoreCase) ? "%Nurse%" :
                             occupation.Contains("secret", StringComparison.OrdinalIgnoreCase) ? "%Secret%" :
                             "%";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT StaffSlotId
            FROM SchoolStaffSlots
            WHERE SchoolId='BHS'
              AND AssignedNpcId IS NULL
              AND RoleTitle LIKE $pattern
            ORDER BY SlotNumber
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$pattern",rolePattern);
        var slot = Convert.ToString(cmd.ExecuteScalar()) ?? "";
        if (slot.Length == 0) return;

        using var update = conn.CreateCommand();
        update.CommandText = """
            UPDATE SchoolStaffSlots
            SET AssignedNpcId=$npc,Status='Filled',
                Notes=CASE WHEN trim(Notes)='' THEN $notes ELSE Notes || char(10) || $notes END,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE StaffSlotId=$slot;
            """;
        update.Parameters.AddWithValue("$npc",npcId);
        update.Parameters.AddWithValue("$notes",$"Generated spouse 2-for-1 school assignment; batch {batchId}. Occupation: {occupation}");
        update.Parameters.AddWithValue("$slot",slot);
        update.ExecuteNonQuery();
    }

    private void RemoveSchoolAssignments(string batchId, IReadOnlyList<int> npcIds)
    {
        using var conn = new SqliteConnection(@"Data Source=D:\ProjectEveData\Database\project_eve_locations.db");
        conn.Open();
        var joined = string.Join(",", npcIds);

        Exec(conn, null, $"""
            UPDATE SchoolStaffSlots
            SET AssignedNpcId=NULL,Status='Open',
                Notes=replace(Notes,$batchText,''),
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE AssignedNpcId IN ({joined});
            """, ("$batchText",$"Generated batch {batchId}"));

        Exec(conn, null, $"DELETE FROM StudentSchoolYears WHERE NpcId IN ({joined}) AND HistoryHooks LIKE $batchLike;",
            ("$batchLike","%" + batchId + "%"));
        Exec(conn, null, $"DELETE FROM StudentCourseEnrollments WHERE NpcId IN ({joined}) AND HistoryHooks LIKE $batchLike;",
            ("$batchLike","%" + batchId + "%"));
    }

    private void SaveChildSchoolRows(IReadOnlyList<int> ids, IReadOnlyList<BhsStaffChildDraft> children, string batchId)
    {
        using var conn = new SqliteConnection(@"Data Source=D:\ProjectEveData\Database\project_eve_locations.db");
        conn.Open();

        for (var i=0;i<ids.Count && i<children.Count;i++)
        {
            var c = children[i];
            if (c.Age < 5 || c.Age > 18) continue;

            var school = c.SchoolId;
            if (string.IsNullOrWhiteSpace(school))
                school = c.Age >= 14 ? "BHS" : c.Age >= 11 ? "BMS" : c.Age >= 8 ? "BIS" : "BES";

            var grade = string.IsNullOrWhiteSpace(c.GradeLevel) ? GradeFromAge(c.Age) : c.GradeLevel;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO StudentSchoolYears
                (SchoolYearRecordId,NpcId,SchoolId,AcademicYear,GradeLevel,AgeStart,AgeEnd,
                 OverallGpa,AcademicPerformance,AttendanceSummary,Activities,Sports,SocialNotes,
                 TeacherMentorNotes,HistoryHooks,CanonicalEventIds,Status,UpdatedRealAt)
                VALUES($id,$npc,$school,$year,$grade,$age,$age,$gpa,$performance,$attendance,
                       $activities,$sports,$social,$teacher,$hooks,'','Current',CURRENT_TIMESTAMP);
                """;
            cmd.Parameters.AddWithValue("$id",$"bhs-schoolyear-{Guid.NewGuid():N}");
            cmd.Parameters.AddWithValue("$npc",ids[i]);
            cmd.Parameters.AddWithValue("$school",school);
            cmd.Parameters.AddWithValue("$year",c.AcademicYear ?? "");
            cmd.Parameters.AddWithValue("$grade",grade);
            cmd.Parameters.AddWithValue("$age",c.Age);
            cmd.Parameters.AddWithValue("$gpa",c.Gpa > 0 ? c.Gpa : DBNull.Value);
            cmd.Parameters.AddWithValue("$performance",c.AcademicPerformance ?? "");
            cmd.Parameters.AddWithValue("$attendance",c.AttendanceSummary ?? "");
            cmd.Parameters.AddWithValue("$activities",string.Join(", ",c.Activities ?? new()));
            cmd.Parameters.AddWithValue("$sports",string.Join(", ",c.Sports ?? new()));
            cmd.Parameters.AddWithValue("$social",c.SocialNotes ?? "");
            cmd.Parameters.AddWithValue("$teacher",c.TeacherMentorNotes ?? "");
            cmd.Parameters.AddWithValue("$hooks",$"Generated batch {batchId}. {c.HistoryHooks}");
            cmd.ExecuteNonQuery();
        }
    }

    private async Task<string> QueuePortraitAsync(
        int npcId, long householdId, string batchId, string name, int age, string gender,
        string race, BhsStaffAppearanceDraft appearance, string role, bool schoolStaff,
        CancellationToken cancellationToken)
    {
        var positive =
            $"single-person professional profile portrait of {name}, age {age}, {gender}, {race}; " +
            $"{appearance.BodyType} build, {appearance.HairColor} {appearance.HairStyle} hair, " +
            $"{appearance.EyeColor} eyes, {appearance.SkinTone} skin tone; " +
            $"clothing: {appearance.ClothingStyle}; distinguishing features: {appearance.DistinguishingFeatures}; " +
            $"{role}; realistic contemporary Bellefontaine Ohio resident, comfortable natural expression, " +
            $"clean neutral background, head and shoulders, one person only, photorealistic";

        var request = new NpcComfyGenerationRequest
        {
            NpcId = npcId,
            ImageType = schoolStaff ? "ProfessionalProfile" : "FamilyProfile",
            PositivePrompt = positive,
            NegativePrompt = "multiple people, group portrait, text, watermark, cartoon, anime, blurry, distorted face, bad eyes, extra fingers, nudity, sexualized pose",
            SavePrefix = schoolStaff
                ? $"ProjectEve/SchoolSystem/BHS/NPC_{npcId:D6}/profile"
                : $"ProjectEve/Families/Household_{householdId:D6}/NPC_{npcId:D6}/profile"
        };

        var result = await _comfy.QueueReferencePortraitAsync(request);

        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        var expectedPath = schoolStaff
            ? _media.GetProfessionalProfilePath("BHS", npcId)
            : _media.GetPrimaryProfilePath(_media.EnsureFamilyNpcFolder(householdId, npcId, age < 18), npcId);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcImageGenerations
            (Id,NpcId,ImageType,PositivePrompt,NegativePrompt,Seed,WorkflowName,Checkpoint,
             Width,Height,Steps,Cfg,Sampler,ImagePath,IsCurrent,Approved,Notes,CreatedRealAt,GenerationBatchId)
            VALUES($id,$npc,$type,$positive,$negative,'-1',$workflow,$checkpoint,768,1152,32,5.0,
                   'dpm_adaptive',$path,1,0,$notes,CURRENT_TIMESTAMP,$batch);
            """;
        cmd.Parameters.AddWithValue("$id",$"bhs-image-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("$npc",npcId);
        cmd.Parameters.AddWithValue("$type",request.ImageType);
        cmd.Parameters.AddWithValue("$positive",positive);
        cmd.Parameters.AddWithValue("$negative",request.NegativePrompt);
        cmd.Parameters.AddWithValue("$workflow",result.WorkflowUsed ?? request.WorkflowName);
        cmd.Parameters.AddWithValue("$checkpoint",request.Checkpoint);
        cmd.Parameters.AddWithValue("$path",expectedPath);
        cmd.Parameters.AddWithValue("$notes",$"Generated batch {batchId}. ComfyPromptId={result.PromptId}. Status={result.Message}");
        cmd.Parameters.AddWithValue("$batch",batchId);
        cmd.ExecuteNonQuery();

        using var app = conn.CreateCommand();
        app.CommandText = """
            UPDATE NpcAppearanceProfiles
            SET ProfileImagePath=$path,
                AppearanceStatus=$status,
                ImagePrompt=$prompt,
                NegativePrompt=$negative,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE NpcId=$npc;
            """;
        app.Parameters.AddWithValue("$path",expectedPath);
        app.Parameters.AddWithValue("$status",result.Success ? "Comfy Queued" : "Comfy Queue Failed");
        app.Parameters.AddWithValue("$prompt",positive);
        app.Parameters.AddWithValue("$negative",request.NegativePrompt);
        app.Parameters.AddWithValue("$npc",npcId);
        app.ExecuteNonQuery();

        return result.Message;
    }

    private static bool LooksLikeSchoolJob(string value)
    {
        var s = value ?? "";
        return s.Contains("teacher",StringComparison.OrdinalIgnoreCase)
            || s.Contains("school",StringComparison.OrdinalIgnoreCase)
            || s.Contains("counsel",StringComparison.OrdinalIgnoreCase)
            || s.Contains("principal",StringComparison.OrdinalIgnoreCase)
            || s.Contains("nurse",StringComparison.OrdinalIgnoreCase)
            || s.Contains("coach",StringComparison.OrdinalIgnoreCase)
            || s.Contains("secret",StringComparison.OrdinalIgnoreCase);
    }

    private static string GeneratePhoneNumber(SqliteConnection conn)
    {
        for (var i=0;i<100;i++)
        {
            var last4 = Random.Shared.Next(1000,9999);
            var number = $"937-555-{last4:D4}";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM NpcPhones WHERE PhoneNumber=$n AND IsActive=1;";
            cmd.Parameters.AddWithValue("$n",number);
            if (Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 0) return number;
        }
        return $"937-555-{Random.Shared.Next(1000,9999):D4}";
    }

    private static int NextCharacterId(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd=conn.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT COALESCE(MAX(Id),0)+1 FROM Characters;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
    }

    private static long NextHouseholdId(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd=conn.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText="SELECT COALESCE(MAX(HouseholdId),0)+1 FROM GeneratedHouseholds;";
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 1L);
    }

    private static (string First,string Middle,string Last) SplitName(string value)
    {
        var parts=(value ?? "").Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==0) return ("Generated","","Resident");
        if(parts.Length==1) return (parts[0],"","Resident");
        return (parts[0],parts.Length>2?string.Join(" ",parts.Skip(1).Take(parts.Length-2)):"",parts[^1]);
    }

    private static double ParseHeightCm(string text)
    {
        if(string.IsNullOrWhiteSpace(text)) return 0;
        var m=Regex.Match(text,@"(?<f>\d+)\s*['′]\s*(?<i>\d+)");
        if(m.Success)
        {
            var feet=int.Parse(m.Groups["f"].Value,CultureInfo.InvariantCulture);
            var inches=int.Parse(m.Groups["i"].Value,CultureInfo.InvariantCulture);
            return (feet*12+inches)*2.54;
        }
        var cm=Regex.Match(text,@"(?<cm>\d+(?:\.\d+)?)\s*cm",RegexOptions.IgnoreCase);
        return cm.Success ? double.Parse(cm.Groups["cm"].Value,CultureInfo.InvariantCulture) : 0;
    }

    private static string GradeFromAge(int age)
        => Math.Clamp(age - 5,0,12).ToString(CultureInfo.InvariantCulture);

    private static string LifeStage(int age)
        => age<=12?"Child":age<=17?"Teenager":age<=29?"Young Adult":age<=49?"Adult":age<=64?"Older Adult":"Elder";

    private static string Slug(string value)
        => new string((value ?? "").ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)?c:'-').ToArray()).Trim('-');

    private static string SafeFolder(string value)
    {
        var invalid=Path.GetInvalidFileNameChars().ToHashSet();
        return new string((value ?? "").Where(c=>!invalid.Contains(c)).ToArray()).Trim();
    }

    private void TryDeleteNpcCaseFolder(int npcId)
    {
        try
        {
            if(!Directory.Exists(_options.NpcRoot)) return;
            foreach(var dir in Directory.EnumerateDirectories(_options.NpcRoot,$"{npcId:D4}_*"))
                Directory.Delete(dir,true);
        }
        catch { }
    }

    private static void TryDeleteFolder(string path)
    {
        try { if(Directory.Exists(path)) Directory.Delete(path,true); } catch { }
    }

    private static void EnsureColumn(SqliteConnection conn,string table,string column,string definition)
    {
        using var check=conn.CreateCommand();
        check.CommandText=$"PRAGMA table_info([{table}]);";
        using var r=check.ExecuteReader();
        while(r.Read())
            if(string.Equals(Convert.ToString(r["name"]),column,StringComparison.OrdinalIgnoreCase))
                return;
        r.Close();
        using var alter=conn.CreateCommand();
        alter.CommandText=$"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};";
        alter.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name,object? Value)[] args)
    {
        using var cmd=conn.CreateCommand();
        cmd.Transaction=tx;
        cmd.CommandText=sql;
        foreach(var (name,value) in args) cmd.Parameters.AddWithValue(name,value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}

public sealed class BhsStaffSaveResult
{
    public bool Success { get; set; }
    public string BatchId { get; set; } = "";
    public long HouseholdId { get; set; }
    public int StaffNpcId { get; set; }
    public List<int> CreatedNpcIds { get; set; } = new();
    public string Message { get; set; } = "";
}

public sealed record BhsStaffDeleteResult(bool Success,string Message);

