using System.Linq;

namespace ProjectEve.Narrative.Scenes
{
    public static class SceneTransitionController
    {
        public static string BuildFullScene(
            SceneState scene,
            string? locationStyle = null,
            string? npcAppearance = null)
        {
            var env = EnvironmentDescriber.Describe(scene);
            var loc = locationStyle ?? LocationStyleDescriber.Describe(scene.Location);
            var npc = npcAppearance ?? "";

            return string.Join(" ",
                new[] { env, loc, npc }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }
}