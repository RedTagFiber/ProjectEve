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
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("DataAudit_PostPass8_SourceOnly_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

# IMPORTANT:
# Only compiled source is audited here. Migration/apply/rollback scripts are
# deliberately excluded because they made the previous comparison look worse
# even though they are not runtime data owners.
$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $p = $_.FullName
        $p -notlike '*\bin\*' -and
        $p -notlike '*\obj\*' -and
        $p -notlike '*\.git\*'
    }

function Match-Table {
    param([string]$Text,[string]$Kind)

    $pattern = switch ($Kind) {
        'CREATE' { '(?i)\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`\[]?([A-Za-z_][A-Za-z0-9_]*)' }
        'INSERT' { '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)' }
        'UPDATE' { '(?i)(?:^|[\s;"@])UPDATE\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)\b' }
        'DELETE' { '(?i)\bDELETE\s+FROM\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)' }
        'FROM'   { '(?i)\bFROM\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)' }
        'JOIN'   { '(?i)\bJOIN\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)' }
    }

    $m = [regex]::Match($Text, $pattern)
    if ($m.Success) { return $m.Groups[1].Value }
    return ''
}

$rows = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $rel = Get-RelativePathCompat $root $file.FullName
    $lines = Get-Content $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        # Avoid LINQ .Select/.Join false positives. We only record SELECT/JOIN
        # when the SQL keyword appears like SQL text, not as a dotted C# method.
        $tests = @(
            @{Kind='CREATE'; Regex='(?i)\bCREATE\s+TABLE\b'},
            @{Kind='INSERT'; Regex='(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\b'},
            @{Kind='UPDATE'; Regex='(?i)(?:^|[\s;"@])UPDATE\s+[A-Za-z_][A-Za-z0-9_]*\b'},
            @{Kind='DELETE'; Regex='(?i)\bDELETE\s+FROM\b'},
            @{Kind='SELECT'; Regex='(?i)(?:^|[\s;"@])SELECT\s+'},
            @{Kind='JOIN';   Regex='(?i)(?:^|[\s;"@])JOIN\s+[A-Za-z_][A-Za-z0-9_]*\b'}
        )

        foreach ($t in $tests) {
            if ([regex]::IsMatch($line, $t.Regex)) {
                $table = ''
                switch ($t.Kind) {
                    'CREATE' { $table = Match-Table $line 'CREATE' }
                    'INSERT' { $table = Match-Table $line 'INSERT' }
                    'UPDATE' { $table = Match-Table $line 'UPDATE' }
                    'DELETE' { $table = Match-Table $line 'DELETE' }
                    'SELECT' { $table = Match-Table $line 'FROM' }
                    'JOIN'   { $table = Match-Table $line 'JOIN' }
                }

                $rows.Add([pscustomobject]@{
                    File  = $rel
                    Line  = $i + 1
                    Kind  = 'SQL_' + $t.Kind
                    Table = $table
                    Text  = $line.Trim()
                })
            }
        }

        if ($line -match '(?i)SqliteConnection|EVE_DB_PATH|ProjectEveDatabaseSetup\.(MainDatabasePath|HistoryDatabasePath|RelationshipDatabasePath|LocationDatabasePath)|ProjectEveDatabaseConnections\.(OpenMain|OpenHistory|OpenRelationships|OpenLocations)') {
            $rows.Add([pscustomobject]@{
                File=$rel; Line=$i+1; Kind='DB_ACCESS'; Table=''; Text=$line.Trim()
            })
        }
    }
}

$sql = $rows | Where-Object { $_.Kind -like 'SQL_*' }
$writes = $sql | Where-Object { $_.Kind -in @('SQL_CREATE','SQL_INSERT','SQL_UPDATE','SQL_DELETE') }
$reads  = $sql | Where-Object { $_.Kind -in @('SQL_SELECT','SQL_JOIN') }

$writers = @{}
$creators = @{}

foreach ($r in $sql) {
    if ([string]::IsNullOrWhiteSpace($r.Table)) { continue }

    if ($r.Kind -in @('SQL_INSERT','SQL_UPDATE','SQL_DELETE')) {
        if (-not $writers.ContainsKey($r.Table)) {
            $writers[$r.Table] = New-Object 'System.Collections.Generic.HashSet[string]'
        }
        [void]$writers[$r.Table].Add($r.File)
    }

    if ($r.Kind -eq 'SQL_CREATE') {
        if (-not $creators.ContainsKey($r.Table)) {
            $creators[$r.Table] = New-Object 'System.Collections.Generic.HashSet[string]'
        }
        [void]$creators[$r.Table].Add($r.File)
    }
}

$multiWriters = foreach ($kv in $writers.GetEnumerator()) {
    if ($kv.Value.Count -gt 1) {
        [pscustomobject]@{
            Table=$kv.Key
            WriterCount=$kv.Value.Count
            Writers=(($kv.Value | Sort-Object) -join ' | ')
        }
    }
}

$multiCreators = foreach ($kv in $creators.GetEnumerator()) {
    if ($kv.Value.Count -gt 1) {
        [pscustomobject]@{
            Table=$kv.Key
            CreatorCount=$kv.Value.Count
            Creators=(($kv.Value | Sort-Object) -join ' | ')
        }
    }
}

$dataFiles = @($rows | Select-Object -ExpandProperty File -Unique)

$summary = [pscustomobject]@{
    CompiledDataFiles = $dataFiles.Count
    TotalFindings = $rows.Count
    SqlWritesCreates = @($writes).Count
    SqlReadsJoins = @($reads).Count
    MultiWriterTables = @($multiWriters).Count
    MultiCreatorTables = @($multiCreators).Count
}

$rows | Export-Csv (Join-Path $outRoot 'source_findings.csv') -NoTypeInformation -Encoding UTF8
@($multiWriters) | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_writer_tables.csv') -NoTypeInformation -Encoding UTF8
@($multiCreators) | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_creator_tables.csv') -NoTypeInformation -Encoding UTF8

# Focus list: actual compiled files touching legacy/canonical ownership hotspots.
$focusTables = @(
    'Characters',
    'Traits',
    'TraitControl',
    'NpcTraitValues',
    'NpcTraitControl',
    'Relationships',
    'RelationshipStates',
    'Memories',
    'PersonalMemories',
    'History',
    'WorldEvents',
    'ConversationSession',
    'ConversationMessage',
    'ConversationEvent',
    'ConversationFact',
    'ConversationPlan',
    'Locations',
    'NpcCurrentLocation',
    'NpcLocationVisits',
    'TravelLocationIndex',
    'PlayerKnownLocation',
    'NpcScheduleBinding',
    'NpcShiftAssignment',
    'NpcScheduleOverride',
    'NpcWorldLocationState',
    'NpcWorldMovementEvent',
    'ActiveScene',
    'ScenePresence',
    'SceneBarrier',
    'ScenePerceptionEvidence',
    'ScenePhysicalContact',
    'SharedScenePlayerMembership'
)

$focus = foreach ($r in $sql) {
    if ($focusTables -contains $r.Table) { $r }
}
$focus | Export-Csv (Join-Path $outRoot 'ownership_hotspots.csv') -NoTypeInformation -Encoding UTF8

$md = @()
$md += '# ProjectEve Runtime Source-Only Data Audit'
$md += ''
$md += 'This audit excludes migration/apply/rollback PowerShell scripts and ignores LINQ Select/Join false positives.'
$md += ''
$md += '## Current compiled-source totals'
$md += ''
$md += "- Compiled files touching data: $($summary.CompiledDataFiles)"
$md += "- Findings: $($summary.TotalFindings)"
$md += "- SQL writes/creates: $($summary.SqlWritesCreates)"
$md += "- SQL reads/joins: $($summary.SqlReadsJoins)"
$md += "- Tables with multiple runtime writers: $($summary.MultiWriterTables)"
$md += "- Tables with multiple runtime creators: $($summary.MultiCreatorTables)"
$md += ''
$md += '## Multiple runtime writers'
$md += ''
foreach ($x in ($multiWriters | Sort-Object Table)) {
    $md += "- $($x.Table): $($x.Writers)"
}
$md += ''
$md += '## Multiple runtime creators'
$md += ''
foreach ($x in ($multiCreators | Sort-Object Table)) {
    $md += "- $($x.Table): $($x.Creators)"
}
$md | Set-Content (Join-Path $outRoot 'SOURCE_ONLY_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Source-only runtime data audit complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('Compiled files touching data : ' + $summary.CompiledDataFiles)
Write-Host ('Findings                    : ' + $summary.TotalFindings)
Write-Host ('SQL writes/creates           : ' + $summary.SqlWritesCreates)
Write-Host ('SQL reads/joins              : ' + $summary.SqlReadsJoins)
Write-Host ('Multi-writer tables          : ' + $summary.MultiWriterTables)
Write-Host ('Multi-creator tables         : ' + $summary.MultiCreatorTables)
Write-Host ''
Write-Host 'Upload these four files next:'
Write-Host '  SOURCE_ONLY_SUMMARY.md'
Write-Host '  multi_writer_tables.csv'
Write-Host '  multi_creator_tables.csv'
Write-Host '  ownership_hotspots.csv'
Write-Host ''
Write-Host 'No source files, NPCs, or databases were modified.'
