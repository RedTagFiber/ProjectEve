$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

$required = @(
    "Models\CanonicalFinanceModels.cs",
    "Data\NpcStudioRepository.CanonicalFinance.cs",
    "Components\Pages\CanonicalFinance.razor",
    "Components\Pages\CanonicalFinance.razor.css",
    "Components\FinanceRowEditor.razor"
)

foreach ($relative in $required) {
    $path = Join-Path $repo $relative

    if (-not (Test-Path $path)) {
        throw "Missing Finance Bridge 3 file: $relative"
    }
}

Write-Host ""
Write-Host "Canonical Write Bridge 3 - Finance installed."
Write-Host "InteractiveServer is already included in the page."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Open:"
Write-Host "  http://localhost:5123/canonical-finance/1"
