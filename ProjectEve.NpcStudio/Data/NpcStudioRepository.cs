using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    private readonly NpcStudioOptions _options;

    public NpcStudioRepository(NpcStudioOptions options)
    {
        _options = options;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + _options.MainDbPath);
        conn.Open();
        return conn;
    }

    private SqliteConnection OpenRelationships()
    {
        var conn = new SqliteConnection("Data Source=" + _options.RelationshipsDbPath);
        conn.Open();
        return conn;
    }

    public Task<NpcStudioDashboard> GetDashboardAsync()
    {
        using var conn = Open();
        var relationshipCounts = GetCanonicalRelationshipCounts();

        var model = new NpcStudioDashboard
        {
            TotalCharacters = ScalarInt(conn, "SELECT COUNT(*) FROM Characters;"),
            CoreCharacters = ScalarInt(conn, "SELECT COUNT(*) FROM Characters WHERE Status = 'Core';"),
            TownCharacters = ScalarInt(conn, "SELECT COUNT(*) FROM Characters WHERE Status = 'Draft';"),
            HistoryCharacters = ScalarInt(conn, "SELECT COUNT(*) FROM Characters WHERE Status = 'HistoryOnly';"),
            RelationshipCount = relationshipCounts.Values.Sum(),
            MissingReferenceImages = ScalarInt(conn, """
                SELECT COUNT(*)
                FROM Characters c
                LEFT JOIN NpcAppearanceProfiles a ON a.NpcId = c.Id
                WHERE c.Status IN ('Core', 'Draft')
                  AND IFNULL(a.Approved, 0) = 0;
                """),
            MissingVoices = ScalarInt(conn, """
                SELECT COUNT(*)
                FROM Characters c
                LEFT JOIN NpcVoiceProfiles v ON v.NpcId = c.Id
                WHERE c.Status IN ('Core', 'Draft')
                  AND IFNULL(v.Approved, 0) = 0;
                """),
            ApprovedImages = ScalarInt(conn, "SELECT COUNT(*) FROM NpcAppearanceProfiles WHERE Approved = 1;"),
            ApprovedVoices = ScalarInt(conn, "SELECT COUNT(*) FROM NpcVoiceProfiles WHERE Approved = 1;")
        };

        model.TopOccupations = CountRows(conn, """
            SELECT IFNULL(Occupation, '(blank)') AS Label, COUNT(*) AS Count
            FROM Characters
            GROUP BY Occupation
            ORDER BY Count DESC
            LIMIT 15;
            """);

        model.TopRelationshipCounts = relationshipCounts
            .OrderByDescending(x => x.Value)
            .Take(15)
            .Select(x => new NpcCountRow
            {
                Label = $"{x.Key} - {GetCharacterName(conn, x.Key)}",
                Count = x.Value
            })
            .ToList();

        return Task.FromResult(model);
    }

    public Task<List<NpcBrowserRow>> SearchNpcsAsync(string search, string status, int? tier)
    {
        using var conn = Open();
        var relationshipCounts = GetCanonicalRelationshipCounts();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        SELECT
            c.Id,
            IFNULL(c.Name, '') AS Name,
            IFNULL(c.Age, 0) AS Age,
            IFNULL(c.Gender, '') AS Gender,
            IFNULL(c.Tier, 5) AS Tier,
            IFNULL(c.Status, '') AS Status,
            IFNULL(c.Occupation, '') AS Occupation,
            IFNULL(c.Location, '') AS Location,
            COALESCE(NULLIF(a.ProfileImagePath, ''), NULLIF(a.ReferenceImagePath, ''), '') AS PortraitPath,
            IFNULL(a.AppearanceStatus, CASE WHEN IFNULL(a.Approved, 0) = 1 THEN 'Approved' ELSE 'Missing' END) AS ImageStatus,
            IFNULL(v.VoiceStatus, CASE WHEN IFNULL(v.Approved, 0) = 1 THEN 'Approved' ELSE 'Missing' END) AS VoiceStatus
        FROM Characters c
        LEFT JOIN NpcAppearanceProfiles a ON a.NpcId = c.Id
        LEFT JOIN NpcVoiceProfiles v ON v.NpcId = c.Id
        WHERE ($search = '' OR c.Name LIKE '%' || $search || '%' OR c.Occupation LIKE '%' || $search || '%')
          AND ($status = '' OR c.Status = $status)
          AND ($tier = 0 OR c.Tier = $tier)
        ORDER BY c.Id
        LIMIT 500;
        """;

        cmd.Parameters.AddWithValue("$search", search ?? "");
        cmd.Parameters.AddWithValue("$status", status ?? "");
        cmd.Parameters.AddWithValue("$tier", tier ?? 0);

        var list = new List<NpcBrowserRow>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var id = ReadInt(reader, "Id");

            list.Add(new NpcBrowserRow
            {
                Id = id,
                Name = ReadString(reader, "Name"),
                Age = ReadInt(reader, "Age"),
                Gender = ReadString(reader, "Gender"),
                Tier = ReadInt(reader, "Tier"),
                Status = ReadString(reader, "Status"),
                Occupation = ReadString(reader, "Occupation"),
                Location = ReadString(reader, "Location"),
                PortraitPath = ReadString(reader, "PortraitPath"),
                ImageStatus = ReadString(reader, "ImageStatus"),
                VoiceStatus = ReadString(reader, "VoiceStatus"),
                RelationshipCount = relationshipCounts.TryGetValue(id, out var count) ? count : 0
            });
        }

        return Task.FromResult(list);
    }

    public Task<NpcCharacterSheet?> GetCharacterSheetAsync(int npcId)
    {
        using var conn = Open();

        var sheet = GetCharacterCore(conn, npcId);
        if (sheet == null)
            return Task.FromResult<NpcCharacterSheet?>(null);

        // Canonical current identity comes from NpcNameProfiles.
        // Characters remains the denormalized/search copy, but must not override
        // an established canonical name profile.
        ApplyCanonicalNameProfile(conn, sheet);

        sheet.Relationships = GetRelationships(conn, npcId);
        sheet.Traits = GetTraits(conn, npcId);
        sheet.Appearance = GetAppearance(conn, npcId);
        sheet.Voice = GetVoice(conn, npcId);
        sheet.Ideas = GetIdeas(conn, npcId);
        sheet.Images = GetImages(conn, npcId);
        sheet.Revisions = GetRevisions(conn, npcId);
        sheet.HistoryEvents = GetHistoryEvents(conn, npcId);
        sheet.CanonicalFoundation = GetCanonicalFoundationSummary(conn, npcId);

        // Bridge existing Project Eve data into the dossier without creating a second truth source.
        // New World Builder tables win when populated; legacy/core tables fill only missing fields.
        HydrateExistingProjectEveData(conn, sheet);

        return Task.FromResult<NpcCharacterSheet?>(sheet);
    }


    public Task SaveCharacterCoreAsync(NpcCharacterSheet sheet)
    {
        using var conn = Open();

        // The manual Core editor exposes Full Name as one field. Normalize it
        // before writing so Characters and NpcNameProfiles cannot drift apart.
        NormalizeEditableIdentity(sheet);

        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        UPDATE Characters
        SET
            Name = $name,
            Nickname = $nickname,
            DirtyName = $dirtyName,
            DarkName = $darkName,
            DisplayName = $displayName,
            FirstName = $firstName,
            LastName = $lastName,
            Age = $age,
            Gender = $gender,
            Occupation = $occupation,
            Location = $location,
            Status = $status,
            Tier = $tier,
            Goal = $goal,
            Need = $need,
            Fear = $fear,
            Want = $want,
            PersonalityContext = $context,
            Hometown = $hometown,
            Address = $address,
            HeightCm = $heightCm,
            WeightKg = $weightKg,
            IQ = $iq,
            Archetype1 = $archetype1,
            Archetype2 = $archetype2,
            Archetype3 = $archetype3,
            PublicPersona = $publicPersona,
            PrivatePersona = $privatePersona,
            HiddenBehavior = $hiddenBehavior,
            AiSummary = $aiSummary,
            StatusNotes = $statusNotes,
            UpdatedRealAt = CURRENT_TIMESTAMP
        WHERE Id = $id;
        """;

        cmd.Parameters.AddWithValue("$id", sheet.Id);
        cmd.Parameters.AddWithValue("$name", sheet.Name ?? "");
        cmd.Parameters.AddWithValue("$nickname", sheet.Nickname ?? "");
        cmd.Parameters.AddWithValue("$dirtyName", sheet.DirtyName ?? "");
        cmd.Parameters.AddWithValue("$darkName", sheet.DarkName ?? "");
        cmd.Parameters.AddWithValue("$displayName", sheet.DisplayName ?? "");
        cmd.Parameters.AddWithValue("$firstName", sheet.FirstName ?? "");
        cmd.Parameters.AddWithValue("$lastName", sheet.LastName ?? "");
        cmd.Parameters.AddWithValue("$age", sheet.Age);
        cmd.Parameters.AddWithValue("$gender", sheet.Gender ?? "");
        cmd.Parameters.AddWithValue("$occupation", sheet.Occupation ?? "");
        cmd.Parameters.AddWithValue("$location", sheet.Location ?? "");
        cmd.Parameters.AddWithValue("$status", sheet.Status ?? "");
        cmd.Parameters.AddWithValue("$tier", sheet.Tier);
        cmd.Parameters.AddWithValue("$goal", sheet.Goal ?? "");
        cmd.Parameters.AddWithValue("$need", sheet.Need ?? "");
        cmd.Parameters.AddWithValue("$fear", sheet.Fear ?? "");
        cmd.Parameters.AddWithValue("$want", sheet.Want ?? "");
        cmd.Parameters.AddWithValue("$context", sheet.PersonalityContext ?? "");
        cmd.Parameters.AddWithValue("$hometown", sheet.Hometown ?? "");
        cmd.Parameters.AddWithValue("$address", sheet.Address ?? "");
        cmd.Parameters.AddWithValue("$heightCm", sheet.HeightCm);
        cmd.Parameters.AddWithValue("$weightKg", sheet.WeightKg);
        cmd.Parameters.AddWithValue("$iq", sheet.IQ);
        cmd.Parameters.AddWithValue("$archetype1", sheet.Archetype1 ?? "");
        cmd.Parameters.AddWithValue("$archetype2", sheet.Archetype2 ?? "");
        cmd.Parameters.AddWithValue("$archetype3", sheet.Archetype3 ?? "");
        cmd.Parameters.AddWithValue("$publicPersona", sheet.PublicPersona ?? "");
        cmd.Parameters.AddWithValue("$privatePersona", sheet.PrivatePersona ?? "");
        cmd.Parameters.AddWithValue("$hiddenBehavior", sheet.HiddenBehavior ?? "");
        cmd.Parameters.AddWithValue("$aiSummary", sheet.AiSummary ?? "");
        cmd.Parameters.AddWithValue("$statusNotes", sheet.StatusNotes ?? "");

        cmd.ExecuteNonQuery();

        // Keep the canonical structured name profile synchronized with every
        // manual Core-editor save. Birth surname is preserved once established.
        SaveCanonicalNameProfile(conn, sheet);

        AddRevision(conn, sheet.Id, "Character Sheet", "Character sheet saved", "NPC Studio saved overview/core character fields and synchronized canonical identity.");

        return Task.CompletedTask;
    }

    private static void ApplyCanonicalNameProfile(SqliteConnection conn, NpcCharacterSheet sheet)
    {
        if (!TableExists(conn, "NpcNameProfiles"))
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(FirstName,''),
                COALESCE(MiddleName,''),
                COALESCE(CurrentLastName,''),
                COALESCE(PreferredName,''),
                COALESCE(Suffix,'')
            FROM NpcNameProfiles
            WHERE NpcId = $id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", sheet.Id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return;

        var first = reader.GetString(0).Trim();
        var middle = reader.GetString(1).Trim();
        var currentSurname = reader.GetString(2).Trim();
        var preferred = reader.GetString(3).Trim();
        var suffix = reader.GetString(4).Trim();

        if (!string.IsNullOrWhiteSpace(first))
            sheet.FirstName = first;

        if (!string.IsNullOrWhiteSpace(currentSurname))
            sheet.LastName = currentSurname;

        var canonicalFullName = JoinName(first, middle, currentSurname, suffix);
        if (!string.IsNullOrWhiteSpace(canonicalFullName))
            sheet.Name = canonicalFullName;

        if (!string.IsNullOrWhiteSpace(preferred))
            sheet.DisplayName = preferred;
        else if (!string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(sheet.DisplayName))
            sheet.DisplayName = first;
    }

    private static void NormalizeEditableIdentity(NpcCharacterSheet sheet)
    {
        var full = (sheet.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(full))
            return;

        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        // Full Name is authoritative in the manual Core editor.
        sheet.FirstName = parts[0];

        if (parts.Length >= 2)
            sheet.LastName = parts[^1];

        if (string.IsNullOrWhiteSpace(sheet.DisplayName))
            sheet.DisplayName = sheet.FirstName;

        sheet.Name = string.Join(" ", parts);
    }

    private static void SaveCanonicalNameProfile(SqliteConnection conn, NpcCharacterSheet sheet)
    {
        if (!TableExists(conn, "NpcNameProfiles"))
            return;

        var full = (sheet.Name ?? "").Trim();
        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var first = !string.IsNullOrWhiteSpace(sheet.FirstName)
            ? sheet.FirstName.Trim()
            : (parts.Length > 0 ? parts[0] : "");

        var currentSurname = !string.IsNullOrWhiteSpace(sheet.LastName)
            ? sheet.LastName.Trim()
            : (parts.Length > 1 ? parts[^1] : "");

        var middle = parts.Length > 2
            ? string.Join(" ", parts.Skip(1).Take(parts.Length - 2))
            : "";

        var preferred = !string.IsNullOrWhiteSpace(sheet.DisplayName)
            ? sheet.DisplayName.Trim()
            : first;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
                MiddleName = CASE
                    WHEN TRIM(excluded.MiddleName) <> '' THEN excluded.MiddleName
                    ELSE NpcNameProfiles.MiddleName
                END,
                CurrentLastName = excluded.CurrentLastName,
                BirthLastName = CASE
                    WHEN TRIM(NpcNameProfiles.BirthLastName) <> '' THEN NpcNameProfiles.BirthLastName
                    ELSE excluded.BirthLastName
                END,
                PreferredName = excluded.PreferredName,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        cmd.Parameters.AddWithValue("$id", sheet.Id);
        cmd.Parameters.AddWithValue("$first", first);
        cmd.Parameters.AddWithValue("$middle", middle);
        cmd.Parameters.AddWithValue("$current", currentSurname);

        // For a brand-new structured-name row only, current surname is the safest
        // initial birth-surname fallback. Once BirthLastName exists, it is preserved.
        cmd.Parameters.AddWithValue("$birth", currentSurname);
        cmd.Parameters.AddWithValue("$preferred", preferred);
        cmd.ExecuteNonQuery();
    }

    private static string JoinName(params string[] parts) =>
        string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
    public Task SaveTraitAsync(NpcTraitRow trait)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(trait.Id))
            trait.Id = Guid.NewGuid().ToString("N");

        cmd.CommandText = """
        INSERT INTO NpcTraitValues
            (Id, NpcId, MainGroup, SubGroup, SubSubGroup, TraitId, TraitName, IsEnabled, StartingValue, CurrentValue, Notes)
        VALUES
            ($id, $npcId, $main, $sub, $subsub, $traitId, $name, $enabled, $start, $current, $notes)
        ON CONFLICT(Id) DO UPDATE SET
            MainGroup = excluded.MainGroup,
            SubGroup = excluded.SubGroup,
            SubSubGroup = excluded.SubSubGroup,
            TraitId = excluded.TraitId,
            TraitName = excluded.TraitName,
            IsEnabled = excluded.IsEnabled,
            StartingValue = excluded.StartingValue,
            CurrentValue = excluded.CurrentValue,
            Notes = excluded.Notes;
        """;
        cmd.Parameters.AddWithValue("$id", trait.Id);
        cmd.Parameters.AddWithValue("$npcId", trait.NpcId);
        cmd.Parameters.AddWithValue("$main", trait.MainGroup ?? "");
        cmd.Parameters.AddWithValue("$sub", trait.SubGroup ?? "");
        cmd.Parameters.AddWithValue("$subsub", trait.SubSubGroup ?? "");
        cmd.Parameters.AddWithValue("$traitId", trait.TraitId ?? "");
        cmd.Parameters.AddWithValue("$name", trait.TraitName ?? "");
        cmd.Parameters.AddWithValue("$enabled", trait.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$start", trait.StartingValue);
        cmd.Parameters.AddWithValue("$current", trait.CurrentValue);
        cmd.Parameters.AddWithValue("$notes", trait.Notes ?? "");
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task AddHistoryEventAsync(NpcHistoryEvent item)
    {
        using var conn = Open();
        if (string.IsNullOrWhiteSpace(item.Id))
            item.Id = Guid.NewGuid().ToString("N");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO NpcHistoryEvents
            (Id, NpcId, EventDate, AgeAtEvent, EventType, Title, Details, Meaning, IsCanon, CreatedRealAt)
        VALUES
            ($id, $npcId, $date, $age, $type, $title, $details, $meaning, $canon, CURRENT_TIMESTAMP);
        """;
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$npcId", item.NpcId);
        cmd.Parameters.AddWithValue("$date", item.EventDate ?? "");
        cmd.Parameters.AddWithValue("$age", item.AgeAtEvent);
        cmd.Parameters.AddWithValue("$type", item.EventType ?? "Life");
        cmd.Parameters.AddWithValue("$title", item.Title ?? "");
        cmd.Parameters.AddWithValue("$details", item.Details ?? "");
        cmd.Parameters.AddWithValue("$meaning", item.Meaning ?? "");
        cmd.Parameters.AddWithValue("$canon", item.IsCanon ? 1 : 0);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task DeleteHistoryEventAsync(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM NpcHistoryEvents WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id ?? "");
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task AddRelationshipAsync(NpcRelationshipDraft draft)
    {
        using var mainConn = Open();

        var targetName = draft.TargetName;
        if (draft.TargetNpcId > 0)
            targetName = GetCharacterName(mainConn, draft.TargetNpcId);

        if (string.IsNullOrWhiteSpace(targetName))
            targetName = "Unknown";

        UpsertCanonicalRelationship(
            draft.SourceNpcId,
            draft.TargetNpcId > 0 ? draft.TargetNpcId : null,
            targetName,
            draft.RelationshipType,
            draft.FamilyRole,
            draft.Affection,
            draft.Trust,
            draft.Respect,
            draft.Loyalty,
            draft.Anger,
            draft.Resentment,
            draft.Fear,
            draft.Jealousy,
            draft.Attraction,
            draft.Tension,
            draft.Importance,
            draft.Notes);

        if (draft.IsMutual && draft.TargetNpcId > 0)
        {
            var sourceName = GetCharacterName(mainConn, draft.SourceNpcId);

            UpsertCanonicalRelationship(
                draft.TargetNpcId,
                draft.SourceNpcId,
                sourceName,
                draft.RelationshipType,
                draft.FamilyRole,
                draft.Affection,
                draft.Trust,
                draft.Respect,
                draft.Loyalty,
                draft.Anger,
                draft.Resentment,
                draft.Fear,
                draft.Jealousy,
                draft.Attraction,
                draft.Tension,
                draft.Importance,
                draft.Notes);
        }

        AddRevision(
            mainConn,
            draft.SourceNpcId,
            "Relationship",
            "Relationship added",
            $"Added {draft.RelationshipType} relationship to {targetName}.");

        return Task.CompletedTask;
    }

    public Task SaveAppearanceAsync(NpcAppearanceProfile profile)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        INSERT INTO NpcAppearanceProfiles
        (
            NpcId, AppearanceStatus, BodyType, HeightText, HairColor, HairStyle, EyeColor, SkinTone,
            ClothingStyle, WorkClothes, CasualClothes, DistinguishingFeatures,
            ImagePrompt, NegativePrompt, ReferenceImagePath, ProfileImagePath, ContactImagePath,
            Approved, Notes, UpdatedRealAt
        )
        VALUES
        (
            $npcId, $status, $body, $height, $hairColor, $hairStyle, $eye, $skin,
            $clothing, $work, $casual, $features,
            $prompt, $negative, $ref, $profile, $contact,
            $approved, $notes, CURRENT_TIMESTAMP
        )
        ON CONFLICT(NpcId) DO UPDATE SET
            AppearanceStatus = $status,
            BodyType = $body,
            HeightText = $height,
            HairColor = $hairColor,
            HairStyle = $hairStyle,
            EyeColor = $eye,
            SkinTone = $skin,
            ClothingStyle = $clothing,
            WorkClothes = $work,
            CasualClothes = $casual,
            DistinguishingFeatures = $features,
            ImagePrompt = $prompt,
            NegativePrompt = $negative,
            ReferenceImagePath = $ref,
            ProfileImagePath = $profile,
            ContactImagePath = $contact,
            Approved = $approved,
            Notes = $notes,
            UpdatedRealAt = CURRENT_TIMESTAMP;
        """;

        cmd.Parameters.AddWithValue("$npcId", profile.NpcId);
        cmd.Parameters.AddWithValue("$status", profile.AppearanceStatus ?? "");
        cmd.Parameters.AddWithValue("$body", profile.BodyType ?? "");
        cmd.Parameters.AddWithValue("$height", profile.HeightText ?? "");
        cmd.Parameters.AddWithValue("$hairColor", profile.HairColor ?? "");
        cmd.Parameters.AddWithValue("$hairStyle", profile.HairStyle ?? "");
        cmd.Parameters.AddWithValue("$eye", profile.EyeColor ?? "");
        cmd.Parameters.AddWithValue("$skin", profile.SkinTone ?? "");
        cmd.Parameters.AddWithValue("$clothing", profile.ClothingStyle ?? "");
        cmd.Parameters.AddWithValue("$work", profile.WorkClothes ?? "");
        cmd.Parameters.AddWithValue("$casual", profile.CasualClothes ?? "");
        cmd.Parameters.AddWithValue("$features", profile.DistinguishingFeatures ?? "");
        cmd.Parameters.AddWithValue("$prompt", profile.ImagePrompt ?? "");
        cmd.Parameters.AddWithValue("$negative", profile.NegativePrompt ?? "");
        cmd.Parameters.AddWithValue("$ref", profile.ReferenceImagePath ?? "");
        cmd.Parameters.AddWithValue("$profile", profile.ProfileImagePath ?? "");
        cmd.Parameters.AddWithValue("$contact", profile.ContactImagePath ?? "");
        cmd.Parameters.AddWithValue("$approved", profile.Approved ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", profile.Notes ?? "");

        cmd.ExecuteNonQuery();
        AddRevision(conn, profile.NpcId, "Appearance", "Appearance profile saved", "NPC Studio V0.1 appearance profile saved.");

        return Task.CompletedTask;
    }

    public Task SaveVoiceAsync(NpcVoiceProfile voice)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        INSERT INTO NpcVoiceProfiles
        (
            NpcId, VoiceStatus, VoiceProvider, VoiceId, VoiceName, VoiceStyle, Accent,
            AgeTone, Energy, Warmth, Roughness, Pace, Pitch, ReferenceAudioPath,
            SampleText, Approved, Notes, UpdatedRealAt
        )
        VALUES
        (
            $npcId, $status, $provider, $voiceId, $voiceName, $style, $accent,
            $ageTone, $energy, $warmth, $roughness, $pace, $pitch, $refAudio,
            $sample, $approved, $notes, CURRENT_TIMESTAMP
        )
        ON CONFLICT(NpcId) DO UPDATE SET
            VoiceStatus = $status,
            VoiceProvider = $provider,
            VoiceId = $voiceId,
            VoiceName = $voiceName,
            VoiceStyle = $style,
            Accent = $accent,
            AgeTone = $ageTone,
            Energy = $energy,
            Warmth = $warmth,
            Roughness = $roughness,
            Pace = $pace,
            Pitch = $pitch,
            ReferenceAudioPath = $refAudio,
            SampleText = $sample,
            Approved = $approved,
            Notes = $notes,
            UpdatedRealAt = CURRENT_TIMESTAMP;
        """;

        cmd.Parameters.AddWithValue("$npcId", voice.NpcId);
        cmd.Parameters.AddWithValue("$status", voice.VoiceStatus ?? "");
        cmd.Parameters.AddWithValue("$provider", voice.VoiceProvider ?? "");
        cmd.Parameters.AddWithValue("$voiceId", voice.VoiceId ?? "");
        cmd.Parameters.AddWithValue("$voiceName", voice.VoiceName ?? "");
        cmd.Parameters.AddWithValue("$style", voice.VoiceStyle ?? "");
        cmd.Parameters.AddWithValue("$accent", voice.Accent ?? "");
        cmd.Parameters.AddWithValue("$ageTone", voice.AgeTone ?? "");
        cmd.Parameters.AddWithValue("$energy", voice.Energy ?? "");
        cmd.Parameters.AddWithValue("$warmth", voice.Warmth ?? "");
        cmd.Parameters.AddWithValue("$roughness", voice.Roughness ?? "");
        cmd.Parameters.AddWithValue("$pace", voice.Pace ?? "");
        cmd.Parameters.AddWithValue("$pitch", voice.Pitch ?? "");
        cmd.Parameters.AddWithValue("$refAudio", voice.ReferenceAudioPath ?? "");
        cmd.Parameters.AddWithValue("$sample", voice.SampleText ?? "");
        cmd.Parameters.AddWithValue("$approved", voice.Approved ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", voice.Notes ?? "");

        cmd.ExecuteNonQuery();
        AddRevision(conn, voice.NpcId, "Voice", "Voice profile saved", "NPC Studio V0.1 voice profile saved.");

        return Task.CompletedTask;
    }

    public Task AddIdeaAsync(NpcStudioIdea idea)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        INSERT INTO NpcStudioIdeas
        (
            Id, NpcId, IdeaType, SourceModel, InputSummary, IdeaText,
            Approved, Rejected, AppliedToCharacter, Notes, CreatedRealAt
        )
        VALUES
        (
            $id, $npcId, $type, $model, $summary, $text,
            $approved, $rejected, $applied, $notes, CURRENT_TIMESTAMP
        );
        """;

        cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(idea.Id) ? Guid.NewGuid().ToString("N") : idea.Id);
        cmd.Parameters.AddWithValue("$npcId", idea.NpcId);
        cmd.Parameters.AddWithValue("$type", idea.IdeaType ?? "");
        cmd.Parameters.AddWithValue("$model", idea.SourceModel ?? "");
        cmd.Parameters.AddWithValue("$summary", idea.InputSummary ?? "");
        cmd.Parameters.AddWithValue("$text", idea.IdeaText ?? "");
        cmd.Parameters.AddWithValue("$approved", idea.Approved ? 1 : 0);
        cmd.Parameters.AddWithValue("$rejected", idea.Rejected ? 1 : 0);
        cmd.Parameters.AddWithValue("$applied", idea.AppliedToCharacter ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", idea.Notes ?? "");

        cmd.ExecuteNonQuery();
        AddRevision(conn, idea.NpcId, "AI Idea", "Prompt Engineer idea saved", $"IdeaType={idea.IdeaType}. SourceModel={idea.SourceModel}.");

        return Task.CompletedTask;
    }


    public Task SavePromptGenerationAsync(NpcPromptGeneration prompt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        var id = string.IsNullOrWhiteSpace(prompt.Id) ? Guid.NewGuid().ToString("N") : prompt.Id;

        cmd.CommandText = """
        INSERT INTO NpcPromptGenerations
        (
            Id, NpcId, PromptType, SourceModel, InputJson, OutputText,
            PositivePrompt, NegativePrompt, Approved, UsedForGeneration, Notes, CreatedRealAt
        )
        VALUES
        (
            $id, $npcId, $promptType, $sourceModel, $inputJson, $outputText,
            $positive, $negative, $approved, $used, $notes, CURRENT_TIMESTAMP
        );
        """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$npcId", prompt.NpcId);
        cmd.Parameters.AddWithValue("$promptType", prompt.PromptType ?? "");
        cmd.Parameters.AddWithValue("$sourceModel", prompt.SourceModel ?? "");
        cmd.Parameters.AddWithValue("$inputJson", prompt.InputJson ?? "");
        cmd.Parameters.AddWithValue("$outputText", prompt.OutputText ?? "");
        cmd.Parameters.AddWithValue("$positive", prompt.PositivePrompt ?? "");
        cmd.Parameters.AddWithValue("$negative", prompt.NegativePrompt ?? "");
        cmd.Parameters.AddWithValue("$approved", prompt.Approved ? 1 : 0);
        cmd.Parameters.AddWithValue("$used", prompt.UsedForGeneration ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", prompt.Notes ?? "");

        cmd.ExecuteNonQuery();
        AddRevision(conn, prompt.NpcId, "Prompt", "Prompt generation saved", $"PromptType={prompt.PromptType}. SourceModel={prompt.SourceModel}.");

        return Task.CompletedTask;
    }

    public Task SaveAppearancePromptAsync(int npcId, string positivePrompt, string negativePrompt)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        INSERT INTO NpcAppearanceProfiles
        (
            NpcId, AppearanceStatus, ImagePrompt, NegativePrompt, Approved, Notes, UpdatedRealAt
        )
        VALUES
        (
            $npcId, 'Prompt Ready', $positive, $negative, 0, 'Prompt saved from Prompt Engineer Lab.', CURRENT_TIMESTAMP
        )
        ON CONFLICT(NpcId) DO UPDATE SET
            AppearanceStatus = 'Prompt Ready',
            ImagePrompt = $positive,
            NegativePrompt = $negative,
            UpdatedRealAt = CURRENT_TIMESTAMP;
        """;

        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$positive", positivePrompt ?? "");
        cmd.Parameters.AddWithValue("$negative", negativePrompt ?? "");

        cmd.ExecuteNonQuery();
        AddRevision(conn, npcId, "Appearance Prompt", "Comfy prompt saved", "Positive/negative prompt saved to appearance profile from Prompt Engineer Lab.");

        return Task.CompletedTask;
    }

    public Task AddImageGenerationAsync(NpcImageGeneration image)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        var id = string.IsNullOrWhiteSpace(image.Id) ? Guid.NewGuid().ToString("N") : image.Id;

        cmd.CommandText = """
        INSERT INTO NpcImageGenerations
        (
            Id, NpcId, ImageType, PromptGenerationId, PositivePrompt, NegativePrompt,
            Seed, WorkflowName, Checkpoint, Width, Height, Steps, Cfg, Sampler,
            ImagePath, IsCurrent, Approved, Notes, CreatedRealAt
        )
        VALUES
        (
            $id, $npcId, $imageType, $promptId, $positive, $negative,
            $seed, $workflow, $checkpoint, $width, $height, $steps, $cfg, $sampler,
            $path, $current, $approved, $notes, CURRENT_TIMESTAMP
        );
        """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$npcId", image.NpcId);
        cmd.Parameters.AddWithValue("$imageType", image.ImageType ?? "");
        cmd.Parameters.AddWithValue("$promptId", image.PromptGenerationId ?? "");
        cmd.Parameters.AddWithValue("$positive", image.PositivePrompt ?? "");
        cmd.Parameters.AddWithValue("$negative", image.NegativePrompt ?? "");
        cmd.Parameters.AddWithValue("$seed", image.Seed ?? "");
        cmd.Parameters.AddWithValue("$workflow", image.WorkflowName ?? "");
        cmd.Parameters.AddWithValue("$checkpoint", image.Checkpoint ?? "");
        cmd.Parameters.AddWithValue("$width", image.Width);
        cmd.Parameters.AddWithValue("$height", image.Height);
        cmd.Parameters.AddWithValue("$steps", image.Steps);
        cmd.Parameters.AddWithValue("$cfg", image.Cfg);
        cmd.Parameters.AddWithValue("$sampler", image.Sampler ?? "");
        cmd.Parameters.AddWithValue("$path", image.ImagePath ?? "");
        cmd.Parameters.AddWithValue("$current", image.IsCurrent ? 1 : 0);
        cmd.Parameters.AddWithValue("$approved", image.Approved ? 1 : 0);
        cmd.Parameters.AddWithValue("$notes", image.Notes ?? "");

        cmd.ExecuteNonQuery();
        AddRevision(conn, image.NpcId, "Comfy Image", "Image generation queued/saved", $"ImageType={image.ImageType}. Workflow={image.WorkflowName}. Seed={image.Seed}.");

        return Task.CompletedTask;
    }



    public Task ApproveImageGenerationAsync(string imageId, int npcId, string imagePath, bool setAsCurrentReference)
    {
        using var conn = Open();

        // If this image becomes current, unset older current images for this NPC first.
        if (setAsCurrentReference)
        {
            using var clear = conn.CreateCommand();
            clear.CommandText = """
            UPDATE NpcImageGenerations
            SET IsCurrent = 0
            WHERE NpcId = $npcId;
            """;
            clear.Parameters.AddWithValue("$npcId", npcId);
            clear.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
            UPDATE NpcImageGenerations
            SET
                ImagePath = $path,
                Approved = 1,
                IsCurrent = CASE WHEN $current = 1 THEN 1 ELSE IsCurrent END,
                Notes = CASE
                    WHEN IFNULL(Notes, '') = '' THEN 'Approved in NPC Studio Image Approval Room.'
                    ELSE Notes || char(10) || 'Approved in NPC Studio Image Approval Room.'
                END
            WHERE Id = $id
              AND NpcId = $npcId;
            """;
            cmd.Parameters.AddWithValue("$id", imageId ?? "");
            cmd.Parameters.AddWithValue("$npcId", npcId);
            cmd.Parameters.AddWithValue("$path", imagePath ?? "");
            cmd.Parameters.AddWithValue("$current", setAsCurrentReference ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        if (setAsCurrentReference)
        {
            using var app = conn.CreateCommand();
            app.CommandText = """
            INSERT INTO NpcAppearanceProfiles
            (
                NpcId, AppearanceStatus, ReferenceImagePath, Approved, Notes, UpdatedRealAt
            )
            VALUES
            (
                $npcId, 'Approved', $path, 1, 'Reference image approved from image generation history.', CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                AppearanceStatus = 'Approved',
                ReferenceImagePath = $path,
                Approved = 1,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
            app.Parameters.AddWithValue("$npcId", npcId);
            app.Parameters.AddWithValue("$path", imagePath ?? "");
            app.ExecuteNonQuery();
        }

        AddRevision(conn, npcId, "Image Approval", "Reference image approved", $"ImageId={imageId}. CurrentReference={setAsCurrentReference}. Path={imagePath}");

        return Task.CompletedTask;
    }

    public Task MarkVoiceApprovedAsync(int npcId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        INSERT INTO NpcVoiceProfiles
        (
            NpcId, VoiceStatus, Approved, Notes, UpdatedRealAt
        )
        VALUES
        (
            $npcId, 'Approved', 1, 'Voice approved in NPC Studio.', CURRENT_TIMESTAMP
        )
        ON CONFLICT(NpcId) DO UPDATE SET
            VoiceStatus = 'Approved',
            Approved = 1,
            UpdatedRealAt = CURRENT_TIMESTAMP;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.ExecuteNonQuery();

        AddRevision(conn, npcId, "Voice Approval", "Voice approved", "Voice profile marked approved in NPC Studio Phase 8.");

        return Task.CompletedTask;
    }

    private static NpcCharacterSheet? GetCharacterCore(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT *
        FROM Characters
        WHERE Id = $id;
        """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new NpcCharacterSheet
        {
            Id = ReadInt(reader, "Id"),
            NpcKey = ReadString(reader, "NpcKey"),
            FolderName = ReadString(reader, "FolderName"),
            FolderPath = ReadString(reader, "FolderPath"),
            Name = ReadString(reader, "Name"),
            Nickname = ReadString(reader, "Nickname"),
            DirtyName = ReadString(reader, "DirtyName"),
            DarkName = ReadString(reader, "DarkName"),
            DisplayName = ReadString(reader, "DisplayName"),
            FirstName = ReadString(reader, "FirstName"),
            LastName = ReadString(reader, "LastName"),
            Age = ReadInt(reader, "Age"),
            Gender = ReadString(reader, "Gender"),
            Tier = ReadInt(reader, "Tier"),
            Status = ReadString(reader, "Status"),
            Occupation = ReadString(reader, "Occupation"),
            Employer = ReadString(reader, "Employer"),
            Location = ReadString(reader, "Location"),
            CurrentLocationId = ReadString(reader, "CurrentLocationId"),
            HomeLocationId = ReadString(reader, "HomeLocationId"),
            WorkLocationId = ReadString(reader, "WorkLocationId"),
            Hometown = ReadString(reader, "Hometown"),
            Address = ReadString(reader, "Address"),
            Goal = ReadString(reader, "Goal"),
            Need = ReadString(reader, "Need"),
            Fear = ReadString(reader, "Fear"),
            Want = ReadString(reader, "Want"),
            PersonalityContext = ReadString(reader, "PersonalityContext"),
            HeightCm = ReadDouble(reader, "HeightCm"),
            WeightKg = ReadDouble(reader, "WeightKg"),
            IQ = ReadInt(reader, "IQ"),
            Archetype1 = ReadString(reader, "Archetype1"),
            Archetype2 = ReadString(reader, "Archetype2"),
            Archetype3 = ReadString(reader, "Archetype3"),
            PublicPersona = ReadString(reader, "PublicPersona"),
            PrivatePersona = ReadString(reader, "PrivatePersona"),
            HiddenBehavior = ReadString(reader, "HiddenBehavior"),
            AiSummary = ReadString(reader, "AiSummary"),
            StatusNotes = ReadString(reader, "StatusNotes")
        };
    }

    private static NpcCanonicalFoundationSummary GetCanonicalFoundationSummary(
        SqliteConnection conn,
        int npcId)
    {
        return new NpcCanonicalFoundationSummary
        {
            EducationRecords = CanonicalCount(
                conn,
                "NpcEducationRecords",
                "NpcId",
                npcId),

            ProfessionalProfiles = CanonicalCount(
                conn,
                "NpcProfessionalProfiles",
                "NpcId",
                npcId),

            Qualifications = CanonicalCount(
                conn,
                "NpcProfessionalQualifications",
                "NpcId",
                npcId),

            ProfessionalCompetencies = CanonicalCount(
                conn,
                "NpcProfessionalCompetencies",
                "NpcId",
                npcId),

            Phones = CanonicalCount(
                conn,
                "NpcPhones",
                "NpcId",
                npcId),

            VehiclesOwnedOrDriven = CanonicalVehicleCount(conn, npcId),

            FinancialAccounts = CanonicalCount(
                conn,
                "FinancialAccounts",
                "OwnerId",
                npcId,
                "OwnerType = 'NPC'"),

            FinancialObligations = CanonicalCount(
                conn,
                "FinancialObligations",
                "OwnerNpcId",
                npcId)
        };
    }

    private static int CanonicalCount(
        SqliteConnection conn,
        string tableName,
        string ownerColumn,
        int npcId,
        string? extraWhere = null)
    {
        if (!CanonicalTableExists(conn, tableName))
            return 0;

        using var cmd = conn.CreateCommand();

        var where = $"{ownerColumn} = $npcId";
        if (!string.IsNullOrWhiteSpace(extraWhere))
            where += " AND " + extraWhere;

        cmd.CommandText =
            $"SELECT COUNT(*) FROM {tableName} WHERE {where};";

        cmd.Parameters.AddWithValue("$npcId", npcId);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static int CanonicalVehicleCount(
        SqliteConnection conn,
        int npcId)
    {
        if (!CanonicalTableExists(conn, "Vehicles"))
            return 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM Vehicles
            WHERE RegisteredOwnerNpcId = $npcId
               OR PrimaryDriverNpcId = $npcId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static bool CanonicalTableExists(
        SqliteConnection conn,
        string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND lower(name) = lower($tableName);
            """;
        cmd.Parameters.AddWithValue("$tableName", tableName);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static List<NpcHistoryEvent> GetHistoryEvents(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT * FROM NpcHistoryEvents
        WHERE NpcId = $npcId
        ORDER BY CASE WHEN EventDate = '' THEN 1 ELSE 0 END, EventDate, AgeAtEvent, CreatedRealAt;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        var list = new List<NpcHistoryEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new NpcHistoryEvent
            {
                Id = ReadString(reader, "Id"),
                NpcId = ReadInt(reader, "NpcId"),
                EventDate = ReadString(reader, "EventDate"),
                AgeAtEvent = ReadInt(reader, "AgeAtEvent"),
                EventType = ReadString(reader, "EventType"),
                Title = ReadString(reader, "Title"),
                Details = ReadString(reader, "Details"),
                Meaning = ReadString(reader, "Meaning"),
                IsCanon = ReadBool(reader, "IsCanon"),
                CreatedRealAt = ReadString(reader, "CreatedRealAt")
            });
        }
        return list;
    }

    private List<NpcRelationshipRow> GetRelationships(SqliteConnection mainConn, int npcId)
    {
        using var relationshipConn = OpenRelationships();
        using var cmd = relationshipConn.CreateCommand();

        cmd.CommandText = """
        SELECT
            RelationshipId,
            SourceCharacterId,
            TargetCharacterId,
            TargetName,
            RelationshipType,
            FamilyRole,
            Love,
            Trust,
            Respect,
            Loyalty,
            Anger,
            Resentment,
            Fear,
            Jealousy,
            Attraction,
            Tension,
            Importance,
            Notes
        FROM RelationshipStates
        WHERE SourceCharacterId = $npcId
        ORDER BY Importance DESC, TargetName;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        var list = new List<NpcRelationshipRow>();
        using var reader = cmd.ExecuteReader();
        var sourceName = GetCharacterName(mainConn, npcId);

        while (reader.Read())
        {
            var targetId = ReadInt(reader, "TargetCharacterId");
            var storedTargetName = ReadString(reader, "TargetName");
            var resolvedTargetName = targetId > 0
                ? GetCharacterName(mainConn, targetId)
                : storedTargetName;

            if (string.IsNullOrWhiteSpace(resolvedTargetName))
                resolvedTargetName = storedTargetName;

            var type = ReadString(reader, "RelationshipType");

            list.Add(new NpcRelationshipRow
            {
                Id = ReadString(reader, "RelationshipId"),
                NpcId = ReadInt(reader, "SourceCharacterId"),
                SourceName = sourceName,
                TargetNpcId = targetId,
                TargetName = resolvedTargetName,
                TargetNameSnapshot = storedTargetName,
                RelationshipType = type,
                RelationshipOrigin = "Canonical relationship state",
                Trust = ReadInt(reader, "Trust"),
                Respect = ReadInt(reader, "Respect"),
                Affection = ReadInt(reader, "Love"),
                Attraction = ReadInt(reader, "Attraction"),
                Tension = ReadInt(reader, "Tension"),
                Anger = ReadInt(reader, "Anger"),
                Resentment = ReadInt(reader, "Resentment"),
                Fear = ReadInt(reader, "Fear"),
                Jealousy = ReadInt(reader, "Jealousy"),
                Loyalty = ReadInt(reader, "Loyalty"),
                Importance = ReadInt(reader, "Importance"),
                RelationshipCategory = RelationshipCategoryFromType(type),
                FamilyRole = ReadString(reader, "FamilyRole"),
                IsMutual = false,
                IsHidden = false,
                IsCoreRelationship = false,
                AffectsDialogue = true,
                Notes = ReadString(reader, "Notes")
            });
        }

        return list;
    }

    private static List<NpcTraitRow> GetTraits(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT *
        FROM NpcTraitValues
        WHERE NpcId = $npcId
        ORDER BY MainGroup, SubGroup, TraitName;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        var list = new List<NpcTraitRow>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new NpcTraitRow
            {
                Id = ReadString(reader, "Id"),
                NpcId = ReadInt(reader, "NpcId"),
                MainGroup = ReadString(reader, "MainGroup"),
                SubGroup = ReadString(reader, "SubGroup"),
                TraitId = ReadString(reader, "TraitId"),
                TraitName = ReadString(reader, "TraitName"),
                StartingValue = ReadInt(reader, "StartingValue"),
                CurrentValue = ReadInt(reader, "CurrentValue"),
                IsEnabled = ReadBool(reader, "IsEnabled"),
                Notes = ReadString(reader, "Notes")
            });
        }

        return list;
    }

    private static NpcAppearanceProfile GetAppearance(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        SELECT
            p.NpcId,
            p.HeightCm,
            p.BodyType,
            p.HairColor,
            p.HairStyle,
            p.EyeColor,
            p.SkinTone,
            p.DefaultClothingStyle AS ClothingStyle,
            p.DistinctiveFeatures,

            COALESCE(a.AppearanceStatus, '') AS AppearanceStatus,
            COALESCE(a.WorkClothes, '') AS WorkClothes,
            COALESCE(a.CasualClothes, '') AS CasualClothes,
            COALESCE(a.ImagePrompt, '') AS ImagePrompt,
            COALESCE(a.NegativePrompt, '') AS NegativePrompt,
            COALESCE(a.ReferenceImagePath, '') AS ReferenceImagePath,
            COALESCE(a.ProfileImagePath, '') AS ProfileImagePath,
            COALESCE(a.ContactImagePath, '') AS ContactImagePath,
            COALESCE(a.Approved, 0) AS Approved,
            COALESCE(a.Notes, '') AS Notes
        FROM NpcPhysicalProfiles p
        LEFT JOIN NpcAppearanceProfiles a
            ON a.NpcId = p.NpcId
        WHERE p.NpcId = $npcId;
        """;

        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            return new NpcAppearanceProfile
            {
                NpcId = npcId
            };
        }

        string heightText = "";

        var heightOrdinal = reader.GetOrdinal("HeightCm");

        if (!reader.IsDBNull(heightOrdinal))
        {
            var heightCm = Convert.ToDouble(reader.GetValue(heightOrdinal));

            var totalInches = heightCm / 2.54;
            var feet = (int)(totalInches / 12.0);
            var inches = (int)Math.Round(totalInches - (feet * 12.0));

            if (inches == 12)
            {
                feet++;
                inches = 0;
            }

            heightText = $"{feet}'{inches}\"";
        }

        return new NpcAppearanceProfile
        {
            NpcId = npcId,

            // Canonical physical truth comes from NpcPhysicalProfiles.
            BodyType = ReadString(reader, "BodyType"),
            HeightText = heightText,
            HairColor = ReadString(reader, "HairColor"),
            HairStyle = ReadString(reader, "HairStyle"),
            EyeColor = ReadString(reader, "EyeColor"),
            SkinTone = ReadString(reader, "SkinTone"),
            ClothingStyle = ReadString(reader, "ClothingStyle"),
            DistinguishingFeatures = ReadString(reader, "DistinctiveFeatures"),

            // Presentation / image-generation metadata remains in
            // NpcAppearanceProfiles.
            AppearanceStatus = ReadString(reader, "AppearanceStatus"),
            WorkClothes = ReadString(reader, "WorkClothes"),
            CasualClothes = ReadString(reader, "CasualClothes"),
            ImagePrompt = ReadString(reader, "ImagePrompt"),
            NegativePrompt = ReadString(reader, "NegativePrompt"),
            ReferenceImagePath = ReadString(reader, "ReferenceImagePath"),
            ProfileImagePath = ReadString(reader, "ProfileImagePath"),
            ContactImagePath = ReadString(reader, "ContactImagePath"),
            Approved = ReadBool(reader, "Approved"),
            Notes = ReadString(reader, "Notes")


        };
    }
    private static NpcVoiceProfile GetVoice(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM NpcVoiceProfiles WHERE NpcId = $npcId;";
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return new NpcVoiceProfile { NpcId = npcId };

        return new NpcVoiceProfile
        {
            NpcId = npcId,
            VoiceStatus = ReadString(reader, "VoiceStatus"),
            VoiceProvider = ReadString(reader, "VoiceProvider"),
            VoiceId = ReadString(reader, "VoiceId"),
            VoiceName = ReadString(reader, "VoiceName"),
            VoiceStyle = ReadString(reader, "VoiceStyle"),
            Accent = ReadString(reader, "Accent"),
            AgeTone = ReadString(reader, "AgeTone"),
            Energy = ReadString(reader, "Energy"),
            Warmth = ReadString(reader, "Warmth"),
            Roughness = ReadString(reader, "Roughness"),
            Pace = ReadString(reader, "Pace"),
            Pitch = ReadString(reader, "Pitch"),
            ReferenceAudioPath = ReadString(reader, "ReferenceAudioPath"),
            SampleText = ReadString(reader, "SampleText"),
            Approved = ReadBool(reader, "Approved"),
            Notes = ReadString(reader, "Notes")
        };
    }

    private static List<NpcStudioIdea> GetIdeas(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT *
        FROM NpcStudioIdeas
        WHERE NpcId = $npcId
        ORDER BY CreatedRealAt DESC;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        var list = new List<NpcStudioIdea>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new NpcStudioIdea
            {
                Id = ReadString(reader, "Id"),
                NpcId = ReadInt(reader, "NpcId"),
                IdeaType = ReadString(reader, "IdeaType"),
                SourceModel = ReadString(reader, "SourceModel"),
                InputSummary = ReadString(reader, "InputSummary"),
                IdeaText = ReadString(reader, "IdeaText"),
                Approved = ReadBool(reader, "Approved"),
                Rejected = ReadBool(reader, "Rejected"),
                AppliedToCharacter = ReadBool(reader, "AppliedToCharacter"),
                Notes = ReadString(reader, "Notes"),
                CreatedRealAt = ReadString(reader, "CreatedRealAt")
            });
        }

        return list;
    }

    private static List<NpcImageGeneration> GetImages(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT *
        FROM NpcImageGenerations
        WHERE NpcId = $npcId
        ORDER BY CreatedRealAt DESC;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        var list = new List<NpcImageGeneration>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new NpcImageGeneration
            {
                Id = ReadString(reader, "Id"),
                NpcId = ReadInt(reader, "NpcId"),
                ImageType = ReadString(reader, "ImageType"),
                PromptGenerationId = ReadString(reader, "PromptGenerationId"),
                PositivePrompt = ReadString(reader, "PositivePrompt"),
                NegativePrompt = ReadString(reader, "NegativePrompt"),
                Seed = ReadString(reader, "Seed"),
                WorkflowName = ReadString(reader, "WorkflowName"),
                Checkpoint = ReadString(reader, "Checkpoint"),
                Width = ReadInt(reader, "Width"),
                Height = ReadInt(reader, "Height"),
                Steps = ReadInt(reader, "Steps"),
                Cfg = ReadDouble(reader, "Cfg"),
                Sampler = ReadString(reader, "Sampler"),
                ImagePath = ReadString(reader, "ImagePath"),
                IsCurrent = ReadBool(reader, "IsCurrent"),
                Approved = ReadBool(reader, "Approved"),
                Notes = ReadString(reader, "Notes"),
                CreatedRealAt = ReadString(reader, "CreatedRealAt")
            });
        }

        return list;
    }

    private static List<NpcRevisionRow> GetRevisions(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT *
        FROM NpcBuildRevisions
        WHERE NpcId = $npcId
        ORDER BY CreatedRealAt DESC
        LIMIT 50;
        """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        var list = new List<NpcRevisionRow>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new NpcRevisionRow
            {
                Id = ReadString(reader, "Id"),
                NpcId = ReadInt(reader, "NpcId"),
                RevisionType = ReadString(reader, "RevisionType"),
                Title = ReadString(reader, "Title"),
                Details = ReadString(reader, "Details"),
                OldValue = ReadString(reader, "OldValue"),
                NewValue = ReadString(reader, "NewValue"),
                CreatedRealAt = ReadString(reader, "CreatedRealAt")
            });
        }

        return list;
    }


    private static string GetCharacterName(SqliteConnection conn, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT IFNULL(Name, '') FROM Characters WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", npcId);
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private Dictionary<int, int> GetCanonicalRelationshipCounts()
    {
        using var conn = OpenRelationships();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
        SELECT SourceCharacterId, COUNT(*) AS Count
        FROM RelationshipStates
        GROUP BY SourceCharacterId;
        """;

        var result = new Dictionary<int, int>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
            result[ReadInt(reader, "SourceCharacterId")] = ReadInt(reader, "Count");

        return result;
    }

    private void UpsertCanonicalRelationship(
        int sourceCharacterId,
        int? targetCharacterId,
        string targetName,
        string relationshipType,
        string familyRole,
        int love,
        int trust,
        int respect,
        int loyalty,
        int anger,
        int resentment,
        int fear,
        int jealousy,
        int attraction,
        int tension,
        int importance,
        string notes)
    {
        using var conn = OpenRelationships();

        var relationshipId = FindCanonicalRelationshipId(
            conn,
            sourceCharacterId,
            targetCharacterId,
            targetName,
            relationshipType);

        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            var targetKey = targetCharacterId?.ToString()
                ?? NormalizeRelationshipKey(targetName);

            relationshipId =
                $"rel:{sourceCharacterId}:{targetKey}:{NormalizeRelationshipKey(relationshipType)}";
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO RelationshipStates
        (
            RelationshipId,
            SourceCharacterId,
            TargetCharacterId,
            TargetName,
            RelationshipType,
            FamilyRole,
            Love,
            Trust,
            Respect,
            Loyalty,
            Anger,
            Resentment,
            Fear,
            Jealousy,
            Attraction,
            Tension,
            Importance,
            Notes,
            UpdatedGameTime,
            UpdatedRealAt
        )
        VALUES
        (
            $id,
            $source,
            $targetId,
            $targetName,
            $type,
            $familyRole,
            $love,
            $trust,
            $respect,
            $loyalty,
            $anger,
            $resentment,
            $fear,
            $jealousy,
            $attraction,
            $tension,
            $importance,
            $notes,
            '',
            CURRENT_TIMESTAMP
        )
        ON CONFLICT(RelationshipId) DO UPDATE SET
            TargetCharacterId = excluded.TargetCharacterId,
            TargetName = excluded.TargetName,
            RelationshipType = excluded.RelationshipType,
            FamilyRole = excluded.FamilyRole,
            Love = excluded.Love,
            Trust = excluded.Trust,
            Respect = excluded.Respect,
            Loyalty = excluded.Loyalty,
            Anger = excluded.Anger,
            Resentment = excluded.Resentment,
            Fear = excluded.Fear,
            Jealousy = excluded.Jealousy,
            Attraction = excluded.Attraction,
            Tension = excluded.Tension,
            Importance = excluded.Importance,
            Notes = excluded.Notes,
            UpdatedRealAt = CURRENT_TIMESTAMP;
        """;

        cmd.Parameters.AddWithValue("$id", relationshipId);
        cmd.Parameters.AddWithValue("$source", sourceCharacterId);
        cmd.Parameters.AddWithValue(
            "$targetId",
            targetCharacterId.HasValue ? targetCharacterId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$targetName", targetName ?? "");
        cmd.Parameters.AddWithValue("$type", relationshipType ?? "");
        cmd.Parameters.AddWithValue("$familyRole", familyRole ?? "");
        cmd.Parameters.AddWithValue("$love", Math.Clamp(love, 0, 100));
        cmd.Parameters.AddWithValue("$trust", Math.Clamp(trust, 0, 100));
        cmd.Parameters.AddWithValue("$respect", Math.Clamp(respect, 0, 100));
        cmd.Parameters.AddWithValue("$loyalty", Math.Clamp(loyalty, 0, 100));
        cmd.Parameters.AddWithValue("$anger", Math.Clamp(anger, 0, 100));
        cmd.Parameters.AddWithValue("$resentment", Math.Clamp(resentment, 0, 100));
        cmd.Parameters.AddWithValue("$fear", Math.Clamp(fear, 0, 100));
        cmd.Parameters.AddWithValue("$jealousy", Math.Clamp(jealousy, 0, 100));
        cmd.Parameters.AddWithValue("$attraction", Math.Clamp(attraction, 0, 100));
        cmd.Parameters.AddWithValue("$tension", Math.Clamp(tension, 0, 100));
        cmd.Parameters.AddWithValue("$importance", Math.Clamp(importance, 0, 100));
        cmd.Parameters.AddWithValue("$notes", notes ?? "");
        cmd.ExecuteNonQuery();
    }

    private static string FindCanonicalRelationshipId(
        SqliteConnection conn,
        int sourceCharacterId,
        int? targetCharacterId,
        string targetName,
        string relationshipType)
    {
        using var cmd = conn.CreateCommand();

        if (targetCharacterId.HasValue)
        {
            cmd.CommandText = """
            SELECT RelationshipId
            FROM RelationshipStates
            WHERE SourceCharacterId = $source
              AND TargetCharacterId = $targetId
              AND lower(trim(RelationshipType)) = lower(trim($type))
            ORDER BY UpdatedRealAt DESC, rowid DESC
            LIMIT 1;
            """;
            cmd.Parameters.AddWithValue("$targetId", targetCharacterId.Value);
        }
        else
        {
            cmd.CommandText = """
            SELECT RelationshipId
            FROM RelationshipStates
            WHERE SourceCharacterId = $source
              AND TargetCharacterId IS NULL
              AND lower(trim(TargetName)) = lower(trim($targetName))
              AND lower(trim(RelationshipType)) = lower(trim($type))
            ORDER BY UpdatedRealAt DESC, rowid DESC
            LIMIT 1;
            """;
            cmd.Parameters.AddWithValue("$targetName", targetName ?? "");
        }

        cmd.Parameters.AddWithValue("$source", sourceCharacterId);
        cmd.Parameters.AddWithValue("$type", relationshipType ?? "");

        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private static string NormalizeRelationshipKey(string? value)
    {
        var text = (value ?? "").Trim().ToLowerInvariant();

        return string.Concat(
            text.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'))
            .Trim('-');
    }

    private static string RelationshipCategoryFromType(string? relationshipType)
    {
        var text = (relationshipType ?? "").Trim().ToLowerInvariant();

        if (text.Contains("mother") || text.Contains("father") ||
            text.Contains("parent") || text.Contains("sibling") ||
            text.Contains("sister") || text.Contains("brother") ||
            text.Contains("child") || text.Contains("family") ||
            text.Contains("cousin") || text.Contains("spouse"))
            return "Family";

        if (text.Contains("wife") || text.Contains("husband") ||
            text.Contains("girlfriend") || text.Contains("boyfriend") ||
            text.Contains("romantic") || text.Contains("partner") ||
            text.Contains("lover") || text.Contains("dating"))
            return "Romantic";

        if (text.Contains("enemy")) return "Enemy";
        if (text.Contains("rival")) return "Rival";
        if (text.Contains("friend")) return "Friend";

        if (text.Contains("boss") || text.Contains("cowork") || text.Contains("work"))
            return "Work";

        return "Other";
    }

    private static void AddRevision(SqliteConnection conn, int npcId, string type, string title, string details)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO NpcBuildRevisions
        (
            Id, NpcId, RevisionType, Title, Details, OldValue, NewValue, CreatedRealAt
        )
        VALUES
        (
            $id, $npcId, $type, $title, $details, '', '', CURRENT_TIMESTAMP
        );
        """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$type", type);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$details", details);
        cmd.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar();
        return value is null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static List<NpcCountRow> CountRows(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var list = new List<NpcCountRow>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new NpcCountRow
            {
                Label = ReadString(reader, "Label"),
                Count = ReadInt(reader, "Count")
            });
        }

        return list;
    }

    private static string ReadString(SqliteDataReader reader, string name)
    {
        try
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? "" : reader.GetValue(ordinal)?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int ReadInt(SqliteDataReader reader, string name)
    {
        var text = ReadString(reader, name);
        return int.TryParse(text, out var value) ? value : 0;
    }

    private static double ReadDouble(SqliteDataReader reader, string name)
    {
        var text = ReadString(reader, name);
        return double.TryParse(text, out var value) ? value : 0;
    }

    private static bool ReadBool(SqliteDataReader reader, string name)
    {
        return ReadInt(reader, name) != 0;
    }
}

