param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path

$programBackup = Join-Path $BackupFolder 'Program.cs'
$repoBackup = Join-Path $BackupFolder 'Characters\Base\CharacterRepository.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}
if (-not (Test-Path $programBackup)) { throw ('Missing backup: ' + $programBackup) }
if (-not (Test-Path $repoBackup)) { throw ('Missing backup: ' + $repoBackup) }

Copy-Item $programBackup (Join-Path $root 'Program.cs') -Force
Copy-Item $repoBackup (Join-Path $root 'Characters\Base\CharacterRepository.cs') -Force

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host 'Pass 12B rollback complete.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
