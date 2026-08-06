namespace ProjectEve.Characters.Traits.Core
{
    public class TraitDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";


        public string Prompt { get; set; } = "";

        public int DefaultValue { get; set; } = 50;
        public int MinValue { get; set; } = 0;
        public int MaxValue { get; set; } = 100;

        public string WeightHint { get; set; } = "medium";

        public List<string> Tags { get; set; } = new();
        public string LlmContext { get; set; } = "";

        public string ExampleHigh { get; set; } = "";
        public string ExampleLow { get; set; } = "";

        public List<string> BehaviorLinks { get; set; } = new();

        public string ImpactDirection { get; set; } = "mixed";

        public bool IsCoreTrait { get; set; } = true;
    }
}