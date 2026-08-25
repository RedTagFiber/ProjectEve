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

$focusTables = @(
    'Characters',
    'NpcTraitValues',
    'PersonalMemories',
    'RelationshipStates',
    'NpcWorldActivity',
    'ScenePhysicalContact'
)

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass14_PostCanonicalSync_RuntimeWriter_Audit_" + $stamp)
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

        foreach ($table in $focusTables) {
            $kind = $null

            if ($line -match ('(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?' + [regex]::Escape($table) + '\b')) {
                $kind = 'INSERT'
            }
            elseif ($line -match ('(?i)(?:^|[\s;"@])UPDATE\s+["`\[]?' + [regex]::Escape($table) + '\b')) {
                $kind = 'UPDATE'
            }
            elseif ($line -match ('(?i)\bDELETE\s+FROM\s+["`\[]?' + [regex]::Escape($table) + '\b')) {
                $kind = 'DELETE'
            }
            elseif ($line -match ('(?i)\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`\[]?' + [regex]::Escape($table) + '\b')) {
                $kind = 'CREATE'
            }
            elseif ($line -match ('(?i)\bFROM\s+["`\[]?' + [regex]::Escape($table) + '\b')) {
                $kind = 'READ'
            }
            elseif ($line -match ('(?i)\bJOIN\s+["`\[]?' + [regex]::Escape($table) + '\b')) {
                $kind = 'JOIN'
            }

            if ($kind) {
                $hits.Add([pscustomobject]@{
                    Table = $table
                    File  = Rel $file.FullName
                    Line  = $i + 1
                    Kind  = $kind
                    Text  = $line.Trim()
                })
            }
        }
    }
}

$hits | Export-Csv (Join-Path $outRoot 'post13e_runtime_writer_hits.csv') -NoTypeInformation -Encoding UTF8

$summary = New-Object 'System.Collections.Generic.List[string]'
$summary.Add('# Pass 14 Post-CanonicalSync Runtime Writer Audit')
$summary.Add('')

foreach ($table in $focusTables) {
    $tableHits = @($hits | Where-Object Table -eq $table)
    $writers = @(
        $tableHits |
        Where-Object Kind -in @('INSERT','UPDATE','DELETE') |
        Select-Object -ExpandProperty File -Unique
    )
    $creators = @(
        $tableHits |
        Where-Object Kind -eq 'CREATE' |
        Select-Object -ExpandProperty File -Unique
    )

    $summary.Add("## $table")
    $summary.Add("")
    $summary.Add("Runtime writers: $($writers.Count)")
    foreach ($w in $writers) { $summary.Add("- $w") }
    $summary.Add("Schema creators: $($creators.Count)")
    foreach ($c in $creators) { $summary.Add("- $c") }
    $summary.Add("")

    foreach ($h in $tableHits) {
        $summary.Add("- [$($h.Kind)] $($h.File):$($h.Line) — $($h.Text)")
    }
    $summary.Add("")
}

$summary.Add('## Expected architectural result')
$summary.Add('')
$summary.Add('- Characters: CharacterRepository only')
$summary.Add('- NpcTraitValues: NpcTraitRepository only')
$summary.Add('- PersonalMemories: MemoryDatabase only')
$summary.Add('- RelationshipStates: RelationshipRepository only')
$summary.Add('- NpcWorldActivity: classify next')
$summary.Add('- ScenePhysicalContact: classify next')
$summary.Add('')
$summary.Add('No source files, databases, or NPCs were modified.')

$summary | Set-Content (Join-Path $outRoot 'PASS14_POST13E_RUNTIME_WRITER_SUMMARY.md') -Encoding UTF8

# Context for only the tables that still have more than one runtime writer,
# plus the two known remaining hotspots regardless.
$context = New-Object 'System.Collections.Generic.List[string]'
$context.Add('# Pass 14 Remaining Writer Context')
$context.Add('')

$tablesForContext = @('NpcWorldActivity','ScenePhysicalContact')

foreach ($table in $focusTables) {
    $writers = @(
        $hits |
        Where-Object { $_.Table -eq $table -and $_.Kind -in @('INSERT','UPDATE','DELETE') } |
        Select-Object -ExpandProperty File -Unique
    )
    if ($writers.Count -gt 1 -and $tablesForContext -notcontains $table) {
        $tablesForContext += $table
    }
}

foreach ($h in ($hits | Where-Object { $_.Kind -in @('INSERT','UPDATE','DELETE') -and $tablesForContext -contains $_.Table })) {
    $path = Join-Path $root $h.File
    $lines = Get-Content $path
    $start = [Math]::Max(1, $h.Line - 26)
    $end = [Math]::Min($lines.Count, $h.Line + 26)

    $context.Add("## $($h.Table) — $($h.File):$($h.Line)")
    $context.Add("")
    $context.Add('```csharp')
    for ($n=$start; $n -le $end; $n++) {
        $mark = if ($n -eq $h.Line) { '>>' } else { '  ' }
        $context.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
    }
    $context.Add('```')
    $context.Add('')
}

$context | Set-Content (Join-Path $outRoot 'PASS14_REMAINING_WRITER_CONTEXT.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 14 post-CanonicalSync runtime-writer audit complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''

foreach ($table in $focusTables) {
    $writers = @(
        $hits |
        Where-Object { $_.Table -eq $table -and $_.Kind -in @('INSERT','UPDATE','DELETE') } |
        Select-Object -ExpandProperty File -Unique
    )
    Write-Host ($table.PadRight(24) + ' writers=' + $writers.Count)
    foreach ($w in $writers) { Write-Host ('  ' + $w) }
}

Write-Host ''
Write-Host 'Upload these two files next:'
Write-Host '  PASS14_POST13E_RUNTIME_WRITER_SUMMARY.md'
Write-Host '  PASS14_REMAINING_WRITER_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, or NPCs were modified.'
