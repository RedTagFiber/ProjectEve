namespace ProjectEve.NpcStudio.Models;

public sealed class PersonalCurrentLifeEdit
{
    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentLocationId { get; set; } = "";
    public string HomeLocationId { get; set; } = "";
    public string WorkLocationId { get; set; } = "";
    public string Hometown { get; set; } = "";
    public string Address { get; set; } = "";

    public string Height { get; set; } = "";
    public string BodyType { get; set; } = "";
    public string HairColor { get; set; } = "";
    public string HairStyle { get; set; } = "";
    public string EyeColor { get; set; } = "";
    public string SkinTone { get; set; } = "";
    public string ClothingStyle { get; set; } = "";
    public string DistinguishingFeatures { get; set; } = "";
}

public sealed class PersonalHomeEdit
{
    public string HomeLocationId { get; set; } = "";
    public string CurrentLocationId { get; set; } = "";
    public string WorkLocationId { get; set; } = "";
    public string Location { get; set; } = "";
    public string Address { get; set; } = "";
}

public sealed class FamilyBuildPlanEdit
{
    public bool CreateMother { get; set; } = true;
    public int MotherSiblingCount { get; set; }
    public bool CreateFather { get; set; } = true;
    public int FatherSiblingCount { get; set; }
    public int BrotherCount { get; set; }
    public int SisterCount { get; set; }
    public string SiblingBirthPattern { get; set; } = "Auto";
    public bool CreateMaternalGrandmother { get; set; } = true;
    public bool CreateMaternalGrandfather { get; set; } = true;
    public bool CreatePaternalGrandmother { get; set; } = true;
    public bool CreatePaternalGrandfather { get; set; } = true;
    public bool GenerateAuntsUncles { get; set; } = true;
    public bool GenerateCousins { get; set; } = true;
    public bool GenerateSpousesInLaws { get; set; } = true;
    public bool ReuseExistingTownNpcForSpouses { get; set; } = true;
    public string ExtendedFamilyDepth { get; set; } = "Deep";
}
