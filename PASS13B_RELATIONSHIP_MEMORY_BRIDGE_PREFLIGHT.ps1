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

$targets = @(
    'DATA\ProjectEveCanonicalSync.cs',
    'Memory\MemoryDatabase.cs',
    'Relationships\RelationshipRepository.cs'
)

foreach ($rel in $targets) {
    if (-not (Test-Path (Join-Path $root $rel))) {
        throw "Required file not found: $rel"
    }
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass13B_RelationshipMemory_Bridge_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$tables = @('PersonalMemories','RelationshipStates')
$hits = New-Object System.Collections.Generic.List[object]

$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -notlike '*\.git\*'
    }

foreach ($file in $files) {
    $lines = Get-Content $file.FullName
    for ($i=0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        foreach ($table in $tables) {
            $kind = $null
            if ($line -match ('(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?' + $table + '\b')) {
                $kind = 'INSERT'
            }
            elseif ($line -match ('(?i)(?:^|[\s;"@])UPDATE\s+["`\[]?' + $table + '\b')) {
                $kind = 'UPDATE'
            }
            elseif ($line -match ('(?i)\bDELETE\s+FROM\s+["`\[]?' + $table + '\b')) {
                $kind = 'DELETE'
            }
            elseif ($line -match ('(?i)\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`\[]?' + $table + '\b')) {
                $kind = 'CREATE'
            }
            elseif ($line -match ('(?i)\bFROM\s+["`\[]?' + $table + '\b')) {
                $kind = 'READ'
            }

            if ($kind) {
                $hits.Add([pscustomobject]@{
                    Table = $table
                    File = Rel $file.FullName
                    Line = $i + 1
                    Kind = $kind
                    Text = $line.Trim()
                })
            }
        }
    }
}

$hits | Export-Csv (Join-Path $outRoot 'relationship_memory_hits.csv') -NoTypeInformation -Encoding UTF8

# Context around writes only
$ctx = New-Object 'System.Collections.Generic.List[string]'
$ctx.Add('# Pass 13B Relationship / Memory Writer Context')
$ctx.Add('')

foreach ($h in ($hits | Where-Object Kind -in @('INSERT','UPDATE','DELETE'))) {
    $path = Join-Path $root $h.File
    $lines = Get-Content $path
    $start = [Math]::Max(1, $h.Line - 30)
    $end = [Math]::Min($lines.Count, $h.Line + 30)

    $ctx.Add("## $($h.Table) — $($h.File):$($h.Line)")
    $ctx.Add('')
    $ctx.Add('```csharp')
    for ($n=$start; $n -le $end; $n++) {
        $mark = if ($n -eq $h.Line) { '>>' } else { '  ' }
        $ctx.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
    }
    $ctx.Add('```')
    $ctx.Add('')
}
$ctx | Set-Content (Join-Path $outRoot 'PASS13B_WRITER_CONTEXT.md') -Encoding UTF8

# API/signature context from the three key files
$api = New-Object 'System.Collections.Generic.List[string]'
$api.Add('# Pass 13B Key API Context')
$api.Add('')

foreach ($rel in $targets) {
    $path = Join-Path $root $rel
    $lines = Get-Content $path
    $api.Add("## $rel")
    $api.Add('')

    for ($i=0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*(public|internal)\s+.*\(' -or
            $line -match '^\s*private\s+.*\(' -and
            ($line -match 'Memory|Relationship|Sync|Mirror|Upsert|Save|Write|Import|Migrate')) {

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

$api | Set-Content (Join-Path $outRoot 'PASS13B_KEY_API_CONTEXT.md') -Encoding UTF8

$summary = New-Object 'System.Collections.Generic.List[string]'
$summary.Add('# Pass 13B Relationship / Memory Bridge Preflight')
$summary.Add('')

foreach ($table in $tables) {
    $writers = @(
        $hits |
        Where-Object { $_.Table -eq $table -and $_.Kind -in @('INSERT','UPDATE','DELETE') } |
        Select-Object -ExpandProperty File -Unique
    )
    $creators = @(
        $hits |
        Where-Object { $_.Table -eq $table -and $_.Kind -eq 'CREATE' } |
        Select-Object -ExpandProperty File -Unique
    )

    $summary.Add("## $table")
    $summary.Add("")
    $summary.Add("Writers: $($writers.Count)")
    foreach ($w in $writers) { $summary.Add("- $w") }
    $summary.Add("Creators: $($creators.Count)")
    foreach ($c in $creators) { $summary.Add("- $c") }
    $summary.Add("")
}

$summary.Add('## Goal')
$summary.Add('')
$summary.Add('Determine whether ProjectEveCanonicalSync is now only a migration bridge.')
$summary.Add('If so, route/retire its live writes while keeping repository/runtime gateways canonical.')
$summary.Add('')
$summary.Add('No source files, databases, memories, relationships, or NPCs were modified.')

$summary | Set-Content (Join-Path $outRoot 'PASS13B_RELATIONSHIP_MEMORY_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 13B relationship/memory bridge preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''

foreach ($table in $tables) {
    $writers = @(
        $hits |
        Where-Object { $_.Table -eq $table -and $_.Kind -in @('INSERT','UPDATE','DELETE') } |
        Select-Object -ExpandProperty File -Unique
    )
    Write-Host ($table.PadRight(22) + ' writers=' + $writers.Count)
    foreach ($w in $writers) { Write-Host ('  ' + $w) }
}

Write-Host ''
Write-Host 'Upload these three files next:'
Write-Host '  PASS13B_RELATIONSHIP_MEMORY_SUMMARY.md'
Write-Host '  PASS13B_WRITER_CONTEXT.md'
Write-Host '  PASS13B_KEY_API_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, memories, relationships, or NPCs were modified.'
