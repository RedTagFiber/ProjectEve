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
$backupRoot = Join-Path 'D:\ProjectEve\Backups\Pass4RawStringFullFix' $stamp
$backupPath = Join-Path $backupRoot 'DATA\ProjectEveDatabaseSetup.cs'
New-Item -ItemType Directory -Path (Split-Path $backupPath -Parent) -Force | Out-Null
Copy-Item $path $backupPath -Force

$lines = [System.Collections.Generic.List[string]](Get-Content $path)

# Find the NpcTraitControl line.
$traitLine = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'CREATE TABLE IF NOT EXISTS NpcTraitControl') {
        $traitLine = $i
        break
    }
}

if ($traitLine -lt 0) {
    throw 'NpcTraitControl schema block was not found.'
}

# Find the opening raw-string delimiter before that line.
$openLine = -1
for ($i = $traitLine; $i -ge 0; $i--) {
    if ($lines[$i] -match '"""') {
        $openLine = $i
        break
    }
}

if ($openLine -lt 0) {
    throw 'Opening raw-string delimiter was not found.'
}

# Find the closing raw-string delimiter after the trait line.
$closeLine = -1
for ($i = $traitLine + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s*"""\s*\)?;?\s*$' -or
        $lines[$i] -match '^\s*"""\s*\);?\s*$' -or
        $lines[$i] -match '^\s*"""\s*;?\s*$') {
        $closeLine = $i
        break
    }
}

if ($closeLine -lt 0) {
    # Fallback: first later line containing only a raw-string delimiter plus punctuation.
    for ($i = $traitLine + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i].TrimStart().StartsWith('"""')) {
            $closeLine = $i
            break
        }
    }
}

if ($closeLine -lt 0) {
    throw 'Closing raw-string delimiter was not found.'
}

$closing = $lines[$closeLine]
$closingIndentLength = $closing.Length - $closing.TrimStart().Length
$closingIndent = $closing.Substring(0, $closingIndentLength)

# C# raw string rule:
# every nonblank content line must begin with at least the same whitespace
# as the closing delimiter.
for ($i = $openLine + 1; $i -lt $closeLine; $i++) {
    $line = $lines[$i]

    if ([string]::IsNullOrWhiteSpace($line)) {
        $lines[$i] = ''
        continue
    }

    if (-not $line.StartsWith($closingIndent, [System.StringComparison]::Ordinal)) {
        $lines[$i] = $closingIndent + $line.TrimStart()
    }
}

$lines | Set-Content $path -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Full raw-string block indentation repaired.' -ForegroundColor Green
Write-Host ('Raw string lines: ' + ($openLine + 1) + ' through ' + ($closeLine + 1))
Write-Host ('Backup: ' + $backupRoot)
Write-Host ''
Write-Host 'Run only:'
Write-Host '  dotnet build'
Write-Host ''
Write-Host 'If build succeeds, then run:'
Write-Host '  dotnet run -- verify'
