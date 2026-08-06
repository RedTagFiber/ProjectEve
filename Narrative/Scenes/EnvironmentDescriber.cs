using System.Linq;
using System.Linq;
using ProjectEve.Narrative.Descriptions;

namespace ProjectEve.Narrative.Scenes
{
    public static class EnvironmentDescriber
    {
        public static string Describe(SceneState scene)
        {
            if (scene == null)
                return SceneText.Building("cozy");

            var parts = new[]
            {
                SceneText.Weather(scene.Weather),
                SceneText.Building(scene.BuildingStyle),
                SceneText.Lighting(scene.Lighting),
                SceneText.Smell(scene.Smell),
                SceneText.Crowd(scene.Crowd)
            };

            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}