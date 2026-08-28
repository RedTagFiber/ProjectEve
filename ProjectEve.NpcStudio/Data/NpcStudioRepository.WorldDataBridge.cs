using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    /// <summary>
    /// Reads existing Project Eve tables and fills dossier fields that are still empty.
    /// This is deliberately a read bridge: project_eve.db remains the single source of truth.
    /// World Builder/NPC Studio values already authored in the new dossier tables take priority.
    /// </summary>
    private static void HydrateExistingProjectEveData(SqliteConnection conn, NpcCharacterSheet sheet)
    {
        HydrateCharacterColumns(conn, sheet);
        HydrateLegacyAppearance(conn, sheet);
        MergeLegacyTraits(conn, sheet);
        MergeLegacyHistory(conn, sheet);
    }

    private static void HydrateCharacterColumns(SqliteConnection conn, NpcCharacterSheet sheet)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Characters WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", sheet.Id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return;

        sheet.AiSummary = FillIfBlank(sheet.AiSummary, ReadString(r, "PersonalitySummary"));
        if (string.IsNullOrWhiteSpace(sheet.PersonalityContext))
        {
            var shortStory = ReadString(r, "BackstoryShort");
            var longStory = ReadString(r, "BackstoryLong");
            sheet.PersonalityContext = string.Join("\n\n", new[] { shortStory, longStory }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        sheet.Appearance.BodyType = FillIfBlank(sheet.Appearance.BodyType, ReadString(r, "BodyShape"));
        sheet.Appearance.HairColor = FillIfBlank(sheet.Appearance.HairColor, ReadString(r, "HairColor"));
        sheet.Appearance.HairStyle = FillIfBlank(sheet.Appearance.HairStyle, ReadString(r, "HairStyle"));
        sheet.Appearance.EyeColor = FillIfBlank(sheet.Appearance.EyeColor, ReadString(r, "EyeColor"));
        sheet.Appearance.SkinTone = FillIfBlank(sheet.Appearance.SkinTone, ReadString(r, "SkinTone"));
        sheet.Appearance.DistinguishingFeatures = FillIfBlank(sheet.Appearance.DistinguishingFeatures, ReadString(r, "ScarNotes"));
        sheet.Appearance.ReferenceImagePath = FillIfBlank(sheet.Appearance.ReferenceImagePath, ReadString(r, "CurrentReferenceImagePath"));
        sheet.Appearance.ProfileImagePath = FillIfBlank(sheet.Appearance.ProfileImagePath, ReadString(r, "CurrentProfileImagePath"));
        sheet.Appearance.ContactImagePath = FillIfBlank(sheet.Appearance.ContactImagePath, ReadString(r, "CurrentContactImagePath"));
        sheet.Voice.ReferenceAudioPath = FillIfBlank(sheet.Voice.ReferenceAudioPath, ReadString(r, "CurrentVoiceReferencePath"));
        sheet.Voice.VoiceStyle = FillIfBlank(sheet.Voice.VoiceStyle, ReadString(r, "SpeakingStyle"));
    }

    private static void HydrateLegacyAppearance(SqliteConnection conn, NpcCharacterSheet sheet)
    {
        if (!TableExists(conn, "Appearance")) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Appearance WHERE NpcId=$id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", sheet.Id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return;

        var height = ReadString(r, "Height");
        sheet.Appearance.HeightText = FillIfBlank(sheet.Appearance.HeightText, height);
        sheet.Appearance.BodyType = FillIfBlank(sheet.Appearance.BodyType, ReadString(r, "BodyType"));
        sheet.Appearance.HairColor = FillIfBlank(sheet.Appearance.HairColor, ReadString(r, "HairColor"));
        sheet.Appearance.HairStyle = FillIfBlank(sheet.Appearance.HairStyle, ReadString(r, "HairStyle"));
        sheet.Appearance.EyeColor = FillIfBlank(sheet.Appearance.EyeColor, ReadString(r, "EyeColor"));
        sheet.Appearance.SkinTone = FillIfBlank(sheet.Appearance.SkinTone, ReadString(r, "SkinTone"));
        sheet.Appearance.DistinguishingFeatures = FillIfBlank(sheet.Appearance.DistinguishingFeatures, ReadString(r, "NotableFeatures"));
        sheet.Appearance.ClothingStyle = FillIfBlank(sheet.Appearance.ClothingStyle, ReadString(r, "Style"));

        if (sheet.HeightCm <= 0 && TryParseHeightCm(height, out var cm))
            sheet.HeightCm = cm;
    }

    private static void MergeLegacyTraits(SqliteConnection conn, NpcCharacterSheet sheet)
    {
        if (!TableExists(conn, "Traits")) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Traits WHERE NpcId=$id;";
        cmd.Parameters.AddWithValue("$id", sheet.Id);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var traitId = ReadString(r, "TraitId");
            if (string.IsNullOrWhiteSpace(traitId) || sheet.Traits.Any(x => x.TraitId.Equals(traitId, StringComparison.OrdinalIgnoreCase)))
                continue;
            var value = ClampScore(ReadDouble(r, "Value"));
            sheet.Traits.Add(new NpcTraitRow
            {
                Id = $"legacy-{sheet.Id}-{traitId}",
                NpcId = sheet.Id,
                MainGroup = "Existing Project Eve",
                TraitId = traitId,
                TraitName = HumanizeTraitId(traitId),
                StartingValue = value,
                CurrentValue = value,
                IsEnabled = true,
                Notes = "Loaded from existing Traits table."
            });
        }
    }

    private static void MergeLegacyHistory(SqliteConnection conn, NpcCharacterSheet sheet)
    {
        if (!TableExists(conn, "History")) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM History WHERE NpcId=$id ORDER BY Age, Timestamp;";
        cmd.Parameters.AddWithValue("$id", sheet.Id);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var summary = ReadString(r, "Summary");
            var story = ReadString(r, "StoryText");
            var title = string.IsNullOrWhiteSpace(summary) ? ReadString(r, "EventId") : summary;
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (sheet.HistoryEvents.Any(x => x.Title.Equals(title, StringComparison.OrdinalIgnoreCase))) continue;

            sheet.HistoryEvents.Add(new NpcHistoryEvent
            {
                Id = $"legacy-history-{ReadInt(r, "Id")}",
                NpcId = sheet.Id,
                EventDate = ReadString(r, "Timestamp"),
                AgeAtEvent = ReadInt(r, "Age"),
                EventType = ReadString(r, "Category"),
                Title = title,
                Details = story,
                Meaning = ReadString(r, "EmotionalImpact"),
                IsCanon = true,
                CreatedRealAt = ReadString(r, "Timestamp")
            });
        }
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
        cmd.Parameters.AddWithValue("$name", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static string FillIfBlank(string target, string value)
        => string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(value) ? value : target;

    private static int ClampScore(double value) => Math.Clamp((int)Math.Round(value), 0, 100);

    private static string HumanizeTraitId(string traitId)
    {
        var s = traitId.Replace("trait.", "", StringComparison.OrdinalIgnoreCase).Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
    }

    private static string ToTitle(string value) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase((value ?? "").Replace('_', ' ').Replace('-', ' '));

    private static bool TryParseHeightCm(string value, out double cm)
    {
        cm = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim().ToLowerInvariant();

        // 5'5", 5 ft 5 in, etc.
        var m = Regex.Match(text, @"(?<ft>\d+)\s*(?:'|ft|feet)\s*(?<inch>\d+(?:\.\d+)?)?", RegexOptions.IgnoreCase);
        if (m.Success && double.TryParse(m.Groups["ft"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var feet))
        {
            var inches = 0d;
            if (m.Groups["inch"].Success)
                double.TryParse(m.Groups["inch"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out inches);
            cm = (feet * 12d + inches) * 2.54d;
            return cm > 0;
        }

        m = Regex.Match(text, @"(?<cm>\d+(?:\.\d+)?)\s*cm");
        if (m.Success && double.TryParse(m.Groups["cm"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out cm))
            return cm > 0;

        return false;
    }
}
