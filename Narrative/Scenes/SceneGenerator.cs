using System;

namespace ProjectEve.Narrative.Scenes
{
    public class SceneGenerator
    {
        private readonly Random _rng = new();

        public SceneState GenerateScene(string? locationName)
        {
            locationName = string.IsNullOrWhiteSpace(locationName) ? "unknown" : locationName;
            var loc = locationName.ToLowerInvariant();

            var scene = new SceneState
            {
                Location = locationName,
                TimeOfDay = Pick("morning", "afternoon", "evening", "night"),
                Weather = Pick("sunny", "overcast", "rain", "windy", "misty", "cold", "hot"),
                Crowd = Pick("empty", "quiet", "half-full", "busy", "lively"),
                Lighting = Pick("warm", "soft", "bright", "dim", "morning", "evening", "night"),
                Smell = "coffee",
                BuildingStyle = "cozy"
            };

            if (loc.Contains("coffee") || loc.Contains("cafe") || loc.Contains("shop"))
            {
                scene.BuildingStyle = "coffee";
                scene.Smell = Pick("coffee", "espresso", "pastry", "vanilla", "bleach");
                scene.Crowd = Pick("quiet", "half-full", "busy", "lively");
            }
            else if (loc.Contains("apartment") || loc.Contains("home") || loc.Contains("house"))
            {
                scene.BuildingStyle = loc.Contains("house") ? "house" : "apartment";
                scene.Smell = Pick("lotion", "shampoo", "vanilla", "coffee", "sex", "food");
                scene.Crowd = Pick("empty", "quiet");
                scene.Lighting = Pick("warm", "dim", "soft", "night", "evening");
            }
            else if (loc.Contains("bar"))
            {
                scene.BuildingStyle = "bar";
                scene.Smell = Pick("beer", "bar", "smoke", "perfume", "cologne");
                scene.Lighting = Pick("dim", "neon", "warm", "night");
                scene.Crowd = Pick("quiet", "half-full", "busy", "packed", "lively");
            }
            else
            {
                scene.BuildingStyle = "cozy";
                scene.Smell = Pick("coffee", "rain", "old wood", "food", "night air");
            }

            if (scene.TimeOfDay is "evening" or "night")
            {
                if (_rng.Next(0, 100) < 55)
                    scene.Lighting = scene.BuildingStyle == "bar" ? "neon" : "dim";
            }

            return scene;
        }

        private string Pick(params string[] options)
            => options[_rng.Next(options.Length)];
    }
}