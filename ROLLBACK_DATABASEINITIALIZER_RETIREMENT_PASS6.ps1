param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}

$backupFile = Join-Path $BackupFolder 'DATA\DatabaseInitializer.cs'
if (-not (Test-Path $backupFile)) {
    throw ('Backup DatabaseInitializer.cs not found: ' + $backupFile)
}

$target = Join-Path $root 'DATA\DatabaseInitializer.cs'
Copy-Item $backupFile $target -Force

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host 'Pass 6 rollback restored the previous DatabaseInitializer.cs.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
