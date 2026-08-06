using ProjectEve.Narrative.Descriptions;
using ProjectEve.Narrative.Scenes;

namespace ProjectEve.Narrative.Transitions
{
    public static class Transitions
    {
        // ============================================================
        // TEXT → SCENE
        // ============================================================
        public static string TextToScene(
            SceneState scene,
            string clothing,
            string hair,
            string expression,
            string? buildingStyle = null)
        {
            scene ??= new SceneState();

            string env = EnvironmentDescriber.Describe(scene);
            string building = SceneText.Building(buildingStyle ?? scene.BuildingStyle);
            string npc = SceneText.Appearance(clothing, hair, expression);

            return $"{env} {building} {npc}".Trim();
        }

        // Optional overload if you still pass a simple appearance bag
        public static string TextToScene(
            SceneState scene,
            SceneNpcLook look,
            string? buildingStyle = null)
        {
            return TextToScene(
                scene,
                look.Clothing,
                look.Hair,
                look.Expression,
                buildingStyle);
        }

        // ============================================================
        // SCENE → TEXT
        // ============================================================
        public static string SceneToText(string npcName)
        {
            npcName = string.IsNullOrWhiteSpace(npcName) ? "They" : npcName;
            return $"{npcName} glances at their phone.";
        }

        // ============================================================
        // PRESENCE SHIFT
        // ============================================================
        public static string EnterScene(
            SceneState scene,
            string clothing,
            string hair,
            string expression,
            string? buildingStyle = null)
            => TextToScene(scene, clothing, hair, expression, buildingStyle);

        public static string EnterText(string npcName)
            => SceneToText(npcName);
    }

    // Lightweight look bag so this file doesn't depend on a local NPCAppearance type
    public class SceneNpcLook
    {
        public string Clothing { get; set; } = "casual";
        public string Hair { get; set; } = "loose";
        public string Expression { get; set; } = "neutral";
        public string Posture { get; set; } = "standing";
    }
}