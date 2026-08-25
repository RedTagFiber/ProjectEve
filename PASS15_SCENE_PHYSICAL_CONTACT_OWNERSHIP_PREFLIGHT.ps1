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

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass15_ScenePhysicalContact_Ownership_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -notlike '*\.git\*'
    }

$hits = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $lines = Get-Content $file.FullName

    for ($i=0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $kind = $null

        if ($line -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?ScenePhysicalContact\b') {
            $kind = 'INSERT'
        }
        elseif ($line -match '(?i)(?:^|[\s;"@])UPDATE\s+["`\[]?ScenePhysicalContact\b') {
            $kind = 'UPDATE'
        }
        elseif ($line -match '(?i)\bDELETE\s+FROM\s+["`\[]?ScenePhysicalContact\b') {
            $kind = 'DELETE'
        }
        elseif ($line -match '(?i)\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`\[]?ScenePhysicalContact\b') {
            $kind = 'CREATE'
        }
        elseif ($line -match '(?i)\bFROM\s+["`\[]?ScenePhysicalContact\b') {
            $kind = 'READ'
        }
        elseif ($line -match '(?i)\bJOIN\s+["`\[]?ScenePhysicalContact\b') {
            $kind = 'JOIN'
        }

        if ($kind) {
            $hits.Add([pscustomobject]@{
                File = Rel $file.FullName
                Line = $i + 1
                Kind = $kind
                Text = $line.Trim()
            })
        }
    }
}

$hits | Export-Csv (Join-Path $outRoot 'scene_physical_contact_hits.csv') -NoTypeInformation -Encoding UTF8

$writers = @(
    $hits |
    Where-Object Kind -in @('INSERT','UPDATE','DELETE') |
    Select-Object -ExpandProperty File -Unique
)
$creators = @(
    $hits |
    Where-Object Kind -eq 'CREATE' |
    Select-Object -ExpandProperty File -Unique
)
$readers = @(
    $hits |
    Where-Object Kind -in @('READ','JOIN') |
    Select-Object -ExpandProperty File -Unique
)

$summary = New-Object 'System.Collections.Generic.List[string]'
$summary.Add('# Pass 15 ScenePhysicalContact Ownership Preflight')
$summary.Add('')
$summary.Add("Runtime writers: $($writers.Count)")
foreach ($w in $writers) { $summary.Add("- $w") }
$summary.Add('')
$summary.Add("Schema creators: $($creators.Count)")
foreach ($c in $creators) { $summary.Add("- $c") }
$summary.Add('')
$summary.Add("Readers: $($readers.Count)")
foreach ($r in $readers) { $summary.Add("- $r") }
$summary.Add('')
$summary.Add('## All hits')
$summary.Add('')
foreach ($h in $hits) {
    $summary.Add("- [$($h.Kind)] $($h.File):$($h.Line) — $($h.Text)")
}
$summary.Add('')
$summary.Add('## Goal')
$summary.Add('')
$summary.Add('Classify ScenePhysicalContact as either:')
$summary.Add('- location/scene physical truth owned by the locations DB, or')
$summary.Add('- temporary MAIN compatibility state that must remain until dependent scene services are migrated.')
$summary.Add('')
$summary.Add('No source files, databases, scene contact rows, or NPCs were modified.')
$summary | Set-Content (Join-Path $outRoot 'PASS15_SCENE_PHYSICAL_CONTACT_SUMMARY.md') -Encoding UTF8

$ctx = New-Object 'System.Collections.Generic.List[string]'
$ctx.Add('# Pass 15 ScenePhysicalContact Context')
$ctx.Add('')

foreach ($h in $hits) {
    if ($h.Kind -notin @('INSERT','UPDATE','DELETE','CREATE'))
        { continue }

    $path = Join-Path $root $h.File
    $lines = Get-Content $path
    $start = [Math]::Max(1, $h.Line - 35)
    $end = [Math]::Min($lines.Count, $h.Line + 35)

    $ctx.Add("## $($h.Kind) — $($h.File):$($h.Line)")
    $ctx.Add('')
    $ctx.Add('```csharp')
    for ($n=$start; $n -le $end; $n++) {
        $mark = if ($n -eq $h.Line) { '>>' } else { '  ' }
        $ctx.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
    }
    $ctx.Add('```')
    $ctx.Add('')
}

$ctx | Set-Content (Join-Path $outRoot 'PASS15_SCENE_PHYSICAL_CONTACT_CONTEXT.md') -Encoding UTF8

# Capture likely scene/occupancy API and connection context from writer files.
$api = New-Object 'System.Collections.Generic.List[string]'
$api.Add('# Pass 15 Writer API / Database Context')
$api.Add('')

foreach ($rel in $writers) {
    $path = Join-Path $root $rel
    if (-not (Test-Path $path)) { continue }

    $lines = Get-Content $path
    $api.Add("## $rel")
    $api.Add('')

    for ($i=0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'ConnStr|DbPath|MainDatabasePath|LocationDatabasePath|OpenMain|Open\(' -or
            $line -match '^\s*(public|private|internal)\s+.*\(') {

            $start = [Math]::Max(0, $i - 2)
            $end = [Math]::Min($lines.Count - 1, $i + 8)
            $api.Add('```csharp')
            for ($n=$start; $n -le $end; $n++) {
                $api.Add(('{0,5}: {1}' -f ($n+1), $lines[$n]))
            }
            $api.Add('```')
            $api.Add('')
        }
    }
}

$api | Set-Content (Join-Path $outRoot 'PASS15_SCENE_WRITER_API_CONTEXT.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 15 ScenePhysicalContact ownership preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('Runtime writers: ' + $writers.Count)
foreach ($w in $writers) { Write-Host ('  ' + $w) }
Write-Host ('Schema creators: ' + $creators.Count)
foreach ($c in $creators) { Write-Host ('  ' + $c) }
Write-Host ''
Write-Host 'Upload these three files next:'
Write-Host '  PASS15_SCENE_PHYSICAL_CONTACT_SUMMARY.md'
Write-Host '  PASS15_SCENE_PHYSICAL_CONTACT_CONTEXT.md'
Write-Host '  PASS15_SCENE_WRITER_API_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, scene contact rows, or NPCs were modified.'
