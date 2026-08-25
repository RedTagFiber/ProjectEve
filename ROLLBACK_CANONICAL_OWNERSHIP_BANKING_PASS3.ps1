param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this rollback script from the folder containing ProjectEve.csproj.'
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

Remove-Item (Join-Path $root 'Money\FinanceModels.cs') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'Money\FinancialLedgerService.cs') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'DATA\ProjectEveDataOwnership.cs') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'DATA\ProjectEveFinanceVerifier.cs') -Force -ErrorAction SilentlyContinue

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host 'Rollback restored the backed-up source files.' -ForegroundColor Yellow
Write-Host 'Run dotnet build.'
