$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$pagePath = Join-Path $repo "Components\Pages\NpcProfile.razor"

if (-not (Test-Path $pagePath)) {
    throw "NpcProfile.razor not found: $pagePath"
}

$page = [System.IO.File]::ReadAllText($pagePath)

if ($page -match 'Golden NPC Canonical Foundation') {
    Write-Host "Golden NPC Canonical Foundation panel already exists. No change needed."
}
else {
    $heroMarker = '<p class="studio-eyebrow">Official NPC Case File</p>'
    $heroIndex = $page.IndexOf($heroMarker)

    if ($heroIndex -lt 0) {
        throw 'Could not find "Official NPC Case File" hero marker in NpcProfile.razor.'
    }

    $sectionEnd = $page.IndexOf('</section>', $heroIndex)

    if ($sectionEnd -lt 0) {
        throw 'Could not find the closing </section> for the Official NPC Case File hero.'
    }

    $insertAt = $sectionEnd + '</section>'.Length

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

    $page = $page.Insert($insertAt, $card)

    [System.IO.File]::WriteAllText(
        $pagePath,
        $page,
        [System.Text.UTF8Encoding]::new($true))

    Write-Host "Inserted Golden NPC Canonical Foundation panel after Official NPC Case File hero."
}

Write-Host ""
Write-Host "UI fix installed."
Write-Host "Next run:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
