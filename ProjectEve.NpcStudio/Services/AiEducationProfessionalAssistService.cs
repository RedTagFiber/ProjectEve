using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// AI assistant for Education & Professional.
/// Produces a preview only. It never writes canon.
/// The user applies the suggestion to the form, edits it, then explicitly saves.
/// </summary>
public sealed class AiEducationProfessionalAssistService
{
    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;

    public AiEducationProfessionalAssistService(HttpClient http, NpcStudioOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<AiEducationProfessionalProposal> BuildPreviewAsync(
        int npcId,
        CanonicalProfessionalBundle existing,
        CancellationToken cancellationToken = default)
    {
        var npc = LoadNpc(npcId);

        var education = existing.Education.Count == 0
            ? "(none)"
            : string.Join("\n", existing.Education.Select(x =>
                $"- {x.EducationType}: {x.InstitutionName}; {x.DegreeOrCredential}; {x.Status}"));

        var prompt = """
You are assisting ProjectEve NPC Studio.

Create a plausible EDUCATION + PROFESSIONAL draft for ONE NPC.
This is preview-only. The user will fine tune it before saving.

RULES:
- Do not create history events, memories, family, friends, or relationships.
- Do not invent exact event IDs.
- Respect existing canon.
- Fill blanks; do not replace meaningful existing facts.
- Education should progress naturally by age:
  Elementary School -> Middle School -> High School -> College / University when plausible.
- College is NOT mandatory. Trade/vocational, military, direct workforce, certification,
  homemaker, retirement, or other plausible routes are allowed.
- School completion ages must make sense.
- Career field, training level, years experience, license standing, motivation,
  performance, reputation, qualifications and competencies must fit the occupation.
- Keep it realistic for Bellefontaine / small-town Ohio unless existing canon says otherwise.
- Never fabricate a precise street address.
- For children/teens, professional fields can be Student / Not Applicable where appropriate.
- Use concise notes.
- Return VALID JSON ONLY.

NPC CANON:
Id: __NPC_ID__
Name: __NPC_NAME__
Age: __NPC_AGE__
Gender: __NPC_GENDER__
Occupation: __NPC_OCCUPATION__
Employer: __NPC_EMPLOYER__
Location: __NPC_LOCATION__
Hometown: __NPC_HOMETOWN__

EXISTING PROFESSIONAL:
PrimaryRoleId: __ROLE_ID__
CareerField: __CAREER_FIELD__
TrainingLevel: __TRAINING_LEVEL__
YearsExperience: __YEARS_EXPERIENCE__
LicenseStanding: __LICENSE_STANDING__

EXISTING EDUCATION:
__EDUCATION__

JSON SHAPE:
{
  "summary": "",
  "professional": {
    "primaryRoleId": "",
    "careerField": "",
    "trainingLevel": "",
    "yearsExperience": 0,
    "licenseStanding": "",
    "burnout": 0,
    "motivation": 50,
    "currentPerformance": 50,
    "professionalReputation": 50,
    "notes": ""
  },
  "education": [
    {
      "educationType": "Elementary School",
      "institutionName": "",
      "degreeOrCredential": "",
      "programName": "",
      "fieldOfStudy": "",
      "status": "Completed",
      "startAge": 5,
      "endAge": 11,
      "gpa": null,
      "honors": "",
      "notes": ""
    }
  ],
  "qualifications": [
    {
      "qualificationType": "",
      "name": "",
      "issuerName": "",
      "status": "Active",
      "credentialNumber": "",
      "notes": ""
    }
  ],
  "competencies": [
    {
      "competencyId": "",
      "competencyName": "",
      "currentValue": 50,
      "setPointValue": 50,
      "confidence": 50,
      "experienceLevel": "",
      "notes": ""
    }
  ]
}
""";

        prompt = prompt
            .Replace("__NPC_ID__", npc.Id.ToString())
            .Replace("__NPC_NAME__", npc.Name ?? "")
            .Replace("__NPC_AGE__", npc.Age.ToString())
            .Replace("__NPC_GENDER__", npc.Gender ?? "")
            .Replace("__NPC_OCCUPATION__", npc.Occupation ?? "")
            .Replace("__NPC_EMPLOYER__", npc.Employer ?? "")
            .Replace("__NPC_LOCATION__", npc.Location ?? "")
            .Replace("__NPC_HOMETOWN__", npc.Hometown ?? "")
            .Replace("__ROLE_ID__", existing.ProfessionalProfile.PrimaryRoleId ?? "")
            .Replace("__CAREER_FIELD__", existing.ProfessionalProfile.CareerField ?? "")
            .Replace("__TRAINING_LEVEL__", existing.ProfessionalProfile.TrainingLevel ?? "")
            .Replace("__YEARS_EXPERIENCE__", existing.ProfessionalProfile.YearsExperience.ToString())
            .Replace("__LICENSE_STANDING__", existing.ProfessionalProfile.LicenseStanding ?? "")
            .Replace("__EDUCATION__", education ?? "(none)");

        await EnsureOllamaAsync(cancellationToken);

        var request = new
        {
            model = _options.OllamaModel,
            prompt,
            stream = false,
            format = "json",
            keep_alive = "30m",
            options = new
            {
                temperature = 0.45,
                num_ctx = 4096,
                num_predict = 1800,
                repeat_penalty = 1.05
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(150));

        using var response = await _http.PostAsJsonAsync(
            new Uri(new Uri(_options.OllamaBaseUrl), "/api/generate"),
            request,
            cts.Token);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var wrapper = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);

        var raw = wrapper.RootElement.TryGetProperty("response", out var value)
            ? value.GetString() ?? "{}"
            : "{}";

        var proposal = JsonSerializer.Deserialize<AiEducationProfessionalProposal>(
            ExtractJson(raw),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return proposal ?? new AiEducationProfessionalProposal
        {
            Summary = "AI returned no usable education/professional proposal."
        };
    }

    private async Task EnsureOllamaAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            using var response = await _http.GetAsync(
                new Uri(new Uri(_options.OllamaBaseUrl), "/api/tags"),
                cts.Token);

            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Could not reach Ollama at {_options.OllamaBaseUrl}. Make sure Ollama is running.",
                ex);
        }
    }

    private NpcFacts LoadNpc(int npcId)
    {
        using var conn = new SqliteConnection($"Data Source={_options.MainDbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id,
                   COALESCE(Name,''),
                   COALESCE(Age,0),
                   COALESCE(Gender,''),
                   COALESCE(Occupation,''),
                   COALESCE(Employer,''),
                   COALESCE(Location,''),
                   COALESCE(Hometown,'')
            FROM Characters
            WHERE Id=$id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
            throw new InvalidOperationException($"NPC {npcId} was not found.");

        return new NpcFacts(
            r.GetInt32(0),
            r.GetString(1),
            r.GetInt32(2),
            r.GetString(3),
            r.GetString(4),
            r.GetString(5),
            r.GetString(6),
            r.GetString(7));
    }

    private static string ExtractJson(string raw)
    {
        raw = (raw ?? "").Trim();
        var first = raw.IndexOf('{');
        var last = raw.LastIndexOf('}');

        return first >= 0 && last > first
            ? raw[first..(last + 1)]
            : raw;
    }

    private sealed record NpcFacts(
        int Id,
        string Name,
        int Age,
        string Gender,
        string Occupation,
        string Employer,
        string Location,
        string Hometown);
}

public sealed class AiEducationProfessionalProposal
{
    public string Summary { get; set; } = "";
    public AiProfessionalProposal Professional { get; set; } = new();
    public List<AiEducationProposal> Education { get; set; } = new();
    public List<AiQualificationProposal> Qualifications { get; set; } = new();
    public List<AiCompetencyProposal> Competencies { get; set; } = new();
}

public sealed class AiProfessionalProposal
{
    public string PrimaryRoleId { get; set; } = "";
    public string CareerField { get; set; } = "";
    public string TrainingLevel { get; set; } = "";
    public int YearsExperience { get; set; }
    public string LicenseStanding { get; set; } = "";
    public int Burnout { get; set; }
    public int Motivation { get; set; } = 50;
    public int CurrentPerformance { get; set; } = 50;
    public int ProfessionalReputation { get; set; } = 50;
    public string Notes { get; set; } = "";
}

public sealed class AiEducationProposal
{
    public string EducationType { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string DegreeOrCredential { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string FieldOfStudy { get; set; } = "";
    public string Status { get; set; } = "Completed";
    public int? StartAge { get; set; }
    public int? EndAge { get; set; }
    public double? Gpa { get; set; }
    public string Honors { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class AiQualificationProposal
{
    public string QualificationType { get; set; } = "";
    public string Name { get; set; } = "";
    public string IssuerName { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string CredentialNumber { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class AiCompetencyProposal
{
    public string CompetencyId { get; set; } = "";
    public string CompetencyName { get; set; } = "";
    public int CurrentValue { get; set; } = 50;
    public int SetPointValue { get; set; } = 50;
    public int Confidence { get; set; } = 50;
    public string ExperienceLevel { get; set; } = "";
    public string Notes { get; set; } = "";
}

