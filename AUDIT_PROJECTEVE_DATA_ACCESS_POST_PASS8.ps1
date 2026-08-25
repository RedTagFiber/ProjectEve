$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("DataAudit_PostPass8_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$baseline = [ordered]@{
    FilesTouchingData        = 55
    Findings                 = 968
    WritesCreates            = 315
    ReadsJoins               = 177
    MultiWriterTables        = 26
    MultiCreatorTables       = 15
}

$skipParts = @(
    '\bin\',
    '\obj\',
    '\.git\',
    '\Archive\',
    '\Backups\',
    '\Audit\'
)

$files = Get-ChildItem -Path $root -Recurse -File -Include *.cs,*.razor,*.ps1 |
    Where-Object {
        $full = $_.FullName
        -not ($skipParts | Where-Object { $full -like "*$_*" })
    }

$patterns = @(
    [pscustomobject]@{ Kind='SQL_CREATE'; Regex='(?i)\bCREATE\s+TABLE\b' },
    [pscustomobject]@{ Kind='SQL_INSERT'; Regex='(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\b' },
    [pscustomobject]@{ Kind='SQL_UPDATE'; Regex='(?i)\bUPDATE\s+[A-Za-z_][A-Za-z0-9_]*' },
    [pscustomobject]@{ Kind='SQL_DELETE'; Regex='(?i)\bDELETE\s+FROM\b' },
    [pscustomobject]@{ Kind='SQL_SELECT'; Regex='(?i)\bSELECT\b' },
    [pscustomobject]@{ Kind='SQL_JOIN'; Regex='(?i)\bJOIN\b' },
    [pscustomobject]@{ Kind='SQL_CONNECTION'; Regex='(?i)\bSqliteConnection\b|Data Source=|EVE_DB_PATH|project_eve(?:_[a-z]+)?\.db' },
    [pscustomobject]@{ Kind='FILE_IO'; Regex='(?i)\bFile\.(Read|Write|Append|Open|Create|Delete|Move|Copy)|\bDirectory\.(Create|Delete|Move)|\bJsonSerializer\.(Serialize|Deserialize)' },
    [pscustomobject]@{ Kind='DB_ROUTING'; Regex='(?i)ProjectEveDatabaseSetup\.(MainDatabasePath|HistoryDatabasePath|RelationshipDatabasePath|LocationDatabasePath)|ProjectEveDatabaseConnections\.(OpenMain|OpenHistory|OpenRelationships|OpenLocations)' }
)

function Get-SqlTableName {
    param([string]$Line, [string]$Kind)

    $m = $null
    switch ($Kind) {
        'SQL_CREATE' {
            $m = [regex]::Match($Line, '(?i)CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`\[]?([A-Za-z_][A-Za-z0-9_]*)')
        }
        'SQL_INSERT' {
            $m = [regex]::Match($Line, '(?i)INSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)')
        }
        'SQL_UPDATE' {
            $m = [regex]::Match($Line, '(?i)UPDATE\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)')
        }
        'SQL_DELETE' {
            $m = [regex]::Match($Line, '(?i)DELETE\s+FROM\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)')
        }
        'SQL_SELECT' {
            $m = [regex]::Match($Line, '(?i)\bFROM\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)')
        }
        'SQL_JOIN' {
            $m = [regex]::Match($Line, '(?i)\bJOIN\s+["`\[]?([A-Za-z_][A-Za-z0-9_]*)')
        }
    }

    if ($m -and $m.Success) { return $m.Groups[1].Value }
    return ''
}

$findings = New-Object System.Collections.Generic.List[object]
$dataFiles = New-Object 'System.Collections.Generic.HashSet[string]'

foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName)
    $lines = Get-Content $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        foreach ($p in $patterns) {
            if ([regex]::IsMatch($line, $p.Regex)) {
                $table = Get-SqlTableName -Line $line -Kind $p.Kind
                $obj = [pscustomobject]@{
                    File       = $relative
                    Line       = $i + 1
                    Kind       = $p.Kind
                    Table      = $table
                    Text       = $line.Trim()
                }
                $findings.Add($obj)
                [void]$dataFiles.Add($relative)
            }
        }
    }
}

$writes = $findings | Where-Object { $_.Kind -in @('SQL_CREATE','SQL_INSERT','SQL_UPDATE','SQL_DELETE') }
$reads  = $findings | Where-Object { $_.Kind -in @('SQL_SELECT','SQL_JOIN') }

$tableWriters = @{}
$tableCreators = @{}

foreach ($f in $findings) {
    if ([string]::IsNullOrWhiteSpace($f.Table)) { continue }

    if ($f.Kind -in @('SQL_INSERT','SQL_UPDATE','SQL_DELETE')) {
        if (-not $tableWriters.ContainsKey($f.Table)) {
            $tableWriters[$f.Table] = New-Object 'System.Collections.Generic.HashSet[string]'
        }
        [void]$tableWriters[$f.Table].Add($f.File)
    }

    if ($f.Kind -eq 'SQL_CREATE') {
        if (-not $tableCreators.ContainsKey($f.Table)) {
            $tableCreators[$f.Table] = New-Object 'System.Collections.Generic.HashSet[string]'
        }
        [void]$tableCreators[$f.Table].Add($f.File)
    }
}

$multiWriters = foreach ($kv in $tableWriters.GetEnumerator()) {
    if ($kv.Value.Count -gt 1) {
        [pscustomobject]@{
            Table = $kv.Key
            WriterCount = $kv.Value.Count
            Writers = (($kv.Value | Sort-Object) -join ' | ')
        }
    }
}

$multiCreators = foreach ($kv in $tableCreators.GetEnumerator()) {
    if ($kv.Value.Count -gt 1) {
        [pscustomobject]@{
            Table = $kv.Key
            CreatorCount = $kv.Value.Count
            Creators = (($kv.Value | Sort-Object) -join ' | ')
        }
    }
}

$current = [ordered]@{
    FilesTouchingData        = $dataFiles.Count
    Findings                 = $findings.Count
    WritesCreates            = @($writes).Count
    ReadsJoins               = @($reads).Count
    MultiWriterTables        = @($multiWriters).Count
    MultiCreatorTables       = @($multiCreators).Count
}

$summaryRows = foreach ($key in $baseline.Keys) {
    $b = [int]$baseline[$key]
    $c = [int]$current[$key]
    [pscustomobject]@{
        Metric   = $key
        Baseline = $b
        Current  = $c
        Delta    = $c - $b
    }
}

$findings | Export-Csv (Join-Path $outRoot 'all_findings.csv') -NoTypeInformation -Encoding UTF8
$writes   | Export-Csv (Join-Path $outRoot 'writes_creates.csv') -NoTypeInformation -Encoding UTF8
$reads    | Export-Csv (Join-Path $outRoot 'reads_joins.csv') -NoTypeInformation -Encoding UTF8
@($multiWriters)  | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_writer_tables.csv') -NoTypeInformation -Encoding UTF8
@($multiCreators) | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_creator_tables.csv') -NoTypeInformation -Encoding UTF8
$summaryRows | Export-Csv (Join-Path $outRoot 'baseline_comparison.csv') -NoTypeInformation -Encoding UTF8

# Targeted remaining legacy-owner scan.
$legacyTokens = @(
    'Traits',
    'TraitControl',
    'Relationships',
    'Memories',
    'History',
    'MoneyProfile',
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
    'ScenePhysicalContact',
    'SharedScenePlayerMembership'
)

$legacyHits = foreach ($token in $legacyTokens) {
    $hits = $findings | Where-Object {
        $_.Text -match ('(?i)\b' + [regex]::Escape($token) + '\b')
    }

    foreach ($h in $hits) {
        [pscustomobject]@{
            Table = $token
            File  = $h.File
            Line  = $h.Line
            Kind  = $h.Kind
            Text  = $h.Text
        }
    }
}

$legacyHits | Export-Csv (Join-Path $outRoot 'legacy_owner_hotspots.csv') -NoTypeInformation -Encoding UTF8

$md = New-Object System.Collections.Generic.List[string]
$md.Add('# ProjectEve Data Access Audit - Post Pass 8')
$md.Add('')
$md.Add('Generated: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$md.Add('')
$md.Add('## Baseline comparison')
$md.Add('')
$md.Add('| Metric | Baseline | Current | Delta |')
$md.Add('|---|---:|---:|---:|')
foreach ($row in $summaryRows) {
    $md.Add('| ' + $row.Metric + ' | ' + $row.Baseline + ' | ' + $row.Current + ' | ' + $row.Delta + ' |')
}

$md.Add('')
$md.Add('## Tables with multiple active writers')
$md.Add('')
if (@($multiWriters).Count -eq 0) {
    $md.Add('None detected.')
}
else {
    foreach ($row in ($multiWriters | Sort-Object Table)) {
        $md.Add('- ' + $row.Table + ' [' + $row.WriterCount + ' writers]: ' + $row.Writers)
    }
}

$md.Add('')
$md.Add('## Tables created in multiple files')
$md.Add('')
if (@($multiCreators).Count -eq 0) {
    $md.Add('None detected.')
}
else {
    foreach ($row in ($multiCreators | Sort-Object Table)) {
        $md.Add('- ' + $row.Table + ' [' + $row.CreatorCount + ' creators]: ' + $row.Creators)
    }
}

$md.Add('')
$md.Add('## Next cleanup use')
$md.Add('')
$md.Add('Use legacy_owner_hotspots.csv together with multi_writer_tables.csv to classify each remaining duplicate as:')
$md.Add('- KEEP canonical')
$md.Add('- MIGRATE reader')
$md.Add('- MIGRATE writer')
$md.Add('- RETIRE duplicate')
$md.Add('- TEMPORARY BRIDGE')

$md | Set-Content (Join-Path $outRoot 'AUDIT_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Post-Pass-8 data-access audit complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host 'Baseline -> Current'
foreach ($row in $summaryRows) {
    $sign = if ($row.Delta -gt 0) { '+' } else { '' }
    Write-Host ('  {0,-24} {1,5} -> {2,5}   ({3}{4})' -f `
        $row.Metric, $row.Baseline, $row.Current, $sign, $row.Delta)
}
Write-Host ''
Write-Host 'Key files:'
Write-Host '  AUDIT_SUMMARY.md'
Write-Host '  baseline_comparison.csv'
Write-Host '  multi_writer_tables.csv'
Write-Host '  multi_creator_tables.csv'
Write-Host '  legacy_owner_hotspots.csv'
Write-Host ''
Write-Host 'No source files, NPCs, or databases were modified.'
