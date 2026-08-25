$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass5' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\CanonicalPass5' $stamp
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null

function Backup-Relative([string]$relativePath) {
    $src = Join-Path $root $relativePath
    if (-not (Test-Path $src)) { return }
    $dst = Join-Path $backupRoot $relativePath
    New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
    Copy-Item $src $dst -Force
}

$familyRel = 'World\SmallTown\Population\FamilyFriendWebSystem.cs'
$setupRel = 'DATA\ProjectEveDatabaseSetup.cs'
$verifyRel = 'DATA\ProjectEveOwnershipVerifier.cs'

foreach ($r in @($familyRel, $setupRel, $verifyRel)) {
    Backup-Relative $r
}

$packageRoot = Split-Path $MyInvocation.MyCommand.Path -Parent
$familySrc = Join-Path $packageRoot $familyRel
$familyDst = Join-Path $root $familyRel

if (-not (Test-Path $familySrc)) {
    if (-not (Test-Path $familyDst)) {
        throw 'FamilyFriendWebSystem.cs package file was not found.'
    }
}
else {
    $srcFull = [System.IO.Path]::GetFullPath($familySrc)
    $dstFull = [System.IO.Path]::GetFullPath($familyDst)
    if (-not $srcFull.Equals($dstFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        New-Item -ItemType Directory -Path (Split-Path $familyDst -Parent) -Force | Out-Null
        Copy-Item $familySrc $familyDst -Force
    }
}

# Archive the legacy implementation copy outside the active project.
$legacy = Join-Path $backupRoot $familyRel
if (Test-Path $legacy) {
    $archiveFile = Join-Path $archiveRoot 'World\SmallTown\Population\FamilyFriendWebSystem.legacy.cs'
    New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null
    Copy-Item $legacy $archiveFile -Force
}

$setupPath = Join-Path $root $setupRel
$setup = Get-Content $setupPath -Raw
$nl = [Environment]::NewLine

# Add call in EnsureAll if missing.
if ($setup -notmatch 'EnsureFamilyFriendWebSchema\(\);') {
    $needle = 'EnsureFinanceSchema();'
    $idx = $setup.IndexOf($needle, [System.StringComparison]::Ordinal)

    if ($idx -lt 0) {
        # Fallback: put it before the first environment-variable line.
        $needle = 'Environment.SetEnvironmentVariable("EVE_DB_PATH", MainDatabasePath);'
        $idx = $setup.IndexOf($needle, [System.StringComparison]::Ordinal)
        if ($idx -lt 0) {
            throw 'Could not find a safe EnsureAll insertion point.'
        }
        $setup = $setup.Insert($idx, '        EnsureFamilyFriendWebSchema();' + $nl + $nl)
    }
    else {
        $after = $idx + $needle.Length
        $setup = $setup.Insert($after, $nl + '        EnsureFamilyFriendWebSchema();')
    }
}

# Add isolated schema method if missing. This avoids editing a large existing raw SQL literal.
if ($setup -notmatch 'private\s+static\s+void\s+EnsureFamilyFriendWebSchema\s*\(') {
$method = @'

    private static void EnsureFamilyFriendWebSchema()
    {
        using var conn = Open(RelationshipDatabasePath);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS FamilyFriendWeb
            (
                OwnerNpcId INTEGER NOT NULL,
                TargetNpcId INTEGER NOT NULL,
                WebTier INTEGER NOT NULL,
                RelationshipType TEXT NOT NULL DEFAULT '',
                IsHistoryOnly INTEGER NOT NULL DEFAULT 0,
                Source TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (OwnerNpcId, TargetNpcId)
            );

            CREATE INDEX IF NOT EXISTS IX_FamilyFriendWeb_OwnerTier
                ON FamilyFriendWeb(OwnerNpcId, WebTier);

            CREATE INDEX IF NOT EXISTS IX_FamilyFriendWeb_Target
                ON FamilyFriendWeb(TargetNpcId);

            CREATE TABLE IF NOT EXISTS HouseholdMembers
            (
                HouseholdId TEXT NOT NULL,
                NpcId INTEGER NOT NULL,
                HouseholdRole TEXT NOT NULL DEFAULT '',
                JoinedAt TEXT NOT NULL DEFAULT '',
                LeftAt TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (HouseholdId, NpcId)
            );

            CREATE INDEX IF NOT EXISTS IX_HouseholdMembers_Npc
                ON HouseholdMembers(NpcId);
            """);
    }
'@

    $executeMatch = [regex]::Match(
        $setup,
        '(?m)^\s*(?:private\s+)?static\s+void\s+Execute\s*\('
    )

    if (-not $executeMatch.Success) {
        throw 'Could not find ProjectEveDatabaseSetup.Execute insertion point.'
    }

    $setup = $setup.Insert($executeMatch.Index, $method + $nl)
}

Set-Content $setupPath $setup -Encoding UTF8

# Add verification lines if the verifier exists.
$verifyPath = Join-Path $root $verifyRel
if (Test-Path $verifyPath) {
    $verify = Get-Content $verifyPath -Raw

    if ($verify -notmatch 'Family / friend web') {
        $anchor = 'bool memories = HasTable(ProjectEveDatabaseSetup.RelationshipDatabasePath, "PersonalMemories");'
        $idx = $verify.IndexOf($anchor, [System.StringComparison]::Ordinal)

        if ($idx -ge 0) {
            $after = $idx + $anchor.Length
            $verify = $verify.Insert(
                $after,
                $nl +
                '        bool familyWeb = HasTable(ProjectEveDatabaseSetup.RelationshipDatabasePath, "FamilyFriendWeb");' + $nl +
                '        bool householdMembers = HasTable(ProjectEveDatabaseSetup.RelationshipDatabasePath, "HouseholdMembers");'
            )
        }

        $printAnchor = 'Console.WriteLine($"  Personal memories     {(memories ? "READY" : "MISSING")}");'
        $idx = $verify.IndexOf($printAnchor, [System.StringComparison]::Ordinal)

        if ($idx -ge 0) {
            $after = $idx + $printAnchor.Length
            $verify = $verify.Insert(
                $after,
                $nl +
                '        Console.WriteLine($"  Family / friend web   {(familyWeb ? "READY" : "MISSING")}");' + $nl +
                '        Console.WriteLine($"  Household members     {(householdMembers ? "READY" : "MISSING")}");'
            )
        }

        Set-Content $verifyPath $verify -Encoding UTF8
    }
}

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Canonical Family/Friend Pass 5 applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'What changed:'
Write-Host '  FamilyFriendWeb -> relationships DB'
Write-Host '  HouseholdMembers -> relationships DB'
Write-Host '  relationship mirror -> RelationshipRepository'
Write-Host '  character name/tier -> main DB only'
Write-Host '  no cross-database SQL JOIN remains in FamilyFriendWebSystem'
Write-Host ''
Write-Host 'No NPCs or databases were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
