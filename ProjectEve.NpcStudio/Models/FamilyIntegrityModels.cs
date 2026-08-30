namespace ProjectEve.NpcStudio.Models;

public sealed class FamilyIntegrityReport
{
    public int NpcId { get; init; }
    public bool IsSafe => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> PassedChecks { get; } = new();
}

public sealed class NpcNameProfileRow
{
    public int NpcId { get; init; }
    public string FirstName { get; init; } = "";
    public string MiddleName { get; init; } = "";
    public string CurrentLastName { get; init; } = "";
    public string BirthLastName { get; init; } = "";
    public string PreferredName { get; init; } = "";
    public string Suffix { get; init; } = "";
}

public sealed class NpcCreationProvenanceRow
{
    public int NpcId { get; init; }
    public string CreationSourceType { get; init; } = "";
    public int? CreatedFromNpcId { get; init; }
    public string CreatedFromNpcName { get; init; } = "";
    public string OriginalRole { get; init; } = "";
    public string CreationBatchId { get; init; } = "";
    public string BuildStatus { get; init; } = "";
}
