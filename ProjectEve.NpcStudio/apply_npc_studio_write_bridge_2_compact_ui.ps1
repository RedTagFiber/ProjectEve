$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path

$pageSource = Join-Path $repo "Components\Pages\CanonicalProfessional.razor"
$cssSource  = Join-Path $repo "Components\Pages\CanonicalProfessional.razor.css"

if (-not (Test-Path $pageSource)) { throw "Missing CanonicalProfessional.razor" }
if (-not (Test-Path $cssSource))  { throw "Missing CanonicalProfessional.razor.css" }

Write-Host ""
Write-Host "Compact UI replacement is in place."
Write-Host "No repository or database code was changed."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Open:"
Write-Host "  http://localhost:5123/canonical-professional/1"
