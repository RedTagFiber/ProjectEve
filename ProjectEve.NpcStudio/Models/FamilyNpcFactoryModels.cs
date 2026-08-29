namespace ProjectEve.NpcStudio.Models;

public sealed class FamilyNpcFactoryManifest
{
    public int RootNpcId { get; set; }
    public string RootName { get; set; } = "";
    public List<FamilyNpcFactoryRow> Rows { get; set; } = new();
    public List<string> ProposedRelationshipWrites { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public int ReuseCount => Rows.Count(x => x.Action == "REUSE NPC");
    public int NewCount => Rows.Count(x => x.Action == "NEW NPC");
}

public sealed class FamilyNpcFactoryRow
{
    public string Action { get; set; } = "";
    public string MemberKey { get; set; } = "";
    public string FamilyRole { get; set; } = "";
    public int? ExistingNpcId { get; set; }

    public string ProposedName { get; set; } = "";
    public int ProposedAge { get; set; }
    public string ProposedGender { get; set; } = "";
    public int ProposedTier { get; set; }

    public string Location { get; set; } = "";
    public string Hometown { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";

    public string PersonalitySummary { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Need { get; set; } = "";
    public string Fear { get; set; } = "";
    public string Want { get; set; } = "";
    public List<string> Interests { get; set; } = new();
    public List<FamilyNpcFactoryTrait> Traits { get; set; } = new();
    public string PhysicalDirection { get; set; } = "";
    public string EducationCareerDirection { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class FamilyNpcFactoryTrait
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}
