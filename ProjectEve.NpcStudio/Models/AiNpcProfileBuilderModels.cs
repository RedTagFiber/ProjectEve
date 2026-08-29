namespace ProjectEve.NpcStudio.Models;

public sealed class AiNpcProfileBuildRequest
{
    public int NpcId { get; init; }
    public int BuildTier { get; init; } = 5;
    public bool FillIdentity { get; init; } = true;
    public bool FillAppearance { get; init; } = true;
    public bool FillTraits { get; init; } = true;
    public bool FillCurrentLife { get; init; } = true;
    public bool FillEducationCareer { get; init; } = true;
    public bool FillHabitsInterests { get; init; } = true;
    public bool FillRelationshipContext { get; init; } = true;
}

public sealed class AiNpcProfilePreview
{
    public int NpcId { get; init; }
    public string ExistingName { get; init; } = "";
    public int BuildTier { get; init; }
    public string SourceModel { get; init; } = "";
    public string RawJson { get; init; } = "";
    public AiNpcProfileProposal? Proposal { get; init; }
    public List<string> Warnings { get; } = new();
    public bool IsValid => Proposal is not null && Warnings.All(w => !w.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase));
}

public sealed class AiNpcProfileProposal
{
    public string FirstName { get; set; } = "";
    public string MiddleName { get; set; } = "";
    public string CurrentLastName { get; set; } = "";
    public string BirthLastName { get; set; } = "";
    public string PreferredName { get; set; } = "";

    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public int IQ { get; set; }

    public string Archetype1 { get; set; } = "";
    public string Archetype2 { get; set; } = "";
    public string Archetype3 { get; set; } = "";

    public string PublicPersona { get; set; } = "";
    public string PrivatePersona { get; set; } = "";
    public string HiddenBehavior { get; set; } = "";

    public string Hometown { get; set; } = "";
    public string Address { get; set; } = "";
    public string SkinTone { get; set; } = "";
    public string DistinguishingFeatures { get; set; } = "";
    public string PersonalitySummary { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Need { get; set; } = "";
    public string Fear { get; set; } = "";
    public string Want { get; set; } = "";

    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";
    public string EducationSummary { get; set; } = "";

    public string BodyType { get; set; } = "";
    public string HairColor { get; set; } = "";
    public string HairStyle { get; set; } = "";
    public string EyeColor { get; set; } = "";
    public string ClothingStyle { get; set; } = "";

    public List<string> Interests { get; set; } = new();
    public List<string> Habits { get; set; } = new();
    public List<AiNpcTraitProposal> Traits { get; set; } = new();

    public string RelationshipStyle { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class AiNpcTraitProposal
{
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public int Value { get; set; } = 50;
}


