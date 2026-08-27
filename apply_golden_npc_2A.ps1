$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$programPath = Join-Path $repoRoot "Program.cs"
$auditPath = Join-Path $repoRoot "DATA\ProjectEveGoldenNpcAudit.cs"

if (-not (Test-Path $programPath)) {
    throw "Could not find Program.cs. Extract this ZIP into the ProjectEve_Clean repository root."
}

if (-not (Test-Path $auditPath)) {
    throw "Could not find DATA\ProjectEveGoldenNpcAudit.cs."
}

$content = Get-Content $programPath -Raw

$caseBlock = @'
            case "golden-audit":
            case "audit-npc":
                {
                    int npcId = ParseIntArg(args, 1, 1);
                    ProjectEveGoldenNpcAudit.PrintToConsole(npcId);
                    break;
                }

'@

if (-not $content.Contains('case "golden-audit":')) {
    $anchor = @'
            case "verify":
            case "verify-db":
                {
                    VerifyTownDatabase();
                    break;
                }

'@

    $count = ([regex]::Matches($content, [regex]::Escape($anchor))).Count

    if ($count -ne 1) {
        throw "Expected exactly one verify command block, found $count. Program.cs was not changed."
    }

    $content = $content.Replace($anchor, $caseBlock + $anchor)
}

$usageLine = '        Console.WriteLine("  dotnet run -- golden-audit [npcId]");'
if (-not $content.Contains($usageLine)) {
    $anchor = '        Console.WriteLine("  dotnet run -- ensure-core");'
    $count = ([regex]::Matches($content, [regex]::Escape($anchor))).Count

    if ($count -ne 1) {
        throw "Expected exactly one ensure-core usage line, found $count. Program.cs was not changed."
    }

    $content = $content.Replace(
        $anchor,
        $anchor + [Environment]::NewLine + $usageLine)
}

Set-Content -Path $programPath -Value $content -Encoding UTF8

Write-Host "Added read-only Golden NPC audit command."
Write-Host ""
Write-Host "Next run:"
Write-Host '  dotnet build'
Write-Host '  dotnet run -- golden-audit 1'
