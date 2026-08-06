using ProjectEve.Narrative.Descriptions;

namespace ProjectEve.Narrative.Scenes
{
    public static class NPCAppearanceDescriber
    {
        public static string Describe(
            string name,
            string clothing,
            string hair,
            string expression,
            string posture = "standing")
        {
            name = string.IsNullOrWhiteSpace(name) ? "They" : name;

            return
                $"{name} is here. " +
                $"{SceneText.Clothing(clothing)} " +
                $"{SceneText.Hair(hair)} " +
                $"{SceneText.Expression(expression)} " +
                $"{SceneText.Posture(posture)}";
        }

        public static string DescribeSimple(string name, string clothing, string expression)
            => Describe(name, clothing, "loose", expression, "standing");
    }
}