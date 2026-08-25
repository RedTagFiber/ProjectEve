$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$path = Join-Path $root 'DATA\ProjectEveDatabaseSetup.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this script from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

if (-not (Test-Path $path)) {
    throw 'DATA\ProjectEveDatabaseSetup.cs was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\Pass4RawStringFix' $stamp
$backupPath = Join-Path $backupRoot 'DATA\ProjectEveDatabaseSetup.cs'
New-Item -ItemType Directory -Path (Split-Path $backupPath -Parent) -Force | Out-Null
Copy-Item $path $backupPath -Force

$lines = [System.Collections.Generic.List[string]](Get-Content $path)

$start = -1
$next = -1

for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($start -lt 0 -and $lines[$i] -match 'CREATE TABLE IF NOT EXISTS NpcTraitControl') {
        $start = $i
        continue
    }

    if ($start -ge 0 -and $lines[$i] -match 'CREATE TABLE IF NOT EXISTS NpcEmotionTriggers') {
        $next = $i
        break
    }
}

if ($start -lt 0) {
    throw 'NpcTraitControl block was not found.'
}

if ($next -lt 0) {
    throw 'NpcEmotionTriggers anchor was not found after NpcTraitControl.'
}

# The next table is in the same raw SQL literal. Match its exact leading whitespace.
$anchorLine = $lines[$next]
$anchorIndent = $anchorLine.Substring(0, $anchorLine.Length - $anchorLine.TrimStart().Length)

# Normalize every line in the inserted NpcTraitControl block to the same raw-string indentation.
for ($i = $start; $i -lt $next; $i++) {
    if ([string]::IsNullOrWhiteSpace($lines[$i])) {
        $lines[$i] = ''
    }
    else {
        $lines[$i] = $anchorIndent + $lines[$i].TrimStart()
    }
}

$lines | Set-Content $path -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Raw-string indentation repaired.' -ForegroundColor Green
Write-Host ('Backup: ' + $backupRoot)
Write-Host ''
Write-Host 'Run:'
Write-Host '  dotnet build'
Write-Host ''
Write-Host 'If the build succeeds, then run:'
Write-Host '  dotnet run -- verify'
