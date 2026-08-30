$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $repo "Components\Pages\CanonicalFoundation.razor"

if (-not (Test-Path $source)) {
    throw "Missing Components\Pages\CanonicalFoundation.razor"
}

Write-Host "Standalone canonical verification page is installed."
Write-Host ""
Write-Host "This patch does NOT touch NpcProfile.razor."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Then open:"
Write-Host "  http://localhost:5123/canonical-foundation/1"
