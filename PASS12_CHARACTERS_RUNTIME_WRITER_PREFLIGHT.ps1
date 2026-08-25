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

function Add-ContextBlock {
    param(
        [System.Collections.Generic.List[string]]$Output,
        [string]$FilePath,
        [int]$HitLine,
        [int]$Radius = 18
    )

    $lines = Get-Content $FilePath
    $start = [Math]::Max(1, $HitLine - $Radius)
    $end = [Math]::Min($lines.Count, $HitLine + $Radius)

    $rel = Get-RelativePathCompat $root $FilePath
    $Output.Add("## $rel : line $HitLine")
    $Output.Add("")
    $Output.Add('```csharp')
    for ($n = $start; $n -le $end; $n++) {
        $prefix = if ($n -eq $HitLine) { '>>' } else { '  ' }
        $Output.Add(('{0} {1,5}: {2}' -f $prefix, $n, $lines[$n-1]))
    }
    $Output.Add('```')
    $Output.Add("")
}

$targets = @(
    'Program.cs',
    'World\SmallTown\Population\FamilyFriendWebSystem.cs',
    'Characters\Base\CharacterRepository.cs'
)

foreach ($rel in $targets) {
    if (-not (Test-Path (Join-Path $root $rel))) {
        throw "Required file not found: $rel"
    }
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass12_Characters_RuntimeWriter_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$hits = New-Object System.Collections.Generic.List[object]

foreach ($rel in $targets) {
    $path = Join-Path $root $rel
    $lines = Get-Content $path

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $kind = $null

        if ($line -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?Characters\b') {
            $kind = 'INSERT'
        }
        elseif ($line -match '(?i)(?:^|[\s;"@])UPDATE\s+["`\[]?Characters\b') {
            $kind = 'UPDATE'
        }
        elseif ($line -match '(?i)\bDELETE\s+FROM\s+["`\[]?Characters\b') {
            $kind = 'DELETE'
        }
        elseif ($line -match '(?i)\bFROM\s+["`\[]?Characters\b') {
            $kind = 'READ'
        }

        if ($kind) {
            $hits.Add([pscustomobject]@{
                File = $rel
                Line = $i + 1
                Kind = $kind
                Text = $line.Trim()
            })
        }
    }
}

$writerHits = @($hits | Where-Object Kind -in @('INSERT','UPDATE','DELETE'))
$readerHits = @($hits | Where-Object Kind -eq 'READ')

$hits | Export-Csv (Join-Path $outRoot 'characters_writer_hits.csv') -NoTypeInformation -Encoding UTF8

# Capture every runtime write with context.
$writerContext = New-Object 'System.Collections.Generic.List[string]'
$writerContext.Add('# Pass 12 Characters Runtime Writer Context')
$writerContext.Add('')

foreach ($hit in $writerHits) {
    Add-ContextBlock -Output $writerContext -FilePath (Join-Path $root $hit.File) -HitLine $hit.Line -Radius 22
}
$writerContext | Set-Content (Join-Path $outRoot 'CHARACTERS_WRITER_CONTEXT.md') -Encoding UTF8

# Capture CharacterRepository API signatures so we can decide whether writers can route through it.
$repoPath = Join-Path $root 'Characters\Base\CharacterRepository.cs'
$repoLines = Get-Content $repoPath

$repoApi = New-Object 'System.Collections.Generic.List[string]'
$repoApi.Add('# CharacterRepository API / SQL Context')
$repoApi.Add('')

for ($i = 0; $i -lt $repoLines.Count; $i++) {
    $line = $repoLines[$i]

    if ($line -match '^\s*(public|internal)\s+.*\(' -or
        $line -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+Characters\b' -or
        $line -match '(?i)(?:^|[\s;"@])UPDATE\s+Characters\b' -or
        $line -match '(?i)\bFROM\s+Characters\b') {

        $start = [Math]::Max(0, $i - 3)
        $end = [Math]::Min($repoLines.Count - 1, $i + 7)

        $repoApi.Add('```csharp')
        for ($n = $start; $n -le $end; $n++) {
            $repoApi.Add(('{0,5}: {1}' -f ($n + 1), $repoLines[$n]))
        }
        $repoApi.Add('```')
        $repoApi.Add('')
    }
}

$repoApi | Set-Content (Join-Path $outRoot 'CHARACTER_REPOSITORY_API_CONTEXT.md') -Encoding UTF8

$summary = New-Object 'System.Collections.Generic.List[string]'
$summary.Add('# Pass 12 Characters Runtime Writer Preflight')
$summary.Add('')
$summary.Add("Runtime write statements found: $($writerHits.Count)")
$summary.Add('')
foreach ($h in $writerHits) {
    $summary.Add("- [$($h.Kind)] $($h.File):$($h.Line) — $($h.Text)")
}
$summary.Add('')
$summary.Add('## Goal')
$summary.Add('')
$summary.Add('Classify each Characters write as:')
$summary.Add('- KEEP canonical')
$summary.Add('- ROUTE through CharacterRepository')
$summary.Add('- RETIRE legacy write')
$summary.Add('- TEMPORARY seeder-only write')
$summary.Add('')
$summary.Add('No source files or databases were modified.')
$summary | Set-Content (Join-Path $outRoot 'PASS12_CHARACTERS_WRITER_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 12 Characters runtime-writer preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('Runtime write statements found: ' + $writerHits.Count)
foreach ($h in $writerHits) {
    Write-Host ('  ' + $h.Kind.PadRight(7) + ' ' + $h.File + ':' + $h.Line)
}
Write-Host ''
Write-Host 'Upload these three files next:'
Write-Host '  PASS12_CHARACTERS_WRITER_SUMMARY.md'
Write-Host '  CHARACTERS_WRITER_CONTEXT.md'
Write-Host '  CHARACTER_REPOSITORY_API_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, or NPCs were modified.'
