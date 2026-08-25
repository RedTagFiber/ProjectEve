$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

function Rel([string]$p) {
    $base = [System.IO.Path]::GetFullPath($root).TrimEnd('\')
    $full = [System.IO.Path]::GetFullPath($p)
    if ($full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($base.Length).TrimStart('\')
    }
    return $full
}

$syncPath = Join-Path $root 'DATA\ProjectEveCanonicalSync.cs'
if (-not (Test-Path $syncPath)) {
    throw 'DATA\ProjectEveCanonicalSync.cs was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass13C_CanonicalSync_Callsite_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -notlike '*\.git\*' -and
        $_.FullName -ne $syncPath
    }

$patterns = @(
    'ProjectEveCanonicalSync\.SyncAll\s*\(',
    'ProjectEveCanonicalSync\.SyncRelationships\s*\(',
    'ProjectEveCanonicalSync\.SyncHistory\s*\(',
    'ProjectEveCanonicalSync\.SyncLocations\s*\(',
    '\bSyncAll\s*\(',
    '\bSyncRelationships\s*\(',
    '\bSyncHistory\s*\(',
    '\bSyncLocations\s*\('
)

$hits = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $lines = Get-Content $file.FullName
    for ($i=0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($p in $patterns) {
            if ($line -match $p) {
                $hits.Add([pscustomobject]@{
                    File = Rel $file.FullName
                    Line = $i + 1
                    Text = $line.Trim()
                })
                break
            }
        }
    }
}

$hits | Export-Csv (Join-Path $outRoot 'canonical_sync_callsites.csv') -NoTypeInformation -Encoding UTF8

$context = New-Object 'System.Collections.Generic.List[string]'
$context.Add('# Pass 13C Canonical Sync Call-Site Context')
$context.Add('')

foreach ($h in $hits) {
    $path = Join-Path $root $h.File
    $lines = Get-Content $path
    $start = [Math]::Max(1, $h.Line - 20)
    $end = [Math]::Min($lines.Count, $h.Line + 20)

    $context.Add("## $($h.File):$($h.Line)")
    $context.Add('')
    $context.Add('```csharp')
    for ($n=$start; $n -le $end; $n++) {
        $mark = if ($n -eq $h.Line) { '>>' } else { '  ' }
        $context.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
    }
    $context.Add('```')
    $context.Add('')
}
$context | Set-Content (Join-Path $outRoot 'PASS13C_CALLSITE_CONTEXT.md') -Encoding UTF8

$summary = New-Object 'System.Collections.Generic.List[string]'
$summary.Add('# Pass 13C Canonical Sync Call-Site Preflight')
$summary.Add('')
$summary.Add("External call sites found: $($hits.Count)")
$summary.Add('')

if ($hits.Count -eq 0) {
    $summary.Add('No compiled source outside ProjectEveCanonicalSync.cs calls its Sync* methods.')
    $summary.Add('This strongly indicates it is now an inactive migration bridge and can be archived next.')
}
else {
    foreach ($h in $hits) {
        $summary.Add("- $($h.File):$($h.Line) — $($h.Text)")
    }
}

$summary.Add('')
$summary.Add('No source files, databases, memories, relationships, locations, history, or NPCs were modified.')
$summary | Set-Content (Join-Path $outRoot 'PASS13C_CANONICAL_SYNC_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 13C canonical-sync call-site preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('External call sites found: ' + $hits.Count)
foreach ($h in $hits) {
    Write-Host ('  ' + $h.File + ':' + $h.Line)
}
Write-Host ''
Write-Host 'Upload these two files next:'
Write-Host '  PASS13C_CANONICAL_SYNC_SUMMARY.md'
Write-Host '  PASS13C_CALLSITE_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, memories, relationships, locations, history, or NPCs were modified.'
