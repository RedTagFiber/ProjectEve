$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

$required = @(
    "Models\CanonicalProfessionalModels.cs",
    "Data\NpcStudioRepository.CanonicalProfessional.cs",
    "Components\Pages\CanonicalProfessional.razor",
    "Components\Pages\CanonicalProfessional.razor.css",
    "Components\CanonicalField.razor"
)

foreach ($relative in $required) {
    $full = Join-Path $repo $relative
    if (-not (Test-Path $full)) {
        throw "Missing patch file: $relative"
    }
}

Write-Host ""
Write-Host "NPC Studio Canonical Write Bridge 2 files are installed."
Write-Host ""
Write-Host "This patch does NOT modify NpcProfile.razor."
Write-Host "It adds a standalone canonical Education/Professional editor."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Then open:"
Write-Host "  http://localhost:5123/canonical-professional/1"
