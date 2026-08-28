$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'ProjectEve.NpcStudio\Tools\NpcWorldReset\NpcWorldReset.csproj'

Write-Host ''
Write-Host 'Project Eve NPC clean reset utility' -ForegroundColor Yellow
Write-Host 'Close Project Eve and NPC Studio before continuing.' -ForegroundColor Yellow
Write-Host ''

dotnet run --project $project --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Reset utility exited with code $LASTEXITCODE." -ForegroundColor Red
    exit $LASTEXITCODE
}
