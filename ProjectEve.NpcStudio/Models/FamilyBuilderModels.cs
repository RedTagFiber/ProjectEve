namespace ProjectEve.NpcStudio.Models;

public sealed class FamilyBuildResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int CreatedCount { get; set; }
    public int ReusedCount { get; set; }
    public List<FamilyBuildMemberResult> Members { get; set; } = new();
}

public sealed class FamilyBuildMemberResult
{
    public string MemberKey { get; set; } = "";
    public int NpcId { get; set; }
    public string DisplayName { get; set; } = "";
    public string FamilyRole { get; set; } = "";
    public bool CreatedNow { get; set; }
}
