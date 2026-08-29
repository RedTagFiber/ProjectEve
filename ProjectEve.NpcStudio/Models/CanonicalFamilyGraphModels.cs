namespace ProjectEve.NpcStudio.Models;

public sealed class CanonicalFamilyGraph
{
    public int RootNpcId { get; init; }
    public string RootName { get; init; } = "";
    public List<CanonicalFamilyPerson> People { get; } = new();
    public List<CanonicalFamilyEdge> Edges { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class CanonicalFamilyPerson
{
    public int NpcId { get; init; }
    public string Name { get; init; } = "";
    public string RoleFromRoot { get; init; } = "";
    public int Generation { get; init; }
    public bool IsDirect { get; init; }
    public bool IsInferred { get; init; }
}

public sealed class CanonicalFamilyEdge
{
    public int FromNpcId { get; init; }
    public int ToNpcId { get; init; }
    public string EdgeType { get; init; } = "";
    public string RoleFromFrom { get; init; } = "";
    public string RoleFromTo { get; init; } = "";
    public bool IsInferred { get; init; }
}

public sealed class CanonicalFamilyMigrationReport
{
    public int ParentChildLinksAdded { get; set; }
    public int UnionLinksAdded { get; set; }
    public int KinshipOverridesAdded { get; set; }
    public int SkippedRows { get; set; }
    public List<string> Warnings { get; } = new();
}
