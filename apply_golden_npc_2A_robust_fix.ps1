$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$source = Join-Path $repoRoot "DATA\ProjectEveGoldenNpcAudit.cs"

if (-not (Test-Path $source)) {
    throw "Could not find DATA\ProjectEveGoldenNpcAudit.cs from this fix package."
}

Write-Host "Robust Golden NPC audit file is in place."
Write-Host ""
Write-Host "Next run:"
Write-Host '  dotnet build'
Write-Host '  dotnet run -- golden-audit 1'
