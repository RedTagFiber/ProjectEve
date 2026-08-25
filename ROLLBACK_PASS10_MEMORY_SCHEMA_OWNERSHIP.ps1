param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$backupFile = Join-Path $BackupFolder 'Memory\MemoryDatabase.cs'
$target = Join-Path $root 'Memory\MemoryDatabase.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}
if (-not (Test-Path $backupFile)) {
    throw ('Backup file not found: ' + $backupFile)
}

Copy-Item $backupFile $target -Force
Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host 'Pass 10 source rollback complete.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
