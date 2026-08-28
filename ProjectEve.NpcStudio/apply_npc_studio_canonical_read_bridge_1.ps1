$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

$modelPath = Join-Path $repo "Models\NpcStudioModels.cs"
$repoPath  = Join-Path $repo "Data\NpcStudioRepository.cs"
$pagePath  = Join-Path $repo "Components\Pages\NpcProfile.razor"

foreach ($path in @($modelPath, $repoPath, $pagePath)) {
    if (-not (Test-Path $path)) {
        throw "Required NPC Studio file not found: $path"
    }
}

function Write-Utf8Bom([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText(
        $Path,
        $Text,
        [System.Text.UTF8Encoding]::new($true))
}

# ------------------------------------------------------------
# 1) Models/NpcStudioModels.cs
# ------------------------------------------------------------
$model = [System.IO.File]::ReadAllText($modelPath)

if ($model -notmatch 'public string Employer \{ get; set; \}') {
    $needle = @'
    public string Occupation { get; set; } = "";
    public string Location { get; set; } = "";
'@

    $replacement = @'
    public string Occupation { get; set; } = "";
    public string Employer { get; set; } = "";
    public string Location { get; set; } = "";
    public string CurrentLocationId { get; set; } = "";
    public string HomeLocationId { get; set; } = "";
    public string WorkLocationId { get; set; } = "";
'@

    if (-not $model.Contains($needle)) {
        throw "Could not find Occupation/Location anchor in Models\NpcStudioModels.cs"
    }

    $model = $model.Replace($needle, $replacement)
}

if ($model -notmatch 'NpcCanonicalFoundationSummary CanonicalFoundation') {
    $needle = @'
    public List<NpcRevisionRow> Revisions { get; set; } = new();
    public List<NpcHistoryEvent> HistoryEvents { get; set; } = new();
'@

    $replacement = @'
    public List<NpcRevisionRow> Revisions { get; set; } = new();
    public List<NpcHistoryEvent> HistoryEvents { get; set; } = new();

    // Golden-NPC canonical foundation bridge.
    // Read-only in Bridge 1; later phases add domain-specific editors.
    public NpcCanonicalFoundationSummary CanonicalFoundation { get; set; } = new();
'@

    if (-not $model.Contains($needle)) {
        throw "Could not find NpcCharacterSheet list anchor in Models\NpcStudioModels.cs"
    }

    $model = $model.Replace($needle, $replacement)
}

if ($model -notmatch 'public sealed class NpcCanonicalFoundationSummary') {
    $anchor = "public sealed class NpcHistoryEvent"
    $idx = $model.IndexOf($anchor)
    if ($idx -lt 0) {
        throw "Could not find NpcHistoryEvent anchor in Models\NpcStudioModels.cs"
    }

    $block = @'
public sealed class NpcCanonicalFoundationSummary
{
    public int EducationRecords { get; set; }
    public int ProfessionalProfiles { get; set; }
    public int Qualifications { get; set; }
    public int ProfessionalCompetencies { get; set; }

    public int Phones { get; set; }
    public int VehiclesOwnedOrDriven { get; set; }
    public int FinancialAccounts { get; set; }
    public int FinancialObligations { get; set; }

    public bool HasFormation =>
        EducationRecords > 0 ||
        ProfessionalProfiles > 0 ||
        Qualifications > 0 ||
        ProfessionalCompetencies > 0;

    public bool HasPropertyOrFinance =>
        Phones > 0 ||
        VehiclesOwnedOrDriven > 0 ||
        FinancialAccounts > 0 ||
        FinancialObligations > 0;
}


'@

    $model = $model.Insert($idx, $block)
}

Write-Utf8Bom $modelPath $model
Write-Host "Updated Models\NpcStudioModels.cs"

# ------------------------------------------------------------
# 2) Data/NpcStudioRepository.cs
# ------------------------------------------------------------
$data = [System.IO.File]::ReadAllText($repoPath)

if ($data -notmatch 'sheet\.CanonicalFoundation = GetCanonicalFoundationSummary') {
    $needle = '        sheet.HistoryEvents = GetHistoryEvents(conn, npcId);'

    if (-not $data.Contains($needle)) {
        throw "Could not find HistoryEvents hydration anchor in Data\NpcStudioRepository.cs"
    }

    $replacement = @'
        sheet.HistoryEvents = GetHistoryEvents(conn, npcId);
        sheet.CanonicalFoundation = GetCanonicalFoundationSummary(conn, npcId);
'@

    $data = $data.Replace($needle, $replacement.TrimEnd())
}

if ($data -notmatch 'Employer = ReadString\(reader, "Employer"\)') {
    $needle = @'
            Occupation = ReadString(reader, "Occupation"),
            Location = ReadString(reader, "Location"),
'@

    $replacement = @'
            Occupation = ReadString(reader, "Occupation"),
            Employer = ReadString(reader, "Employer"),
            Location = ReadString(reader, "Location"),
            CurrentLocationId = ReadString(reader, "CurrentLocationId"),
            HomeLocationId = ReadString(reader, "HomeLocationId"),
            WorkLocationId = ReadString(reader, "WorkLocationId"),
'@

    if (-not $data.Contains($needle)) {
        throw "Could not find GetCharacterCore Occupation/Location anchor."
    }

    $data = $data.Replace($needle, $replacement)
}

if ($data -notmatch 'private static NpcCanonicalFoundationSummary GetCanonicalFoundationSummary') {
    $anchor = '    private static List<NpcHistoryEvent> GetHistoryEvents'
    $idx = $data.IndexOf($anchor)

    if ($idx -lt 0) {
        throw "Could not find GetHistoryEvents method anchor in Data\NpcStudioRepository.cs"
    }

    $helper = @'
    private static NpcCanonicalFoundationSummary GetCanonicalFoundationSummary(
        SqliteConnection conn,
        int npcId)
    {
        return new NpcCanonicalFoundationSummary
        {
            EducationRecords = CanonicalCount(
                conn,
                "NpcEducationRecords",
                "NpcId",
                npcId),

            ProfessionalProfiles = CanonicalCount(
                conn,
                "NpcProfessionalProfiles",
                "NpcId",
                npcId),

            Qualifications = CanonicalCount(
                conn,
                "NpcProfessionalQualifications",
                "NpcId",
                npcId),

            ProfessionalCompetencies = CanonicalCount(
                conn,
                "NpcProfessionalCompetencies",
                "NpcId",
                npcId),

            Phones = CanonicalCount(
                conn,
                "NpcPhones",
                "NpcId",
                npcId),

            VehiclesOwnedOrDriven = CanonicalVehicleCount(conn, npcId),

            FinancialAccounts = CanonicalCount(
                conn,
                "FinancialAccounts",
                "OwnerId",
                npcId,
                "OwnerType = 'NPC'"),

            FinancialObligations = CanonicalCount(
                conn,
                "FinancialObligations",
                "OwnerNpcId",
                npcId)
        };
    }

    private static int CanonicalCount(
        SqliteConnection conn,
        string tableName,
        string ownerColumn,
        int npcId,
        string? extraWhere = null)
    {
        if (!CanonicalTableExists(conn, tableName))
            return 0;

        using var cmd = conn.CreateCommand();

        var where = $"{ownerColumn} = $npcId";
        if (!string.IsNullOrWhiteSpace(extraWhere))
            where += " AND " + extraWhere;

        cmd.CommandText =
            $"SELECT COUNT(*) FROM {tableName} WHERE {where};";

        cmd.Parameters.AddWithValue("$npcId", npcId);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static int CanonicalVehicleCount(
        SqliteConnection conn,
        int npcId)
    {
        if (!CanonicalTableExists(conn, "Vehicles"))
            return 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM Vehicles
            WHERE RegisteredOwnerNpcId = $npcId
               OR PrimaryDriverNpcId = $npcId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static bool CanonicalTableExists(
        SqliteConnection conn,
        string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND lower(name) = lower($tableName);
            """;
        cmd.Parameters.AddWithValue("$tableName", tableName);

        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }


'@

    $data = $data.Insert($idx, $helper)
}

Write-Utf8Bom $repoPath $data
Write-Host "Updated Data\NpcStudioRepository.cs"

# ------------------------------------------------------------
# 3) Components/Pages/NpcProfile.razor
# ------------------------------------------------------------
$page = [System.IO.File]::ReadAllText($pagePath)

if ($page -notmatch 'Golden NPC Canonical Foundation') {
    $pattern = '@if\s*\(_sheet is not null\)\s*\{'
    $match = [regex]::Match($page, $pattern)

    if (-not $match.Success) {
        throw "Could not find the main _sheet render block in NpcProfile.razor"
    }

    $card = @'

        <div class="studio-card case-section">
            <div class="studio-card-head">
                <h2>Golden NPC Canonical Foundation</h2>
                <span class="studio-pill">read bridge 1</span>
            </div>

            <p class="studio-muted">
                This panel reads the canonical Golden NPC tables directly. It does not create or modify canon yet.
            </p>

            <div class="studio-two-column">
                <div>
                    <p><strong>Employer:</strong> @(string.IsNullOrWhiteSpace(_sheet.Employer) ? "(blank)" : _sheet.Employer)</p>
                    <p><strong>Current Location ID:</strong> @(string.IsNullOrWhiteSpace(_sheet.CurrentLocationId) ? "(dynamic / blank)" : _sheet.CurrentLocationId)</p>
                    <p><strong>Home Location ID:</strong> @(string.IsNullOrWhiteSpace(_sheet.HomeLocationId) ? "(blank)" : _sheet.HomeLocationId)</p>
                    <p><strong>Work Location ID:</strong> @(string.IsNullOrWhiteSpace(_sheet.WorkLocationId) ? "(blank)" : _sheet.WorkLocationId)</p>
                </div>

                <div>
                    <p><strong>Education:</strong> @_sheet.CanonicalFoundation.EducationRecords</p>
                    <p><strong>Professional Profile:</strong> @_sheet.CanonicalFoundation.ProfessionalProfiles</p>
                    <p><strong>Qualifications:</strong> @_sheet.CanonicalFoundation.Qualifications</p>
                    <p><strong>Competencies:</strong> @_sheet.CanonicalFoundation.ProfessionalCompetencies</p>
                </div>
            </div>

            <div class="studio-two-column">
                <div>
                    <p><strong>Phones:</strong> @_sheet.CanonicalFoundation.Phones</p>
                    <p><strong>Vehicles:</strong> @_sheet.CanonicalFoundation.VehiclesOwnedOrDriven</p>
                </div>

                <div>
                    <p><strong>Financial Accounts:</strong> @_sheet.CanonicalFoundation.FinancialAccounts</p>
                    <p><strong>Financial Obligations:</strong> @_sheet.CanonicalFoundation.FinancialObligations</p>
                </div>
            </div>
        </div>
'@

    $insertAt = $match.Index + $match.Length
    $page = $page.Insert($insertAt, $card)
}

Write-Utf8Bom $pagePath $page
Write-Host "Updated Components\Pages\NpcProfile.razor"

Write-Host ""
Write-Host "NPC Studio Canonical Read Bridge 1 installed."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Open Eve (NPC 1) and verify the Golden NPC Canonical Foundation panel."
