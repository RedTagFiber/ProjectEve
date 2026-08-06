using ProjectEve.Narrative.Descriptions;

namespace ProjectEve.Narrative.Scenes
{
    public static class LocationStyleDescriber
    {
        public static string Describe(string? locationName)
        {
            var loc = (locationName ?? "").ToLowerInvariant();

            if (loc.Contains("coffee") || loc.Contains("cafe") || loc.Contains("shop"))
                return SceneText.Building("coffee");

            if (loc.Contains("apartment"))
                return SceneText.Building("apartment");

            if (loc.Contains("house") || loc.Contains("ryan"))
                return SceneText.Building("house");

            if (loc.Contains("bar"))
                return SceneText.Building("bar");

            return SceneText.Building(string.IsNullOrWhiteSpace(locationName) ? "cozy" : locationName);
        }
    }
}