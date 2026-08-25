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
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass11_Characters_Ownership_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$files = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -notlike '*\.git\*'
    }

$hits = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $rel = Get-RelativePathCompat $root $file.FullName
    $lines = Get-Content $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        $kind = $null

        if ($line -match '(?i)\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`\[]?Characters\b') {
            $kind = 'CREATE'
        }
        elseif ($line -match '(?i)\bINSERT\s+(?:OR\s+\w+\s+)?INTO\s+["`\[]?Characters\b') {
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
        elseif ($line -match '(?i)\bJOIN\s+["`\[]?Characters\b') {
            $kind = 'JOIN'
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

$creators = @($hits | Where-Object Kind -eq 'CREATE' | Select-Object -ExpandProperty File -Unique)
$writers  = @($hits | Where-Object Kind -in @('INSERT','UPDATE','DELETE') | Select-Object -ExpandProperty File -Unique)
$readers  = @($hits | Where-Object Kind -in @('READ','JOIN') | Select-Object -ExpandProperty File -Unique)

$hits | Export-Csv (Join-Path $outRoot 'characters_all_hits.csv') -NoTypeInformation -Encoding UTF8

$summary = @()
$summary += '# Pass 11 Characters Ownership Preflight'
$summary += ''
$summary += "Schema creators: $($creators.Count)"
foreach ($x in $creators) { $summary += "- CREATE: $x" }
$summary += ''
$summary += "Runtime writers: $($writers.Count)"
foreach ($x in $writers) { $summary += "- WRITE: $x" }
$summary += ''
$summary += "Readers: $($readers.Count)"
foreach ($x in $readers) { $summary += "- READ: $x" }
$summary += ''
$summary += '## All hits'
$summary += ''
foreach ($h in $hits) {
    $summary += "- [$($h.Kind)] $($h.File):$($h.Line) — $($h.Text)"
}
$summary | Set-Content (Join-Path $outRoot 'PASS11_CHARACTERS_OWNERSHIP_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 11 Characters ownership preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('Schema creators: ' + $creators.Count)
foreach ($x in $creators) { Write-Host ('  CREATE  ' + $x) }
Write-Host ''
Write-Host ('Runtime writers: ' + $writers.Count)
foreach ($x in $writers) { Write-Host ('  WRITE   ' + $x) }
Write-Host ''
Write-Host ('Readers: ' + $readers.Count)
foreach ($x in $readers) { Write-Host ('  READ    ' + $x) }
Write-Host ''
Write-Host 'Upload this file next:'
Write-Host '  PASS11_CHARACTERS_OWNERSHIP_SUMMARY.md'
Write-Host ''
Write-Host 'No source files, databases, or NPCs were modified.'
