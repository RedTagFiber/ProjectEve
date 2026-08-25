$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

function Get-RelativePathCompat {
    param([string]$BasePath,[string]$FullPath)

    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\')
    $full = [System.IO.Path]::GetFullPath($FullPath)

    if ($full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($base.Length).TrimStart('\')
    }

    return $full
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass16_PostPass15_Ownership_Audit_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -notlike '*\.git\*'
    }

$rows = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $rel = Get-RelativePathCompat $root $file.FullName
    $lines = Get-Content $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        $patterns = @(
            @{ Kind='CREATE'; Regex='(?i)\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`\[]?([A-Za-z_][A-Za-z0-9_]*)' },
            @{ Kind='INSERT'; Regex='(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)' },
            @{ Kind='UPDATE'; Regex='(?i)(?:^|[\s;"@])UPDATE\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)' },
            @{ Kind='DELETE'; Regex='(?i)\bDELETE\s+FROM\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)' }
        )

        foreach ($p in $patterns) {
            $m = [regex]::Match($line, $p.Regex)
            if ($m.Success) {
                $table = $m.Groups[1].Value

                # Filter known SQL parser false positives / non-table tokens.
                if ($table -in @('SET','the','IF','SELECT','WHERE','VALUES')) {
                    continue
                }

                $rows.Add([pscustomobject]@{
                    Table = $table
                    File  = $rel
                    Line  = $i + 1
                    Kind  = $p.Kind
                    Text  = $line.Trim()
                })
            }
        }
    }
}

$rows | Export-Csv (Join-Path $outRoot 'all_runtime_ownership_hits.csv') -NoTypeInformation -Encoding UTF8

$writerGroups = @(
    $rows |
    Where-Object Kind -in @('INSERT','UPDATE','DELETE') |
    Group-Object Table
)

$creatorGroups = @(
    $rows |
    Where-Object Kind -eq 'CREATE' |
    Group-Object Table
)

$multiWriters = New-Object System.Collections.Generic.List[object]
foreach ($g in $writerGroups) {
    $filesForTable = @($g.Group | Select-Object -ExpandProperty File -Unique)
    if ($filesForTable.Count -gt 1) {
        $multiWriters.Add([pscustomobject]@{
            Table = $g.Name
            Count = $filesForTable.Count
            Files = ($filesForTable -join '; ')
        })
    }
}

$multiCreators = New-Object System.Collections.Generic.List[object]
foreach ($g in $creatorGroups) {
    $filesForTable = @($g.Group | Select-Object -ExpandProperty File -Unique)
    if ($filesForTable.Count -gt 1) {
        $multiCreators.Add([pscustomobject]@{
            Table = $g.Name
            Count = $filesForTable.Count
            Files = ($filesForTable -join '; ')
        })
    }
}

$multiWriters | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_writer_tables.csv') -NoTypeInformation -Encoding UTF8
$multiCreators | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_creator_tables.csv') -NoTypeInformation -Encoding UTF8

$summary = New-Object 'System.Collections.Generic.List[string]'
$summary.Add('# Pass 16 Post-Pass15 Ownership Audit')
$summary.Add('')
$summary.Add("Compiled C# files scanned: $($files.Count)")
$summary.Add("SQL ownership hits: $($rows.Count)")
$summary.Add("Tables with multiple runtime writers: $($multiWriters.Count)")
$summary.Add("Tables with multiple schema creators: $($multiCreators.Count)")
$summary.Add('')

$summary.Add('## Multiple runtime writers')
$summary.Add('')
if ($multiWriters.Count -eq 0) {
    $summary.Add('None.')
} else {
    foreach ($x in ($multiWriters | Sort-Object Table)) {
        $summary.Add("### $($x.Table)")
        foreach ($f in ($x.Files -split '; ')) {
            $summary.Add("- $f")
        }

        $hits = @(
            $rows |
            Where-Object { $_.Table -eq $x.Table -and $_.Kind -in @('INSERT','UPDATE','DELETE') }
        )
        foreach ($h in $hits) {
            $summary.Add("- [$($h.Kind)] $($h.File):$($h.Line) — $($h.Text)")
        }
        $summary.Add('')
    }
}

$summary.Add('## Multiple schema creators')
$summary.Add('')
if ($multiCreators.Count -eq 0) {
    $summary.Add('None.')
} else {
    foreach ($x in ($multiCreators | Sort-Object Table)) {
        $summary.Add("### $($x.Table)")
        foreach ($f in ($x.Files -split '; ')) {
            $summary.Add("- $f")
        }

        $hits = @(
            $rows |
            Where-Object { $_.Table -eq $x.Table -and $_.Kind -eq 'CREATE' }
        )
        foreach ($h in $hits) {
            $summary.Add("- [CREATE] $($h.File):$($h.Line) — $($h.Text)")
        }
        $summary.Add('')
    }
}

$summary.Add('## Canonical areas expected to remain clean')
$summary.Add('')
$summary.Add('- Characters')
$summary.Add('- NpcTraitValues')
$summary.Add('- PersonalMemories')
$summary.Add('- RelationshipStates')
$summary.Add('- NpcWorldActivity')
$summary.Add('- ScenePhysicalContact')
$summary.Add('')
$summary.Add('No source files, databases, rows, or NPCs were modified.')

$summary | Set-Content (Join-Path $outRoot 'PASS16_POSTPASS15_OWNERSHIP_SUMMARY.md') -Encoding UTF8

# Context for only the remaining duplicate writer/creator tables.
$context = New-Object 'System.Collections.Generic.List[string]'
$context.Add('# Pass 16 Remaining Duplicate Ownership Context')
$context.Add('')

$focusTables = @(
    ($multiWriters | Select-Object -ExpandProperty Table),
    ($multiCreators | Select-Object -ExpandProperty Table)
) | ForEach-Object { $_ } | Select-Object -Unique

foreach ($table in $focusTables) {
    $tableHits = @(
        $rows |
        Where-Object { $_.Table -eq $table -and $_.Kind -in @('CREATE','INSERT','UPDATE','DELETE') }
    )

    foreach ($h in $tableHits) {
        $path = Join-Path $root $h.File
        if (-not (Test-Path $path)) { continue }

        $lines = Get-Content $path
        $start = [Math]::Max(1, $h.Line - 25)
        $end   = [Math]::Min($lines.Count, $h.Line + 25)

        $context.Add("## $table — $($h.Kind) — $($h.File):$($h.Line)")
        $context.Add('')
        $context.Add('```csharp')
        for ($n = $start; $n -le $end; $n++) {
            $mark = if ($n -eq $h.Line) { '>>' } else { '  ' }
            $context.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
        }
        $context.Add('```')
        $context.Add('')
    }
}

$context | Set-Content (Join-Path $outRoot 'PASS16_REMAINING_DUPLICATE_CONTEXT.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 16 post-Pass15 ownership audit complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('Multiple runtime writer tables: ' + $multiWriters.Count)
foreach ($x in ($multiWriters | Sort-Object Table)) {
    Write-Host ('  ' + $x.Table + ' -> ' + $x.Count + ' writers')
}
Write-Host ''
Write-Host ('Multiple schema creator tables: ' + $multiCreators.Count)
foreach ($x in ($multiCreators | Sort-Object Table)) {
    Write-Host ('  ' + $x.Table + ' -> ' + $x.Count + ' creators')
}
Write-Host ''
Write-Host 'Upload these two files next:'
Write-Host '  PASS16_POSTPASS15_OWNERSHIP_SUMMARY.md'
Write-Host '  PASS16_REMAINING_DUPLICATE_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, rows, or NPCs were modified.'
