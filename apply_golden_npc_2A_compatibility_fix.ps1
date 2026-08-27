$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$setupPath = Join-Path $repoRoot "DATA\ProjectEveDatabaseSetup.cs"
$auditPath = Join-Path $repoRoot "DATA\ProjectEveGoldenNpcAudit.cs"

if (-not (Test-Path $setupPath)) {
    throw "Could not find DATA\ProjectEveDatabaseSetup.cs."
}

if (-not (Test-Path $auditPath)) {
    throw "Could not find DATA\ProjectEveGoldenNpcAudit.cs."
}

# ------------------------------------------------------------
# 1) Retrofit only the live Characters columns proven missing
#    by the Golden NPC audit.
# ------------------------------------------------------------
$setup = Get-Content $setupPath -Raw

$compatAnchor = @'
        // Legacy SQLite tables may also predate this timestamp column.
        AddColumnIfMissing(
            connection,
            "NpcTraitValues",
            "UpdatedRealAt",
            "TEXT NOT NULL DEFAULT ''");
'@

if (-not $setup.Contains('"Characters",' + [Environment]::NewLine + '            "Employer"')) {
    $count = ([regex]::Matches($setup, [regex]::Escape($compatAnchor))).Count

    if ($count -ne 1) {
        throw "Could not locate the EnsureMainCompatibilityColumns anchor. Setup file was not changed."
    }

    $characterCompatibility = @'

        // Existing live databases may predate these canonical Characters columns.
        // CREATE TABLE IF NOT EXISTS does not retrofit them.
        AddColumnIfMissing(
            connection,
            "Characters",
            "Employer",
            "TEXT NOT NULL DEFAULT ''");

        AddColumnIfMissing(
            connection,
            "Characters",
            "CurrentLocationId",
            "TEXT NOT NULL DEFAULT ''");

        AddColumnIfMissing(
            connection,
            "Characters",
            "HomeLocationId",
            "TEXT NOT NULL DEFAULT ''");

        AddColumnIfMissing(
            connection,
            "Characters",
            "WorkLocationId",
            "TEXT NOT NULL DEFAULT ''");
'@

    $setup = $setup.Replace(
        $compatAnchor,
        $compatAnchor + $characterCompatibility)

    Set-Content -Path $setupPath -Value $setup -Encoding UTF8
    Write-Host "Added four proven-missing Characters compatibility columns."
}
else {
    Write-Host "Characters compatibility columns already present in setup."
}

# ------------------------------------------------------------
# 2) HouseholdMembers belongs to RELATIONSHIPS, not MAIN.
#    Correct the read-only audit only.
# ------------------------------------------------------------
$audit = Get-Content $auditPath -Raw

$wrong = 'PrintDomain("Household memberships", CountAnyId(main, "HouseholdMembers", npcId, "NpcId", "CharacterId"));'
$right = 'PrintDomain("Household memberships", CountAnyId(relationships, "HouseholdMembers", npcId, "NpcId", "CharacterId"));'

if ($audit.Contains($wrong)) {
    $audit = $audit.Replace($wrong, $right)
    Set-Content -Path $auditPath -Value $audit -Encoding UTF8
    Write-Host "Corrected HouseholdMembers audit to read RELATIONSHIPS DB."
}
elseif ($audit.Contains($right)) {
    Write-Host "HouseholdMembers audit already uses RELATIONSHIPS DB."
}
else {
    throw "Could not locate HouseholdMembers audit line. Audit file was not changed."
}

Write-Host ""
Write-Host "Golden NPC 2A compatibility correction applied."
Write-Host "Next run:"
Write-Host '  dotnet build'
Write-Host '  dotnet run -- ensure-core'
Write-Host '  dotnet run -- golden-audit 1'
