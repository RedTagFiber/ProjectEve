$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

$aRel = 'DATA\World\Scene\ScenePerceptionService.cs'
$bRel = 'Scene\ScenePerceptionService.cs'
$a = Join-Path $root $aRel
$b = Join-Path $root $bRel

if (-not (Test-Path $a)) { throw "Missing: $aRel" }
if (-not (Test-Path $b)) { throw "Missing: $bRel" }

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass9_SceneDuplicate_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$aText = Get-Content $a -Raw
$bText = Get-Content $b -Raw
$aLines = Get-Content $a
$bLines = Get-Content $b

$aHash = (Get-FileHash $a -Algorithm SHA256).Hash
$bHash = (Get-FileHash $b -Algorithm SHA256).Hash

# Normalize only the namespace so we can see whether these are functionally
# duplicate implementations stored in two namespaces.
$aNorm = $aText -replace 'namespace\s+ProjectEve\.DATA\.World\.Scene\s*;', 'namespace ProjectEve.Scene;'
$bNorm = $bText

# Normalize line endings/BOM/trailing whitespace for meaningful comparison.
function Normalize-Source([string]$s) {
    $s = $s.TrimStart([char]0xFEFF)
    $s = $s -replace "`r`n", "`n"
    $s = $s -replace "`r", "`n"
    $parts = $s -split "`n"
    $parts = $parts | ForEach-Object { $_.TrimEnd() }
    return ($parts -join "`n").Trim()
}

$aNorm = Normalize-Source $aNorm
$bNorm = Normalize-Source $bNorm
$normalizedEqual = ($aNorm -ceq $bNorm)

# Find references to either concrete namespace or concrete class name.
$sourceFiles = Get-ChildItem -Path $root -Recurse -File -Filter *.cs |
    Where-Object {
        $_.FullName -notlike '*\bin\*' -and
        $_.FullName -notlike '*\obj\*' -and
        $_.FullName -ne $a -and
        $_.FullName -ne $b
    }

$refs = New-Object System.Collections.Generic.List[object]

foreach ($f in $sourceFiles) {
    $lines = Get-Content $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if ($line -match 'ProjectEve\.DATA\.World\.Scene' -or
            $line -match 'ProjectEve\.Scene\.ScenePerceptionService' -or
            $line -match '\bScenePerceptionService\b' -or
            $line -match '\bIScenePerceptionService\b') {

            $rel = $f.FullName.Substring($root.TrimEnd('\').Length).TrimStart('\')
            $refs.Add([pscustomobject]@{
                File = $rel
                Line = $i + 1
                Text = $line.Trim()
            })
        }
    }
}

$refs | Export-Csv (Join-Path $outRoot 'scene_service_references.csv') -NoTypeInformation -Encoding UTF8

# Write normalized sources for side-by-side inspection outside project.
Set-Content (Join-Path $outRoot 'DATA_ScenePerceptionService.normalized.txt') $aNorm -Encoding UTF8
Set-Content (Join-Path $outRoot 'Scene_ScenePerceptionService.normalized.txt') $bNorm -Encoding UTF8

$summary = @()
$summary += '# Pass 9 ScenePerception Duplicate Preflight'
$summary += ''
$summary += "DATA copy: $aRel"
$summary += "Scene copy: $bRel"
$summary += ''
$summary += "DATA lines: $($aLines.Count)"
$summary += "Scene lines: $($bLines.Count)"
$summary += "DATA SHA256: $aHash"
$summary += "Scene SHA256: $bHash"
$summary += "Equal after namespace/whitespace normalization: $normalizedEqual"
$summary += "Reference hits outside both files: $($refs.Count)"
$summary += ''
$summary += '## References'
$summary += ''
foreach ($r in $refs) {
    $summary += "- $($r.File):$($r.Line) — $($r.Text)"
}
$summary | Set-Content (Join-Path $outRoot 'PASS9_PREFLIGHT_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 9 scene duplicate preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host ('DATA copy lines                         : ' + $aLines.Count)
Write-Host ('Scene copy lines                        : ' + $bLines.Count)
Write-Host ('Equal after namespace/whitespace cleanup: ' + $normalizedEqual)
Write-Host ('Reference hits outside duplicate files : ' + $refs.Count)
Write-Host ''
Write-Host 'Upload this file next:'
Write-Host '  PASS9_PREFLIGHT_SUMMARY.md'
Write-Host ''
Write-Host 'If Equal is False, also upload:'
Write-Host '  DATA_ScenePerceptionService.normalized.txt'
Write-Host '  Scene_ScenePerceptionService.normalized.txt'
Write-Host ''
Write-Host 'No source files, NPCs, or databases were modified.'
