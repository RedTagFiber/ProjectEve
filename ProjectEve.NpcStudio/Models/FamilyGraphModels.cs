namespace ProjectEve.NpcStudio.Models;

public sealed class FamilyGraphPreview
{
    public int RootNpcId { get; set; }
    public string RootName { get; set; } = "";
    public List<FamilyGraphPerson> ExistingPeople { get; set; } = new();
    public List<FamilyGraphMissingBranch> MissingBranches { get; set; } = new();

    public int ReuseCount => ExistingPeople.Count;
    public int MissingCount => MissingBranches.Count;
}

public sealed class FamilyGraphPerson
{
    public int NpcId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string RelationshipPath { get; set; } = "";
    public string Source { get; set; } = "";
    public bool LockedReuse { get; set; } = true;
}

public sealed class FamilyGraphMissingBranch
{
    public string BranchKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string WhyMissing { get; set; } = "";
}
