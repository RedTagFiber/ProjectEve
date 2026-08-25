param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path

$repoBackup = Join-Path $BackupFolder 'Characters\Base\CharacterRepository.cs'
$webBackup  = Join-Path $BackupFolder 'World\SmallTown\Population\FamilyFriendWebSystem.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}
if (-not (Test-Path $repoBackup)) { throw ('Missing backup: ' + $repoBackup) }
if (-not (Test-Path $webBackup))  { throw ('Missing backup: ' + $webBackup) }

Copy-Item $repoBackup (Join-Path $root 'Characters\Base\CharacterRepository.cs') -Force
Copy-Item $webBackup  (Join-Path $root 'World\SmallTown\Population\FamilyFriendWebSystem.cs') -Force

Remove-Item -Recurse -Force `
    (Join-Path $root 'bin'), `
    (Join-Path $root 'obj') `
    -ErrorAction SilentlyContinue

Write-Host 'Pass 12A rollback complete.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
