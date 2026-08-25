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
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass13_Remaining_Runtime_Writers_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$focusTables = @(
    'Characters',
    'NpcTraitValues',
    'NpcWorldActivity',
    'PersonalMemories',
    'RelationshipStates',
    'ScenePhysicalContact'
)

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

            if ($kind) {
                $rows.Add([pscustomobject]@{
                    Table = $table
                    File  = $rel
                    Line  = $i + 1
                    Kind  = $kind
                    Text  = $line.Trim()
                })
            }
        }
    }
}

$rows | Export-Csv (Join-Path $outRoot 'remaining_runtime_writer_hits.csv') -NoTypeInformation -Encoding UTF8

$summary = New-Object 'System.Collections.Generic.List[string]'
$summary.Add('# Pass 13 Remaining Runtime Writers Preflight')
$summary.Add('')

foreach ($table in $focusTables) {
    $hits = @($rows | Where-Object Table -eq $table)
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

    $summary.Add("## $table")
    $summary.Add("")
    $summary.Add("Writers: $($writers.Count)")
    foreach ($w in $writers) { $summary.Add("- $w") }
    $summary.Add("Creators: $($creators.Count)")
    foreach ($c in $creators) { $summary.Add("- $c") }
    $summary.Add("")
    foreach ($h in $hits) {
        $summary.Add("- [$($h.Kind)] $($h.File):$($h.Line) — $($h.Text)")
    }
    $summary.Add("")
}

$summary | Set-Content (Join-Path $outRoot 'PASS13_REMAINING_WRITERS_SUMMARY.md') -Encoding UTF8

# Rich context around every write statement.
$context = New-Object 'System.Collections.Generic.List[string]'
$context.Add('# Pass 13 Writer Context')
$context.Add('')

foreach ($h in ($rows | Where-Object Kind -in @('INSERT','UPDATE','DELETE'))) {
    $path = Join-Path $root $h.File
    $lines = Get-Content $path
    $start = [Math]::Max(1, $h.Line - 22)
    $end   = [Math]::Min($lines.Count, $h.Line + 22)

    $context.Add("## $($h.Table) — $($h.File):$($h.Line)")
    $context.Add("")
    $context.Add('```csharp')
    for ($n = $start; $n -le $end; $n++) {
        $mark = if ($n -eq $h.Line) { '>>' } else { '  ' }
        $context.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
    }
    $context.Add('```')
    $context.Add('')
}

$context | Set-Content (Join-Path $outRoot 'PASS13_WRITER_CONTEXT.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 13 remaining runtime-writer preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
foreach ($table in $focusTables) {
    $writers = @(
        $rows |
        Where-Object { $_.Table -eq $table -and $_.Kind -in @('INSERT','UPDATE','DELETE') } |
        Select-Object -ExpandProperty File -Unique
    )
    Write-Host ($table.PadRight(24) + ' writers=' + $writers.Count)
}
Write-Host ''
Write-Host 'Upload these two files next:'
Write-Host '  PASS13_REMAINING_WRITERS_SUMMARY.md'
Write-Host '  PASS13_WRITER_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, or NPCs were modified.'
