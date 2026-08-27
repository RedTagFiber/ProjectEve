$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$programPath = Join-Path $repoRoot "Program.cs"
$auditPath = Join-Path $repoRoot "DATA\ProjectEveGoldenNpcAudit.cs"

if (-not (Test-Path $programPath)) {
    throw "Could not find Program.cs. Extract this ZIP into the ProjectEve_Clean repository root."
}

if (-not (Test-Path $auditPath)) {
    throw "Could not find DATA\ProjectEveGoldenNpcAudit.cs. Keep the file from Golden NPC 2A in DATA."
}

$content = Get-Content $programPath -Raw

if (-not $content.Contains('case "golden-audit":')) {
    $verifyPattern = '(?ms)([ \t]*case "verify":\r?\n[ \t]*case "verify-db":\r?\n[ \t]*\{\r?\n[ \t]*VerifyTownDatabase\(\);\r?\n[ \t]*break;\r?\n[ \t]*\}\r?\n)'

    $match = [regex]::Match($content, $verifyPattern)

    if (-not $match.Success) {
        throw "Could not locate the verify/verify-db command block. Program.cs was not changed."
    }

    $indent = "            "
    $caseBlock =
        $indent + 'case "golden-audit":' + [Environment]::NewLine +
        $indent + 'case "audit-npc":' + [Environment]::NewLine +
        $indent + '    {' + [Environment]::NewLine +
        $indent + '        int npcId = ParseIntArg(args, 1, 1);' + [Environment]::NewLine +
        $indent + '        ProjectEveGoldenNpcAudit.PrintToConsole(npcId);' + [Environment]::NewLine +
        $indent + '        break;' + [Environment]::NewLine +
        $indent + '    }' + [Environment]::NewLine +
        [Environment]::NewLine

    $content =
        $content.Substring(0, $match.Index) +
        $caseBlock +
        $content.Substring($match.Index)

    Write-Host "Added golden-audit command block."
}
else {
    Write-Host "golden-audit command block already present."
}

$usageLine = '        Console.WriteLine("  dotnet run -- golden-audit [npcId]");'

if (-not $content.Contains($usageLine)) {
    $ensureUsagePattern =
        '(?m)^(?<indent>[ \t]*)Console\.WriteLine\("  dotnet run -- ensure-core"\);\r?$'

    $usageMatch = [regex]::Match($content, $ensureUsagePattern)

    if (-not $usageMatch.Success) {
        throw "Could not locate ensure-core usage line. Program.cs was not changed."
    }

    $lineEnd =
        if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }

    $insertAt = $usageMatch.Index + $usageMatch.Length

    $content =
        $content.Substring(0, $insertAt) +
        $lineEnd +
        $usageLine +
        $content.Substring($insertAt)

    Write-Host "Added golden-audit usage line."
}
else {
    Write-Host "golden-audit usage line already present."
}

Set-Content -Path $programPath -Value $content -Encoding UTF8

Write-Host ""
Write-Host "Golden NPC 2A installer fix applied."
Write-Host "Next run:"
Write-Host '  dotnet build'
Write-Host '  dotnet run -- golden-audit 1'
