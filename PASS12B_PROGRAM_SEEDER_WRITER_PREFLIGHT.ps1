$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$programPath = Join-Path $root 'Program.cs'
$repoPath = Join-Path $root 'Characters\Base\CharacterRepository.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $programPath)) { throw 'Program.cs was not found.' }
if (-not (Test-Path $repoPath)) { throw 'Characters\Base\CharacterRepository.cs was not found.' }

function Get-RelativePathCompat {
    param([string]$BasePath,[string]$FullPath)
    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\')
    $full = [System.IO.Path]::GetFullPath($FullPath)
    if ($full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($base.Length).TrimStart('\')
    }
    return $full
}

function Add-Context {
    param(
        [System.Collections.Generic.List[string]]$Out,
        [string]$FilePath,
        [int]$LineNumber,
        [int]$Radius = 35
    )

    $lines = Get-Content $FilePath
    $start = [Math]::Max(1, $LineNumber - $Radius)
    $end = [Math]::Min($lines.Count, $LineNumber + $Radius)

    $Out.Add("## $(Get-RelativePathCompat $root $FilePath) : line $LineNumber")
    $Out.Add("")
    $Out.Add('```csharp')
    for ($n = $start; $n -le $end; $n++) {
        $mark = if ($n -eq $LineNumber) { '>>' } else { '  ' }
        $Out.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
    }
    $Out.Add('```')
    $Out.Add('')
}

$programLines = Get-Content $programPath

$hits = New-Object System.Collections.Generic.List[object]

for ($i = 0; $i -lt $programLines.Count; $i++) {
    $line = $programLines[$i]

    if ($line -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+Characters\b') {
        $hits.Add([pscustomobject]@{
            Line = $i + 1
            Kind = 'INSERT'
            Text = $line.Trim()
        })
    }
    elseif ($line -match '(?i)(?:^|[\s;"@])UPDATE\s+Characters\b') {
        $hits.Add([pscustomobject]@{
            Line = $i + 1
            Kind = 'UPDATE'
            Text = $line.Trim()
        })
    }
    elseif ($line -match '(?i)\bDELETE\s+FROM\s+Characters\b') {
        $hits.Add([pscustomobject]@{
            Line = $i + 1
            Kind = 'DELETE'
            Text = $line.Trim()
        })
    }
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass12B_Program_Seeder_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$hits | Export-Csv (Join-Path $outRoot 'program_characters_write_hits.csv') -NoTypeInformation -Encoding UTF8

$ctx = New-Object 'System.Collections.Generic.List[string]'
$ctx.Add('# Pass 12B Program Characters Seeder Writer Context')
$ctx.Add('')
foreach ($h in $hits) {
    Add-Context -Out $ctx -FilePath $programPath -LineNumber $h.Line -Radius 40
}
$ctx | Set-Content (Join-Path $outRoot 'PROGRAM_CHARACTERS_WRITER_CONTEXT.md') -Encoding UTF8

# Extract relevant CharacterRepository methods and all Characters write SQL.
$repoLines = Get-Content $repoPath
$repoOut = New-Object 'System.Collections.Generic.List[string]'
$repoOut.Add('# CharacterRepository Creation/Save API Context')
$repoOut.Add('')

for ($i = 0; $i -lt $repoLines.Count; $i++) {
    $line = $repoLines[$i]

    $interesting =
        $line -match '^\s*(public|internal)\s+.*\(' -or
        $line -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+Characters\b' -or
        $line -match '(?i)(?:^|[\s;"@])UPDATE\s+Characters\b'

    if ($interesting) {
        $start = [Math]::Max(0, $i - 5)
        $end = [Math]::Min($repoLines.Count - 1, $i + 14)
        $repoOut.Add('```csharp')
        for ($n = $start; $n -le $end; $n++) {
            $repoOut.Add(('{0,5}: {1}' -f ($n + 1), $repoLines[$n]))
        }
        $repoOut.Add('```')
        $repoOut.Add('')
    }
}
$repoOut | Set-Content (Join-Path $outRoot 'CHARACTER_REPOSITORY_CREATION_API.md') -Encoding UTF8

# Find method names surrounding each Program write.
$methodSummary = New-Object 'System.Collections.Generic.List[string]'
$methodSummary.Add('# Pass 12B Program Seeder Classification Summary')
$methodSummary.Add('')
$methodSummary.Add("Direct Program Characters writes found: $($hits.Count)")
$methodSummary.Add('')

foreach ($h in $hits) {
    $methodName = '(unknown)'
    for ($j = $h.Line - 2; $j -ge 0; $j--) {
        $candidate = $programLines[$j]
        $m = [regex]::Match($candidate, '^\s*static\s+[^\(]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(')
        if ($m.Success) {
            $methodName = $m.Groups[1].Value
            break
        }
    }

    $methodSummary.Add("- $($h.Kind) line $($h.Line) in method $methodName")
    $methodSummary.Add("  $($h.Text)")
}

$methodSummary.Add('')
$methodSummary.Add('## Goal')
$methodSummary.Add('')
$methodSummary.Add('For each Program write, decide:')
$methodSummary.Add('- route through CharacterRepository existing API')
$methodSummary.Add('- add a dedicated repository method')
$methodSummary.Add('- keep as explicit seeder-only compatibility write')
$methodSummary.Add('- retire obsolete write')
$methodSummary.Add('')
$methodSummary.Add('No source files or databases were modified.')
$methodSummary | Set-Content (Join-Path $outRoot 'PASS12B_PROGRAM_SEEDER_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 12B Program seeder writer preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('Direct Program Characters writes found: ' + $hits.Count)
foreach ($h in $hits) {
    Write-Host ('  ' + $h.Kind.PadRight(7) + ' Program.cs:' + $h.Line)
}
Write-Host ''
Write-Host 'Upload these three files next:'
Write-Host '  PASS12B_PROGRAM_SEEDER_SUMMARY.md'
Write-Host '  PROGRAM_CHARACTERS_WRITER_CONTEXT.md'
Write-Host '  CHARACTER_REPOSITORY_CREATION_API.md'
Write-Host ''
Write-Host 'No source files, databases, or NPCs were modified.'
