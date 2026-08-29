namespace ProjectEve.NpcStudio.Models;

public sealed class RelationshipCandidate
{
    public int NpcId { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public int Tier { get; set; }
    public string Occupation { get; set; } = "";
    public string Location { get; set; } = "";

    public bool Eligible { get; set; }
    public string Availability { get; set; } = "";
    public int CompatibilityScore { get; set; }
    public int CompatibilityConfidence { get; set; }

    public List<string> StrongMatches { get; set; } = new();
    public List<string> Differences { get; set; } = new();
    public List<string> Conflicts { get; set; } = new();
    public List<string> DeepeningSuggestions { get; set; } = new();

    public bool CanDeepenToFit => Tier >= 5 && Eligible && DeepeningSuggestions.Count > 0;
}

public sealed class RelationshipBuildPreview
{
    public int RootNpcId { get; set; }
    public int CandidateNpcId { get; set; }
    public string RootName { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string ProposedRelationship { get; set; } = "";
    public string ReverseRelationship { get; set; } = "";
    public int CompatibilityScore { get; set; }
    public int CompatibilityConfidence { get; set; }
    public List<string> Writes { get; set; } = new();
    public List<string> ProposedTier5Updates { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
