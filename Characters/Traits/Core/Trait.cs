namespace ProjectEve.Characters.Traits.Core
{
    /// <summary>
    /// Legacy text tag (not 0–100). Prefer NpcTraits Fast/Mid/Slow.
    /// Safe for flavor lists / old UI only.
    /// </summary>
    public class Trait
    {
        public string Name { get; set; }
        public string Category { get; set; } = "General";
        public string Description { get; set; } = "";

        public Trait(string name, string category = "General", string description = "")
        {
            Name = name;
            Category = category;
            Description = description;
        }
    }
}