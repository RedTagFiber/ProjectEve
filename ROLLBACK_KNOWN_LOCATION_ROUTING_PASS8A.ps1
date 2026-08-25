param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}

$backupFile = Join-Path $BackupFolder 'World\KnownLocationService.cs'
if (-not (Test-Path $backupFile)) {
    throw ('Backup file not found: ' + $backupFile)
}

Copy-Item $backupFile (Join-Path $root 'World\KnownLocationService.cs') -Force
Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host 'Pass 8A source rollback complete.' -ForegroundColor Yellow
Write-Host 'No database rows were deleted.'
Write-Host 'Run dotnet build.'
