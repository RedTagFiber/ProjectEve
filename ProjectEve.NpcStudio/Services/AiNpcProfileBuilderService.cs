using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// AI-assisted NPC profile builder.
///
/// PASS 1 IS PREVIEW-ONLY.
/// It reads canon, asks Ollama for structured JSON, validates the proposal,
/// and returns a manifest. It does NOT write NPC data.
/// </summary>
public sealed class AiNpcProfileBuilderService
{
    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;
    private readonly CanonicalFamilyGraphService _familyGraph;

    public AiNpcProfileBuilderService(
        HttpClient http,
        NpcStudioOptions options,
        CanonicalFamilyGraphService familyGraph)
    {
        _http = http;
        _options = options;
        _familyGraph = familyGraph;
    }

    public async Task<AiNpcProfilePreview> BuildPreviewAsync(
        AiNpcProfileBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        var snapshot = LoadSnapshot(request.NpcId);
        var family = _familyGraph.Resolve(request.NpcId);
        var existingTraits = LoadExistingTraits(request.NpcId);

        var prompt = BuildPrompt(snapshot, family, existingTraits, request);
        var raw = await GenerateAsync(prompt, request.BuildTier, cancellationToken);

        var preview = new AiNpcProfilePreview
        {
            NpcId = request.NpcId,
            ExistingName = snapshot.Name,
            BuildTier = Math.Clamp(request.BuildTier, 1, 5),
            SourceModel = _options.OllamaModel,
            RawJson = raw
        };

        AiNpcProfileProposal? proposal = null;

        try
        {
            var json = ExtractJson(raw);
            proposal = JsonSerializer.Deserialize<AiNpcProfileProposal>(
                json,
                CreateProfileJsonOptions());
        }
        catch (Exception ex)
        {
            preview.Warnings.Add("BLOCK: AI output was not valid profile JSON: " + ex.Message);
        }

        if (proposal is null)
            return preview;

        // Existing NPC data wins. AI only fills blanks/defaults.
        MergeExistingCanonIntoProposal(snapshot, proposal);

        Validate(snapshot, family, request, proposal, preview.Warnings);

        return new AiNpcProfilePreview
        {
            NpcId = preview.NpcId,
            ExistingName = preview.ExistingName,
            BuildTier = preview.BuildTier,
            SourceModel = preview.SourceModel,
            RawJson = preview.RawJson,
            Proposal = proposal
        }.CopyWarningsFrom(preview);
    }

    public Task ApplyApprovedPreviewAsync(AiNpcProfilePreview preview)
    {
        if (preview.Proposal is null)
            throw new InvalidOperationException("There is no AI profile proposal to apply.");

        var proposal = preview.Proposal;
        var snapshot = LoadSnapshot(preview.NpcId);
        var family = _familyGraph.Resolve(preview.NpcId);

        var applyWarnings = new List<string>();

        var isFamilyDraft =
            snapshot.Status.Equals("FamilyDraft", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Name.StartsWith("[Family Draft]", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(proposal.FirstName))
            applyWarnings.Add("First name is required.");

        if (string.IsNullOrWhiteSpace(proposal.CurrentLastName))
            applyWarnings.Add("Current surname is required.");

        if (snapshot.Gender.Equals("Female", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(proposal.BirthLastName))
        {
            applyWarnings.Add("Female NPCs must have a birth surname.");
        }

        var familyFirstNames = family.People
            .Where(p => p.NpcId != preview.NpcId)
            .Select(p => FirstToken(p.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (isFamilyDraft &&
            familyFirstNames.Contains((proposal.FirstName ?? "").Trim()))
        {
            applyWarnings.Add(
                $"First name '{proposal.FirstName}' is already used by a family member.");
        }

        if (!isFamilyDraft &&
            !string.IsNullOrWhiteSpace(snapshot.CurrentLastName) &&
            !snapshot.CurrentLastName.Equals(
                proposal.CurrentLastName,
                StringComparison.OrdinalIgnoreCase))
        {
            applyWarnings.Add(
                $"Established current surname '{snapshot.CurrentLastName}' cannot be changed here.");
        }

        if (applyWarnings.Count > 0)
            throw new InvalidOperationException(string.Join(" ", applyWarnings));

        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();

        var characterColumns = GetColumns(conn, "Characters");

        void SetCharacterValue(
            SqliteCommand cmd,
            List<string> sets,
            string column,
            string parameter,
            object? value,
            bool fillMissingOnly = true)
        {
            if (!characterColumns.Contains(column))
                return;

            if (fillMissingOnly)
            {
                sets.Add(
                    $"[{column}] = CASE " +
                    $"WHEN [{column}] IS NULL OR trim(CAST([{column}] AS TEXT)) = '' OR CAST([{column}] AS TEXT) = '0' " +
                    $"THEN {parameter} ELSE [{column}] END");
            }
            else
            {
                sets.Add($"[{column}] = {parameter}");
            }

            cmd.Parameters.AddWithValue(parameter, value ?? "");
        }

        var fullName = string.Join(
            " ",
            new[]
            {
                (proposal.FirstName ?? "").Trim(),
                (proposal.MiddleName ?? "").Trim(),
                (proposal.CurrentLastName ?? "").Trim()
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            var sets = new List<string>();

            SetCharacterValue(cmd, sets, "Name", "$name", fullName, fillMissingOnly: !isFamilyDraft);
            SetCharacterValue(cmd, sets, "DisplayName", "$displayName",
                string.IsNullOrWhiteSpace(proposal.PreferredName) ? fullName : proposal.PreferredName.Trim(),
                fillMissingOnly: !isFamilyDraft);
            SetCharacterValue(cmd, sets, "FirstName", "$firstName", proposal.FirstName.Trim(), fillMissingOnly: !isFamilyDraft);
            SetCharacterValue(cmd, sets, "LastName", "$lastName", proposal.CurrentLastName.Trim(), fillMissingOnly: !isFamilyDraft);
            SetCharacterValue(cmd, sets, "Age", "$age", proposal.Age);
            SetCharacterValue(cmd, sets, "Gender", "$gender",
                string.IsNullOrWhiteSpace(snapshot.Gender) ? proposal.Gender?.Trim() ?? "" : snapshot.Gender);
            SetCharacterValue(cmd, sets, "RaceEthnicity", "$raceEthnicity", proposal.RaceEthnicity?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "HeightCm", "$heightCm", proposal.HeightCm);
            SetCharacterValue(cmd, sets, "WeightKg", "$weightKg", proposal.WeightKg);
            SetCharacterValue(cmd, sets, "IQ", "$iq", proposal.IQ);

            SetCharacterValue(cmd, sets, "Archetype1", "$archetype1", proposal.Archetype1?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "Archetype2", "$archetype2", proposal.Archetype2?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "Archetype3", "$archetype3", proposal.Archetype3?.Trim() ?? "");

            SetCharacterValue(cmd, sets, "PublicPersona", "$publicPersona", proposal.PublicPersona?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "PrivatePersona", "$privatePersona", proposal.PrivatePersona?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "HiddenBehavior", "$hiddenBehavior", proposal.HiddenBehavior?.Trim() ?? "");

            SetCharacterValue(cmd, sets, "Hometown", "$hometown",
                string.IsNullOrWhiteSpace(proposal.Hometown) ? snapshot.Hometown : proposal.Hometown.Trim());
            if (!string.IsNullOrWhiteSpace(proposal.Address))
                SetCharacterValue(cmd, sets, "Address", "$address", proposal.Address.Trim());

            SetCharacterValue(cmd, sets, "Occupation", "$occupation", proposal.Occupation?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "Employer", "$employer", proposal.Employer?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "Goal", "$goal", proposal.Goal?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "Need", "$need", proposal.Need?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "Fear", "$fear", proposal.Fear?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "Want", "$want", proposal.Want?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "PersonalityContext", "$personality", proposal.PersonalitySummary?.Trim() ?? "");
            SetCharacterValue(cmd, sets, "AiSummary", "$aiSummary", proposal.PersonalitySummary?.Trim() ?? "");

            if (isFamilyDraft)
                SetCharacterValue(cmd, sets, "Status", "$status", "Draft", fillMissingOnly: false);

            if (characterColumns.Contains("UpdatedRealAt"))
                sets.Add("[UpdatedRealAt] = CURRENT_TIMESTAMP");

            if (sets.Count == 0)
                throw new InvalidOperationException("Characters table has no compatible profile columns.");

            cmd.CommandText = $"UPDATE Characters SET {string.Join(", ", sets)} WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", preview.NpcId);
            cmd.ExecuteNonQuery();
        }

        if (TableExists(conn, "NpcNameProfiles"))
        {
            using var name = conn.CreateCommand();
            name.Transaction = tx;
            name.CommandText = """
                INSERT INTO NpcNameProfiles
                (
                    NpcId, FirstName, MiddleName, CurrentLastName,
                    BirthLastName, PreferredName, Suffix, UpdatedRealAt
                )
                VALUES
                (
                    $id, $first, $middle, $current,
                    $birth, $preferred, '', CURRENT_TIMESTAMP
                )
                ON CONFLICT(NpcId) DO UPDATE SET
                    FirstName = excluded.FirstName,
                    MiddleName = excluded.MiddleName,
                    CurrentLastName = excluded.CurrentLastName,
                    BirthLastName = excluded.BirthLastName,
                    PreferredName = excluded.PreferredName,
                    UpdatedRealAt = CURRENT_TIMESTAMP;
                """;
            name.Parameters.AddWithValue("$id", preview.NpcId);
            name.Parameters.AddWithValue("$first", proposal.FirstName?.Trim() ?? "");
            name.Parameters.AddWithValue("$middle", proposal.MiddleName?.Trim() ?? "");
            name.Parameters.AddWithValue("$current", proposal.CurrentLastName?.Trim() ?? "");
            name.Parameters.AddWithValue("$birth", proposal.BirthLastName?.Trim() ?? "");
            name.Parameters.AddWithValue("$preferred", proposal.PreferredName?.Trim() ?? "");
            name.ExecuteNonQuery();

            // Keep a married Family Builder couple on one current family surname
            // when the father/current-surname anchor is changed in AI Builder.
            // This updates CURRENT surname only; spouse BirthLastName remains untouched.
            SyncActiveSpouseCurrentSurname(
                conn,
                tx,
                preview.NpcId,
                proposal.Gender,
                proposal.CurrentLastName?.Trim() ?? "");
        }

        if (TableExists(conn, "NpcAppearanceProfiles"))
        {
            using var appearance = conn.CreateCommand();
            appearance.Transaction = tx;
            appearance.CommandText = """
                INSERT INTO NpcAppearanceProfiles
                (
                    NpcId, AppearanceStatus, BodyType, HairColor, HairStyle,
                    EyeColor, SkinTone, ClothingStyle, DistinguishingFeatures,
                    Approved, Notes, UpdatedRealAt
                )
                VALUES
                (
                    $id, 'Draft', $body, $hairColor, $hairStyle,
                    $eyes, $skinTone, $clothing, $features,
                    0, 'AI Profile Builder approved preview', CURRENT_TIMESTAMP
                )
                ON CONFLICT(NpcId) DO UPDATE SET
                    BodyType = CASE WHEN excluded.BodyType <> '' THEN excluded.BodyType ELSE BodyType END,
                    HairColor = CASE WHEN excluded.HairColor <> '' THEN excluded.HairColor ELSE HairColor END,
                    HairStyle = CASE WHEN excluded.HairStyle <> '' THEN excluded.HairStyle ELSE HairStyle END,
                    EyeColor = CASE WHEN excluded.EyeColor <> '' THEN excluded.EyeColor ELSE EyeColor END,
                    SkinTone = CASE WHEN excluded.SkinTone <> '' THEN excluded.SkinTone ELSE SkinTone END,
                    ClothingStyle = CASE WHEN excluded.ClothingStyle <> '' THEN excluded.ClothingStyle ELSE ClothingStyle END,
                    DistinguishingFeatures = CASE WHEN excluded.DistinguishingFeatures <> '' THEN excluded.DistinguishingFeatures ELSE DistinguishingFeatures END,
                    UpdatedRealAt = CURRENT_TIMESTAMP;
                """;
            appearance.Parameters.AddWithValue("$id", preview.NpcId);
            appearance.Parameters.AddWithValue("$body", proposal.BodyType?.Trim() ?? "");
            appearance.Parameters.AddWithValue("$hairColor", proposal.HairColor?.Trim() ?? "");
            appearance.Parameters.AddWithValue("$hairStyle", proposal.HairStyle?.Trim() ?? "");
            appearance.Parameters.AddWithValue("$eyes", proposal.EyeColor?.Trim() ?? "");
            appearance.Parameters.AddWithValue("$skinTone", proposal.SkinTone?.Trim() ?? "");
            appearance.Parameters.AddWithValue("$clothing", proposal.ClothingStyle?.Trim() ?? "");
            appearance.Parameters.AddWithValue("$features", proposal.DistinguishingFeatures?.Trim() ?? "");
            appearance.ExecuteNonQuery();
        }

        if (TableExists(conn, "NpcPhysicalProfiles"))
        {
            var physicalColumns = GetColumns(conn, "NpcPhysicalProfiles");

            using var physical = conn.CreateCommand();
            physical.Transaction = tx;

            var assignments = new List<string>();
            void AddPhysical(string column, string parameter, object value)
            {
                if (!physicalColumns.Contains(column))
                    return;

                assignments.Add($"[{column}] = {parameter}");
                physical.Parameters.AddWithValue(parameter, value);
            }

            // Ensure a row exists first.
            if (physicalColumns.Contains("NpcId"))
            {
                using var ensure = conn.CreateCommand();
                ensure.Transaction = tx;
                ensure.CommandText = "INSERT OR IGNORE INTO NpcPhysicalProfiles (NpcId) VALUES ($id);";
                ensure.Parameters.AddWithValue("$id", preview.NpcId);
                ensure.ExecuteNonQuery();
            }

            AddPhysical("HeightCm", "$phHeight", proposal.HeightCm);
            AddPhysical("WeightKg", "$phWeight", proposal.WeightKg);
            AddPhysical("BodyType", "$phBody", proposal.BodyType?.Trim() ?? "");
            AddPhysical("HairColor", "$phHairColor", proposal.HairColor?.Trim() ?? "");
            AddPhysical("HairStyle", "$phHairStyle", proposal.HairStyle?.Trim() ?? "");
            AddPhysical("EyeColor", "$phEyes", proposal.EyeColor?.Trim() ?? "");
            AddPhysical("SkinTone", "$phSkin", proposal.SkinTone?.Trim() ?? "");
            AddPhysical("DistinguishingFeatures", "$phFeatures", proposal.DistinguishingFeatures?.Trim() ?? "");

            if (assignments.Count > 0 && physicalColumns.Contains("NpcId"))
            {
                physical.CommandText =
                    $"UPDATE NpcPhysicalProfiles SET {string.Join(", ", assignments)} WHERE NpcId = $npcId;";
                physical.Parameters.AddWithValue("$npcId", preview.NpcId);
                physical.ExecuteNonQuery();
            }
        }
        if (TableExists(conn, "NpcTraitValues"))
        {
            var existing = LoadExistingTraits(preview.NpcId)
                .ToDictionary(
                    t => $"{NormalizeGroup(t.Group)}|{t.Name}",
                    t => t,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var trait in proposal.Traits
                         .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                         .Take(80))
            {
                var group = NormalizeGroup(trait.Group);
                if (group is not ("Fast20" or "Mid" or "Slow"))
                    continue;

                var key = $"{group}|{trait.Name.Trim()}";

                if (existing.TryGetValue(key, out var row))
                {
                    // Only untouched Fast20 defaults may be changed.
                    if (group == "Fast20" && row.Value == 50 && trait.Value != 50)
                    {
                        using var update = conn.CreateCommand();
                        update.Transaction = tx;
                        update.CommandText = """
                            UPDATE NpcTraitValues
                            SET StartingValue = $value,
                                CurrentValue = $value,
                                Notes = CASE WHEN IFNULL(Notes,'') = ''
                                    THEN 'AI personalized default Fast20 value' ELSE Notes END
                            WHERE NpcId = $npcId
                              AND lower(TraitName) = lower($name)
                              AND lower(MainGroup) = lower($group);
                            """;
                        update.Parameters.AddWithValue("$value", Math.Clamp(trait.Value, 0, 100));
                        update.Parameters.AddWithValue("$npcId", preview.NpcId);
                        update.Parameters.AddWithValue("$name", trait.Name.Trim());
                        update.Parameters.AddWithValue("$group", group);
                        update.ExecuteNonQuery();
                    }
                    continue;
                }

                // Fast20 is a fixed existing set. Do not invent replacement Fast20 names.
                if (group == "Fast20")
                    continue;

                using var traitCmd = conn.CreateCommand();
                traitCmd.Transaction = tx;
                traitCmd.CommandText = """
                    INSERT INTO NpcTraitValues
                    (
                        Id, NpcId, MainGroup, SubGroup, SubSubGroup,
                        TraitId, TraitName, IsEnabled,
                        StartingValue, CurrentValue, Notes
                    )
                    VALUES
                    (
                        $rowId, $npcId, $group, 'AI Generated', '',
                        $traitId, $name, 1,
                        $value, $value, 'AI Profile Builder â€” fill missing only'
                    );
                    """;
                traitCmd.Parameters.AddWithValue("$rowId", Guid.NewGuid().ToString("N"));
                traitCmd.Parameters.AddWithValue("$npcId", preview.NpcId);
                traitCmd.Parameters.AddWithValue("$group", group);
                traitCmd.Parameters.AddWithValue("$traitId", "ai-" + group.ToLowerInvariant() + "-" + Slug(trait.Name));
                traitCmd.Parameters.AddWithValue("$name", trait.Name.Trim());
                traitCmd.Parameters.AddWithValue("$value", Math.Clamp(trait.Value, 0, 100));
                traitCmd.ExecuteNonQuery();
            }
        }

        FillSharedFamilyHomeAddressIfSafe(conn, tx, preview.NpcId, family);
        if (TableExists(conn, "NpcCreationProvenance"))
        {
            using var provenance = conn.CreateCommand();
            provenance.Transaction = tx;
            provenance.CommandText = """
                UPDATE NpcCreationProvenance
                SET BuildStatus = 'ProfileBuilt',
                    UpdatedRealAt = CURRENT_TIMESTAMP
                WHERE NpcId = $id;
                """;
            provenance.Parameters.AddWithValue("$id", preview.NpcId);
            provenance.ExecuteNonQuery();
        }

        tx.Commit();
        return Task.CompletedTask;
    }
    public async Task<List<AiNpcProfilePreview>> BuildBatchPreviewAsync(
        IEnumerable<AiNpcProfileBuildRequest> requests,
        int maxBatchSize = 10,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AiNpcProfilePreview>();

        // Deliberately serial for local Ollama stability.
        foreach (var request in requests.Take(Math.Clamp(maxBatchSize, 1, 10)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await BuildPreviewAsync(request, cancellationToken));
        }

        return results;
    }

    private string BuildPrompt(
        NpcSnapshot npc,
        CanonicalFamilyGraph family,
        IReadOnlyList<ExistingTrait> existingTraits,
        AiNpcProfileBuildRequest request)
    {
        var tier = Math.Clamp(request.BuildTier, 1, 5);

        var depth = tier switch
        {
            1 => "DEEP: major NPC. Rich but concise current-life profile, strong internal contradictions, 8 traits, 6 interests, 5 habits.",
            2 => "STRONG: recurring NPC. Detailed profile, 7 traits, 5 interests, 4 habits.",
            3 => "MEDIUM: meaningful support NPC. Solid profile, 6 traits, 4 interests, 3 habits.",
            4 => "LIGHT: support NPC. Functional profile, 5 traits, 3 interests, 2 habits.",
            _ => "MINIMAL: background NPC. Plausible compact profile, 4 traits, 2 interests, 1 habit."
        };

        var familyText = family.People.Count == 0
            ? "No canonical family links currently resolved."
            : string.Join(
                "\n",
                family.People.Take(12).Select(
                    p => $"- NPC {p.NpcId}: {p.Name} | role to this NPC: {p.RoleFromRoot}"));

        var usedFamilyFirstNames = family.People
            .Select(p => FirstToken(p.Name))
            .Append(FirstToken(npc.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();

        var usedNamesText = usedFamilyFirstNames.Count == 0
            ? "(none)"
            : string.Join(", ", usedFamilyFirstNames);

        var traitContext = existingTraits.Count == 0
            ? "No existing trait rows."
            : string.Join(
                "\n",
                existingTraits.Select(t => $"- {t.Group}: {t.Name} = {t.Value}"));

        return $$"""
        You are Project Eve's NPC Profile Builder.

        Your job is to fill MISSING profile information for one fictional NPC.
        You are NOT allowed to rewrite canon.

        HARD CANON RULES:
        - Never change the NPC ID.
        - Never change established family links.
        - Every NPC must have a real current surname.
        - Every female NPC must have a real birth surname.
        - If Status is FamilyDraft, the current displayed name/surname is provisional scaffold data,
          not locked canon. Replace it with a distinct plausible identity for this person.
        - Never copy the root NPC's first name just because the FamilyDraft shell contains it.
        - Never invent a second spouse, second biological mother, or second biological father.
        - Never create children, parents, siblings, spouses, or other NPCs in this task.
        - Never overwrite a nonblank canonical surname, occupation, location, age, gender, or established name.
        - Birth surname and current surname are different concepts.
        - A married person's parents do not automatically share the married surname.
        - Do not make relatives clones of one another.
        - Keep age, family generation, job, location, and life stage plausible.
        - This is a COMPLETE PROFILE BUILD, not an idea list.
        - FILL MISSING ONLY. Existing meaningful canon must never be replaced.
        - Read EXISTING CANON first. Every nonblank/nonzero value is LOCKED.
        - Never rename an established Draft/Core NPC.
        - Never change established age, appearance, occupation, archetype, persona, or physical facts.
        - If a field already has a meaningful nonblank/nonzero value, repeat it unchanged.
        - The one exception is an untouched Fast20 value of exactly 50; that is a default and may be personalized.
        - Fill every requested field with a usable value unless canon makes it impossible.
        - Occupation is required when missing. If retired, use a useful status such as Retired Teacher, Retired Business Owner, Homemaker, Student, etc.
        - Do not leave age, height, weight, IQ, archetype, public/private persona, or physical profile blank.
        - Age must make sense relative to canonical parents, children, spouse, grandchildren, and generation.
        - HeightCm and WeightKg must be realistic human values.
        - IQ is a character design value, not a medical diagnosis; keep it plausible.
        - Address may stay blank if there is no canonical home/location record. Never invent a precise street address.
        - Keep every text field concise: usually one short sentence, not a paragraph.
        - Trait names should be 1-3 words.
        - Habits and interests should be short phrases.
        - Output proposal data only. Do not write prose outside JSON.

        BUILD DEPTH:
        Tier {{tier}} — {{depth}}

        EXISTING CANON:
        NPC ID: {{npc.Id}}
        Name: {{npc.Name}}
        Age: {{npc.Age}}
        Gender: {{npc.Gender}}
        Race / Ethnicity: {{npc.RaceEthnicity}}
        Location: {{npc.Location}}
        Hometown: {{npc.Hometown}}
        Occupation: {{npc.Occupation}}
        Status: {{npc.Status}}
        First Name: {{npc.FirstName}}
        Middle Name: {{npc.MiddleName}}
        Current Last Name: {{npc.CurrentLastName}}
        Birth Last Name: {{npc.BirthLastName}}
        Preferred Name: {{npc.PreferredName}}
        HeightCm: {{npc.HeightCm}}
        WeightKg: {{npc.WeightKg}}
        IQ: {{npc.IQ}}
        Archetype1: {{npc.Archetype1}}
        Archetype2: {{npc.Archetype2}}
        Archetype3: {{npc.Archetype3}}
        Public Persona: {{npc.PublicPersona}}
        Private Persona: {{npc.PrivatePersona}}
        Hidden Behavior: {{npc.HiddenBehavior}}
        Body Type: {{npc.BodyType}}
        Hair Color: {{npc.HairColor}}
        Hair Style: {{npc.HairStyle}}
        Eye Color: {{npc.EyeColor}}
        Skin Tone: {{npc.SkinTone}}
        Clothing Style: {{npc.ClothingStyle}}
        Distinguishing Features: {{npc.DistinguishingFeatures}}
        Goal: {{npc.Goal}}
        Need: {{npc.Need}}
        Fear: {{npc.Fear}}
        Want: {{npc.Want}}
        Personality Summary: {{npc.PersonalitySummary}}
        Employer: {{npc.Employer}}

        CANONICAL FAMILY:
        {{familyText}}

        FIRST NAMES ALREADY USED IN THIS FAMILY / DRAFT CONTEXT:
        {{usedNamesText}}

        NAME RULE:
        - For a FamilyDraft NPC, choose a DISTINCT first name that is NOT in the used-name list above.
        - Do not choose Adam, Evelyn, or another relative's existing first name unless that is already this NPC's established non-draft canon.
        - Regeneration should produce a genuinely different plausible identity.

        EXISTING TRAITS:
        {{traitContext}}

        TRAIT BUILD RULES:
        - FAST20: keep existing Fast20 names exactly. Personalize only values still at default 50.
        - FAST20 must not all stay 50; use a believable spread, usually 15-85.
        - MID: ensure at least 10 Mid traits total.
        - SLOW: ensure at least 20 Slow traits total.
        - Never replace an existing non-default trait value.
        - New Mid/Slow names must be distinct and psychologically useful.
        - Return Group as exactly "Fast20", "Mid", or "Slow".

        FILL SWITCHES:
        Identity={{request.FillIdentity}}
        Appearance={{request.FillAppearance}}
        Traits={{request.FillTraits}}
        CurrentLife={{request.FillCurrentLife}}
        EducationCareer={{request.FillEducationCareer}}
        HabitsInterests={{request.FillHabitsInterests}}
        RelationshipContext={{request.FillRelationshipContext}}

        RETURN VALID JSON ONLY WITH THIS EXACT SHAPE:
        {
          "firstName": "",
          "middleName": "",
          "currentLastName": "",
          "birthLastName": "",
          "preferredName": "",
          "age": 0,
          "gender": "",
          "raceEthnicity": "",
          "heightCm": 0,
          "weightKg": 0,
          "iq": 0,
          "archetype1": "",
          "archetype2": "",
          "archetype3": "",
          "publicPersona": "",
          "privatePersona": "",
          "hiddenBehavior": "",
          "hometown": "",
          "address": "",
          "skinTone": "",
          "distinguishingFeatures": "",
          "personalitySummary": "",
          "goal": "",
          "need": "",
          "fear": "",
          "want": "",
          "occupation": "",
          "employer": "",
          "educationSummary": "",
          "bodyType": "",
          "hairColor": "",
          "hairStyle": "",
          "eyeColor": "",
          "clothingStyle": "",
          "interests": [],
          "habits": [],
          "traits": [
            { "group": "Fast20", "name": "", "value": 50 }
          ],
          "relationshipStyle": "",
          "notes": ""
        }

        If a field is already canonical and should not be changed, repeat the canonical value when known.
        Do not use placeholders like TBD, Unknown, Family Draft, or NPC.
        """;
    }

    private void Validate(
        NpcSnapshot snapshot,
        CanonicalFamilyGraph family,
        AiNpcProfileBuildRequest request,
        AiNpcProfileProposal proposal,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Occupation) &&
            string.IsNullOrWhiteSpace(proposal.Occupation))
        {
            warnings.Add("BLOCK: Occupation is missing. AI must fill it before Confirm.");
        }
        if (proposal.Traits.Any(t => t.Value < 0 || t.Value > 100))
            warnings.Add("BLOCK: Trait values must be between 0 and 100.");

        if (proposal.Age <= 0 || proposal.Age > 110)
            warnings.Add("BLOCK: AI must provide a plausible age.");

        if (proposal.HeightCm < 120 || proposal.HeightCm > 225)
            warnings.Add("BLOCK: AI must provide a plausible height in centimeters.");

        if (proposal.WeightKg < 35 || proposal.WeightKg > 250)
            warnings.Add("BLOCK: AI must provide a plausible weight in kilograms.");

        if (proposal.IQ < 60 || proposal.IQ > 160)
            warnings.Add("BLOCK: AI must provide a plausible IQ design value.");

        if (string.IsNullOrWhiteSpace(proposal.Archetype1))
            warnings.Add("BLOCK: Primary archetype is required.");

        if (string.IsNullOrWhiteSpace(proposal.PublicPersona))
            warnings.Add("BLOCK: Public persona is required.");

        if (string.IsNullOrWhiteSpace(proposal.PrivatePersona))
            warnings.Add("BLOCK: Private persona is required.");

        var isFamilyDraft =
            snapshot.Status.Equals("FamilyDraft", StringComparison.OrdinalIgnoreCase) ||
            snapshot.Name.StartsWith("[Family Draft]", StringComparison.OrdinalIgnoreCase);

        if (!isFamilyDraft &&
            !string.IsNullOrWhiteSpace(snapshot.CurrentLastName) &&
            !string.IsNullOrWhiteSpace(proposal.CurrentLastName) &&
            !snapshot.CurrentLastName.Equals(proposal.CurrentLastName, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"BLOCK: AI tried to change canonical current surname '{snapshot.CurrentLastName}' to '{proposal.CurrentLastName}'.");
        }

        if (!isFamilyDraft &&
            !string.IsNullOrWhiteSpace(snapshot.BirthLastName) &&
            !string.IsNullOrWhiteSpace(proposal.BirthLastName) &&
            !snapshot.BirthLastName.Equals(proposal.BirthLastName, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"BLOCK: AI tried to change canonical birth surname '{snapshot.BirthLastName}' to '{proposal.BirthLastName}'.");
        }

        if (string.IsNullOrWhiteSpace(proposal.CurrentLastName))
            warnings.Add("BLOCK: Every NPC must have a current surname.");

        if (snapshot.Gender.Equals("Female", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(proposal.BirthLastName))
        {
            warnings.Add("BLOCK: Female NPCs must have a birth surname.");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Occupation) &&
            !string.IsNullOrWhiteSpace(proposal.Occupation) &&
            !snapshot.Occupation.Equals(proposal.Occupation, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                $"BLOCK: AI tried to change canonical occupation '{snapshot.Occupation}' to '{proposal.Occupation}'.");
        }

        if (proposal.FirstName.Contains("Draft", StringComparison.OrdinalIgnoreCase) ||
            proposal.CurrentLastName.Contains("Draft", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("BLOCK: Draft markers cannot be part of the actual NPC name.");
        }

        var proposalFirst = (proposal.FirstName ?? "").Trim();
        var familyFirstNames = family.People
            .Select(p => FirstToken(p.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (isFamilyDraft &&
            !string.IsNullOrWhiteSpace(proposalFirst) &&
            familyFirstNames.Contains(proposalFirst))
        {
            warnings.Add(
                $"BLOCK: First name '{proposalFirst}' is already used by a family member. Regenerate or edit the name.");
        }

        if (family.People.GroupBy(p => p.NpcId).Any(g => g.Count() > 1))
            warnings.Add("WARN: Canonical family graph contains repeated NPC IDs; review family structure before applying AI data.");
    }

    private NpcSnapshot LoadSnapshot(int npcId)
    {
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        var columns = GetColumns(conn, "Characters");

        string Expr(string column, string fallback) =>
            columns.Contains(column)
                ? $"COALESCE([{column}], {fallback})"
                : fallback;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                {Expr("Id", "0")},
                {Expr("Name", "''")},
                {Expr("Age", "0")},
                {Expr("Gender", "''")},
                {Expr("Location", "''")},
                {Expr("Hometown", "''")},
                {Expr("Occupation", "''")},
                {Expr("Status", "''")}
            FROM Characters
            WHERE Id = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"NPC {npcId} was not found.");

        var snapshot = new NpcSnapshot
        {
            Id = Convert.ToInt32(reader.GetValue(0)),
            Name = reader.GetString(1),
            Age = Convert.ToInt32(reader.GetValue(2)),
            Gender = reader.GetString(3),
            Location = reader.GetString(4),
            Hometown = reader.GetString(5),
            Occupation = reader.GetString(6),
            Status = reader.GetString(7)
        };

        reader.Close();
        snapshot.FirstName = ReadCharacterText(conn, npcId, "FirstName");
        snapshot.PreferredName = ReadCharacterText(conn, npcId, "DisplayName");
        snapshot.HeightCm = ReadCharacterDouble(conn, npcId, "HeightCm");
        snapshot.WeightKg = ReadCharacterDouble(conn, npcId, "WeightKg");
        snapshot.IQ = ReadCharacterInt(conn, npcId, "IQ");
        snapshot.Archetype1 = ReadCharacterText(conn, npcId, "Archetype1");
        snapshot.Archetype2 = ReadCharacterText(conn, npcId, "Archetype2");
        snapshot.Archetype3 = ReadCharacterText(conn, npcId, "Archetype3");
        snapshot.PublicPersona = ReadCharacterText(conn, npcId, "PublicPersona");
        snapshot.PrivatePersona = ReadCharacterText(conn, npcId, "PrivatePersona");
        snapshot.HiddenBehavior = ReadCharacterText(conn, npcId, "HiddenBehavior");
        snapshot.Goal = ReadCharacterText(conn, npcId, "Goal");
        snapshot.Need = ReadCharacterText(conn, npcId, "Need");
        snapshot.Fear = ReadCharacterText(conn, npcId, "Fear");
        snapshot.Want = ReadCharacterText(conn, npcId, "Want");
        snapshot.PersonalitySummary = ReadCharacterText(conn, npcId, "PersonalityContext");
        snapshot.Employer = ReadCharacterText(conn, npcId, "Employer");
        snapshot.RaceEthnicity = ReadCharacterText(conn, npcId, "RaceEthnicity");

        if (TableExists(conn, "NpcNameProfiles"))
        {
            using var nameCmd = conn.CreateCommand();
            nameCmd.CommandText = """
                SELECT COALESCE(CurrentLastName,''), COALESCE(BirthLastName,'')
                FROM NpcNameProfiles
                WHERE NpcId = $id
                LIMIT 1;
                """;
            nameCmd.Parameters.AddWithValue("$id", npcId);

            using var nameReader = nameCmd.ExecuteReader();
            if (nameReader.Read())
            {
                snapshot.CurrentLastName = nameReader.GetString(0);
                snapshot.BirthLastName = nameReader.GetString(1);
            }
        }


        // Pull the rest of the established dossier before asking AI to fill anything.
        if (TableExists(conn, "NpcNameProfiles"))
        {
            using var nameCmd = conn.CreateCommand();
            nameCmd.CommandText = """
                SELECT COALESCE(FirstName,''), COALESCE(MiddleName,''),
                       COALESCE(CurrentLastName,''), COALESCE(BirthLastName,''),
                       COALESCE(PreferredName,'')
                FROM NpcNameProfiles
                WHERE NpcId = $id
                LIMIT 1;
                """;
            nameCmd.Parameters.AddWithValue("$id", npcId);
            using var nr = nameCmd.ExecuteReader();
            if (nr.Read())
            {
                if (!string.IsNullOrWhiteSpace(nr.GetString(0))) snapshot.FirstName = nr.GetString(0);
                snapshot.MiddleName = nr.GetString(1);
                if (!string.IsNullOrWhiteSpace(nr.GetString(2))) snapshot.CurrentLastName = nr.GetString(2);
                snapshot.BirthLastName = nr.GetString(3);
                if (!string.IsNullOrWhiteSpace(nr.GetString(4))) snapshot.PreferredName = nr.GetString(4);
            }
        }

        if (TableExists(conn, "NpcAppearanceProfiles"))
        {
            var ac = GetColumns(conn, "NpcAppearanceProfiles");
            string A(string c) => ac.Contains(c) ? $"COALESCE([{c}], '')" : "''";

            using var ap = conn.CreateCommand();
            ap.CommandText = $"""
                SELECT {A("BodyType")}, {A("HairColor")}, {A("HairStyle")},
                       {A("EyeColor")}, {A("SkinTone")}, {A("ClothingStyle")},
                       {A("DistinguishingFeatures")}
                FROM NpcAppearanceProfiles
                WHERE NpcId = $id
                LIMIT 1;
                """;
            ap.Parameters.AddWithValue("$id", npcId);
            using var ar = ap.ExecuteReader();
            if (ar.Read())
            {
                snapshot.BodyType = ar.GetString(0);
                snapshot.HairColor = ar.GetString(1);
                snapshot.HairStyle = ar.GetString(2);
                snapshot.EyeColor = ar.GetString(3);
                snapshot.SkinTone = ar.GetString(4);
                snapshot.ClothingStyle = ar.GetString(5);
                snapshot.DistinguishingFeatures = ar.GetString(6);
            }
        }

        if (TableExists(conn, "NpcPhysicalProfiles"))
        {
            var pc = GetColumns(conn, "NpcPhysicalProfiles");
            string P(string c, string f) => pc.Contains(c) ? $"COALESCE([{c}], {f})" : f;

            using var ph = conn.CreateCommand();
            ph.CommandText = $"""
                SELECT {P("HeightCm","0")}, {P("WeightKg","0")},
                       {P("BodyType","''")}, {P("HairColor","''")},
                       {P("HairStyle","''")}, {P("EyeColor","''")},
                       {P("SkinTone","''")}, {P("DistinguishingFeatures","''")}
                FROM NpcPhysicalProfiles
                WHERE NpcId = $id
                LIMIT 1;
                """;
            ph.Parameters.AddWithValue("$id", npcId);
            using var pr = ph.ExecuteReader();
            if (pr.Read())
            {
                var h = Convert.ToDouble(pr.GetValue(0));
                var w = Convert.ToDouble(pr.GetValue(1));
                if (h > 0) snapshot.HeightCm = h;
                if (w > 0) snapshot.WeightKg = w;

                if (!string.IsNullOrWhiteSpace(pr.GetString(2))) snapshot.BodyType = pr.GetString(2);
                if (!string.IsNullOrWhiteSpace(pr.GetString(3))) snapshot.HairColor = pr.GetString(3);
                if (!string.IsNullOrWhiteSpace(pr.GetString(4))) snapshot.HairStyle = pr.GetString(4);
                if (!string.IsNullOrWhiteSpace(pr.GetString(5))) snapshot.EyeColor = pr.GetString(5);
                if (!string.IsNullOrWhiteSpace(pr.GetString(6))) snapshot.SkinTone = pr.GetString(6);
                if (!string.IsNullOrWhiteSpace(pr.GetString(7))) snapshot.DistinguishingFeatures = pr.GetString(7);
            }
        }        return snapshot;
    }

    private void MergeExistingCanonIntoProposal(
        NpcSnapshot existing,
        AiNpcProfileProposal proposal)
    {
        var isFamilyDraft =
            existing.Status.Equals("FamilyDraft", StringComparison.OrdinalIgnoreCase) ||
            existing.Name.StartsWith("[Family Draft]", StringComparison.OrdinalIgnoreCase) ||
            (existing.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase) &&
             IsFamilyBuilderDraft(existing.Id));

        string KeepText(string current, string proposed) =>
            !string.IsNullOrWhiteSpace(current) ? current : (proposed ?? "");

        double KeepDouble(double current, double proposed) => current > 0 ? current : proposed;

        int KeepInt(int current, int proposed) => current > 0 ? current : proposed;

        // Identity may only replace the temporary FamilyDraft scaffold.
        if (!isFamilyDraft)
        {
            proposal.FirstName = KeepText(existing.FirstName, proposal.FirstName);
            proposal.MiddleName = KeepText(existing.MiddleName, proposal.MiddleName);
            proposal.CurrentLastName = KeepText(existing.CurrentLastName, proposal.CurrentLastName);
            proposal.BirthLastName = KeepText(existing.BirthLastName, proposal.BirthLastName);
            proposal.PreferredName = KeepText(existing.PreferredName, proposal.PreferredName);
        }
        else
        {
            // Family Builder drafts are still editable identity scaffolds.
            // Fill the identity pieces the structural shell intentionally did not know yet.
            if (string.IsNullOrWhiteSpace(proposal.MiddleName))
                proposal.MiddleName = StableMiddleName(existing.Id, proposal.Gender);

            if (string.IsNullOrWhiteSpace(proposal.PreferredName))
                proposal.PreferredName = proposal.FirstName;

            // A married current surname must not automatically become the birth surname.
            // If AI leaves birth surname blank or simply copies the current surname,
            // generate a stable distinct birth-family surname for the preview.
            if (!string.IsNullOrWhiteSpace(proposal.CurrentLastName) &&
                (string.IsNullOrWhiteSpace(proposal.BirthLastName) ||
                 proposal.BirthLastName.Equals(proposal.CurrentLastName, StringComparison.OrdinalIgnoreCase)))
            {
                proposal.BirthLastName = StableBirthSurname(existing.Id, proposal.CurrentLastName);
            }
        }

        proposal.Age = KeepInt(existing.Age, proposal.Age);
        proposal.Gender = KeepText(existing.Gender, proposal.Gender);
        proposal.RaceEthnicity = KeepText(existing.RaceEthnicity, proposal.RaceEthnicity);
        proposal.HeightCm = KeepDouble(existing.HeightCm, proposal.HeightCm);
        proposal.WeightKg = KeepDouble(existing.WeightKg, proposal.WeightKg);
        proposal.IQ = KeepInt(existing.IQ, proposal.IQ);

        proposal.Archetype1 = KeepText(existing.Archetype1, proposal.Archetype1);
        proposal.Archetype2 = KeepText(existing.Archetype2, proposal.Archetype2);
        proposal.Archetype3 = KeepText(existing.Archetype3, proposal.Archetype3);

        proposal.PublicPersona = KeepText(existing.PublicPersona, proposal.PublicPersona);
        proposal.PrivatePersona = KeepText(existing.PrivatePersona, proposal.PrivatePersona);
        proposal.HiddenBehavior = KeepText(existing.HiddenBehavior, proposal.HiddenBehavior);

        proposal.Occupation = KeepText(existing.Occupation, proposal.Occupation);
        proposal.Employer = KeepText(existing.Employer, proposal.Employer);
        proposal.Goal = KeepText(existing.Goal, proposal.Goal);
        proposal.Need = KeepText(existing.Need, proposal.Need);
        proposal.Fear = KeepText(existing.Fear, proposal.Fear);
        proposal.Want = KeepText(existing.Want, proposal.Want);
        proposal.PersonalitySummary = KeepText(existing.PersonalitySummary, proposal.PersonalitySummary);

        proposal.BodyType = KeepText(existing.BodyType, proposal.BodyType);
        proposal.HairColor = KeepText(existing.HairColor, proposal.HairColor);
        proposal.HairStyle = KeepText(existing.HairStyle, proposal.HairStyle);
        proposal.EyeColor = KeepText(existing.EyeColor, proposal.EyeColor);
        proposal.SkinTone = KeepText(existing.SkinTone, proposal.SkinTone);
        proposal.ClothingStyle = KeepText(existing.ClothingStyle, proposal.ClothingStyle);
        proposal.DistinguishingFeatures =
            KeepText(existing.DistinguishingFeatures, proposal.DistinguishingFeatures);
    }
    private async Task<string> GenerateAsync(string prompt, int buildTier, CancellationToken cancellationToken)
    {

        // Fail fast when Ollama is not reachable instead of leaving the UI spinning.
        using (var healthCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            healthCts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                using var health = await _http.GetAsync(new Uri(new Uri(_options.OllamaBaseUrl), "/api/tags"), healthCts.Token);
                health.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"Ollama did not answer within 5 seconds at {_options.OllamaBaseUrl}. " +
                    "Make sure Ollama is running.");
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Could not reach Ollama at {_options.OllamaBaseUrl}. " +
                    "Make sure Ollama is running. " + ex.Message, ex);
            }
        }

        var tier = Math.Clamp(buildTier, 1, 5);

        var maxOutputTokens = tier switch
        {
            1 => 2400,
            2 => 1800,
            3 => 1400,
            4 => 1000,
            _ => 800
        };

        var generationTimeoutSeconds = tier switch
        {
            1 => 180,
            2 => 150,
            3 => 120,
            _ => 90
        };

        var request = new
        {
            model = _options.OllamaModel,
            prompt,
            stream = false,
            format = "json",
            keep_alive = "30m",
            options = new
            {
                temperature = 0.55,
                num_ctx = 4096,
                num_predict = maxOutputTokens,
                repeat_penalty = 1.05,
                seed = Random.Shared.Next(1, int.MaxValue)
            }
        };

        // Profile generation should never leave the Studio waiting forever.
        using var generationCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        generationCts.CancelAfter(TimeSpan.FromSeconds(generationTimeoutSeconds));

        try
        {
            using var response =
                await _http.PostAsJsonAsync(new Uri(new Uri(_options.OllamaBaseUrl), "/api/generate"), request, generationCts.Token);

            response.EnsureSuccessStatusCode();

            using var stream =
                await response.Content.ReadAsStreamAsync(generationCts.Token);

            using var doc =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: generationCts.Token);

            return doc.RootElement.TryGetProperty("response", out var value)
                ? value.GetString() ?? "{}"
                : "{}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Ollama profile generation exceeded the tier-specific timeout using model '{_options.OllamaModel}'. " +
                "The request was stopped so the Studio does not hang.");
        }
    }

    private static JsonSerializerOptions CreateProfileJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new FlexibleStringListConverter());
        return options;
    }

    /// <summary>
    /// Qwen occasionally returns habits/interests as objects instead of plain
    /// strings, for example:
    /// { "habit": "Drinks tea before bed" }
    /// This converter accepts both shapes so a harmless formatting variation
    /// does not block the whole preview.
    /// </summary>
    private sealed class FlexibleStringListConverter : JsonConverter<List<string>>
    {
        public override List<string> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var result = new List<string>();

            if (reader.TokenType == JsonTokenType.Null)
                return result;

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected an array.");

            using var doc = JsonDocument.ParseValue(ref reader);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                switch (item.ValueKind)
                {
                    case JsonValueKind.String:
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            result.Add(value.Trim());
                        break;
                    }

                    case JsonValueKind.Object:
                    {
                        var value =
                            TryGetString(item, "name") ??
                            TryGetString(item, "habit") ??
                            TryGetString(item, "interest") ??
                            TryGetString(item, "text") ??
                            TryGetString(item, "description") ??
                            TryGetString(item, "value");

                        if (!string.IsNullOrWhiteSpace(value))
                            result.Add(value.Trim());
                        break;
                    }

                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        result.Add(item.ToString());
                        break;
                }
            }

            return result;
        }

        public override void Write(
            Utf8JsonWriter writer,
            List<string> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            foreach (var item in value ?? new List<string>())
                writer.WriteStringValue(item);

            writer.WriteEndArray();
        }

        private static string? TryGetString(JsonElement obj, string propertyName)
        {
            if (!obj.TryGetProperty(propertyName, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }
    }
    private static string FirstToken(string? name)
    {
        var clean = (name ?? "")
            .Replace("[Family Draft]", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        var dash = clean.IndexOf('—');
        if (dash >= 0)
            clean = clean[..dash].Trim();

        return clean
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
    }

    private static string Slug(string? value)
    {
        var chars = (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        return new string(chars).Trim('-');
    }
    private List<ExistingTrait> LoadExistingTraits(int npcId)
    {
        var result = new List<ExistingTrait>();
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();
        if (!TableExists(conn, "NpcTraitValues")) return result;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(MainGroup,''), COALESCE(TraitName,''),
                   COALESCE(CurrentValue, StartingValue, 50)
            FROM NpcTraitValues
            WHERE NpcId = $id AND IFNULL(IsEnabled,1) = 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ExistingTrait
            {
                Group = reader.GetString(0),
                Name = reader.GetString(1),
                Value = Convert.ToInt32(reader.GetValue(2))
            });
        }
        return result;
    }

    private static string NormalizeGroup(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Equals("Fast20", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Fast", StringComparison.OrdinalIgnoreCase)) return "Fast20";
        if (v.Equals("Mid", StringComparison.OrdinalIgnoreCase)) return "Mid";
        if (v.Equals("Slow", StringComparison.OrdinalIgnoreCase)) return "Slow";
        return v;
    }

    private static void FillSharedFamilyHomeAddressIfSafe(
        SqliteConnection conn,
        SqliteTransaction tx,
        int npcId,
        CanonicalFamilyGraph family)
    {
        var columns = GetColumns(conn, "Characters");
        if (!columns.Contains("Address") || !columns.Contains("HomeLocationId"))
            return;

        using var current = conn.CreateCommand();
        current.Transaction = tx;
        current.CommandText = """
            SELECT COALESCE(Address,''), COALESCE(HomeLocationId,'')
            FROM Characters
            WHERE Id = $id
            LIMIT 1;
            """;
        current.Parameters.AddWithValue("$id", npcId);

        string currentAddress = "";
        string homeLocationId = "";

        using (var reader = current.ExecuteReader())
        {
            if (!reader.Read()) return;
            currentAddress = reader.GetString(0);
            homeLocationId = reader.GetString(1);
        }

        // Never replace an address the user already has.
        if (!string.IsNullOrWhiteSpace(currentAddress))
            return;

        // A family relationship does not automatically mean same household.
        // We only inherit from a relative with the exact same HomeLocationId.
        if (string.IsNullOrWhiteSpace(homeLocationId))
            return;

        foreach (var relative in family.People.Where(p => p.NpcId > 0 && p.NpcId != npcId))
        {
            using var find = conn.CreateCommand();
            find.Transaction = tx;
            find.CommandText = """
                SELECT COALESCE(Address,'')
                FROM Characters
                WHERE Id = $relative
                  AND COALESCE(HomeLocationId,'') = $home
                  AND COALESCE(Address,'') <> ''
                LIMIT 1;
                """;
            find.Parameters.AddWithValue("$relative", relative.NpcId);
            find.Parameters.AddWithValue("$home", homeLocationId);

            var address = Convert.ToString(find.ExecuteScalar()) ?? "";
            if (string.IsNullOrWhiteSpace(address))
                continue;

            using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE Characters
                SET Address = $address
                WHERE Id = $id
                  AND COALESCE(Address,'') = '';
                """;
            update.Parameters.AddWithValue("$address", address);
            update.Parameters.AddWithValue("$id", npcId);
            update.ExecuteNonQuery();
            return;
        }
    }
    private sealed class ExistingTrait
    {
        public string Group { get; init; } = "";
        public string Name { get; init; } = "";
        public int Value { get; init; }
    }
    private static string ExtractJson(string raw)
    {
        var text = (raw ?? "").Trim();

        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0) text = text[(firstNewline + 1)..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) text = text[..fence];
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end < start)
            throw new InvalidOperationException("No JSON object was returned.");

        return text[start..(end + 1)];
    }

    private static string ReadCharacterText(SqliteConnection conn, int npcId, string column)
    {
        var columns = GetColumns(conn, "Characters");
        if (!columns.Contains(column)) return "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COALESCE([{column}], '') FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    private static double ReadCharacterDouble(SqliteConnection conn, int npcId, string column)
    {
        var columns = GetColumns(conn, "Characters");
        if (!columns.Contains(column)) return 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COALESCE([{column}], 0) FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
    }

    private static int ReadCharacterInt(SqliteConnection conn, int npcId, string column)
    {
        var columns = GetColumns(conn, "Characters");
        if (!columns.Contains(column)) return 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COALESCE([{column}], 0) FROM Characters WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
    private static HashSet<string> GetColumns(SqliteConnection conn, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(1));

        return result;
    }
    private bool IsFamilyBuilderDraft(int npcId)
    {
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        if (!TableExists(conn, "NpcCreationProvenance"))
            return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(CreationSourceType,''), COALESCE(BuildStatus,'')
            FROM NpcCreationProvenance
            WHERE NpcId = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return false;

        var source = reader.GetString(0);
        var buildStatus = reader.GetString(1);

        return source.Contains("Family", StringComparison.OrdinalIgnoreCase) &&
               !buildStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase) &&
               !buildStatus.Equals("Locked", StringComparison.OrdinalIgnoreCase) &&
               !buildStatus.Equals("Canon", StringComparison.OrdinalIgnoreCase);
    }

    private static string StableMiddleName(int npcId, string? gender)
    {
        string[] neutral =
        [
            "Lee", "James", "Marie", "Grace", "Anne", "Ray", "Michael", "Jane",
            "Louise", "Thomas", "Rose", "Allen", "Jean", "David", "Elaine", "Joseph",
            "Mae", "Robert", "Claire", "Edward", "Renee", "William", "Nicole", "Scott"
        ];

        var seed = unchecked(npcId * 1103515245 + 12345);
        var index = (int)((uint)seed % (uint)neutral.Length);
        return neutral[index];
    }

    private static string StableBirthSurname(int npcId, string currentSurname)
    {
        string[] pool =
        [
            "Stevenson","Bennett","Carter","Hayes","Mercer","Collins","Parker","Foster",
            "Sullivan","Reed","Walsh","Turner","Hughes","Morgan","Dalton","Harris",
            "Brooks","Griffin","Mason","Walker","Porter","Dawson","Snyder","Keller"
        ];

        var start = (int)((uint)unchecked(npcId * 2654435761) % (uint)pool.Length);

        for (var i = 0; i < pool.Length; i++)
        {
            var candidate = pool[(start + i) % pool.Length];
            if (!candidate.Equals(currentSurname, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return "Stevenson";
    }

    private void SyncActiveSpouseCurrentSurname(
        SqliteConnection mainConn,
        SqliteTransaction tx,
        int npcId,
        string? gender,
        string currentSurname)
    {
        if (string.IsNullOrWhiteSpace(currentSurname))
            return;

        // Requested family convention: father/current-family-name change carries
        // to the active spouse. We deliberately do not rewrite either birth surname.
        if (!string.Equals(gender?.Trim(), "Male", StringComparison.OrdinalIgnoreCase))
            return;

        var relPath = _options.GetType().GetProperty("RelationshipsDbPath")?.GetValue(_options) as string
            ?? @"D:\ProjectEveData\Database\project_eve_relationships.db";

        if (!File.Exists(relPath))
            return;

        using var rel = new SqliteConnection($"Data Source={relPath}");
        rel.Open();

        using var spouseCmd = rel.CreateCommand();
        spouseCmd.CommandText = """
            SELECT CASE
                     WHEN Person1NpcId = $id THEN Person2NpcId
                     ELSE Person1NpcId
                   END
            FROM FamilyUnionLinks
            WHERE IsCurrent = 1
              AND lower(COALESCE(UnionType,'')) = 'marriage'
              AND lower(COALESCE(Status,'')) = 'active'
              AND (Person1NpcId = $id OR Person2NpcId = $id)
            ORDER BY Id DESC
            LIMIT 1;
            """;
        spouseCmd.Parameters.AddWithValue("$id", npcId);

        var raw = spouseCmd.ExecuteScalar();
        if (raw is null || raw == DBNull.Value)
            return;

        var spouseId = Convert.ToInt32(raw);
        if (spouseId <= 0)
            return;

        string first = "";
        string middle = "";
        string preferred = "";
        string suffix = "";

        using (var readName = mainConn.CreateCommand())
        {
            readName.Transaction = tx;
            readName.CommandText = """
                SELECT COALESCE(FirstName,''), COALESCE(MiddleName,''),
                       COALESCE(PreferredName,''), COALESCE(Suffix,'')
                FROM NpcNameProfiles
                WHERE NpcId = $id
                LIMIT 1;
                """;
            readName.Parameters.AddWithValue("$id", spouseId);

            using var nr = readName.ExecuteReader();
            if (nr.Read())
            {
                first = nr.GetString(0);
                middle = nr.GetString(1);
                preferred = nr.GetString(2);
                suffix = nr.GetString(3);
            }
        }

        using (var updateName = mainConn.CreateCommand())
        {
            updateName.Transaction = tx;
            updateName.CommandText = """
                UPDATE NpcNameProfiles
                SET CurrentLastName = $surname,
                    UpdatedRealAt = CURRENT_TIMESTAMP
                WHERE NpcId = $id;
                """;
            updateName.Parameters.AddWithValue("$surname", currentSurname);
            updateName.Parameters.AddWithValue("$id", spouseId);
            updateName.ExecuteNonQuery();
        }

        var fullName = string.Join(" ",
            new[] { first, middle, currentSurname, suffix }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()));

        using (var updateCharacter = mainConn.CreateCommand())
        {
            updateCharacter.Transaction = tx;
            updateCharacter.CommandText = """
                UPDATE Characters
                SET LastName = $surname,
                    Name = CASE WHEN $name <> '' THEN $name ELSE Name END,
                    UpdatedRealAt = CURRENT_TIMESTAMP
                WHERE Id = $id;
                """;
            updateCharacter.Parameters.AddWithValue("$surname", currentSurname);
            updateCharacter.Parameters.AddWithValue("$name", fullName);
            updateCharacter.Parameters.AddWithValue("$id", spouseId);
            updateCharacter.ExecuteNonQuery();
        }
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private sealed class NpcSnapshot
    {
        public int Id { get; init; }        public string FirstName { get; set; } = "";
        public string MiddleName { get; set; } = "";
        public string PreferredName { get; set; } = "";
        public double HeightCm { get; set; }
        public double WeightKg { get; set; }
        public int IQ { get; set; }
        public string Archetype1 { get; set; } = "";
        public string Archetype2 { get; set; } = "";
        public string Archetype3 { get; set; } = "";
        public string PublicPersona { get; set; } = "";
        public string PrivatePersona { get; set; } = "";
        public string HiddenBehavior { get; set; } = "";
        public string BodyType { get; set; } = "";
        public string HairColor { get; set; } = "";
        public string HairStyle { get; set; } = "";
        public string EyeColor { get; set; } = "";
        public string SkinTone { get; set; } = "";
        public string ClothingStyle { get; set; } = "";
        public string DistinguishingFeatures { get; set; } = "";
        public string Goal { get; set; } = "";
        public string Need { get; set; } = "";
        public string Fear { get; set; } = "";
        public string Want { get; set; } = "";
        public string PersonalitySummary { get; set; } = "";
        public string Employer { get; set; } = "";
        public string RaceEthnicity { get; set; } = "";
        public string Name { get; init; } = "";
        public int Age { get; init; }
        public string Gender { get; init; } = "";
        public string Location { get; init; } = "";
        public string Hometown { get; init; } = "";
        public string Occupation { get; init; } = "";
        public string Status { get; init; } = "";
        public string CurrentLastName { get; set; } = "";
        public string BirthLastName { get; set; } = "";
    }
}

internal static class AiNpcProfilePreviewExtensions
{
    public static AiNpcProfilePreview CopyWarningsFrom(
        this AiNpcProfilePreview target,
        AiNpcProfilePreview source)
    {
        target.Warnings.AddRange(source.Warnings);
        return target;
    }
}















