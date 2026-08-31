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

            // A Foundation proposal is a PREVIEW artifact, not canonical NPC data.
            // Lock the proposal whenever one exists so downstream preview-only
            // stages (including detailed appearance) have a durable parent row.
            //
            // Validation warnings remain on the preview. Commit safety is separate:
            // an invalid Foundation must not be committed merely because its
            // preview row exists.
            if (profile.Proposal is not null)
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

        // FamilyDraft shell rows commonly have Age=0. AI is allowed to propose
        // an age, but zero/implausible output must never leak into Foundation.
        // Repair it structurally from Eve / biological family position BEFORE
        // housing, current-life, appearance, vehicle and finance logic run.
        var foundationAgeWasRepaired = false;
        var originalFoundationDraftAge = profile.Proposal?.Age ?? 0;

        if (profile.Proposal is not null &&
            current.Age <= 0)
        {
            var proposedAge = profile.Proposal.Age;

            var needsRepair =
                !IsPlausibleFoundationAge(proposedAge) ||
                !IsAgeCompatibleWithCanonicalFamily(
                    npcId,
                    proposedAge);

            if (needsRepair)
            {
                var repairedAge =
                    ResolvePlausibleFamilyAge(npcId);

                if (repairedAge > 0)
                {
                    profile.Proposal.Age = repairedAge;
                    foundationAgeWasRepaired =
                        proposedAge != repairedAge;

                    result.Warnings.RemoveAll(x =>
                        x.Contains(
                            "plausible age",
                            StringComparison.OrdinalIgnoreCase));

                    result.Warnings.Add(
                        $"INFO: Draft age {proposedAge} was adjusted to age {repairedAge} to stay consistent with canonical family age relationships.");
                }
            }
        }
        if (profile.Proposal is not null)
        {
            NormalizeFoundationBirthSurname(
                npcId,
                current,
                profile.Proposal,
                result.Warnings);

            if (foundationAgeWasRepaired)
            {
                NormalizeFoundationAgeDependentContext(
                    originalFoundationDraftAge,
                    profile.Proposal.Age,
                    profile.Proposal,
                    result.Warnings);
            }

            NormalizeFoundationPhysicalSize(
                npcId,
                profile.Proposal,
                result.Warnings);
        }
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
        // Resolve ancestry from structural biological parentage before
        // detailed appearance. Existing specific canon wins; drafts derive
        // from biological parents; founders receive a stable specific ancestry.
        profile.Proposal.RaceEthnicity =
            ResolveFoundationAncestry(npcId);


        var detailedAppearance =
            LoadCachedDetailedAppearance(npcId);

        // A locked Foundation draft can predate the canonical Characters row.
        // Never reuse detailed appearance that was generated with the wrong
        // age/gender/life-stage context.
        if (detailedAppearance is not null &&
            !DetailedAppearanceMatchesFoundation(
                detailedAppearance,
                age,
                profile.Proposal.Gender))
        {
            result.Warnings.Add(
                "INFO: Discarding stale locked detailed appearance because its age/gender context does not match the locked Foundation proposal.");

            detailedAppearance = null;
        }

        if (detailedAppearance is not null &&
            !string.Equals(
                (detailedAppearance.RaceEthnicity ?? "").Trim(),
                (profile.Proposal.RaceEthnicity ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add(
                "INFO: Discarding stale locked detailed appearance because its ancestry does not match the Eve-rooted Foundation ancestry.");

            detailedAppearance = null;
        }

        if (detailedAppearance is null)
        {
            // Start with existing authored appearance facts, but overwrite
            // Foundation context with the LOCKED proposal before asking AI.
            // This is critical for draft NPCs whose Characters row may still
            // contain Age=0 or other shell values.
            var appearanceContext = _appearanceDetails.Load(npcId);

            appearanceContext.Age = age;
            appearanceContext.Gender =
                Explicit(profile.Proposal.Gender, appearanceContext.Gender);            appearanceContext.RaceEthnicity =
                Explicit(profile.Proposal.RaceEthnicity, appearanceContext.RaceEthnicity);

            appearanceContext.Occupation =
                Explicit(profile.Proposal.Occupation, appearanceContext.Occupation);
            appearanceContext.Location =
                Explicit(profile.Proposal.Hometown, appearanceContext.Location);
            appearanceContext.Tier = Math.Clamp(current.Tier, 1, 5);

            // Broad Foundation appearance facts are constraints for the
            // detailed pass, not values to invent again afterward.
            appearanceContext.BodyBuild =
                Explicit(profile.Proposal.BodyType, appearanceContext.BodyBuild);
            appearanceContext.EyeBaseColor =
                Explicit(profile.Proposal.EyeColor, appearanceContext.EyeBaseColor);
            appearanceContext.HairColor =
                Explicit(profile.Proposal.HairColor, appearanceContext.HairColor);
            appearanceContext.HairStyle =
                Explicit(profile.Proposal.HairStyle, appearanceContext.HairStyle);
            appearanceContext.SkinTone =
                Explicit(profile.Proposal.SkinTone, appearanceContext.SkinTone);
            appearanceContext.DefaultClothingStyle =
                Explicit(profile.Proposal.ClothingStyle, appearanceContext.DefaultClothingStyle);
            appearanceContext.DistinguishingFeatures =
                Explicit(profile.Proposal.DistinguishingFeatures, appearanceContext.DistinguishingFeatures);

            detailedAppearance =
                await _appearanceAi.BuildDraftAsync(
                    appearanceContext,
                    cancellationToken);

            // Reassert Foundation context after AI so a malformed response can
            // never change age/gender/life-stage identity.
            detailedAppearance.Age = age;
            detailedAppearance.Gender = appearanceContext.Gender;
            detailedAppearance.RaceEthnicity = appearanceContext.RaceEthnicity;
            detailedAppearance.Occupation = appearanceContext.Occupation;
            detailedAppearance.Location = appearanceContext.Location;
            detailedAppearance.Tier = appearanceContext.Tier;

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

        if (string.IsNullOrWhiteSpace(profile.Proposal.Address) &&
            result.Housing is not null &&
            !string.IsNullOrWhiteSpace(result.Housing.Address))
        {
            profile.Proposal.Address = result.Housing.Address.Trim();
        }

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

    private static bool DetailedAppearanceMatchesFoundation(
        NpcAppearanceDetailProfile detailed,
        int foundationAge,
        string? foundationGender)
    {
        if (detailed.Age != foundationAge)
            return false;

        var expectedGender = (foundationGender ?? "").Trim();
        var actualGender = (detailed.Gender ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(expectedGender) &&
            !actualGender.Equals(
                expectedGender,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Guard against the exact stale-draft failure we found: an adult
        // Foundation proposal carrying a child appearance classification.
        if (foundationAge >= 18 &&
            (detailed.AppearanceLevel ?? "")
                .Contains("child", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var validAppearanceLevels = new HashSet<string>(
            new[]
            {
                "Below Average","Normal / Everyday","Pleasant","Above Average",
                "Attractive","Very Attractive","Striking","Model-Like","Supermodel-Like"
            },
            StringComparer.OrdinalIgnoreCase);

        if (!validAppearanceLevels.Contains((detailed.AppearanceLevel ?? "").Trim()))
            return false;

        // Completeness sentinels are allowed only when the field is truly
        // non-applicable. Applicable visual identity fields must be concrete
        // before the locked detailed Foundation draft is considered complete.
        static bool Unresolved(string? value)
        {
            var x = (value ?? "").Trim();
            return string.IsNullOrWhiteSpace(x)
                || x.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                || x.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                || x.Equals("Not Yet Determined", StringComparison.OrdinalIgnoreCase);
        }

        if (Unresolved(detailed.EyeVariant) ||
            Unresolved(detailed.EyePattern))
        {
            return false;
        }

        var gender = (foundationGender ?? detailed.Gender ?? "").Trim();
        var female =
            gender.Contains("female", StringComparison.OrdinalIgnoreCase) ||
            gender.Equals("woman", StringComparison.OrdinalIgnoreCase);

        var male =
            (gender.Contains("male", StringComparison.OrdinalIgnoreCase) &&
             !gender.Contains("female", StringComparison.OrdinalIgnoreCase)) ||
            gender.Equals("man", StringComparison.OrdinalIgnoreCase);

        if (foundationAge >= 18 && female && Unresolved(detailed.BraSize))
            return false;

        if (foundationAge >= 18 && male &&
            (Unresolved(detailed.PenisSize) ||
             Unresolved(detailed.CircumcisionStatus)))
        {
            return false;
        }

        return true;
    }

    private static string Explicit(string? proposed, string? fallback)
    {
        var value = (proposed ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        value = (fallback ?? "").Trim();
        return string.IsNullOrWhiteSpace(value) ? "N/A" : value;
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

    private static bool IsPlausibleFoundationAge(int age)
        => age >= 1 && age <= 110;

    private bool IsAgeCompatibleWithCanonicalFamily(
        int npcId,
        int proposedAge)
    {
        if (!IsPlausibleFoundationAge(proposedAge))
            return false;

        const int anchorNpcId = 1;

        var anchorAge = ReadCanonicalAge(anchorNpcId);
        if (anchorAge <= 0)
            anchorAge = 21;

        if (npcId == anchorNpcId)
            return proposedAge == anchorAge;

        var anchorParents =
            GetBiologicalParentIds(anchorNpcId)
                .OrderBy(x => x)
                .ToArray();

        var npcParents =
            GetBiologicalParentIds(npcId)
                .OrderBy(x => x)
                .ToArray();

        // Full siblings should normally be close in age.
        // Keep them within five years of Eve unless an age was already
        // manually/canonically established in Characters.
        if (npcParents.Length >= 2 &&
            anchorParents.Length >= 2 &&
            npcParents.SequenceEqual(anchorParents))
        {
            return Math.Abs(proposedAge - anchorAge) <= 5;
        }

        // Half siblings may reasonably have a somewhat wider spread.
        var sharedParents =
            npcParents.Intersect(anchorParents).Count();

        if (sharedParents == 1)
        {
            return Math.Abs(proposedAge - anchorAge) <= 8;
        }

        // Eve's biological parents must be old enough to plausibly be parents.
        if (anchorParents.Contains(npcId))
        {
            var gap = proposedAge - anchorAge;
            return gap >= 18 && gap <= 45;
        }

        // Ancestors should become progressively older by generation.
        var ancestorDistance =
            BiologicalGenerationDistance(
                npcId,
                anchorNpcId);

        if (ancestorDistance >= 2)
        {
            var minimum =
                anchorAge + ancestorDistance * 18;

            var maximum =
                Math.Min(
                    110,
                    anchorAge + ancestorDistance * 45);

            return proposedAge >= minimum &&
                   proposedAge <= maximum;
        }

        // Descendants must be younger by a plausible generation interval.
        var descendantDistance =
            BiologicalGenerationDistance(
                anchorNpcId,
                npcId);

        if (descendantDistance >= 1)
        {
            var maximum =
                anchorAge - descendantDistance * 14;

            return proposedAge >= 1 &&
                   proposedAge <= Math.Max(1,maximum);
        }

        // Unrelated spouse/in-law age is not constrained by Eve here.
        return true;
    }
    private int ResolvePlausibleFamilyAge(int npcId)
    {
        const int anchorNpcId = 1;

        var anchorAge = ReadCanonicalAge(anchorNpcId);
        if (anchorAge <= 0)
            anchorAge = 21;

        if (npcId == anchorNpcId)
            return anchorAge;

        // Biological parent of Eve.
        var eveParents = GetBiologicalParentIds(anchorNpcId);
        var parentIndex = eveParents.IndexOf(npcId);

        if (parentIndex >= 0)
        {
            // Stable, plausible parent gap. Different parents do not have to
            // be the same age, but both remain biologically credible.
            var gap = 23 + StableInt($"family-age|parent|{npcId}") % 10;
            return Math.Clamp(anchorAge + gap,18,75);
        }

        // Full sibling: same two biological parents as Eve.
        var npcParents = GetBiologicalParentIds(npcId)
            .OrderBy(x => x)
            .ToArray();

        var anchorParents = eveParents
            .OrderBy(x => x)
            .ToArray();

        if (npcParents.Length >= 2 &&
            anchorParents.Length >= 2 &&
            npcParents.SequenceEqual(anchorParents))
        {
            // Keep siblings reasonably close to Eve while allowing both older
            // and younger siblings. Avoid identical age unless deterministically
            // selected.
            // Most full siblings remain close in age, but 0 is intentionally
            // included so some sibling pairs can be twins. StableInt keeps the
            // result deterministic for the same NPC across rebuilds.
            var offsets = new[] { -5,-4,-3,-2,-1,0,1,2,3,4,5 };
            var offset =
                offsets[
                    StableInt($"family-age|full-sibling|{npcId}") %
                    offsets.Length];

            return Math.Clamp(anchorAge + offset,1,70);
        }

        // Half sibling: exactly one biological parent shared with Eve.
        var sharedParents =
            npcParents.Intersect(anchorParents).Count();

        if (sharedParents == 1)
        {
            var offsets = new[] { -8,-6,-4,-2,2,4,6,8 };
            var offset =
                offsets[
                    StableInt($"family-age|half-sibling|{npcId}") %
                    offsets.Length];

            return Math.Clamp(anchorAge + offset,1,75);
        }

        // Direct biological child of Eve.
        if (npcParents.Contains(anchorNpcId))
        {
            var gap = 18 + StableInt($"family-age|child|{npcId}") % 8;
            return Math.Clamp(anchorAge - gap,1,60);
        }

        // Grandparent / deeper ancestor: use biological generation distance.
        var ancestorDistance =
            BiologicalGenerationDistance(
                npcId,
                anchorNpcId);

        if (ancestorDistance >= 2)
        {
            var perGeneration =
                24 + StableInt($"family-age|ancestor|{npcId}") % 8;

            return Math.Clamp(
                anchorAge + ancestorDistance * perGeneration,
                35,
                110);
        }

        // Descendant beyond a direct child.
        var descendantDistance =
            BiologicalGenerationDistance(
                anchorNpcId,
                npcId);

        if (descendantDistance >= 1)
        {
            var perGeneration =
                20 + StableInt($"family-age|descendant|{npcId}") % 7;

            return Math.Clamp(
                anchorAge - descendantDistance * perGeneration,
                1,
                90);
        }

        // Unrelated spouse / in-law / other family member:
        // use known partner age when structural union data is available later.
        // For now provide a stable adult fallback instead of ever returning 0.
        return 20 + StableInt($"family-age|fallback|{npcId}") % 46;
    }

    private int ReadCanonicalAge(int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT COALESCE(Age,0)
            FROM Characters
            WHERE Id=$id
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue("$id",npcId);

        return Convert.ToInt32(
            cmd.ExecuteScalar() ?? 0);
    }

    private int BiologicalGenerationDistance(
        int possibleAncestorNpcId,
        int descendantNpcId)
    {
        if (possibleAncestorNpcId <= 0 ||
            descendantNpcId <= 0)
        {
            return -1;
        }

        if (possibleAncestorNpcId == descendantNpcId)
            return 0;

        var queue =
            new Queue<(int NpcId,int Distance)>();

        var seen =
            new HashSet<int>();

        queue.Enqueue(
            (descendantNpcId,0));

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();

            if (!seen.Add(item.NpcId))
                continue;

            foreach (var parentId in
                     GetBiologicalParentIds(item.NpcId))
            {
                if (parentId == possibleAncestorNpcId)
                    return item.Distance + 1;

                queue.Enqueue(
                    (parentId,item.Distance + 1));
            }
        }

        return -1;
    }
    private void NormalizeFoundationBirthSurname(
        int npcId,
        CurrentFoundationState current,
        AiNpcProfileProposal proposal,
        List<string> warnings)
    {
        var existingBirth =
            (current.BirthLastName ?? "")
            .Trim();

        // Structural/canonical birth surname always wins over an AI guess.
        if (!string.IsNullOrWhiteSpace(existingBirth))
        {
            var proposed =
                (proposal.BirthLastName ?? "")
                .Trim();

            if (!proposed.Equals(
                    existingBirth,
                    StringComparison.OrdinalIgnoreCase))
            {
                proposal.BirthLastName =
                    existingBirth;

                warnings.Add(
                    $"INFO: AI birth surname '{proposed}' was replaced with canonical family birth surname '{existingBirth}'.");
            }

            return;
        }

        // Full biological siblings of Eve share the same two biological
        // parents, so if the draft shell did not already carry a birth surname,
        // inherit Eve's canonical birth-family surname rather than inventing one.
        const int anchorNpcId = 1;

        var anchorParents =
            GetBiologicalParentIds(anchorNpcId)
                .OrderBy(x => x)
                .ToArray();

        var npcParents =
            GetBiologicalParentIds(npcId)
                .OrderBy(x => x)
                .ToArray();

        if (npcParents.Length < 2 ||
            anchorParents.Length < 2 ||
            !npcParents.SequenceEqual(anchorParents))
        {
            return;
        }

        var anchorBirth =
            ReadCanonicalBirthSurname(anchorNpcId);

        if (string.IsNullOrWhiteSpace(anchorBirth))
            return;

        proposal.BirthLastName =
            anchorBirth;

        warnings.Add(
            $"INFO: Full sibling birth surname was inherited from Eve's canonical birth-family surname '{anchorBirth}'.");
    }

    private string ReadCanonicalBirthSurname(int npcId)
    {
        using var conn = Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COALESCE(BirthLastName,'')
                FROM NpcNameProfiles
                WHERE NpcId=$id
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue("$id",npcId);

            var value =
                Convert.ToString(
                    cmd.ExecuteScalar())
                ?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COALESCE(CurrentLastName,'')
                FROM NpcNameProfiles
                WHERE NpcId=$id
                LIMIT 1;
                """;

            cmd.Parameters.AddWithValue("$id",npcId);

            return Convert.ToString(
                cmd.ExecuteScalar())
                ?.Trim() ?? "";
        }
    }

    private static void NormalizeFoundationAgeDependentContext(
        int originalAge,
        int repairedAge,
        AiNpcProfileProposal proposal,
        List<string> warnings)
    {
        if (originalAge == repairedAge)
            return;

        if (repairedAge >= 18)
        {
            var clothing =
                (proposal.ClothingStyle ?? "")
                .Trim();

            var childOnlyClothing =
                clothing.Contains("school",StringComparison.OrdinalIgnoreCase) ||
                clothing.Contains("play",StringComparison.OrdinalIgnoreCase) ||
                clothing.Contains("child",StringComparison.OrdinalIgnoreCase) ||
                clothing.Contains("kid",StringComparison.OrdinalIgnoreCase) ||
                clothing.Contains("boy",StringComparison.OrdinalIgnoreCase) ||
                clothing.Contains("girl",StringComparison.OrdinalIgnoreCase);

            if (childOnlyClothing)
            {
                proposal.ClothingStyle =
                    "Casual, age-appropriate adult clothing";

                warnings.Add(
                    $"INFO: Child-life clothing context from draft age {originalAge} was replaced after family age repair to {repairedAge}.");
            }

            var bodyType =
                (proposal.BodyType ?? "")
                .Trim();

            if (bodyType.Contains("child",StringComparison.OrdinalIgnoreCase) ||
                bodyType.Contains("kid",StringComparison.OrdinalIgnoreCase) ||
                bodyType.Contains("boy",StringComparison.OrdinalIgnoreCase) ||
                bodyType.Contains("girl",StringComparison.OrdinalIgnoreCase))
            {
                proposal.BodyType =
                    "Average";

                warnings.Add(
                    $"INFO: Child-only body description from draft age {originalAge} was replaced after family age repair to {repairedAge}.");
            }

            var hairStyle =
                (proposal.HairStyle ?? "")
                .Trim();

            if (hairStyle.Contains("child",StringComparison.OrdinalIgnoreCase) ||
                hairStyle.Contains("kid",StringComparison.OrdinalIgnoreCase) ||
                hairStyle.Contains("schoolgirl",StringComparison.OrdinalIgnoreCase) ||
                hairStyle.Contains("schoolboy",StringComparison.OrdinalIgnoreCase))
            {
                proposal.HairStyle =
                    "Natural adult style";

                warnings.Add(
                    $"INFO: Child-only hair context from draft age {originalAge} was replaced after family age repair to {repairedAge}.");
            }
        }
        else
        {
            // A repaired minor must not retain explicitly adult-only broad
            // presentation wording from an older malformed AI draft.
            var clothing =
                (proposal.ClothingStyle ?? "")
                .Trim();

            if (clothing.Contains("club",StringComparison.OrdinalIgnoreCase) ||
                clothing.Contains("nightlife",StringComparison.OrdinalIgnoreCase) ||
                clothing.Contains("cocktail",StringComparison.OrdinalIgnoreCase))
            {
                proposal.ClothingStyle =
                    "Casual, age-appropriate clothing";

                warnings.Add(
                    $"INFO: Adult-only clothing context from draft age {originalAge} was replaced after family age repair to {repairedAge}.");
            }
        }
    }
    private void NormalizeFoundationPhysicalSize(
        int npcId,
        AiNpcProfileProposal proposal,
        List<string> warnings)
    {
        var canonical =
            ReadCanonicalPhysicalState(npcId);

        var age =
            canonical.Age > 0
                ? canonical.Age
                : proposal.Age;

        var gender =
            !string.IsNullOrWhiteSpace(canonical.Gender)
                ? canonical.Gender
                : (proposal.Gender ?? "").Trim();

        // Existing canonical/manual values always win.
        if (canonical.HeightCm > 0)
            proposal.HeightCm = canonical.HeightCm;

        if (canonical.WeightKg > 0)
            proposal.WeightKg = canonical.WeightKg;

        var canonicalHeight = canonical.HeightCm;
        var canonicalWeight = canonical.WeightKg;

        var height =
            Convert.ToDouble(proposal.HeightCm);

        var weight =
            Convert.ToDouble(proposal.WeightKg);
        var minHeight = 0.0;
        var maxHeight = 0.0;

        if (age <= 2)
        {
            minHeight = 65;
            maxHeight = 100;
        }
        else if (age <= 5)
        {
            minHeight = 85;
            maxHeight = 125;
        }
        else if (age <= 9)
        {
            minHeight = 100;
            maxHeight = 150;
        }
        else if (age <= 12)
        {
            minHeight = 125;
            maxHeight = 170;
        }
        else if (age <= 15)
        {
            minHeight = 140;
            maxHeight = 195;
        }
        else
        {
            var male =
                gender.Equals(
                    "Male",
                    StringComparison.OrdinalIgnoreCase) ||
                gender.Equals(
                    "Man",
                    StringComparison.OrdinalIgnoreCase);

            minHeight = male ? 155 : 145;
            maxHeight = male ? 210 : 200;
        }

        if (canonicalHeight <= 0 &&
            (height < minHeight ||
             height > maxHeight))
        {
            proposal.HeightCm =
                GeneratePlausibleHeightCm(
                    npcId,
                    age,
                    gender);

            warnings.RemoveAll(x =>
                x.Contains(
                    "plausible height",
                    StringComparison.OrdinalIgnoreCase));

            warnings.Add(
                $"INFO: Implausible generated height {height:0.#} cm was repaired to {proposal.HeightCm:0.#} cm for age {age}.");
        }

        // Re-read after possible height repair.
        height = Convert.ToDouble(proposal.HeightCm);
        weight = Convert.ToDouble(proposal.WeightKg);

        if (canonicalWeight <= 0 &&
            !IsPlausibleWeightForHeight(
                age,
                height,
                weight))
        {
            proposal.WeightKg =
                GeneratePlausibleWeightKg(
                    npcId,
                    age,
                    height);

            warnings.RemoveAll(x =>
                x.Contains(
                    "plausible weight",
                    StringComparison.OrdinalIgnoreCase));

            warnings.Add(
                $"INFO: Implausible generated weight {weight:0.#} kg was repaired to {proposal.WeightKg:0.#} kg for age {age} and height {height:0.#} cm.");
        }
    }

    private CanonicalPhysicalState ReadCanonicalPhysicalState(
        int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            SELECT
                COALESCE(Age,0),
                COALESCE(Gender,''),
                COALESCE(HeightCm,0),
                COALESCE(WeightKg,0)
            FROM Characters
            WHERE Id=$id
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue("$id",npcId);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return new CanonicalPhysicalState();

        return new CanonicalPhysicalState
        {
            Age = r.GetInt32(0),
            Gender = r.GetString(1),
            HeightCm = Convert.ToDouble(r.GetValue(2)),
            WeightKg = Convert.ToDouble(r.GetValue(3))
        };
    }

    private sealed class CanonicalPhysicalState
    {
        public int Age { get; set; }
        public string Gender { get; set; } = "";
        public double HeightCm { get; set; }
        public double WeightKg { get; set; }
    }
    private static bool IsPlausibleWeightForHeight(
        int age,
        double heightCm,
        double weightKg)
    {
        if (weightKg <= 0 ||
            heightCm <= 0)
        {
            return false;
        }

        if (age >= 16)
        {
            var meters =
                heightCm / 100.0;

            var bmi =
                weightKg /
                (meters * meters);

            // Broad realistic guardrails. This is not a health judgment;
            // it only prevents obviously corrupted AI measurements.
            return bmi >= 15.0 &&
                   bmi <= 42.0;
        }

        return age switch
        {
            <= 2  => weightKg >= 6  && weightKg <= 18,
            <= 5  => weightKg >= 10 && weightKg <= 30,
            <= 9  => weightKg >= 16 && weightKg <= 55,
            <= 12 => weightKg >= 24 && weightKg <= 80,
            <= 15 => weightKg >= 32 && weightKg <= 110,
            _ => false
        };
    }

    private static double GeneratePlausibleHeightCm(
        int npcId,
        int age,
        string gender)
    {
        var seed =
            StableInt(
                $"foundation-height|{npcId}|{age}|{gender}");

        double min;
        double max;

        if (age <= 2)
        {
            min = 72;
            max = 95;
        }
        else if (age <= 5)
        {
            min = 92;
            max = 118;
        }
        else if (age <= 9)
        {
            min = 110;
            max = 142;
        }
        else if (age <= 12)
        {
            min = 135;
            max = 163;
        }
        else if (age <= 15)
        {
            min = 150;
            max = 183;
        }
        else
        {
            var male =
                gender.Equals(
                    "Male",
                    StringComparison.OrdinalIgnoreCase) ||
                gender.Equals(
                    "Man",
                    StringComparison.OrdinalIgnoreCase);

            min = male ? 165 : 155;
            max = male ? 193 : 183;
        }

        var fraction =
            (seed % 1000) / 999.0;

        return Math.Round(
            min + (max - min) * fraction,
            1);
    }

    private static double GeneratePlausibleWeightKg(
        int npcId,
        int age,
        double heightCm)
    {
        if (age >= 16)
        {
            var seed =
                StableInt(
                    $"foundation-weight|{npcId}|{age}|{heightCm:0.0}");

            // Common-population BMI band. Appearance/body-build may later
            // refine presentation, but Foundation must never start corrupted.
            var bmi =
                19.0 +
                (seed % 1000) / 999.0 * 10.0;

            var meters =
                heightCm / 100.0;

            return Math.Round(
                bmi * meters * meters,
                1);
        }

        var ranges =
            age switch
            {
                <= 2  => (8.0,15.0),
                <= 5  => (13.0,24.0),
                <= 9  => (20.0,42.0),
                <= 12 => (30.0,60.0),
                _     => (42.0,82.0)
            };

        var childSeed =
            StableInt(
                $"foundation-child-weight|{npcId}|{age}");

        var fraction =
            (childSeed % 1000) / 999.0;

        return Math.Round(
            ranges.Item1 +
            (ranges.Item2 - ranges.Item1) *
            fraction,
            1);
    }
    private void NormalizeAdultCurrentLife(
        int npcId,
        int age,
        AiNpcProfileProposal proposal,
        List<string> warnings)
    {
        var occupation = (proposal.Occupation ?? "").Trim();
        var employer = (proposal.Employer ?? "").Trim();

        // ------------------------------------------------------------
        // SCHOOL-AGE CHILDREN
        // ------------------------------------------------------------
        if (age < 16)
        {
            if (string.IsNullOrWhiteSpace(occupation))
            {
                proposal.Occupation = "Student";
                proposal.Employer = "N/A";

                warnings.RemoveAll(x =>
                    x.Contains(
                        "Occupation is missing",
                        StringComparison.OrdinalIgnoreCase));

                warnings.Add(
                    $"INFO: Age {age} is below the canonical working-age job pool, so current-life role was set to Student.");
            }

            return;
        }

        // ------------------------------------------------------------
        // TEENS / YOUNG ADULTS WITH NO CURRENT ROLE
        // Use the EXISTING Project Eve jobs master and real workplace list.
        // ------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(occupation))
        {
            var catalogJob =
                ResolveAgeAppropriateCanonicalJob(
                    npcId,
                    age);

            if (catalogJob is not null)
            {
                proposal.Occupation = catalogJob.Value.JobName;
                proposal.Employer =
                    string.IsNullOrWhiteSpace(catalogJob.Value.Employer)
                        ? "N/A"
                        : catalogJob.Value.Employer;

                warnings.RemoveAll(x =>
                    x.Contains(
                        "Occupation is missing",
                        StringComparison.OrdinalIgnoreCase));

                warnings.Add(
                    $"INFO: Blank occupation was filled from the canonical Project Eve job catalog: {proposal.Occupation}.");
            }
            else if (age <= 22)
            {
                // Student is already an explicitly supported employment state
                // in the master employment rules. Never invent a profession.
                proposal.Occupation = "Student";
                proposal.Employer = "N/A";

                warnings.RemoveAll(x =>
                    x.Contains(
                        "Occupation is missing",
                        StringComparison.OrdinalIgnoreCase));

                warnings.Add(
                    $"INFO: No eligible open starter-job definition was selected for age {age}; current-life role was set to Student.");
            }
            else
            {
                proposal.Occupation = "Between Jobs";
                proposal.Employer = "N/A";

                warnings.RemoveAll(x =>
                    x.Contains(
                        "Occupation is missing",
                        StringComparison.OrdinalIgnoreCase));

                warnings.Add(
                    "INFO: No age-appropriate canonical job could be resolved, so current-life role was set to Between Jobs.");
            }

            return;
        }

        // Existing authored/AI occupation remains intact. Normalize only
        // employer semantics for roles that do not require an employer.
        var normalized = occupation.ToLowerInvariant();
        var isMother = IsCanonicalMother(npcId);

        var homeRoles = new[]
        {
            "stay-at-home mom",
            "stay at home mom",
            "stay-at-home mother",
            "stay at home mother",
            "stay-at-home dad",
            "stay at home dad",
            "stay-at-home parent",
            "stay at home parent",
            "homemaker",
            "family caregiver",
            "student",
            "between jobs",
            "unemployed",
            "retired"
        };

        if (homeRoles.Any(x => normalized.Contains(x)))
        {
            if (isMother &&
                (normalized.Contains("homemaker") ||
                 normalized.Contains("stay-at-home") ||
                 normalized.Contains("stay at home")))
            {
                proposal.Occupation = "Stay-at-home Mom";
            }

            proposal.Employer = "N/A";

            warnings.RemoveAll(x =>
                x.Contains(
                    "Occupation is missing",
                    StringComparison.OrdinalIgnoreCase));

            return;
        }

        // Working-age adults with a normal occupation should have a
        // concrete employer/work context unless the role itself clearly
        // represents self-employment.
        if (age >= 18 &&
            age <= 67 &&
            string.IsNullOrWhiteSpace(employer) &&
            !normalized.Contains("self-employed") &&
            !normalized.Contains("freelance") &&
            !normalized.Contains("independent"))
        {
            warnings.Add(
                $"REVIEW: Adult occupation '{proposal.Occupation}' has no employer. " +
                "Foundation should supply a canonical employer/organization or use a valid non-employer role.");
        }
    }

    private static string? FindProjectEveDataFile(
        params string[] relativeParts)
    {
        var starts = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var start in starts)
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            var current =
                Path.GetFullPath(start);

            for (var depth = 0;
                 depth < 8 &&
                 !string.IsNullOrWhiteSpace(current);
                 depth++)
            {
                var candidate =
                    relativeParts.Aggregate(
                        current,
                        Path.Combine);

                if (File.Exists(candidate))
                    return candidate;

                var parent =
                    Directory.GetParent(current);

                if (parent is null)
                    break;

                current =
                    parent.FullName;
            }
        }

        return null;
    }
    private (string JobName,string Employer)? ResolveAgeAppropriateCanonicalJob(
        int npcId,
        int age)
    {
        var jobsPath =
            FindProjectEveDataFile(
                "Characters",
                "JOBS",
                "jobs_master.json")
            ?? FindProjectEveDataFile(
                "DATA",
                "World",
                "Ohio",
                "project_eve_all_jobs_master.json");

        var workplacesPath =
            FindProjectEveDataFile(
                "Characters",
                "JOBS",
                "town_workplaces.json")
            ?? FindProjectEveDataFile(
                "DATA",
                "World",
                "Ohio",
                "project_eve_town_workplaces.json");

        if (string.IsNullOrWhiteSpace(jobsPath) ||
            !File.Exists(jobsPath))
        {
            return null;
        }

        try
        {
            using var jobsDoc =
                System.Text.Json.JsonDocument.Parse(
                    File.ReadAllText(jobsPath));

            if (!jobsDoc.RootElement.TryGetProperty(
                    "jobDefinitions",
                    out var jobsElement) ||
                jobsElement.ValueKind !=
                    System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            var candidates =
                new List<(string Id,string Name,int CareerLevel,int MinAge,int Experience,int Weight)>();

            foreach (var job in jobsElement.EnumerateArray())
            {
                var id =
                    job.TryGetProperty("id",out var idEl)
                        ? idEl.GetString() ?? ""
                        : "";

                var name =
                    job.TryGetProperty("jobName",out var nameEl)
                        ? nameEl.GetString() ?? ""
                        : "";

                var careerLevel =
                    job.TryGetProperty("careerLevel",out var levelEl) &&
                    levelEl.TryGetInt32(out var level)
                        ? level
                        : 99;

                var minAge =
                    job.TryGetProperty("minimumAge",out var ageEl) &&
                    ageEl.TryGetInt32(out var minimumAge)
                        ? minimumAge
                        : 18;

                var weight =
                    job.TryGetProperty("selectionWeight",out var weightEl) &&
                    weightEl.TryGetInt32(out var selectionWeight)
                        ? Math.Max(1,selectionWeight)
                        : 1;

                var experience = 0;

                if (job.TryGetProperty("requirements",out var req) &&
                    req.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    req.TryGetProperty("experienceYears",out var expEl) &&
                    expEl.TryGetInt32(out var exp))
                {
                    experience = exp;
                }

                if (string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(name) ||
                    age < minAge)
                {
                    continue;
                }

                // Age / life-stage gates.
                var eligible =
                    age <= 22
                        ? careerLevel <= 1 && experience <= 0
                        : age <= 29
                            ? careerLevel <= 2 && experience <= 3
                            : age <= 39
                                ? careerLevel <= 3 && experience <= 8
                                : careerLevel <= 4;

                if (!eligible)
                    continue;

                candidates.Add(
                    (id,name,careerLevel,minAge,experience,weight));
            }

            if (candidates.Count == 0)
                return null;

            // Prefer real Bellefontaine workplace positions from the existing
            // registry. Do not invent an employer.
            var workplaceMatches =
                new List<(string JobId,string JobName,string Employer,int Weight)>();

            if (File.Exists(workplacesPath))
            {
                using var workplaceDoc =
                    System.Text.Json.JsonDocument.Parse(
                        File.ReadAllText(workplacesPath));

                if (workplaceDoc.RootElement.TryGetProperty(
                        "workplaces",
                        out var workplaces) &&
                    workplaces.ValueKind ==
                        System.Text.Json.JsonValueKind.Array)
                {
                    var candidateIds =
                        candidates
                            .Select(x => x.Id)
                            .ToHashSet(
                                StringComparer.OrdinalIgnoreCase);

                    foreach (var workplace in workplaces.EnumerateArray())
                    {
                        var employer =
                            workplace.TryGetProperty("name",out var employerEl)
                                ? employerEl.GetString() ?? ""
                                : "";

                        if (!workplace.TryGetProperty(
                                "positions",
                                out var positions) ||
                            positions.ValueKind !=
                                System.Text.Json.JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var position in positions.EnumerateArray())
                        {
                            var jobId =
                                position.TryGetProperty("jobId",out var jobIdEl)
                                    ? jobIdEl.GetString() ?? ""
                                    : "";

                            if (!candidateIds.Contains(jobId))
                                continue;

                            var candidate =
                                candidates.First(x =>
                                    x.Id.Equals(
                                        jobId,
                                        StringComparison.OrdinalIgnoreCase));

                            var slots =
                                position.TryGetProperty("slots",out var slotsEl) &&
                                slotsEl.TryGetInt32(out var slotCount)
                                    ? Math.Max(1,slotCount)
                                    : 1;

                            workplaceMatches.Add(
                                (
                                    candidate.Id,
                                    candidate.Name,
                                    employer,
                                    candidate.Weight * slots
                                ));
                        }
                    }
                }
            }

            if (workplaceMatches.Count > 0)
            {
                var ordered =
                    workplaceMatches
                        .OrderBy(x =>
                            StableInt(
                                $"foundation-job|{npcId}|{age}|{x.JobId}|{x.Employer}"))
                        .ToList();

                var picked =
                    ordered[
                        StableInt(
                            $"foundation-job-pick|{npcId}|{age}") %
                        ordered.Count];

                return (
                    picked.JobName,
                    picked.Employer);
            }

            // Catalog definition exists but no local employer slot could be
            // resolved. Return the canonical job name with no invented employer.
            var fallback =
                candidates
                    .OrderBy(x =>
                        StableInt(
                            $"foundation-job-fallback|{npcId}|{age}|{x.Id}"))
                    .First();

            return (
                fallback.Name,
                "");
        }
        catch
        {
            // Keep Foundation safe, but never invent a profession here.
            // Returning null means caller may choose Student/Between Jobs.
            return null;
        }
    }
    private const int FoundationAncestryAnchorNpcId = 1;

    private string ResolveFoundationAncestry(int npcId)
    {
        var anchorAncestry = ReadSpecificCanonicalAncestry(FoundationAncestryAnchorNpcId);

        if (!IsSpecificAncestry(anchorAncestry))
            return ResolveFallbackAncestry(npcId,new HashSet<int>());

        return ResolveAnchorAwareAncestry(
            npcId,
            FoundationAncestryAnchorNpcId,
            anchorAncestry,
            new HashSet<int>());
    }

    private string ResolveAnchorAwareAncestry(
        int npcId,
        int anchorNpcId,
        string anchorAncestry,
        HashSet<int> visiting)
    {
        if (npcId == anchorNpcId)
            return anchorAncestry;

        if (!visiting.Add(npcId))
            return StableFounderAncestry(npcId);

        try
        {
            if (IsBiologicalAncestorOf(npcId,anchorNpcId))
            {
                var derived = DeriveAncestorAncestryFromAnchor(
                    npcId,
                    anchorNpcId,
                    anchorAncestry);

                if (IsSpecificAncestry(derived))
                    return derived;
            }

            var parentIds = GetBiologicalParentIds(npcId);

            if (parentIds.Count > 0)
            {
                var parentAncestries = parentIds
                    .Select(parentId => ResolveAnchorAwareAncestry(
                        parentId,
                        anchorNpcId,
                        anchorAncestry,
                        visiting))
                    .Where(IsSpecificAncestry)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (parentAncestries.Count >= 2)
                    return CombineParentAncestries(parentAncestries[0],parentAncestries[1]);

                if (parentAncestries.Count == 1)
                    return parentAncestries[0];
            }

            var authored = ReadSpecificCanonicalAncestry(npcId);
            if (IsSpecificAncestry(authored))
                return authored;

            return StableFounderAncestry(npcId);
        }
        finally
        {
            visiting.Remove(npcId);
        }
    }

    private string ResolveFallbackAncestry(int npcId,HashSet<int> visiting)
    {
        if (!visiting.Add(npcId))
            return StableFounderAncestry(npcId);

        try
        {
            var canonical = ReadSpecificCanonicalAncestry(npcId);
            if (IsSpecificAncestry(canonical))
                return canonical;

            var parentAncestries = GetBiologicalParentIds(npcId)
                .Select(parentId => ResolveFallbackAncestry(parentId,visiting))
                .Where(IsSpecificAncestry)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (parentAncestries.Count >= 2)
                return CombineParentAncestries(parentAncestries[0],parentAncestries[1]);

            if (parentAncestries.Count == 1)
                return parentAncestries[0];

            return StableFounderAncestry(npcId);
        }
        finally
        {
            visiting.Remove(npcId);
        }
    }

    private string ReadSpecificCanonicalAncestry(int npcId)
    {
        // Use the exact same canonical load path as the Appearance editor and
        // the media prompt engine. NpcAppearanceDetailService.Load() reads
        // RaceEthnicity from Characters, which is also where Save() writes the
        // user's multi-select ancestry canon.
        var appearance =
            _appearanceDetails.Load(npcId);

        var ancestry =
            (appearance.RaceEthnicity ?? "")
            .Trim();

        return IsSpecificAncestry(ancestry)
            ? ancestry
            : "";
    }
    private string DeriveAncestorAncestryFromAnchor(
        int ancestorNpcId,
        int currentDescendantNpcId,
        string currentDescendantAncestry)
    {
        if (ancestorNpcId == currentDescendantNpcId)
            return currentDescendantAncestry;

        var parents = GetBiologicalParentIds(currentDescendantNpcId)
            .OrderBy(x => x)
            .ToList();

        for (var parentIndex = 0; parentIndex < parents.Count; parentIndex++)
        {
            var parentId = parents[parentIndex];

            if (parentId != ancestorNpcId &&
                !IsBiologicalAncestorOf(ancestorNpcId,parentId))
                continue;

            var parentShare = SelectParentAncestryShare(
                currentDescendantAncestry,
                parentIndex,
                parents.Count);

            if (parentId == ancestorNpcId)
                return parentShare;

            return DeriveAncestorAncestryFromAnchor(
                ancestorNpcId,
                parentId,
                parentShare);
        }

        return "";
    }

    private static string SelectParentAncestryShare(
        string childAncestry,
        int parentIndex,
        int parentCount)
    {
        var parts = ParseAncestryComponents(childAncestry);

        if (parts.Count == 0)
            return "Not Yet Determined";

        if (parentCount <= 1 || parts.Count == 1)
            return BuildAncestryFromComponents(parts);

        var selected = new List<string>();

        for (var i = 0; i < parts.Count; i++)
            if ((i % parentCount) == parentIndex)
                selected.Add(parts[i]);

        if (selected.Count == 0)
            selected.Add(parts[parentIndex % parts.Count]);

        return BuildAncestryFromComponents(selected);
    }

    private bool IsBiologicalAncestorOf(int possibleAncestorNpcId,int descendantNpcId)
    {
        if (possibleAncestorNpcId <= 0 ||
            descendantNpcId <= 0 ||
            possibleAncestorNpcId == descendantNpcId)
            return false;

        var queue = new Queue<int>();
        var seen = new HashSet<int>();
        queue.Enqueue(descendantNpcId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
                continue;

            foreach (var parentId in GetBiologicalParentIds(current))
            {
                if (parentId == possibleAncestorNpcId)
                    return true;

                if (!seen.Contains(parentId))
                    queue.Enqueue(parentId);
            }
        }

        return false;
    }

    private List<int> GetBiologicalParentIds(int npcId)
    {
        var result = new List<int>();

        var mainDir = Path.GetDirectoryName(_options.MainDbPath);
        if (string.IsNullOrWhiteSpace(mainDir))
            return result;

        var relationshipsPath = Path.Combine(mainDir,"project_eve_relationships.db");
        if (!File.Exists(relationshipsPath))
            return result;

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = relationshipsPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT ParentNpcId
            FROM FamilyParentChildLinks
            WHERE ChildNpcId=$id
              AND IsCurrent=1
              AND (
                    lower(trim(COALESCE(ParentKind,'')))='biological'
                    OR lower(trim(COALESCE(ParentKind,'')))='birth'
                  )
            ORDER BY ParentNpcId;
            """;
        cmd.Parameters.AddWithValue("$id",npcId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var parentId = Convert.ToInt32(reader.GetValue(0));
            if (parentId > 0)
                result.Add(parentId);
        }

        return result;
    }

    private static bool IsSpecificAncestry(string? value)
        => ProjectEve.NpcStudio.Models.NpcAncestryCatalog.IsSpecific(value);

    private static string StableFounderAncestry(int npcId)
    {
        var pool = ProjectEve.NpcStudio.Models.NpcAncestryCatalog.FounderOptions;
        if (pool.Count == 0)
            return "Not Yet Determined";

        var seed = StableInt($"ancestry-founder|{npcId}");
        return pool[seed % pool.Count];
    }

    private static string CombineParentAncestries(string parent1,string parent2)
    {
        var parts = new List<string>();
        AddAncestryParts(parts,parent1);
        AddAncestryParts(parts,parent2);
        return BuildAncestryFromComponents(parts);
    }

    private static IReadOnlyList<string> ParseAncestryComponents(string? ancestry)
    {
        var result = new List<string>();
        AddAncestryParts(result,ancestry);

        return result
            .Select(NormalizeAncestryPart)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildAncestryFromComponents(IEnumerable<string> components)
    {
        var unique = components
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeAncestryPart)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unique.Count == 0)
            return "Not Yet Determined";

        if (unique.Count == 1)
            return unique[0];

        return "Mixed - " + string.Join(" + ",unique);
    }

    private static void AddAncestryParts(List<string> destination,string? ancestry)
    {
        var value = (ancestry ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return;

        while (value.StartsWith("Mixed - ",StringComparison.OrdinalIgnoreCase))
            value = value["Mixed - ".Length..].Trim();

        foreach (var part in value.Split(
            " + ",
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries))
        {
            var clean = part.Trim();

            while (clean.StartsWith("Mixed - ",StringComparison.OrdinalIgnoreCase))
                clean = clean["Mixed - ".Length..].Trim();

            if (!string.IsNullOrWhiteSpace(clean))
                destination.Add(clean);
        }
    }

    private static string NormalizeAncestryPart(string value)
    {
        var clean = (value ?? "").Trim();

        if (string.IsNullOrWhiteSpace(clean))
            return "";

        if (clean.Contains(" (",StringComparison.Ordinal) &&
            clean.EndsWith(")",StringComparison.Ordinal))
            return clean;

        var separator = clean.IndexOf(" - ",StringComparison.Ordinal);
        if (separator <= 0)
            return clean;

        var broad = clean[..separator].Trim();
        var specific = clean[(separator + 3)..].Trim();

        if (string.IsNullOrWhiteSpace(specific))
            return broad;

        return $"{broad} ({specific})";
    }
    private static HashSet<string> GetTableColumns(
        SqliteConnection conn,
        string table)
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(1));

        return result;
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

    public Task<NpcFoundationCommitResult> CommitFoundationAsync(
        int npcId,
        CancellationToken cancellationToken = default)
    {
        if (npcId != 6)
            throw new InvalidOperationException(
                "Stage 4B controlled commit is currently locked to NPC 6 only.");

        return CommitFoundationCoreAsync(
            npcId,
            cancellationToken);
    }

    /// <summary>
    /// Canonical Phase 1 entry point used only by the durable family orchestrator.
    /// This intentionally bypasses the standalone NPC-6 test-page lock while
    /// preserving all Foundation validation and missing-only write rules.
    /// </summary>
    public Task<NpcFoundationCommitResult> CommitFoundationForFamilyAsync(
        int npcId,
        CancellationToken cancellationToken = default)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        return CommitFoundationCoreAsync(
            npcId,
            cancellationToken);
    }

    private async Task<NpcFoundationCommitResult> CommitFoundationCoreAsync(
        int npcId,
        CancellationToken cancellationToken)
    {
        var locked = LoadCachedFoundationProfile(npcId);

        if (locked?.Proposal is null)
            throw new InvalidOperationException(
                $"NPC {npcId} has no locked Foundation draft. Build the preview first.");

        var preview = await BuildPreviewAsync(
            npcId,
            cancellationToken);

        if (preview.Profile?.Proposal is null)
            throw new InvalidOperationException(
                "Foundation preview is not valid.");

        var blocking = preview.Warnings
            .Where(x => x.StartsWith(
                "BLOCK:",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (blocking.Count > 0)
            throw new InvalidOperationException(
                "Foundation commit blocked: " +
                string.Join(" | ", blocking));

        var p = preview.Profile.Proposal;
        var age = p.Age;

        var result = new NpcFoundationCommitResult
        {
            NpcId = npcId
        };

        // ----------------------------------------------------
        // 1. Canonical identity + current-life, missing-only.
        //    Do NOT write traits, archetypes, personas, goals,
        //    PersonalityContext, AiSummary or history fields.
        // ----------------------------------------------------
        ApplyFoundationIdentityCurrentLife(
            npcId,
            p);

        result.IdentityApplied = true;

        // ----------------------------------------------------
        // 2. Canonical appearance.
        //    Broad profile remains missing-only.
        //    Detailed appearance comes from the locked preview.
        // ----------------------------------------------------
        ApplyFoundationAppearance(
            npcId,
            p);

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

            finance.Values["AccountId"] =
                preview.Finance.AccountId;
            finance.Values["OwnerType"] = "NPC";
            finance.Values["OwnerId"] = npcId.ToString();
            finance.Values["AccountType"] =
                preview.Finance.AccountType;
            finance.Values["InstitutionName"] =
                "Cullen Federal Bank";
            finance.Values["AccountName"] =
                preview.Finance.AccountName;
            finance.Values["Balance"] =
                preview.Finance.Balance.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            finance.Values["CurrencyCode"] =
                preview.Finance.CurrencyCode;
            finance.Values["IsPrimary"] =
                preview.Finance.IsPrimary ? "1" : "0";
            finance.Values["Status"] =
                preview.Finance.Status;
            finance.Values["Notes"] =
                "Created by Stage 4B Foundation.";

            await _repo.SaveCanonicalFinanceRowAsync(
                finance,
                npcId);

            result.FinanceApplied = true;
            result.BankName = "Cullen Federal Bank";
        }

        MarkFoundationDraftCommitted(npcId);

        result.Success = true;
        result.Message =
            $"NPC {npcId} Foundation committed. Traits, AI Summary, photos and history were not changed.";

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
                    END,                    RaceEthnicity = CASE
                        WHEN trim(COALESCE(RaceEthnicity,''))='' OR
                             lower(trim(COALESCE(RaceEthnicity,''))) IN
                             ('white','black','asian','islander','pacific islander','mixed','multiracial')
                        THEN $race ELSE RaceEthnicity
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
            character.Parameters.AddWithValue("$race", p.RaceEthnicity ?? "N/A");
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
