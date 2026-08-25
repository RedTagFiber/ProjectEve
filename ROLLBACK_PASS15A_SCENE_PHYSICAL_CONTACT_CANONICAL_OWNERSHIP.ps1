param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference='Stop'
$root=(Get-Location).Path

$rels=@(
    'DATA\ProjectEveDatabaseSetup.cs',
    'Scene\SceneSpatialInteractionService.cs',
    'World\PlayerWorldPresenceService.cs',
    'World\WorldOccupancyService.cs'
)

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}

foreach ($rel in $rels) {
    $src=Join-Path $BackupFolder $rel
    $dst=Join-Path $root $rel
    if (-not (Test-Path $src)) { throw ('Missing backup: '+$src) }
    Copy-Item $src $dst -Force
}

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host 'Pass 15A rollback complete.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
