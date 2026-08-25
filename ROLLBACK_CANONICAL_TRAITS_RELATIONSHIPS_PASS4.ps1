param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from the folder containing ProjectEve.csproj.'
}

if (-not (Test-Path $BackupFolder)) {
    throw ('Backup folder not found: ' + $BackupFolder)
}

Get-ChildItem $BackupFolder -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($BackupFolder.Length).TrimStart('\')
    $dest = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
    Copy-Item $_.FullName $dest -Force
}

Remove-Item (Join-Path $root 'Characters\Traits\NpcTraitRepository.cs') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'Relationships\RelationshipRepository.cs') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'DATA\ProjectEveOwnershipVerifier.cs') -Force -ErrorAction SilentlyContinue

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host 'Pass 4 source rollback complete.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
