using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectEve.NpcStudio.Models;
namespace ProjectEve.NpcStudio.Services;

public sealed class BhsStaffDraftService
{
    private readonly NpcStudioOptions _options;

    public BhsStaffDraftService(NpcStudioOptions options)
    {
        _options = options;
    }

    public async Task<BhsStaffDraftBatch> GenerateAsync(int batchSize = 5, CancellationToken cancellationToken = default)
    {
        batchSize = Math.Clamp(batchSize, 1, 5);

        var roles = new[]
        {
            "Principal",
            "Associate Principal",
            "Guidance Counselor",
            "School Nurse",
            "Attendance Officer / Secretary",
            "Administrative Secretary",
            "Head Custodian",
            "Custodian / Janitor",
            "Food Service / Cafeteria",
            "Intervention Specialist",
            "Educational Aide / Paraprofessional",
            "Athletic / Activities Director",
            "School Resource Officer"
        };

        var selected = roles.Take(batchSize).ToArray();

        var prompt = """
You are ProjectEve NPC Studio.

Create a COMPLETE FICTIONAL adult staff draft for Bellefontaine High School in Bellefontaine, Ohio.
Do not use the names of real Bellefontaine school employees.
Every proposed adult must feel like a complete person and must be internally consistent.

For each requested role, return ONE fictional adult NPC draft with:
- first, middle, last name
- age and gender, race/ethnicity
- marital status
- spouse proposal when appropriate; spouse must be a COMPLETE Tier-5 NPC with gender, race/ethnicity, job/employer, education, banking, own vehicle, own phone, appearance, traits, personality, goals/needs/fears
- children proposals when appropriate; children are COMPLETE Tier-5 NPCs. If school age, include current school, grade, school year, GPA/performance, attendance, activities, sports, social notes, teacher/mentor notes, and non-canonical history hooks. Minor appearance must remain age-appropriate and development-neutral.
- occupation and exact school role
- annual salary estimate appropriate to the role
- education: high school, college/trade, degree/credential, certifications
- banking: checking, savings, approximate balances, major debts/obligations
- housing: plausible Bellefontaine neighborhood/street name and housing type based on household income
- vehicle: year, make, model, financing status
- phone/device
- appearance: height, weight, build, hair, eyes, skin tone, clothing style, distinguishing features
- adult anatomy profile fields when relevant to the existing ProjectEve physical schema
- IQ estimate appropriate to education, occupation, and individual variation; use a realistic integer, not 0
- ProjectEve archetype seed: choose 1 required primary and up to 2 secondary archetypes ONLY from:
  Heart, Connector, Outsider, Protector, Rival, Instigator, Caregiver, Authority,
  Wildcard, Mentor, Drifter, Social Hub, Troublemaker, Survivor, Observer, Hothead,
  Control Freak, Manipulator, Gossip, Opportunist, Bitter, Jealous, Arrogant, Cold,
  Cruel, Unreliable, Reckless, Vindictive, Bully
- a concrete personal Want distinct from Goal and Need
- hidden behavior/private contradiction when plausible
- personality / traits summary
- hobbies/interests
- public persona, private persona, goals, needs, fears
- work reputation and professional strengths
- notes about how this person could connect to students/families later

IMPORTANT:
- This is PREVIEW ONLY.
- Do not invent history events or EventIds.
- Do not assign friendships, romances, enemies, or memories yet.
- Do not create any under-18 sexualized or intimate anatomy detail.
- Do not use exact real residential house numbers.
- Street/neighborhood may be Bellefontaine-realistic, but residence must remain fictional.
- Return valid JSON only.
- certifications MUST be an array of plain strings, not objects.
- traits, hobbies, strengths, activities, sports, and connection hooks MUST also be arrays of plain strings.

ROLES:
__ROLES__

JSON:
{
  "summary": "",
  "staff": [
    {
      "roleTitle": "",
      "firstName": "",
      "middleName": "",
      "lastName": "",
      "age": 0,
      "gender": "",
      "raceEthnicity": "",
      "iq": 0,
      "archetype1": "",
      "archetype2": "",
      "archetype3": "",
      "want": "",
      "hiddenBehavior": "",
      "tier": 3,
      "maritalStatus": "",
      "occupation": "",
      "annualSalary": 0,
      "education": [
        {
          "level": "",
          "institution": "",
          "credential": "",
          "field": "",
          "status": ""
        }
      ],
      "certifications": [],
      "banking": {
        "checkingBalance": 0,
        "savingsBalance": 0,
        "monthlyDebtPayments": 0,
        "debtSummary": ""
      },
      "home": {
        "streetOrArea": "",
        "housingType": "",
        "estimatedMonthlyHousingCost": 0,
        "notes": ""
      },
      "vehicle": {
        "year": 0,
        "make": "",
        "model": "",
        "financingStatus": "",
        "monthlyPayment": 0
      },
      "phone": {
        "device": "",
        "carrier": ""
      },
      "appearance": {
        "heightText": "",
        "weightLb": 0,
        "bodyType": "",
        "hairColor": "",
        "hairStyle": "",
        "eyeColor": "",
        "skinTone": "",
        "clothingStyle": "",
        "distinguishingFeatures": "",
        "adultAnatomyNotes": ""
      },
      "personality": {
        "summary": "",
        "traits": [],
        "hobbies": [],
        "publicPersona": "",
        "privatePersona": "",
        "goal": "",
        "need": "",
        "fear": ""
      },
      "professional": {
        "reputation": "",
        "strengths": [],
        "studentFamilyConnectionHooks": []
      },
      "spouse": {
        "include": false,
        "name": "",
        "age": 0,
        "gender": "",
        "raceEthnicity": "",
      "iq": 0,
      "archetype1": "",
      "archetype2": "",
      "archetype3": "",
      "want": "",
      "hiddenBehavior": "",
        "occupation": "",
        "employer": "",
        "tier": 5,
        "education": [],
        "banking": {"checkingBalance":0,"savingsBalance":0,"monthlyDebtPayments":0,"debtSummary":""},
        "vehicle": {"year":0,"make":"","model":"","financingStatus":"","monthlyPayment":0},
        "phone": {"device":"","carrier":""},
        "appearance": {"heightText":"","weightLb":0,"bodyType":"","hairColor":"","hairStyle":"","eyeColor":"","skinTone":"","clothingStyle":"","distinguishingFeatures":"","adultAnatomyNotes":""},
        "traits": [],
        "personalitySummary": "",
        "publicPersona": "",
        "privatePersona": "",
        "goal": "",
        "need": "",
        "fear": ""
      },
      "children": [
        {
          "name": "",
          "age": 0,
          "gender": "",
          "raceEthnicity": "",
      "iq": 0,
      "archetype1": "",
      "archetype2": "",
      "archetype3": "",
      "want": "",
      "hiddenBehavior": "",
          "tier": 5,
          "schoolId": "",
          "gradeLevel": "",
          "academicYear": "",
          "gpa": 0,
          "academicPerformance": "",
          "attendanceSummary": "",
          "activities": [],
          "sports": [],
          "socialNotes": "",
          "teacherMentorNotes": "",
          "historyHooks": "",
          "phone": {"device":"","carrier":""},
          "appearance": {"heightText":"","weightLb":0,"bodyType":"","hairColor":"","hairStyle":"","eyeColor":"","skinTone":"","clothingStyle":"","distinguishingFeatures":"","adultAnatomyNotes":""},
          "traits": [],
          "personalitySummary": "",
          "publicPersona": "",
          "privatePersona": "",
          "goal": "",
          "need": "",
          "fear": ""
        }
      ]
    }
  ]
}
""";

        prompt = prompt.Replace("__ROLES__", string.Join("\n", selected.Select(x => "- " + x)));

        using var client = new HttpClient
        {
            BaseAddress = new Uri(_options.OllamaBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(180)
        };

        var request = new
        {
            model = _options.OllamaModel,
            prompt,
            stream = false,
            format = "json"
        };

        using var response = await client.PostAsJsonAsync("api/generate", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;

        if (!root.TryGetProperty("response", out var responseNode))
            throw new InvalidOperationException("Ollama response did not include a response field.");

        var json = responseNode.GetString();
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Ollama returned an empty staff draft.");

        var result = JsonSerializer.Deserialize<BhsStaffDraftBatch>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (result is null || result.Staff.Count == 0)
            throw new InvalidOperationException("AI did not return any BHS staff drafts.");

        foreach (var item in result.Staff)
        {
            item.Education ??= new();
            item.Certifications ??= new();
            item.Banking ??= new();
            item.Home ??= new();
            item.Vehicle ??= new();
            item.Phone ??= new();
            item.Appearance ??= new();
            item.Personality ??= new();
            item.Professional ??= new();
            item.Spouse ??= new();
            item.Children ??= new();

            item.Spouse.Education ??= new();
            item.Spouse.Banking ??= new();
            item.Spouse.Vehicle ??= new();
            item.Spouse.Phone ??= new();
            item.Spouse.Appearance ??= new();
            item.Spouse.Traits ??= new();

            foreach (var child in item.Children)
            {
                child.Activities ??= new();
                child.Sports ??= new();
                child.Phone ??= new();
                child.Appearance ??= new();
                child.Traits ??= new();
            }

            item.Tier = item.RoleTitle.Contains("Principal", StringComparison.OrdinalIgnoreCase) ? 3 : Math.Clamp(item.Tier, 3, 4);
        }

        return result;
    }
}

public sealed class BhsStaffDraftBatch
{
    public string Summary { get; set; } = "";
    public List<BhsStaffDraft> Staff { get; set; } = new();
}

public sealed class BhsStaffDraft
{
    // Canonical demographic field used by staff generation and profile persistence.
    public string RaceEthnicity { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string MiddleName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public int Tier { get; set; } = 3;
    public int IQ { get; set; }
    public string Archetype1 { get; set; } = "";
    public string Archetype2 { get; set; } = "";
    public string Archetype3 { get; set; } = "";
    public string Want { get; set; } = "";
    public string HiddenBehavior { get; set; } = "";
    public string MaritalStatus { get; set; } = "";
    public string Occupation { get; set; } = "";
    public decimal AnnualSalary { get; set; }
    public List<BhsStaffEducationDraft> Education { get; set; } = new();
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Certifications { get; set; } = new();
    public BhsStaffBankingDraft Banking { get; set; } = new();
    public BhsStaffHomeDraft Home { get; set; } = new();
    public BhsStaffVehicleDraft Vehicle { get; set; } = new();
    public BhsStaffPhoneDraft Phone { get; set; } = new();
    public BhsStaffAppearanceDraft Appearance { get; set; } = new();
    public BhsStaffPersonalityDraft Personality { get; set; } = new();
    public BhsStaffProfessionalDraft Professional { get; set; } = new();
    public BhsStaffSpouseDraft Spouse { get; set; } = new();
    public List<BhsStaffChildDraft> Children { get; set; } = new();

    public string FullName =>
        string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class BhsStaffEducationDraft
{
    public string Level { get; set; } = "";
    public string Institution { get; set; } = "";
    public string Credential { get; set; } = "";
    public string Field { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class BhsStaffBankingDraft
{
    public decimal CheckingBalance { get; set; }
    public decimal SavingsBalance { get; set; }
    public decimal MonthlyDebtPayments { get; set; }
    public string DebtSummary { get; set; } = "";
}

public sealed class BhsStaffHomeDraft
{
    public string StreetOrArea { get; set; } = "";
    public string HousingType { get; set; } = "";
    public decimal EstimatedMonthlyHousingCost { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class BhsStaffVehicleDraft
{
    public int Year { get; set; }
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string FinancingStatus { get; set; } = "";
    public decimal MonthlyPayment { get; set; }
}

public sealed class BhsStaffPhoneDraft
{
    public string Device { get; set; } = "";
    public string Carrier { get; set; } = "";
}

public sealed class BhsStaffAppearanceDraft
{
    public string HeightText { get; set; } = "";
    public decimal WeightLb { get; set; }
    public string BodyType { get; set; } = "";
    public string HairColor { get; set; } = "";
    public string HairStyle { get; set; } = "";
    public string EyeColor { get; set; } = "";
    public string SkinTone { get; set; } = "";
    public string ClothingStyle { get; set; } = "";
    public string DistinguishingFeatures { get; set; } = "";
    public string AdultAnatomyNotes { get; set; } = "";
}

public sealed class BhsStaffPersonalityDraft
{
    public string Summary { get; set; } = "";
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Traits { get; set; } = new();
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Hobbies { get; set; } = new();
    public string PublicPersona { get; set; } = "";
    public string PrivatePersona { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Need { get; set; } = "";
    public string Fear { get; set; } = "";
}

public sealed class BhsStaffProfessionalDraft
{
    public string Reputation { get; set; } = "";
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Strengths { get; set; } = new();
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> StudentFamilyConnectionHooks { get; set; } = new();
}

public sealed class BhsStaffSpouseDraft
{
    public bool Include { get; set; }
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public string RaceEthnicity { get; set; } = "";
    public int IQ { get; set; }
    public string Archetype1 { get; set; } = "";
    public string Archetype2 { get; set; } = "";
    public string Archetype3 { get; set; } = "";
    public string Want { get; set; } = "";
    public string HiddenBehavior { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";
    public int Tier { get; set; } = 5;
    public List<BhsStaffEducationDraft> Education { get; set; } = new();
    public BhsStaffBankingDraft Banking { get; set; } = new();
    public BhsStaffVehicleDraft Vehicle { get; set; } = new();
    public BhsStaffPhoneDraft Phone { get; set; } = new();
    public BhsStaffAppearanceDraft Appearance { get; set; } = new();
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Traits { get; set; } = new();
    public string PersonalitySummary { get; set; } = "";
    public string PublicPersona { get; set; } = "";
    public string PrivatePersona { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Need { get; set; } = "";
    public string Fear { get; set; } = "";
}

public sealed class BhsStaffChildDraft
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string Gender { get; set; } = "";
    public int IQ { get; set; }
    public string Archetype1 { get; set; } = "";
    public string Archetype2 { get; set; } = "";
    public string Archetype3 { get; set; } = "";
    public string Want { get; set; } = "";
    public string HiddenBehavior { get; set; } = "";
    public string RaceEthnicity { get; set; } = "";
    public int Tier { get; set; } = 5;
    public string SchoolId { get; set; } = "";
    public string GradeLevel { get; set; } = "";
    public string AcademicYear { get; set; } = "";
    public decimal Gpa { get; set; }
    public string AcademicPerformance { get; set; } = "";
    public string AttendanceSummary { get; set; } = "";
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Activities { get; set; } = new();
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Sports { get; set; } = new();
    public string SocialNotes { get; set; } = "";
    public string TeacherMentorNotes { get; set; } = "";
    public string HistoryHooks { get; set; } = "";
    public BhsStaffPhoneDraft Phone { get; set; } = new();
    public BhsStaffAppearanceDraft Appearance { get; set; } = new();
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string> Traits { get; set; } = new();
    public string PersonalitySummary { get; set; } = "";
    public string PublicPersona { get; set; } = "";
    public string PrivatePersona { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Need { get; set; } = "";
    public string Fear { get; set; } = "";
}

/// <summary>
/// Ollama sometimes returns a list item as an object instead of a plain string
/// (for example a certification with name/issuer fields). This converter accepts
/// either shape and turns it into readable text instead of failing the whole draft.
/// </summary>
public sealed class FlexibleStringListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var result = new List<string>();

        if (reader.TokenType == JsonTokenType.Null)
            return result;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array.");

        using var document = JsonDocument.ParseValue(ref reader);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(text.Trim());
                    break;
                }

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    result.Add(element.ToString());
                    break;

                case JsonValueKind.Object:
                {
                    var preferredKeys = new[]
                    {
                        "name", "title", "certification", "credential",
                        "trait", "activity", "sport", "strength",
                        "hook", "description", "value", "issuer"
                    };

                    var pieces = new List<string>();

                    foreach (var key in preferredKeys)
                    {
                        if (element.TryGetProperty(key, out var prop) &&
                            prop.ValueKind == JsonValueKind.String)
                        {
                            var value = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(value) &&
                                !pieces.Contains(value, StringComparer.OrdinalIgnoreCase))
                            {
                                pieces.Add(value.Trim());
                            }
                        }
                    }

                    if (pieces.Count == 0)
                    {
                        foreach (var property in element.EnumerateObject())
                        {
                            if (property.Value.ValueKind == JsonValueKind.String)
                            {
                                var value = property.Value.GetString();
                                if (!string.IsNullOrWhiteSpace(value) &&
                                    !pieces.Contains(value, StringComparer.OrdinalIgnoreCase))
                                {
                                    pieces.Add(value.Trim());
                                }
                            }
                        }
                    }

                    if (pieces.Count > 0)
                        result.Add(string.Join(" · ", pieces));
                    else
                        result.Add(element.GetRawText());

                    break;
                }

                default:
                    result.Add(element.GetRawText());
                    break;
            }
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        List<string> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value ?? new List<string>())
            writer.WriteStringValue(item ?? "");

        writer.WriteEndArray();
    }
}




