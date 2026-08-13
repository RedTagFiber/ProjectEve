using System;
using System.IO;
using System.Text.Json;

namespace ProjectEve.Worlds.SmallTownSystems
{
    /// <summary>
    /// Loads the real Project Eve town locations into ActivityPlanner's venue registry.
    /// </summary>
    public static class ActivityPlannerBootstrap
    {
        public static string VenueFilePath =>
            Environment.GetEnvironmentVariable("EVE_ACTIVITY_VENUES")
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "World",
                "Ohio",
                "town_activity_venues.json");

        public static int ImportTownVenues()
        {
            ActivityPlanner.Initialize();

            if (!File.Exists(VenueFilePath))
                return 0;

            using JsonDocument doc = JsonDocument.Parse(
                File.ReadAllText(VenueFilePath));

            if (!doc.RootElement.TryGetProperty("venues", out JsonElement venues) ||
                venues.ValueKind != JsonValueKind.Array)
                return 0;

            int count = 0;

            foreach (JsonElement v in venues.EnumerateArray())
            {
                string id = ReadString(v, "locationId");
                string name = ReadString(v, "name");
                string category = ReadString(v, "category");

                if (string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(category))
                    continue;

                var tags = new System.Collections.Generic.List<string>();

                if (v.TryGetProperty("tags", out JsonElement tagArray) &&
                    tagArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement t in tagArray.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(t.GetString()))
                            tags.Add(t.GetString()!);
                    }
                }

                ActivityPlanner.RegisterVenue(
                    id,
                    name,
                    category,
                    tags,
                    minimumAge: ReadInt(v, "minimumAge", 0),
                    costLevel: ReadInt(v, "costLevel", 1),
                    openHour: ReadInt(v, "openHour", 0),
                    closeHour: ReadInt(v, "closeHour", 24),
                    enabled: ReadBool(v, "enabled", true));

                count++;
            }

            ActivityPlanner.SeedKnownProjectEveVenues();

            return count;
        }

        private static string ReadString(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";

        private static int ReadInt(JsonElement e, string name, int fallback)
            => e.TryGetProperty(name, out var v) && v.TryGetInt32(out int n)
                ? n
                : fallback;

        private static bool ReadBool(JsonElement e, string name, bool fallback)
            => e.TryGetProperty(name, out var v) &&
               (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
                ? v.GetBoolean()
                : fallback;
    }
}
