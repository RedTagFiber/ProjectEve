using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

using ProjectEve.NpcStudio.Data;
namespace ProjectEve.NpcStudio.Services;

public sealed class NpcFoundationBuildService
{
    private readonly AiNpcProfileBuilderService _profileBuilder;
    private readonly NpcStudioOptions _options;
    private readonly BellefontaineHousingInventoryService _housing;
    private readonly NpcStudioRepository _repo;
    private readonly AiAppearanceProfileAssistService _appearanceAi;
    private readonly NpcAppearanceDetailService _appearanceDetails;

    public NpcFoundationBuildService(
        AiNpcProfileBuilderService profileBuilder,
        NpcStudioOptions options,
        BellefontaineHousingInventoryService housing,
        NpcStudioRepository repo,
        AiAppearanceProfileAssistService appearanceAi,
        NpcAppearanceDetailService appearanceDetails)
    {
        _profileBuilder = profileBuilder;
        _options = options;
        _housing = housing;
        _repo = repo;
        _appearanceAi = appearanceAi;
        _appearanceDetails = appearanceDetails;
    }

    public async Task<NpcFoundationPreview> BuildPreviewAsync(
        int npcId,
        CancellationToken cancellationToken = default)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        var current = LoadCurrent(npcId);

        var profile = LoadCachedFoundationProfile(npcId);

        if (profile is null)
        {
            profile = await _profileBuilder.BuildPreviewAsync(
                new AiNpcProfileBuildRequest
                {
                    NpcId = npcId,
                    BuildTier = Math.Clamp(current.Tier, 1, 5),
                    FillIdentity = true,
                    FillAppearance = true,
                    FillTraits = false,
                    FillCurrentLife = true,
                    FillEducationCareer = false,
                    FillHabitsInterests = false,
                    FillRelationshipContext = false
                },
                cancellationToken);

            if (profile.Proposal is not null && profile.IsValid)
                SaveCachedFoundationProfile(profile);
        }

        var result = new NpcFoundationPreview
        {
            NpcId = npcId,
            ExistingName = current.Name,
            Tier = current.Tier,
            ExistingCurrentLastName = current.CurrentLastName,
            ExistingBirthLastName = current.BirthLastName,
            ExistingAddress = current.Address,
            ExistingHomeLocationId = current.HomeLocationId,
            ExistingPhoneCount = current.PhoneCount,
            ExistingVehicleCount = current.VehicleCount,
            ExistingFinanceAccountCount = current.FinanceAccountCount,
            Profile = profile
        };

        foreach (var warning in profile.Warnings)
            result.Warnings.Add(warning);

        var householdSize = _housing.ResolveHouseholdSize(npcId);

        var effectiveAge = current.Age > 0
            ? current.Age
            : profile.Proposal?.Age ?? 0;

        result.Housing = _housing.PreviewForNpc(
            npcId,
            householdSize,
            effectiveAge);

        result.HouseholdSize = householdSize;

        if (profile.Proposal is null)
            return result;

        var age = current.Age > 0
            ? current.Age
            : profile.Proposal.Age;

        NormalizeAdultCurrentLife(
            npcId,
            age,
            profile.Proposal,
            result.Warnings);

        var detailedAppearance =
            LoadCachedDetailedAppearance(npcId);

        if (detailedAppearance is null)
        {
            detailedAppearance =
                await _appearanceAi.BuildDraftAsync(
                    npcId,
                    cancellationToken);

            if (!string.IsNullOrWhiteSpace(profile.Proposal.BodyType))
                detailedAppearance.BodyBuild = profile.Proposal.BodyType.Trim();

            if (!string.IsNullOrWhiteSpace(profile.Proposal.EyeColor))
                detailedAppearance.EyeBaseColor = profile.Proposal.EyeColor.Trim();

            if (!string.IsNullOrWhiteSpace(profile.Proposal.HairColor))
                detailedAppearance.HairColor = profile.Proposal.HairColor.Trim();

            if (!string.IsNullOrWhiteSpace(profile.Proposal.HairStyle))
                detailedAppearance.HairStyle = profile.Proposal.HairStyle.Trim();

            if (!string.IsNullOrWhiteSpace(profile.Proposal.SkinTone))
                detailedAppearance.SkinTone = profile.Proposal.SkinTone.Trim();

            if (!string.IsNullOrWhiteSpace(profile.Proposal.ClothingStyle))
                detailedAppearance.DefaultClothingStyle =
                    profile.Proposal.ClothingStyle.Trim();

            if (!string.IsNullOrWhiteSpace(profile.Proposal.DistinguishingFeatures))
                detailedAppearance.DistinguishingFeatures =
                    profile.Proposal.DistinguishingFeatures.Trim();

            NpcAppearanceDetailService.NormalizeCompleteness(
                detailedAppearance);

            SaveCachedDetailedAppearance(
                npcId,
                detailedAppearance);
        }

        result.DetailedAppearance = detailedAppearance;

        if (current.PhoneCount == 0)
            result.Phone = CreateUniquePhone(npcId);

        if (current.VehicleCount == 0 && age >= 18)
            result.Vehicle = CreateUniqueVehicle(npcId, age);

        if (current.FinanceAccountCount == 0 && age >= 18)
            result.Finance = CreateFinancePreview(npcId);

        var proposedAddress = profile.Proposal.Address?.Trim() ?? "";

        var hasHousingCandidate =
            result.Housing is not null &&
            !string.IsNullOrWhiteSpace(result.Housing.UnitId) &&
            !string.IsNullOrWhiteSpace(result.Housing.Address) &&
            !string.IsNullOrWhiteSpace(result.Housing.HomeLocationId);

        if (!hasHousingCandidate &&
            string.IsNullOrWhiteSpace(current.HomeLocationId) &&
            string.IsNullOrWhiteSpace(current.Address) &&
            string.IsNullOrWhiteSpace(proposedAddress))
        {
            result.Warnings.Add(
                "REVIEW: Home is unresolved. No canonical housing unit could be selected. " +
                "Mark Needs Repair instead of inventing or duplicating an address.");
        }

        return result;
    }

    private CurrentFoundationState LoadCurrent(int npcId)
    {
        using var conn = Open();

        var state = new CurrentFoundationState { NpcId = npcId };

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    COALESCE(Name,''),
                    COALESCE(Age,0),
                    COALESCE(Tier,5),
                    COALESCE(Address,''),
                    COALESCE(HomeLocationId,'')
                FROM Characters
                WHERE Id=$id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);

            using var r = cmd.ExecuteReader();

            if (!r.Read())
                throw new InvalidOperationException($"NPC {npcId} was not found.");

            state.Name = r.GetString(0);
            state.Age = r.GetInt32(1);
            state.Tier = r.GetInt32(2);
            state.Address = r.GetString(3);
            state.HomeLocationId = r.GetString(4);
        }

        if (TableExists(conn, "NpcNameProfiles"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    COALESCE(CurrentLastName,''),
                    COALESCE(BirthLastName,'')
                FROM NpcNameProfiles
                WHERE NpcId=$id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);

            using var r = cmd.ExecuteReader();

            if (r.Read())
            {
                state.CurrentLastName = r.GetString(0);
                state.BirthLastName = r.GetString(1);
            }
        }

        state.PhoneCount = Count(
            conn,
            "SELECT COUNT(*) FROM NpcPhones WHERE NpcId=$id;",
            npcId);

        state.VehicleCount = Count(
            conn,
            """
            SELECT COUNT(*)
            FROM Vehicles
            WHERE RegisteredOwnerNpcId=$id OR PrimaryDriverNpcId=$id;
            """,
            npcId);

        state.FinanceAccountCount = Count(
            conn,
            """
            SELECT COUNT(*)
            FROM FinancialAccounts
            WHERE OwnerType='NPC' AND OwnerId=$id;
            """,
            npcId);

        return state;
    }

    private FoundationPhonePreview CreateUniquePhone(int npcId)
    {
        string[] areaCodes =
        {
            "937", "326", "614", "380", "513",
            "283", "419", "567", "740", "220"
        };

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var index = Math.Abs(StableInt($"phone|{npcId}|{attempt}"));
            var area = areaCodes[(index / 100) % areaCodes.Length];
            var suffix = 100 + (index % 100);
            var digits = $"{area}555{suffix:0000}";

            if (PhoneDigitsAvailable(digits))
            {
                var carriers = new[] { "Verizon", "AT&T", "T-Mobile" };
                var devices = new[]
                {
                    ("Apple", "iPhone 15"),
                    ("Samsung", "Galaxy S24"),
                    ("Google", "Pixel 8")
                };

                var carrier = carriers[index % carriers.Length];
                var device = devices[(index / 7) % devices.Length];

                return new FoundationPhonePreview
                {
                    PhoneNumber = $"({area}) 555-{suffix:0000}",
                    PhoneType = "Mobile",
                    CarrierName = carrier,
                    DeviceMake = device.Item1,
                    DeviceModel = device.Item2,
                    DeviceLabel = "Primary Phone"
                };
            }
        }

        throw new InvalidOperationException(
            "Could not allocate a unique fictional phone number after 1000 attempts.");
    }

    private FoundationVehiclePreview CreateUniqueVehicle(int npcId, int age)
    {
        var choices = new[]
        {
            ("Toyota", "Camry"),
            ("Honda", "CR-V"),
            ("Subaru", "Outback"),
            ("Ford", "Escape"),
            ("Chevrolet", "Equinox"),
            ("Hyundai", "Tucson")
        };

        var colors = new[]
        {
            "White", "Black", "Silver", "Gray",
            "Blue", "Red", "Dark Green"
        };

        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var seed = Math.Abs(StableInt($"vehicle|{npcId}|{attempt}"));
            var pair = choices[seed % choices.Length];
            var plate = BuildPlate(seed);

            if (!PlateAvailable(plate))
                continue;

            var year = 2016 + (seed % 9);
            var miles = 18000 + (seed % 105000);

            return new FoundationVehiclePreview
            {
                VehicleType = "Car",
                Make = pair.Item1,
                Model = pair.Item2,
                ModelYear = year,
                Color = colors[(seed / 5) % colors.Length],
                Vin = BuildVin(npcId, attempt),
                PlateNumber = plate,
                PlateState = "OH",
                Status = "Active",
                OdometerMiles = miles
            };
        }

        throw new InvalidOperationException(
            "Could not allocate a unique license plate after 1000 attempts.");
    }

    private FoundationFinancePreview CreateFinancePreview(int npcId)
    {
        var seed = Math.Abs(StableInt($"finance|{npcId}"));
        var balance = 1800 + (seed % 13200);

        return new FoundationFinancePreview
        {
            AccountId = $"acct-{npcId}-primary",
            AccountType = "Checking",
            InstitutionName = "Cullen Federal Bank",
            AccountName = "Primary Checking",
            Balance = balance,
            CurrencyCode = "USD",
            IsPrimary = true,
            Status = "Active"
        };
    }

    private bool PhoneDigitsAvailable(string digits)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM NpcPhones
            WHERE replace(
                    replace(
                      replace(
                        replace(
                          replace(trim(COALESCE(PhoneNumber,'')),'(', ''),
                        ')', ''),
                      '-', ''),
                    ' ', ''),
                  '.', '') = $digits;
            """;
        cmd.Parameters.AddWithValue("$digits", digits);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 0;
    }

    private bool PlateAvailable(string plate)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM Vehicles
            WHERE upper(
                    replace(
                      replace(
                        replace(trim(COALESCE(PlateNumber,'')),'-',''),
                      ' ',''),
                    '.','')
                  ) = $plate;
            """;
        cmd.Parameters.AddWithValue("$plate", PlateKey(plate));

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 0;
    }

    private static string BuildPlate(int seed)
    {
        const string letters = "ABCDEFGHJKLMNPRSTUVWXYZ";
        var a = letters[(seed / 11) % letters.Length];
        var b = letters[(seed / 37) % letters.Length];
        var c = letters[(seed / 101) % letters.Length];
        var number = seed % 10000;

        return $"{a}{b}{c}-{number:0000}";
    }

    private static string BuildVin(int npcId, int attempt)
    {
        const string chars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"ProjectEve|VIN|{npcId}|{attempt}"));

        var sb = new StringBuilder(17);

        for (var i = 0; i < 17; i++)
            sb.Append(chars[bytes[i] % chars.Length]);

        return sb.ToString();
    }

    private static string PlateKey(string? value)
        => new string(
            (value ?? "")
            .Where(ch => ch != '-' && ch != ' ' && ch != '.')
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static int StableInt(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
    }

    private static int Count(
        SqliteConnection conn,
        string sql,
        int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", npcId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static bool TableExists(
        SqliteConnection conn,
        string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type='table' AND name=$name;
            """;
        cmd.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(
            $"Data Source={_options.MainDbPath}");
        conn.Open();
        return conn;
    }

    private sealed class CurrentFoundationState
    {
        public int NpcId { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public int Tier { get; set; } = 5;
        public string CurrentLastName { get; set; } = "";
        public string BirthLastName { get; set; } = "";
        public string Address { get; set; } = "";
        public string HomeLocationId { get; set; } = "";
        public int PhoneCount { get; set; }
        public int VehicleCount { get; set; }
        public int FinanceAccountCount { get; set; }
    }

    private AiNpcProfilePreview? LoadCachedFoundationProfile(int npcId)
    {
        using var conn = Open();
        EnsureFoundationDraftSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT BuildTier, SourceModel, RawJson, ProposalJson, WarningsJson
            FROM NpcFoundationDrafts
            WHERE NpcId=$id
              AND Status='PreviewLocked'
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return null;

        var proposalJson = r.IsDBNull(3) ? "" : r.GetString(3);

        if (string.IsNullOrWhiteSpace(proposalJson))
            return null;

        var proposal = JsonSerializer.Deserialize<AiNpcProfileProposal>(
            proposalJson,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (proposal is null)
            return null;

        var preview = new AiNpcProfilePreview
        {
            NpcId = npcId,
            BuildTier = r.IsDBNull(0) ? 5 : r.GetInt32(0),
            SourceModel = r.IsDBNull(1) ? "" : r.GetString(1),
            RawJson = r.IsDBNull(2) ? "" : r.GetString(2),
            Proposal = proposal
        };

        if (!r.IsDBNull(4))
        {
            var warningJson = r.GetString(4);

            if (!string.IsNullOrWhiteSpace(warningJson))
            {
                var warnings = JsonSerializer.Deserialize<List<string>>(warningJson);

                if (warnings is not null)
                {
                    foreach (var warning in warnings)
                        preview.Warnings.Add(warning);
                }
            }
        }

        preview.Warnings.Add(
            "INFO: Reusing locked Foundation draft preview. Identity and appearance will not change on rebuild.");

        return preview;
    }

    private NpcAppearanceDetailProfile? LoadCachedDetailedAppearance(
        int npcId)
    {
        using var conn = Open();
        EnsureFoundationDraftSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(DetailedAppearanceJson,'')
            FROM NpcFoundationDrafts
            WHERE NpcId=$id
              AND Status='PreviewLocked'
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        var json = Convert.ToString(cmd.ExecuteScalar()) ?? "";

        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<NpcAppearanceDetailProfile>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }

    private void SaveCachedDetailedAppearance(
        int npcId,
        NpcAppearanceDetailProfile profile)
    {
        using var conn = Open();
        EnsureFoundationDraftSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcFoundationDrafts
            SET DetailedAppearanceJson=$json,
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE NpcId=$id
              AND Status='PreviewLocked';
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue(
            "$json",
            JsonSerializer.Serialize(profile));

        if (cmd.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException(
                "Foundation detailed appearance could not be locked because the Foundation draft row is missing.");
        }
    }

    private void SaveCachedFoundationProfile(AiNpcProfilePreview preview)
    {
        if (preview.Proposal is null)
            return;

        using var conn = Open();
        EnsureFoundationDraftSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcFoundationDrafts
            (
                NpcId, BuildTier, SourceModel, RawJson,
                ProposalJson, WarningsJson, Status,
                CreatedRealAt, UpdatedRealAt
            )
            VALUES
            (
                $id, $tier, $model, $raw,
                $proposal, $warnings, 'PreviewLocked',
                CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                BuildTier=excluded.BuildTier,
                SourceModel=excluded.SourceModel,
                RawJson=excluded.RawJson,
                ProposalJson=excluded.ProposalJson,
                WarningsJson=excluded.WarningsJson,
                Status='PreviewLocked',
                UpdatedRealAt=CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", preview.NpcId);
        cmd.Parameters.AddWithValue("$tier", preview.BuildTier);
        cmd.Parameters.AddWithValue("$model", preview.SourceModel ?? "");
        cmd.Parameters.AddWithValue("$raw", preview.RawJson ?? "");
        cmd.Parameters.AddWithValue("$proposal", JsonSerializer.Serialize(preview.Proposal));
        cmd.Parameters.AddWithValue("$warnings", JsonSerializer.Serialize(preview.Warnings));
        cmd.ExecuteNonQuery();
    }

    private static void EnsureFoundationDraftSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcFoundationDrafts
            (
                NpcId INTEGER PRIMARY KEY,
                BuildTier INTEGER NOT NULL DEFAULT 5,
                SourceModel TEXT NOT NULL DEFAULT '',
                RawJson TEXT NOT NULL DEFAULT '',
                ProposalJson TEXT NOT NULL DEFAULT '',
                WarningsJson TEXT NOT NULL DEFAULT '[]',
                Status TEXT NOT NULL DEFAULT 'PreviewLocked',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
        cmd.ExecuteNonQuery();
        EnsureFoundationDraftColumn(
            conn,
            "DetailedAppearanceJson",
            "TEXT NOT NULL DEFAULT ''");
    }


    private static void EnsureFoundationDraftColumn(
        SqliteConnection conn,
        string columnName,
        string definition)
    {
        using var check = conn.CreateCommand();
        check.CommandText = "PRAGMA table_info(NpcFoundationDrafts);";

        using var reader = check.ExecuteReader();
        var exists = false;

        while (reader.Read())
        {
            var name = reader.IsDBNull(1)
                ? ""
                : reader.GetString(1);

            if (name.Equals(
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        reader.Close();

        if (exists)
            return;

        using var alter = conn.CreateCommand();
        alter.CommandText =
            $"ALTER TABLE NpcFoundationDrafts ADD COLUMN [{columnName}] {definition};";
        alter.ExecuteNonQuery();
    }

    private void NormalizeAdultCurrentLife(
        int npcId,
        int age,
        AiNpcProfileProposal proposal,
        List<string> warnings)
    {
        if (age < 18)
            return;

        var occupation = (proposal.Occupation ?? "").Trim();
        var employer = (proposal.Employer ?? "").Trim();

        var isMother = IsCanonicalMother(npcId);

        if (string.IsNullOrWhiteSpace(occupation))
        {
            if (isMother)
            {
                proposal.Occupation = "Stay-at-home Mom";
                proposal.Employer = "";
                warnings.Add(
                    "INFO: Adult current-life role was blank, so this canonical mother was normalized to Stay-at-home Mom.");
            }
            else
            {
                proposal.Occupation = "Homemaker";
                proposal.Employer = "";
                warnings.Add(
                    "INFO: Adult current-life role was blank, so it was normalized to Homemaker.");
            }

            return;
        }

        var homeRoles = new[]
        {
            "homemaker",
            "stay-at-home mom",
            "stay at home mom",
            "stay-at-home mother",
            "stay at home mother",
            "stay-at-home parent",
            "stay at home parent",
            "full-time caregiver",
            "caregiver"
        };

        var normalized = occupation.ToLowerInvariant();

        if (homeRoles.Any(x => normalized == x))
        {
            proposal.Occupation = isMother
                ? "Stay-at-home Mom"
                : "Homemaker";

            proposal.Employer = "";
            return;
        }

        // Working-age adults with a normal occupation should have a
        // concrete employer/work context unless the role itself clearly
        // represents self-employment.
        if (age <= 67 &&
            string.IsNullOrWhiteSpace(employer) &&
            !normalized.Contains("self-employed") &&
            !normalized.Contains("freelance") &&
            !normalized.Contains("independent") &&
            !normalized.Contains("retired"))
        {
            warnings.Add(
                $"REVIEW: Adult occupation '{proposal.Occupation}' has no employer. " +
                "Foundation should supply a canonical employer/organization or use a valid non-employer role such as Stay-at-home Mom.");
        }
    }

    private bool IsCanonicalMother(int npcId)
    {
        var mainDir = Path.GetDirectoryName(_options.MainDbPath);

        if (string.IsNullOrWhiteSpace(mainDir))
            return false;

        var relationshipsPath = Path.Combine(
            mainDir,
            "project_eve_relationships.db");

        if (!File.Exists(relationshipsPath))
            return false;

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = relationshipsPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM FamilyParentChildLinks
            WHERE ParentNpcId=$id
              AND IsCurrent=1
              AND lower(trim(COALESCE(ParentSlot,'')))='mother';
            """;
        cmd.Parameters.AddWithValue("$id",npcId);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

            var detailedAppearance =
            preview.DetailedAppearance
            ?? throw new InvalidOperationException(
                "Locked detailed Foundation appearance is missing.");

        NpcAppearanceDetailService.NormalizeCompleteness(
            detailedAppearance);

        _appearanceDetails.Save(
            detailedAppearance);

        result.AppearanceApplied = true;

        // ----------------------------------------------------
        // 3. Housing assignment.
        //    One unit per household; same address to all actual
        //    members resolved by the housing service.
        // ----------------------------------------------------
        if (preview.Housing is not null &&
            !string.IsNullOrWhiteSpace(preview.Housing.UnitId))
        {
            var assignment = _housing.AssignHouseholdUnit(
                npcId,
                preview.HouseholdSize,
                age);

            result.HousingApplied = true;
            result.HousingUnitId = assignment.UnitId;
            result.Address = assignment.Address;
        }

        // ----------------------------------------------------
        // 4. Phone - only if missing.
        // ----------------------------------------------------
        if (preview.ExistingPhoneCount == 0 &&
            preview.Phone is not null)
        {
            await _repo.SavePhoneAsync(
                npcId,
                new CanonicalPhoneRow
                {
                    PhoneNumber = preview.Phone.PhoneNumber,
                    PhoneType = preview.Phone.PhoneType,
                    CarrierName = preview.Phone.CarrierName,
                    DeviceMake = preview.Phone.DeviceMake,
                    DeviceModel = preview.Phone.DeviceModel,
                    DeviceLabel = preview.Phone.DeviceLabel,
                    IsPrimary = true,
                    IsActive = true
                });

            result.PhoneApplied = true;
            result.PhoneNumber = preview.Phone.PhoneNumber;
        }

        // ----------------------------------------------------
        // 5. Vehicle - only if missing.
        // ----------------------------------------------------
        if (preview.ExistingVehicleCount == 0 &&
            preview.Vehicle is not null)
        {
            await _repo.SaveVehicleAsync(
                npcId,
                new CanonicalVehicleRow
                {
                    VehicleType = preview.Vehicle.VehicleType,
                    Make = preview.Vehicle.Make,
                    Model = preview.Vehicle.Model,
                    ModelYear = preview.Vehicle.ModelYear,
                    Color = preview.Vehicle.Color,
                    Vin = preview.Vehicle.Vin,
                    PlateNumber = preview.Vehicle.PlateNumber,
                    PlateState = preview.Vehicle.PlateState,
                    Status = preview.Vehicle.Status,
                    OdometerMiles = preview.Vehicle.OdometerMiles
                });

            result.VehicleApplied = true;
            result.PlateNumber = preview.Vehicle.PlateNumber;
        }

        // ----------------------------------------------------
        // 6. Finance - only if missing.
        // ----------------------------------------------------
        if (preview.ExistingFinanceAccountCount == 0 &&
            preview.Finance is not null)
        {
            var finance = new CanonicalDynamicRow
            {
                TableName = "FinancialAccounts",
                PrimaryKeyColumn = "AccountId"
            };

            finance.Values["AccountId"] = preview.Finance.AccountId;
            finance.Values["OwnerType"] = "NPC";
            finance.Values["OwnerId"] = npcId.ToString();
            finance.Values["AccountType"] = preview.Finance.AccountType;
            finance.Values["InstitutionName"] = "Cullen Federal Bank";
            finance.Values["AccountName"] = preview.Finance.AccountName;
            finance.Values["Balance"] = preview.Finance.Balance.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            finance.Values["CurrencyCode"] = preview.Finance.CurrencyCode;
            finance.Values["IsPrimary"] = preview.Finance.IsPrimary ? "1" : "0";
            finance.Values["Status"] = preview.Finance.Status;
            finance.Values["Notes"] = "Created by Stage 4B Foundation.";

            await _repo.SaveCanonicalFinanceRowAsync(
                finance,
                npcId);

            result.FinanceApplied = true;
            result.BankName = "Cullen Federal Bank";
        }

        MarkFoundationDraftCommitted(npcId);

        result.Success = true;
        result.Message =
            "NPC 6 Foundation committed. Traits, AI Summary, photos and history were not changed.";

        return result;
    }

    private void ApplyFoundationIdentityCurrentLife(
        int npcId,
        AiNpcProfileProposal p)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        string currentSurname = "";

        using (var surname = conn.CreateCommand())
        {
            surname.Transaction = tx;
            surname.CommandText = """
                SELECT COALESCE(CurrentLastName,'')
                FROM NpcNameProfiles
                WHERE NpcId=$id
                LIMIT 1;
                """;
            surname.Parameters.AddWithValue("$id", npcId);
            currentSurname =
                Convert.ToString(surname.ExecuteScalar())?.Trim() ?? "";
        }

        if (string.IsNullOrWhiteSpace(currentSurname))
            currentSurname = (p.CurrentLastName ?? "").Trim();

        var first = (p.FirstName ?? "").Trim();
        var middle = (p.MiddleName ?? "").Trim();
        var preferred = string.IsNullOrWhiteSpace(p.PreferredName)
            ? first
            : p.PreferredName.Trim();

        var fullName = string.Join(
            " ",
            new[] { first, middle, currentSurname }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        using (var name = conn.CreateCommand())
        {
            name.Transaction = tx;
            name.CommandText = """
                UPDATE NpcNameProfiles
                SET
                    FirstName = CASE
                        WHEN trim(COALESCE(FirstName,''))='' THEN $first
                        ELSE FirstName
                    END,
                    MiddleName = CASE
                        WHEN trim(COALESCE(MiddleName,''))='' THEN $middle
                        ELSE MiddleName
                    END,
                    PreferredName = CASE
                        WHEN trim(COALESCE(PreferredName,''))='' THEN $preferred
                        ELSE PreferredName
                    END,
                    UpdatedRealAt=CURRENT_TIMESTAMP
                WHERE NpcId=$id;
                """;
            name.Parameters.AddWithValue("$id", npcId);
            name.Parameters.AddWithValue("$first", first);
            name.Parameters.AddWithValue("$middle", middle);
            name.Parameters.AddWithValue("$preferred", preferred);
            name.ExecuteNonQuery();
        }

        using (var character = conn.CreateCommand())
        {
            character.Transaction = tx;
            character.CommandText = """
                UPDATE Characters
                SET
                    Name = CASE
                        WHEN Name LIKE '[Family Draft %]%' OR trim(COALESCE(Name,''))=''
                        THEN $name ELSE Name
                    END,
                    DisplayName = CASE
                        WHEN trim(COALESCE(DisplayName,''))=''
                        THEN $name ELSE DisplayName
                    END,
                    FirstName = CASE
                        WHEN trim(COALESCE(FirstName,''))=''
                        THEN $first ELSE FirstName
                    END,
                    LastName = CASE
                        WHEN trim(COALESCE(LastName,''))='' OR lower(LastName)='sinclair'
                        THEN $last ELSE LastName
                    END,
                    Age = CASE WHEN COALESCE(Age,0)<=0 THEN $age ELSE Age END,
                    Gender = CASE
                        WHEN trim(COALESCE(Gender,''))='' THEN $gender ELSE Gender
                    END,
                    Occupation = CASE
                        WHEN trim(COALESCE(Occupation,''))='' THEN $occupation ELSE Occupation
                    END,
                    Employer = CASE
                        WHEN trim(COALESCE(Employer,''))='' THEN $employer ELSE Employer
                    END,
                    Hometown = CASE
                        WHEN trim(COALESCE(Hometown,''))='' THEN $hometown ELSE Hometown
                    END,
                    HeightCm = CASE
                        WHEN COALESCE(HeightCm,0)<=0 THEN $height ELSE HeightCm
                    END,
                    WeightKg = CASE
                        WHEN COALESCE(WeightKg,0)<=0 THEN $weight ELSE WeightKg
                    END,
                    UpdatedRealAt=CURRENT_TIMESTAMP
                WHERE Id=$id;
                """;
            character.Parameters.AddWithValue("$id", npcId);
            character.Parameters.AddWithValue("$name", fullName);
            character.Parameters.AddWithValue("$first", first);
            character.Parameters.AddWithValue("$last", currentSurname);
            character.Parameters.AddWithValue("$age", p.Age);
            character.Parameters.AddWithValue("$gender", p.Gender ?? "");
            character.Parameters.AddWithValue("$occupation", p.Occupation ?? "");
            character.Parameters.AddWithValue("$employer", p.Employer ?? "");
            character.Parameters.AddWithValue("$hometown", p.Hometown ?? "");
            character.Parameters.AddWithValue("$height", p.HeightCm);
            character.Parameters.AddWithValue("$weight", p.WeightKg);
            character.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void ApplyFoundationAppearance(
        int npcId,
        AiNpcProfileProposal p)
    {
        using var conn = Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcAppearanceProfiles
            (
                NpcId,
                AppearanceStatus,
                BodyType,
                HairColor,
                HairStyle,
                EyeColor,
                SkinTone,
                ClothingStyle,
                DistinguishingFeatures,
                Approved,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $id,
                'Foundation Ready',
                $body,
                $hairColor,
                $hairStyle,
                $eyes,
                $skin,
                $clothing,
                $features,
                0,
                'Stage 4B Foundation',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                BodyType = CASE
                    WHEN trim(COALESCE(BodyType,''))='' THEN excluded.BodyType
                    ELSE BodyType
                END,
                HairColor = CASE
                    WHEN trim(COALESCE(HairColor,''))='' THEN excluded.HairColor
                    ELSE HairColor
                END,
                HairStyle = CASE
                    WHEN trim(COALESCE(HairStyle,''))='' THEN excluded.HairStyle
                    ELSE HairStyle
                END,
                EyeColor = CASE
                    WHEN trim(COALESCE(EyeColor,''))='' THEN excluded.EyeColor
                    ELSE EyeColor
                END,
                SkinTone = CASE
                    WHEN trim(COALESCE(SkinTone,''))='' THEN excluded.SkinTone
                    ELSE SkinTone
                END,
                ClothingStyle = CASE
                    WHEN trim(COALESCE(ClothingStyle,''))='' THEN excluded.ClothingStyle
                    ELSE ClothingStyle
                END,
                DistinguishingFeatures = CASE
                    WHEN trim(COALESCE(DistinguishingFeatures,''))=''
                    THEN excluded.DistinguishingFeatures
                    ELSE DistinguishingFeatures
                END,
                AppearanceStatus = CASE
                    WHEN trim(COALESCE(AppearanceStatus,''))=''
                    THEN 'Foundation Ready'
                    ELSE AppearanceStatus
                END,
                UpdatedRealAt=CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$body", p.BodyType ?? "");
        cmd.Parameters.AddWithValue("$hairColor", p.HairColor ?? "");
        cmd.Parameters.AddWithValue("$hairStyle", p.HairStyle ?? "");
        cmd.Parameters.AddWithValue("$eyes", p.EyeColor ?? "");
        cmd.Parameters.AddWithValue("$skin", p.SkinTone ?? "");
        cmd.Parameters.AddWithValue("$clothing", p.ClothingStyle ?? "");
        cmd.Parameters.AddWithValue("$features", p.DistinguishingFeatures ?? "");
        cmd.ExecuteNonQuery();
    }

    private void MarkFoundationDraftCommitted(int npcId)
    {
        using var conn = Open();
        EnsureFoundationDraftSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcFoundationDrafts
            SET Status='Committed',
                UpdatedRealAt=CURRENT_TIMESTAMP
            WHERE NpcId=$id;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.ExecuteNonQuery();
    }

}


public sealed class NpcFoundationCommitResult
{
    public int NpcId { get; set; }
    public bool Success { get; set; }
    public bool IdentityApplied { get; set; }
    public bool AppearanceApplied { get; set; }
    public bool HousingApplied { get; set; }
    public bool PhoneApplied { get; set; }
    public bool VehicleApplied { get; set; }
    public bool FinanceApplied { get; set; }
    public string HousingUnitId { get; set; } = "";
    public string Address { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string PlateNumber { get; set; } = "";
    public string BankName { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class NpcFoundationPreview
{
    public NpcAppearanceDetailProfile? DetailedAppearance { get; set; }

    public int NpcId { get; set; }
    public string ExistingName { get; set; } = "";
    public int Tier { get; set; }
    public string ExistingCurrentLastName { get; set; } = "";
    public string ExistingBirthLastName { get; set; } = "";
    public string ExistingAddress { get; set; } = "";
    public string ExistingHomeLocationId { get; set; } = "";
    public int ExistingPhoneCount { get; set; }
    public int ExistingVehicleCount { get; set; }
    public int ExistingFinanceAccountCount { get; set; }
    public AiNpcProfilePreview? Profile { get; set; }
    public FoundationPhonePreview? Phone { get; set; }
    public FoundationVehiclePreview? Vehicle { get; set; }
    public FoundationFinancePreview? Finance { get; set; }
    public HousingSelectionPreview? Housing { get; set; }
    public int HouseholdSize { get; set; } = 1;
    public List<string> Warnings { get; } = new();

    public bool IsValid =>
        Profile?.Proposal is not null &&
        Warnings.All(x =>
            !x.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase));
}

public sealed class FoundationPhonePreview
{
    public string PhoneNumber { get; set; } = "";
    public string PhoneType { get; set; } = "";
    public string CarrierName { get; set; } = "";
    public string DeviceMake { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string DeviceLabel { get; set; } = "";
}

public sealed class FoundationVehiclePreview
{
    public string VehicleType { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public int ModelYear { get; set; }
    public string Color { get; set; } = "";
    public string Vin { get; set; } = "";
    public string PlateNumber { get; set; } = "";
    public string PlateState { get; set; } = "";
    public string Status { get; set; } = "";
    public double OdometerMiles { get; set; }
}

public sealed class FoundationFinancePreview
{
    public string AccountId { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public double Balance { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public bool IsPrimary { get; set; }
    public string Status { get; set; } = "";
}
