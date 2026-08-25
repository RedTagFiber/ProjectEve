param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$backupFile = Join-Path $BackupFolder 'DATA\ProjectEveCanonicalSync.cs'
$target = Join-Path $root 'DATA\ProjectEveCanonicalSync.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}
if (-not (Test-Path $backupFile)) {
    throw ('Backup file not found: ' + $backupFile)
}

New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
Copy-Item $backupFile $target -Force

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host 'Pass 13E rollback complete.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
