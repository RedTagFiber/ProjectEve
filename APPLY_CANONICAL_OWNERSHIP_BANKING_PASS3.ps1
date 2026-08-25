$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this script from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass3' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\MigrationArtifacts' $stamp
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null

function Backup-Relative([string]$relativePath) {
    $source = Join-Path $root $relativePath
    if (-not (Test-Path $source)) { return }
    $dest = Join-Path $backupRoot $relativePath
    $destDir = Split-Path $dest -Parent
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    Copy-Item $source $dest -Force
}

# ---------- PRE-FLIGHT ----------
$required = @(
    'DATA\ProjectEveDatabaseSetup.cs',
    'DATA\ProjectEveDatabaseConnections.cs',
    'Characters\Base\CharacterRepository.cs',
    'Program.cs'
)

foreach ($r in $required) {
    if (-not (Test-Path (Join-Path $root $r))) {
        throw ('Required file missing: ' + $r)
    }
}

Backup-Relative 'DATA\ProjectEveDatabaseSetup.cs'
Backup-Relative 'Characters\Base\CharacterRepository.cs'
Backup-Relative 'Program.cs'

# ---------- COPY NEW CANONICAL FILES ----------
$packageRoot = Split-Path $MyInvocation.MyCommand.Path -Parent

$newFiles = @(
    'Money\FinanceModels.cs',
    'Money\FinancialLedgerService.cs',
    'DATA\ProjectEveDataOwnership.cs',
    'DATA\ProjectEveFinanceVerifier.cs'
)

foreach ($relative in $newFiles) {
    $src = Join-Path $packageRoot $relative
    $dst = Join-Path $root $relative

    if (-not (Test-Path $src)) {
        # If the user copied the entire package directly into the project,
        # the source is already the target.
        if (-not (Test-Path $dst)) {
            throw ('Package file missing: ' + $relative)
        }
        continue
    }

    $srcFull = [System.IO.Path]::GetFullPath($src)
    $dstFull = [System.IO.Path]::GetFullPath($dst)

    if (-not $srcFull.Equals($dstFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        Backup-Relative $relative
        New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
        Copy-Item $src $dst -Force
    }
}

# ---------- PATCH DATABASE SETUP ----------
$setupPath = Join-Path $root 'DATA\ProjectEveDatabaseSetup.cs'
$setup = Get-Content $setupPath -Raw
$nl = [Environment]::NewLine

if ($setup -notmatch 'EnsureFinanceSchema\(\);') {
    $envNeedle = 'Environment.SetEnvironmentVariable("EVE_DB_PATH", MainDatabasePath);'
    $pos = $setup.IndexOf($envNeedle)
    if ($pos -lt 0) {
        throw 'Could not find EVE_DB_PATH setup line in ProjectEveDatabaseSetup.cs'
    }

    $setup = $setup.Insert(
        $pos,
        '        EnsureFinanceSchema();' + $nl + $nl
    )
}

if ($setup -notmatch 'private\s+static\s+void\s+EnsureFinanceSchema\s*\(') {
$financeMethod = @'

    private static void EnsureFinanceSchema()
    {
        using (var main = Open(MainDatabasePath))
        {
            Execute(main, """
                CREATE TABLE IF NOT EXISTS FinancialAccounts
                (
                    Id TEXT PRIMARY KEY,
                    OwnerType TEXT NOT NULL DEFAULT 'NPC',
                    OwnerId INTEGER NOT NULL,
                    AccountType TEXT NOT NULL DEFAULT '',
                    InstitutionName TEXT NOT NULL DEFAULT '',
                    AccountName TEXT NOT NULL DEFAULT '',
                    Currency TEXT NOT NULL DEFAULT 'USD',
                    Status TEXT NOT NULL DEFAULT 'Open',
                    CreditLimit REAL NOT NULL DEFAULT 0,
                    InterestRate REAL NOT NULL DEFAULT 0,
                    OpenedGameTime TEXT NOT NULL DEFAULT '',
                    CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS IX_FinancialAccounts_Owner
                    ON FinancialAccounts(OwnerType, OwnerId);

                CREATE TABLE IF NOT EXISTS FinancialObligations
                (
                    Id TEXT PRIMARY KEY,
                    OwnerNpcId INTEGER NOT NULL,
                    AccountId TEXT NOT NULL DEFAULT '',
                    PayeeName TEXT NOT NULL DEFAULT '',
                    ObligationType TEXT NOT NULL DEFAULT '',
                    Amount REAL NOT NULL DEFAULT 0,
                    Frequency TEXT NOT NULL DEFAULT '',
                    DueDay INTEGER NULL,
                    AutoPay INTEGER NOT NULL DEFAULT 0,
                    Status TEXT NOT NULL DEFAULT 'Active',
                    NextDueGameTime TEXT NOT NULL DEFAULT '',
                    CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS IX_FinancialObligations_Owner
                    ON FinancialObligations(OwnerNpcId);
                """);
        }

        using (var history = Open(HistoryDatabasePath))
        {
            Execute(history, """
                CREATE TABLE IF NOT EXISTS FinancialTransactions
                (
                    Id TEXT PRIMARY KEY,
                    AccountId TEXT NOT NULL,
                    OwnerType TEXT NOT NULL DEFAULT 'NPC',
                    OwnerId INTEGER NOT NULL,
                    TransferGroupId TEXT NOT NULL DEFAULT '',
                    TransactionType TEXT NOT NULL DEFAULT '',
                    Amount REAL NOT NULL DEFAULT 0,
                    CounterpartyAccountId TEXT NOT NULL DEFAULT '',
                    MerchantId INTEGER NULL,
                    LocationId INTEGER NULL,
                    Category TEXT NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    GameTime TEXT NOT NULL DEFAULT '',
                    RelatedEventId TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Posted',
                    CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE INDEX IF NOT EXISTS IX_FinancialTransactions_Account
                    ON FinancialTransactions(AccountId, CreatedRealAt);

                CREATE INDEX IF NOT EXISTS IX_FinancialTransactions_Owner
                    ON FinancialTransactions(OwnerType, OwnerId, CreatedRealAt);

                CREATE INDEX IF NOT EXISTS IX_FinancialTransactions_Transfer
                    ON FinancialTransactions(TransferGroupId);
                """);
        }
    }
'@

    $executeMatch = [regex]::Match(
        $setup,
        '(?m)^\s*private\s+static\s+void\s+Execute\s*\('
    )

    if (-not $executeMatch.Success) {
        throw 'Could not find the private Execute method in ProjectEveDatabaseSetup.cs'
    }

    $setup = $setup.Insert($executeMatch.Index, $financeMethod + $nl)
}

Set-Content $setupPath $setup -Encoding UTF8

# ---------- PATCH CHARACTER REPOSITORY MONEY SECTION ----------
$repoPath = Join-Path $root 'Characters\Base\CharacterRepository.cs'
$repo = Get-Content $repoPath -Raw

if ($repo -notmatch 'using\s+ProjectEve\.Data\s*;') {
    $repo = $repo.Replace(
        'using ProjectEve.Money;',
        'using ProjectEve.Money;' + $nl + 'using ProjectEve.Data;'
    )
}

$moneyStartMarker = '// MONEY'
$jobMarker = '// JOB'
$moneyMarkerPos = $repo.IndexOf($moneyStartMarker)
$jobMarkerPos = $repo.IndexOf($jobMarker, $moneyMarkerPos + 1)

if ($moneyMarkerPos -lt 0 -or $jobMarkerPos -lt 0) {
    throw 'Could not locate MONEY/JOB sections in CharacterRepository.cs'
}

# Keep the separator comments around the replacement.
$sectionStart = $repo.LastIndexOf('// ============================================================', $moneyMarkerPos)
$sectionEnd = $repo.LastIndexOf('// ============================================================', $jobMarkerPos)

if ($sectionStart -lt 0 -or $sectionEnd -le $sectionStart) {
    throw 'Could not determine CharacterRepository MONEY section boundaries'
}

$newMoneySection = @'
        // ============================================================
        // MONEY / BANKING
        // Canonical account definitions: project_eve.db
        // Canonical transaction ledger: project_eve_history.db
        // MoneyProfile remains a runtime compatibility object only.
        // ============================================================
        private static void LoadMoney(SqliteConnection conn, SimCharacter npc)
        {
            npc.Money ??= new MoneyProfile();

            // One-time migration from the old MoneyProfile snapshot when no
            // canonical finance transactions exist for this NPC.
            try
            {
                decimal legacyCash = 0m;
                decimal legacyBank = 0m;
                decimal legacyDebt = 0m;
                bool hasLegacy = false;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = """
                        SELECT Cash, Bank, Debt
                        FROM MoneyProfile
                        WHERE NpcId = $id
                        LIMIT 1;
                        """;
                    cmd.Parameters.AddWithValue("$id", npc.Id);

                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        legacyCash = reader.IsDBNull(0) ? 0m : Convert.ToDecimal(reader.GetValue(0));
                        legacyBank = reader.IsDBNull(1) ? 0m : Convert.ToDecimal(reader.GetValue(1));
                        legacyDebt = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2));
                        hasLegacy = true;
                    }
                }

                if (hasLegacy)
                    FinancialLedgerService.TryMigrateLegacyMoney(
                        npc.Id, legacyCash, legacyBank, legacyDebt);
            }
            catch
            {
                // Legacy table may not exist in a future clean database.
            }

            var snapshot = FinancialLedgerService.GetNpcSnapshot(npc.Id);
            npc.Money.Cash = snapshot.Cash;
            npc.Money.Bank = snapshot.Bank;
            npc.Money.Debt = snapshot.Debt;
        }

        public static void SaveMoney(SimCharacter npc)
        {
            if (npc?.Money == null)
                return;

            FinancialLedgerService.ReconcileNpcSnapshot(
                npc.Id,
                npc.Money.Cash,
                npc.Money.Bank,
                npc.Money.Debt,
                "CharacterRepository.SaveMoney compatibility reconciliation");
        }

'@

$repo = $repo.Substring(0, $sectionStart) + $newMoneySection + $repo.Substring($sectionEnd)
Set-Content $repoPath $repo -Encoding UTF8

# ---------- PATCH VERIFY OUTPUT ----------
$programPath = Join-Path $root 'Program.cs'
$program = Get-Content $programPath -Raw

if ($program -notmatch 'ProjectEveFinanceVerifier\.PrintToConsole\(\);') {
    $needle = 'ProjectEveDatabaseVerifier.PrintToConsole();'
    $idx = $program.IndexOf($needle)
    if ($idx -ge 0) {
        $after = $idx + $needle.Length
        $program = $program.Insert(
            $after,
            $nl + '        ProjectEveFinanceVerifier.PrintToConsole();'
        )
    }
}

Set-Content $programPath $program -Encoding UTF8

# ---------- WRITE OWNERSHIP MANIFEST OUTSIDE ACTIVE PROJECT ----------
$architectureRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $architectureRoot -Force | Out-Null

$manifest = @'
PROJECTEVE CANONICAL DATA OWNERSHIP
===================================

ONE FACT = ONE AUTHORITATIVE SOURCE

project_eve.db
--------------
Current NPC/build truth:
identity, appearance, physical metrics, traits, cognition, job,
voice/media metadata, current financial account definitions,
current obligations.

project_eve_history.db
----------------------
Objective truth:
events, conversations, calls, messages, movement history,
purchases, paychecks, deposits, withdrawals, transfers,
refunds, bills, and all financial ledger transactions.

project_eve_relationships.db
----------------------------
Subjective human truth:
directed relationship state, reasons, personal memories,
knowledge, beliefs, rumors, secrets, confidence, source,
interpretation and emotional meaning.

project_eve_locations.db
------------------------
Physical world truth:
location definitions, instances, rooms/areas, scene assets,
audio, motion regions, scene state, current location,
visits and occupancy.

FILESYSTEM
----------
Actual PNG/WAV/MP4/audio/workflow output.
SQL stores canonical paths/metadata; binary files are not duplicated in SQL.

FINANCE RULE
------------
FinancialAccounts defines accounts.
FinancialTransactions is the money ledger.
Displayed balance = SUM(posted transaction Amount).
Do not create another canonical Balance field.
PhoneOS Banking must read/write through this same finance service.
'@

$manifest | Set-Content (Join-Path $architectureRoot 'CANONICAL_DATA_OWNERSHIP.txt') -Encoding UTF8

# ---------- ARCHIVE OLD MIGRATION/AUDIT ARTIFACTS ----------
$currentScript = [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
$artifactCandidates = Get-ChildItem $root -File |
    Where-Object {
        $_.Name -like 'APPLY_*.ps1' -or
        $_.Name -like 'AUDIT_*.ps1' -or
        $_.Name -like 'Program.cs.*.bak'
    }

foreach ($file in $artifactCandidates) {
    if ($file.FullName.Equals($currentScript, [System.StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    Move-Item $file.FullName (Join-Path $archiveRoot $file.Name) -Force
}

# Remove generated build folders so the next build is clean.
Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Canonical Ownership + Banking Pass 3 applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'No NPCs or databases were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host '  dotnet run -- verify'
