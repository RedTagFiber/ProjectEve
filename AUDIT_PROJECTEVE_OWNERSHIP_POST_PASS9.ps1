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
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("OwnershipAudit_PostPass9_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -notlike '*\.git\*'
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

$focusTables = @(
    'ActiveScene',
    'ScenePresence',
    'SceneBarrier',
    'ScenePerceptionEvidence',
    'ScenePhysicalContact',
    'SharedScenePlayerMembership',
    'RelationshipStates',
    'PersonalMemories',
    'NpcTraitValues',
    'NpcTraitControl',
    'Characters',
    'Locations',
    'NpcCurrentLocation',
    'NpcLocationVisits'
)

$focus = foreach ($r in $sql) {
    if ($focusTables -contains $r.Table) { $r }
}

@($multiWriters) | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_writer_tables.csv') -NoTypeInformation -Encoding UTF8
@($multiCreators) | Sort-Object Table | Export-Csv (Join-Path $outRoot 'multi_creator_tables.csv') -NoTypeInformation -Encoding UTF8
$focus | Export-Csv (Join-Path $outRoot 'focus_ownership_hotspots.csv') -NoTypeInformation -Encoding UTF8
$rows | Export-Csv (Join-Path $outRoot 'all_source_findings.csv') -NoTypeInformation -Encoding UTF8

$summary = @()
$summary += '# ProjectEve Ownership Audit - Post Pass 9'
$summary += ''
$summary += "- Multi-writer tables: $(@($multiWriters).Count)"
$summary += "- Multi-creator tables: $(@($multiCreators).Count)"
$summary += ''
$summary += '## Multi-writer tables'
$summary += ''
foreach ($x in ($multiWriters | Sort-Object Table)) {
    $summary += "- $($x.Table): $($x.Writers)"
}
$summary += ''
$summary += '## Multi-creator tables'
$summary += ''
foreach ($x in ($multiCreators | Sort-Object Table)) {
    $summary += "- $($x.Table): $($x.Creators)"
}
$summary | Set-Content (Join-Path $outRoot 'POSTPASS9_OWNERSHIP_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Post-Pass-9 ownership audit complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('Multi-writer tables : ' + @($multiWriters).Count)
Write-Host ('Multi-creator tables: ' + @($multiCreators).Count)
Write-Host ''
Write-Host 'Upload these next:'
Write-Host '  POSTPASS9_OWNERSHIP_SUMMARY.md'
Write-Host '  multi_writer_tables.csv'
Write-Host '  multi_creator_tables.csv'
Write-Host '  focus_ownership_hotspots.csv'
Write-Host ''
Write-Host 'No source files, NPCs, or databases were modified.'
