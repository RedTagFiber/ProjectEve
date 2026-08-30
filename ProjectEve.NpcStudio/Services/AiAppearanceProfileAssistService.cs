using System.Net.Http.Json;
using System.Text.Json;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

public sealed class AiAppearanceProfileAssistService
{
    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;
    private readonly NpcAppearanceDetailService _appearance;

    public AiAppearanceProfileAssistService(
        HttpClient http,
        NpcStudioOptions options,
        NpcAppearanceDetailService appearance)
    {
        _http = http;
        _options = options;
        _appearance = appearance;
    }

    public async Task<NpcAppearanceDetailProfile> BuildDraftAsync(
        int npcId,
        CancellationToken cancellationToken = default)
    {
        var current = _appearance.Load(npcId);

        var prompt = $$"""
You are the ProjectEve canonical NPC appearance author.

Create ONE coherent appearance draft for this existing NPC.
Do NOT choose unrelated random traits. Every choice must make sense with age,
gender, race/ethnicity, occupation, income/social role implied by tier,
location, personality, goals, needs, fears, wants, and existing canonical facts.

NPC:
age: {{current.Age}}
gender: {{current.Gender}}
raceEthnicity: {{current.RaceEthnicity}}
occupation: {{current.Occupation}}
location: {{current.Location}}
tier: {{current.Tier}}
personality: {{current.PersonalitySummary}}
goal: {{current.Goal}}
need: {{current.Need}}
fear: {{current.Fear}}
want: {{current.Want}}

EXISTING APPEARANCE FACTS ARE CANONICAL CONSTRAINTS WHEN NONEMPTY:
appearanceLevel: {{current.AppearanceLevel}}
bodyBuild: {{current.BodyBuild}}
eyes: {{NpcAppearanceDetailService.ComposeEyes(current)}}
hair: {{NpcAppearanceDetailService.ComposeHair(current)}}
skin: {{NpcAppearanceDetailService.ComposeSkin(current)}}
face: {{NpcAppearanceDetailService.ComposeFace(current)}}
work clothing: {{current.WorkClothingStyle}}
home clothing: {{current.HomeClothingStyle}}
going out clothing: {{current.GoingOutClothingStyle}}
club clothing: {{current.ClubClothingStyle}}
family event clothing: {{current.FamilyEventClothingStyle}}

Rules:
- Preserve existing nonempty canonical facts unless they conflict internally.
- Fill missing details with plausible, distinctive choices.
- Small details should make this NPC feel individual, not generic.
- Related visual details should harmonize.
- Race/ethnicity informs plausible complexion range but never forces one exact tone.
- Clothing must fit age, occupation, personality, income, Bellefontaine/Ohio life, and occasion.
- "Not a Club Person" is valid when personality/lifestyle suggests it.
- Adult anatomy is ONLY for age >= 18.
- If female, braSize may be filled and penisSize/circumcisionStatus MUST be empty.
- If male, penisSize/circumcisionStatus may be filled and braSize MUST be empty.
- If under 18, braSize, penisSize, circumcisionStatus, adultAnatomyNotes MUST all be empty.
- Do not sexualize minors.
- Output JSON only. No markdown.

Return exactly:
{
  "appearanceLevel":"",
  "bodyBuild":"",
  "eyeBaseColor":"",
  "eyeVariant":"",
  "eyePattern":"",
  "eyeShape":"",
  "eyeExpression":"",
  "eyeNotes":"",
  "hairColor":"",
  "hairUndertone":"",
  "hairHighlights":"",
  "hairLength":"",
  "hairTexture":"",
  "hairStyle":"",
  "hairDensity":"",
  "skinTone":"",
  "skinUndertone":"",
  "complexionDetails":"",
  "faceShape":"",
  "jawShape":"",
  "noseShape":"",
  "lipShape":"",
  "browStyle":"",
  "cheekboneStyle":"",
  "distinguishingFeatures":"",
  "defaultClothingStyle":"",
  "workClothingStyle":"",
  "homeClothingStyle":"",
  "goingOutClothingStyle":"",
  "clubClothingStyle":"",
  "familyEventClothingStyle":"",
  "formalClothingStyle":"",
  "athleticClothingStyle":"",
  "sleepwearStyle":"",
  "winterClothingStyle":"",
  "braSize":"",
  "penisSize":"",
  "circumcisionStatus":"",
  "adultAnatomyNotes":""
}
""";

        var request = new
        {
            model = _options.OllamaModel,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0.55, num_predict = 1800 }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(90));

        using var response = await _http.PostAsJsonAsync(
            new Uri(new Uri(_options.OllamaBaseUrl), "/api/generate"),
            request,
            cts.Token);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var doc = await JsonDocument.ParseAsync(stream,cancellationToken:cts.Token);
        var raw = doc.RootElement.TryGetProperty("response",out var value) ? value.GetString() ?? "{}" : "{}";
        var json = ExtractJson(raw);

        var draft = JsonSerializer.Deserialize<AppearanceAiDraft>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("AI returned no appearance draft.");

        Apply(current,draft);
        EnforceAdultGenderRules(current);
        return current;
    }

    private static void Apply(NpcAppearanceDetailProfile p, AppearanceAiDraft d)
    {
        // Preserve current authored values where present; AI fills missing fields.
        p.AppearanceLevel = Choose(p.AppearanceLevel,d.AppearanceLevel);
        p.BodyBuild = Choose(p.BodyBuild,d.BodyBuild);
        p.EyeBaseColor = Choose(p.EyeBaseColor,d.EyeBaseColor);
        p.EyeVariant = Choose(p.EyeVariant,d.EyeVariant);
        p.EyePattern = Choose(p.EyePattern,d.EyePattern);
        p.EyeShape = Choose(p.EyeShape,d.EyeShape);
        p.EyeExpression = Choose(p.EyeExpression,d.EyeExpression);
        p.EyeNotes = Choose(p.EyeNotes,d.EyeNotes);
        p.HairColor = Choose(p.HairColor,d.HairColor);
        p.HairUndertone = Choose(p.HairUndertone,d.HairUndertone);
        p.HairHighlights = Choose(p.HairHighlights,d.HairHighlights);
        p.HairLength = Choose(p.HairLength,d.HairLength);
        p.HairTexture = Choose(p.HairTexture,d.HairTexture);
        p.HairStyle = Choose(p.HairStyle,d.HairStyle);
        p.HairDensity = Choose(p.HairDensity,d.HairDensity);
        p.SkinTone = Choose(p.SkinTone,d.SkinTone);
        p.SkinUndertone = Choose(p.SkinUndertone,d.SkinUndertone);
        p.ComplexionDetails = Choose(p.ComplexionDetails,d.ComplexionDetails);
        p.FaceShape = Choose(p.FaceShape,d.FaceShape);
        p.JawShape = Choose(p.JawShape,d.JawShape);
        p.NoseShape = Choose(p.NoseShape,d.NoseShape);
        p.LipShape = Choose(p.LipShape,d.LipShape);
        p.BrowStyle = Choose(p.BrowStyle,d.BrowStyle);
        p.CheekboneStyle = Choose(p.CheekboneStyle,d.CheekboneStyle);
        p.DistinguishingFeatures = Choose(p.DistinguishingFeatures,d.DistinguishingFeatures);
        p.DefaultClothingStyle = Choose(p.DefaultClothingStyle,d.DefaultClothingStyle);
        p.WorkClothingStyle = Choose(p.WorkClothingStyle,d.WorkClothingStyle);
        p.HomeClothingStyle = Choose(p.HomeClothingStyle,d.HomeClothingStyle);
        p.GoingOutClothingStyle = Choose(p.GoingOutClothingStyle,d.GoingOutClothingStyle);
        p.ClubClothingStyle = Choose(p.ClubClothingStyle,d.ClubClothingStyle);
        p.FamilyEventClothingStyle = Choose(p.FamilyEventClothingStyle,d.FamilyEventClothingStyle);
        p.FormalClothingStyle = Choose(p.FormalClothingStyle,d.FormalClothingStyle);
        p.AthleticClothingStyle = Choose(p.AthleticClothingStyle,d.AthleticClothingStyle);
        p.SleepwearStyle = Choose(p.SleepwearStyle,d.SleepwearStyle);
        p.WinterClothingStyle = Choose(p.WinterClothingStyle,d.WinterClothingStyle);
        p.BraSize = Choose(p.BraSize,d.BraSize);
        p.PenisSize = Choose(p.PenisSize,d.PenisSize);
        p.CircumcisionStatus = Choose(p.CircumcisionStatus,d.CircumcisionStatus);
        p.AdultAnatomyNotes = Choose(p.AdultAnatomyNotes,d.AdultAnatomyNotes);
    }

    private static void EnforceAdultGenderRules(NpcAppearanceDetailProfile p)
    {
        if (p.Age < 18)
        {
            p.BraSize = p.PenisSize = p.CircumcisionStatus = p.AdultAnatomyNotes = "";
            return;
        }

        var g = p.Gender ?? "";
        var female = g.Contains("female",StringComparison.OrdinalIgnoreCase) || g.Equals("woman",StringComparison.OrdinalIgnoreCase);
        var male = (g.Contains("male",StringComparison.OrdinalIgnoreCase) && !g.Contains("female",StringComparison.OrdinalIgnoreCase))
                   || g.Equals("man",StringComparison.OrdinalIgnoreCase);

        if (female)
        {
            p.PenisSize = "";
            p.CircumcisionStatus = "";
        }
        if (male) p.BraSize = "";
    }

    private static string Choose(string current,string? proposed)
        => !string.IsNullOrWhiteSpace(current) ? current : proposed?.Trim() ?? "";

    private static string ExtractJson(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.StartsWith("```"))
        {
            var first = t.IndexOf('\n');
            if (first >= 0) t = t[(first + 1)..];
            var endFence = t.LastIndexOf("```",StringComparison.Ordinal);
            if (endFence >= 0) t = t[..endFence];
        }
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start < 0 || end < start) throw new InvalidOperationException("AI response did not contain JSON.");
        return t[start..(end+1)];
    }

    private sealed class AppearanceAiDraft
    {
        public string AppearanceLevel { get; set; }="";
        public string BodyBuild { get; set; }="";
        public string EyeBaseColor { get; set; }="";
        public string EyeVariant { get; set; }="";
        public string EyePattern { get; set; }="";
        public string EyeShape { get; set; }="";
        public string EyeExpression { get; set; }="";
        public string EyeNotes { get; set; }="";
        public string HairColor { get; set; }="";
        public string HairUndertone { get; set; }="";
        public string HairHighlights { get; set; }="";
        public string HairLength { get; set; }="";
        public string HairTexture { get; set; }="";
        public string HairStyle { get; set; }="";
        public string HairDensity { get; set; }="";
        public string SkinTone { get; set; }="";
        public string SkinUndertone { get; set; }="";
        public string ComplexionDetails { get; set; }="";
        public string FaceShape { get; set; }="";
        public string JawShape { get; set; }="";
        public string NoseShape { get; set; }="";
        public string LipShape { get; set; }="";
        public string BrowStyle { get; set; }="";
        public string CheekboneStyle { get; set; }="";
        public string DistinguishingFeatures { get; set; }="";
        public string DefaultClothingStyle { get; set; }="";
        public string WorkClothingStyle { get; set; }="";
        public string HomeClothingStyle { get; set; }="";
        public string GoingOutClothingStyle { get; set; }="";
        public string ClubClothingStyle { get; set; }="";
        public string FamilyEventClothingStyle { get; set; }="";
        public string FormalClothingStyle { get; set; }="";
        public string AthleticClothingStyle { get; set; }="";
        public string SleepwearStyle { get; set; }="";
        public string WinterClothingStyle { get; set; }="";
        public string BraSize { get; set; }="";
        public string PenisSize { get; set; }="";
        public string CircumcisionStatus { get; set; }="";
        public string AdultAnatomyNotes { get; set; }="";
    }
}

