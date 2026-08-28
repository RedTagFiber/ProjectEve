$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$cssPath = Join-Path $repo "Components\FinanceRowEditor.razor.css"

if (-not (Test-Path (Join-Path $repo "Components\FinanceRowEditor.razor"))) {
    throw "Components\FinanceRowEditor.razor was not found."
}

if (-not (Test-Path $cssPath)) {
    throw "Components\FinanceRowEditor.razor.css was not extracted."
}

Write-Host ""
Write-Host "Finance row component styling is in place."
Write-Host "This fixes Blazor CSS isolation for the child editor."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Then hard refresh:"
Write-Host "  Ctrl+F5"
Write-Host ""
Write-Host "Open:"
Write-Host "  http://localhost:5123/canonical-finance/1"
