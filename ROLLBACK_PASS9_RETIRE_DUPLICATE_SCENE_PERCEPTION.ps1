param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from the folder containing ProjectEve.csproj.'
}

$backupFile = Join-Path $BackupFolder 'DATA\World\Scene\ScenePerceptionService.cs'
if (-not (Test-Path $backupFile)) {
    throw ('Backup file not found: ' + $backupFile)
}

$target = Join-Path $root 'DATA\World\Scene\ScenePerceptionService.cs'
New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
Copy-Item $backupFile $target -Force

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host 'Pass 9 duplicate source restored.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
