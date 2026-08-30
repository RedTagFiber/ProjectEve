using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class NpcAppearanceDetailService
{
    private readonly NpcStudioOptions _options;

    public NpcAppearanceDetailService(NpcStudioOptions options)
    {
        _options = options;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();
        EnsureSchema(conn);
        return conn;
    }

    public NpcAppearanceDetailProfile Load(int npcId)
    {
        using var conn = Open();
        var p = new NpcAppearanceDetailProfile { NpcId = npcId };

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT IFNULL(Age,0), IFNULL(Gender,''), IFNULL(RaceEthnicity,''),
                       IFNULL(Occupation,''), IFNULL(Location,''), IFNULL(Tier,5),
                       IFNULL(PersonalitySummary,''), IFNULL(Goal,''), IFNULL(Need,''),
                       IFNULL(Fear,''), IFNULL(Want,'')
                FROM Characters WHERE Id=$id;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                p.Age = Convert.ToInt32(r.GetValue(0));
                p.Gender = S(r,1);
                p.RaceEthnicity = S(r,2);
                p.Occupation = S(r,3);
                p.Location = S(r,4);
                p.Tier = Convert.ToInt32(r.GetValue(5));
                p.PersonalitySummary = S(r,6);
                p.Goal = S(r,7);
                p.Need = S(r,8);
                p.Fear = S(r,9);
                p.Want = S(r,10);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    AppearanceLevel, BodyBuild,
                    EyeBaseColor, EyeVariant, EyePattern, EyeShape, EyeExpression, EyeNotes,
                    HairColor, HairUndertone, HairHighlights, HairLength, HairTexture,
                    HairStyle, HairDensity,
                    SkinTone, SkinUndertone, ComplexionDetails,
                    FaceShape, JawShape, NoseShape, LipShape, BrowStyle, CheekboneStyle,
                    DistinguishingFeatures,
                    DefaultClothingStyle, WorkClothingStyle, HomeClothingStyle,
                    GoingOutClothingStyle, ClubClothingStyle, FamilyEventClothingStyle,
                    FormalClothingStyle, AthleticClothingStyle, SleepwearStyle, WinterClothingStyle,
                    BraSize, PenisSize, CircumcisionStatus, AdultAnatomyNotes
                FROM NpcAppearanceDetailProfiles
                WHERE NpcId=$id;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);

            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                p.AppearanceLevel = S(r,0);
                p.BodyBuild = S(r,1);
                p.EyeBaseColor = S(r,2);
                p.EyeVariant = S(r,3);
                p.EyePattern = S(r,4);
                p.EyeShape = S(r,5);
                p.EyeExpression = S(r,6);
                p.EyeNotes = S(r,7);
                p.HairColor = S(r,8);
                p.HairUndertone = S(r,9);
                p.HairHighlights = S(r,10);
                p.HairLength = S(r,11);
                p.HairTexture = S(r,12);
                p.HairStyle = S(r,13);
                p.HairDensity = S(r,14);
                p.SkinTone = S(r,15);
                p.SkinUndertone = S(r,16);
                p.ComplexionDetails = S(r,17);
                p.FaceShape = S(r,18);
                p.JawShape = S(r,19);
                p.NoseShape = S(r,20);
                p.LipShape = S(r,21);
                p.BrowStyle = S(r,22);
                p.CheekboneStyle = S(r,23);
                p.DistinguishingFeatures = S(r,24);
                p.DefaultClothingStyle = S(r,25);
                p.WorkClothingStyle = S(r,26);
                p.HomeClothingStyle = S(r,27);
                p.GoingOutClothingStyle = S(r,28);
                p.ClubClothingStyle = S(r,29);
                p.FamilyEventClothingStyle = S(r,30);
                p.FormalClothingStyle = S(r,31);
                p.AthleticClothingStyle = S(r,32);
                p.SleepwearStyle = S(r,33);
                p.WinterClothingStyle = S(r,34);
                p.BraSize = S(r,35);
                p.PenisSize = S(r,36);
                p.CircumcisionStatus = S(r,37);
                p.AdultAnatomyNotes = S(r,38);
            }
        }

        // Backfill from older canonical physical fields only when detail fields are empty.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT IFNULL(BodyType,''),IFNULL(HairColor,''),IFNULL(HairStyle,''),
                       IFNULL(EyeColor,''),IFNULL(SkinTone,''),IFNULL(DefaultClothingStyle,'')
                FROM NpcPhysicalProfiles WHERE NpcId=$id;
                """;
            cmd.Parameters.AddWithValue("$id", npcId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                if (Blank(p.BodyBuild)) p.BodyBuild = S(r,0);
                if (Blank(p.HairColor)) p.HairColor = S(r,1);
                if (Blank(p.HairStyle)) p.HairStyle = S(r,2);
                if (Blank(p.EyeVariant)) p.EyeVariant = S(r,3);
                if (Blank(p.SkinTone)) p.SkinTone = S(r,4);
                if (Blank(p.DefaultClothingStyle)) p.DefaultClothingStyle = S(r,5);
            }
        }

        return p;
    }

    public void Save(NpcAppearanceDetailProfile p)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var adult = p.Age >= 18;
        var female = IsFemale(p.Gender);
        var male = IsMale(p.Gender);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO NpcAppearanceDetailProfiles
                (
                    NpcId, AppearanceLevel, BodyBuild,
                    EyeBaseColor, EyeVariant, EyePattern, EyeShape, EyeExpression, EyeNotes,
                    HairColor, HairUndertone, HairHighlights, HairLength, HairTexture,
                    HairStyle, HairDensity,
                    SkinTone, SkinUndertone, ComplexionDetails,
                    FaceShape, JawShape, NoseShape, LipShape, BrowStyle, CheekboneStyle,
                    DistinguishingFeatures,
                    DefaultClothingStyle, WorkClothingStyle, HomeClothingStyle,
                    GoingOutClothingStyle, ClubClothingStyle, FamilyEventClothingStyle,
                    FormalClothingStyle, AthleticClothingStyle, SleepwearStyle, WinterClothingStyle,
                    BraSize, PenisSize, CircumcisionStatus, AdultAnatomyNotes,
                    UpdatedRealAt
                )
                VALUES
                (
                    $id,$look,$build,
                    $eyeBase,$eyeVariant,$eyePattern,$eyeShape,$eyeExpression,$eyeNotes,
                    $hairColor,$hairUndertone,$hairHighlights,$hairLength,$hairTexture,
                    $hairStyle,$hairDensity,
                    $skin,$skinUnder,$complexion,
                    $face,$jaw,$nose,$lips,$brows,$cheeks,
                    $features,
                    $defaultClothes,$work,$home,$out,$club,$family,$formal,$athletic,$sleep,$winter,
                    $bra,$penis,$circ,$adultNotes,
                    CURRENT_TIMESTAMP
                )
                ON CONFLICT(NpcId) DO UPDATE SET
                    AppearanceLevel=excluded.AppearanceLevel,
                    BodyBuild=excluded.BodyBuild,
                    EyeBaseColor=excluded.EyeBaseColor,
                    EyeVariant=excluded.EyeVariant,
                    EyePattern=excluded.EyePattern,
                    EyeShape=excluded.EyeShape,
                    EyeExpression=excluded.EyeExpression,
                    EyeNotes=excluded.EyeNotes,
                    HairColor=excluded.HairColor,
                    HairUndertone=excluded.HairUndertone,
                    HairHighlights=excluded.HairHighlights,
                    HairLength=excluded.HairLength,
                    HairTexture=excluded.HairTexture,
                    HairStyle=excluded.HairStyle,
                    HairDensity=excluded.HairDensity,
                    SkinTone=excluded.SkinTone,
                    SkinUndertone=excluded.SkinUndertone,
                    ComplexionDetails=excluded.ComplexionDetails,
                    FaceShape=excluded.FaceShape,
                    JawShape=excluded.JawShape,
                    NoseShape=excluded.NoseShape,
                    LipShape=excluded.LipShape,
                    BrowStyle=excluded.BrowStyle,
                    CheekboneStyle=excluded.CheekboneStyle,
                    DistinguishingFeatures=excluded.DistinguishingFeatures,
                    DefaultClothingStyle=excluded.DefaultClothingStyle,
                    WorkClothingStyle=excluded.WorkClothingStyle,
                    HomeClothingStyle=excluded.HomeClothingStyle,
                    GoingOutClothingStyle=excluded.GoingOutClothingStyle,
                    ClubClothingStyle=excluded.ClubClothingStyle,
                    FamilyEventClothingStyle=excluded.FamilyEventClothingStyle,
                    FormalClothingStyle=excluded.FormalClothingStyle,
                    AthleticClothingStyle=excluded.AthleticClothingStyle,
                    SleepwearStyle=excluded.SleepwearStyle,
                    WinterClothingStyle=excluded.WinterClothingStyle,
                    BraSize=excluded.BraSize,
                    PenisSize=excluded.PenisSize,
                    CircumcisionStatus=excluded.CircumcisionStatus,
                    AdultAnatomyNotes=excluded.AdultAnatomyNotes,
                    UpdatedRealAt=CURRENT_TIMESTAMP;
                """;

            Add(cmd,"$id",p.NpcId); Add(cmd,"$look",p.AppearanceLevel); Add(cmd,"$build",p.BodyBuild);
            Add(cmd,"$eyeBase",p.EyeBaseColor); Add(cmd,"$eyeVariant",p.EyeVariant);
            Add(cmd,"$eyePattern",p.EyePattern); Add(cmd,"$eyeShape",p.EyeShape);
            Add(cmd,"$eyeExpression",p.EyeExpression); Add(cmd,"$eyeNotes",p.EyeNotes);
            Add(cmd,"$hairColor",p.HairColor); Add(cmd,"$hairUndertone",p.HairUndertone);
            Add(cmd,"$hairHighlights",p.HairHighlights); Add(cmd,"$hairLength",p.HairLength);
            Add(cmd,"$hairTexture",p.HairTexture); Add(cmd,"$hairStyle",p.HairStyle);
            Add(cmd,"$hairDensity",p.HairDensity);
            Add(cmd,"$skin",p.SkinTone); Add(cmd,"$skinUnder",p.SkinUndertone);
            Add(cmd,"$complexion",p.ComplexionDetails);
            Add(cmd,"$face",p.FaceShape); Add(cmd,"$jaw",p.JawShape); Add(cmd,"$nose",p.NoseShape);
            Add(cmd,"$lips",p.LipShape); Add(cmd,"$brows",p.BrowStyle); Add(cmd,"$cheeks",p.CheekboneStyle);
            Add(cmd,"$features",p.DistinguishingFeatures);
            Add(cmd,"$defaultClothes",p.DefaultClothingStyle); Add(cmd,"$work",p.WorkClothingStyle);
            Add(cmd,"$home",p.HomeClothingStyle); Add(cmd,"$out",p.GoingOutClothingStyle);
            Add(cmd,"$club",p.ClubClothingStyle); Add(cmd,"$family",p.FamilyEventClothingStyle);
            Add(cmd,"$formal",p.FormalClothingStyle); Add(cmd,"$athletic",p.AthleticClothingStyle);
            Add(cmd,"$sleep",p.SleepwearStyle); Add(cmd,"$winter",p.WinterClothingStyle);

            // Adult-only and gender-appropriate. Wrong-sex values are actively cleared.
            Add(cmd,"$bra", adult && female ? p.BraSize : "");
            Add(cmd,"$penis", adult && male ? p.PenisSize : "");
            Add(cmd,"$circ", adult && male ? p.CircumcisionStatus : "");
            Add(cmd,"$adultNotes", adult ? p.AdultAnatomyNotes : "");
            cmd.ExecuteNonQuery();
        }

        EnsurePhysical(conn, tx, p.NpcId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE NpcPhysicalProfiles SET
                    BodyType=$build,
                    HairColor=$hairColor,
                    HairStyle=$hairStyle,
                    EyeColor=$eye,
                    SkinTone=$skin,
                    DefaultClothingStyle=$clothes,
                    UpdatedRealAt=CURRENT_TIMESTAMP
                WHERE NpcId=$id;
                """;
            Add(cmd,"$id",p.NpcId); Add(cmd,"$build",p.BodyBuild); Add(cmd,"$hairColor",p.HairColor);
            Add(cmd,"$hairStyle",ComposeHair(p)); Add(cmd,"$eye",ComposeEyes(p));
            Add(cmd,"$skin",ComposeSkin(p)); Add(cmd,"$clothes",Best(p.DefaultClothingStyle,p.WorkClothingStyle));
            cmd.ExecuteNonQuery();
        }

        using (var identity = conn.CreateCommand())
        {
            identity.Transaction = tx;
            identity.CommandText = """
                UPDATE Characters
                SET RaceEthnicity=$race
                WHERE Id=$id;
                """;
            Add(identity,"$race",p.RaceEthnicity);
            Add(identity,"$id",p.NpcId);
            identity.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static string ComposeEyes(NpcAppearanceDetailProfile p)
        => Join(", ", p.EyeVariant, p.EyePattern, p.EyeShape, p.EyeExpression, p.EyeNotes);

    public static string ComposeHair(NpcAppearanceDetailProfile p)
        => Join(", ", p.HairColor, p.HairUndertone, p.HairHighlights, p.HairLength, p.HairTexture, p.HairStyle, p.HairDensity);

    public static string ComposeSkin(NpcAppearanceDetailProfile p)
        => Join(", ", p.SkinTone, p.SkinUndertone, p.ComplexionDetails);

    public static string ComposeFace(NpcAppearanceDetailProfile p)
        => Join(", ", p.FaceShape, p.JawShape, p.NoseShape, p.LipShape, p.BrowStyle, p.CheekboneStyle, p.DistinguishingFeatures);

    public static string ClothingForImage(NpcAppearanceDetailProfile p, string imageType)
        => imageType switch
        {
            "PhonePicture" or "ContactPicture" => Best(p.GoingOutClothingStyle,p.HomeClothingStyle,p.DefaultClothingStyle),
            "PersonalPicture" => Best(p.HomeClothingStyle,p.DefaultClothingStyle),
            "ClubPicture" => Best(p.ClubClothingStyle,p.GoingOutClothingStyle,p.DefaultClothingStyle),
            "HistoryPicture" or "SchoolHistoryPicture" => Best(p.FamilyEventClothingStyle,p.DefaultClothingStyle),
            "BodyReference" or "FrontReference" or "SideReference" => Best(p.DefaultClothingStyle,p.WorkClothingStyle),
            _ => Best(p.WorkClothingStyle,p.DefaultClothingStyle,p.GoingOutClothingStyle)
        };

    private static void EnsurePhysical(SqliteConnection conn, SqliteTransaction tx, int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO NpcPhysicalProfiles(NpcId,UpdatedRealAt)
            VALUES($id,CURRENT_TIMESTAMP)
            ON CONFLICT(NpcId) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("$id",npcId);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureSchema(SqliteConnection conn)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS NpcAppearanceDetailProfiles
                (
                    NpcId INTEGER PRIMARY KEY,
                    AppearanceLevel TEXT NOT NULL DEFAULT '',
                    BodyBuild TEXT NOT NULL DEFAULT '',
                    EyeBaseColor TEXT NOT NULL DEFAULT '',
                    EyeVariant TEXT NOT NULL DEFAULT '',
                    EyePattern TEXT NOT NULL DEFAULT '',
                    EyeShape TEXT NOT NULL DEFAULT '',
                    EyeExpression TEXT NOT NULL DEFAULT '',
                    EyeNotes TEXT NOT NULL DEFAULT '',
                    HairColor TEXT NOT NULL DEFAULT '',
                    HairUndertone TEXT NOT NULL DEFAULT '',
                    HairHighlights TEXT NOT NULL DEFAULT '',
                    HairLength TEXT NOT NULL DEFAULT '',
                    HairTexture TEXT NOT NULL DEFAULT '',
                    HairStyle TEXT NOT NULL DEFAULT '',
                    HairDensity TEXT NOT NULL DEFAULT '',
                    SkinTone TEXT NOT NULL DEFAULT '',
                    SkinUndertone TEXT NOT NULL DEFAULT '',
                    ComplexionDetails TEXT NOT NULL DEFAULT '',
                    FaceShape TEXT NOT NULL DEFAULT '',
                    JawShape TEXT NOT NULL DEFAULT '',
                    NoseShape TEXT NOT NULL DEFAULT '',
                    LipShape TEXT NOT NULL DEFAULT '',
                    BrowStyle TEXT NOT NULL DEFAULT '',
                    CheekboneStyle TEXT NOT NULL DEFAULT '',
                    DistinguishingFeatures TEXT NOT NULL DEFAULT '',
                    DefaultClothingStyle TEXT NOT NULL DEFAULT '',
                    WorkClothingStyle TEXT NOT NULL DEFAULT '',
                    HomeClothingStyle TEXT NOT NULL DEFAULT '',
                    GoingOutClothingStyle TEXT NOT NULL DEFAULT '',
                    ClubClothingStyle TEXT NOT NULL DEFAULT '',
                    FamilyEventClothingStyle TEXT NOT NULL DEFAULT '',
                    FormalClothingStyle TEXT NOT NULL DEFAULT '',
                    AthleticClothingStyle TEXT NOT NULL DEFAULT '',
                    SleepwearStyle TEXT NOT NULL DEFAULT '',
                    WinterClothingStyle TEXT NOT NULL DEFAULT '',
                    BraSize TEXT NOT NULL DEFAULT '',
                    PenisSize TEXT NOT NULL DEFAULT '',
                    CircumcisionStatus TEXT NOT NULL DEFAULT '',
                    AdultAnatomyNotes TEXT NOT NULL DEFAULT '',
                    UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """;
            cmd.ExecuteNonQuery();
        }

        // Migration-safe upgrades from earlier ProjectEve appearance-detail versions.
        var columns = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AppearanceLevel"]="TEXT NOT NULL DEFAULT ''",
            ["BodyBuild"]="TEXT NOT NULL DEFAULT ''",

            ["EyeBaseColor"]="TEXT NOT NULL DEFAULT ''",
            ["EyeVariant"]="TEXT NOT NULL DEFAULT ''",
            ["EyePattern"]="TEXT NOT NULL DEFAULT ''",
            ["EyeShape"]="TEXT NOT NULL DEFAULT ''",
            ["EyeExpression"]="TEXT NOT NULL DEFAULT ''",
            ["EyeNotes"]="TEXT NOT NULL DEFAULT ''",

            ["HairColor"]="TEXT NOT NULL DEFAULT ''",
            ["HairUndertone"]="TEXT NOT NULL DEFAULT ''",
            ["HairHighlights"]="TEXT NOT NULL DEFAULT ''",
            ["HairLength"]="TEXT NOT NULL DEFAULT ''",
            ["HairTexture"]="TEXT NOT NULL DEFAULT ''",
            ["HairStyle"]="TEXT NOT NULL DEFAULT ''",
            ["HairDensity"]="TEXT NOT NULL DEFAULT ''",

            ["SkinTone"]="TEXT NOT NULL DEFAULT ''",
            ["SkinUndertone"]="TEXT NOT NULL DEFAULT ''",
            ["ComplexionDetails"]="TEXT NOT NULL DEFAULT ''",

            ["FaceShape"]="TEXT NOT NULL DEFAULT ''",
            ["JawShape"]="TEXT NOT NULL DEFAULT ''",
            ["NoseShape"]="TEXT NOT NULL DEFAULT ''",
            ["LipShape"]="TEXT NOT NULL DEFAULT ''",
            ["BrowStyle"]="TEXT NOT NULL DEFAULT ''",
            ["CheekboneStyle"]="TEXT NOT NULL DEFAULT ''",
            ["DistinguishingFeatures"]="TEXT NOT NULL DEFAULT ''",

            ["DefaultClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["WorkClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["HomeClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["GoingOutClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["ClubClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["FamilyEventClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["FormalClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["AthleticClothingStyle"]="TEXT NOT NULL DEFAULT ''",
            ["SleepwearStyle"]="TEXT NOT NULL DEFAULT ''",
            ["WinterClothingStyle"]="TEXT NOT NULL DEFAULT ''",

            ["BraSize"]="TEXT NOT NULL DEFAULT ''",
            ["PenisSize"]="TEXT NOT NULL DEFAULT ''",
            ["CircumcisionStatus"]="TEXT NOT NULL DEFAULT ''",
            ["AdultAnatomyNotes"]="TEXT NOT NULL DEFAULT ''",
            ["UpdatedRealAt"]="TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP"
        };

        var existing = GetColumns(conn,"NpcAppearanceDetailProfiles");
        foreach (var pair in columns)
        {
            if (existing.Contains(pair.Key)) continue;
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE NpcAppearanceDetailProfiles ADD COLUMN [{pair.Key}] {pair.Value};";
            alter.ExecuteNonQuery();
        }
    }

    private static HashSet<string> GetColumns(SqliteConnection conn, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(1));
        return result;
    }

    private static bool IsFemale(string? value)
        => (value ?? "").Contains("female",StringComparison.OrdinalIgnoreCase)
           || string.Equals(value,"woman",StringComparison.OrdinalIgnoreCase);

    private static bool IsMale(string? value)
        => (value ?? "").Contains("male",StringComparison.OrdinalIgnoreCase)
           && !(value ?? "").Contains("female",StringComparison.OrdinalIgnoreCase)
           || string.Equals(value,"man",StringComparison.OrdinalIgnoreCase);

    private static string Best(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";

    private static string Join(string separator, params string?[] values)
        => string.Join(separator, values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static string S(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";
    private static void Add(SqliteCommand cmd,string name,object? value) => cmd.Parameters.AddWithValue(name,value ?? "");
}

public sealed class NpcAppearanceDetailProfile
{
    public int NpcId { get; set; }
    public int Age { get; set; }
    public int Tier { get; set; } = 5;
    public string Gender { get; set; } = "";
    public string RaceEthnicity { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string Location { get; set; } = "";
    public string PersonalitySummary { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Need { get; set; } = "";
    public string Fear { get; set; } = "";
    public string Want { get; set; } = "";

    public string AppearanceLevel { get; set; } = "";
    public string BodyBuild { get; set; } = "";

    public string EyeBaseColor { get; set; } = "";
    public string EyeVariant { get; set; } = "";
    public string EyePattern { get; set; } = "";
    public string EyeShape { get; set; } = "";
    public string EyeExpression { get; set; } = "";
    public string EyeNotes { get; set; } = "";

    public string HairColor { get; set; } = "";
    public string HairUndertone { get; set; } = "";
    public string HairHighlights { get; set; } = "";
    public string HairLength { get; set; } = "";
    public string HairTexture { get; set; } = "";
    public string HairStyle { get; set; } = "";
    public string HairDensity { get; set; } = "";

    public string SkinTone { get; set; } = "";
    public string SkinUndertone { get; set; } = "";
    public string ComplexionDetails { get; set; } = "";

    public string FaceShape { get; set; } = "";
    public string JawShape { get; set; } = "";
    public string NoseShape { get; set; } = "";
    public string LipShape { get; set; } = "";
    public string BrowStyle { get; set; } = "";
    public string CheekboneStyle { get; set; } = "";
    public string DistinguishingFeatures { get; set; } = "";

    public string DefaultClothingStyle { get; set; } = "";
    public string WorkClothingStyle { get; set; } = "";
    public string HomeClothingStyle { get; set; } = "";
    public string GoingOutClothingStyle { get; set; } = "";
    public string ClubClothingStyle { get; set; } = "";
    public string FamilyEventClothingStyle { get; set; } = "";
    public string FormalClothingStyle { get; set; } = "";
    public string AthleticClothingStyle { get; set; } = "";
    public string SleepwearStyle { get; set; } = "";
    public string WinterClothingStyle { get; set; } = "";

    public string BraSize { get; set; } = "";
    public string PenisSize { get; set; } = "";
    public string CircumcisionStatus { get; set; } = "";
    public string AdultAnatomyNotes { get; set; } = "";
}


