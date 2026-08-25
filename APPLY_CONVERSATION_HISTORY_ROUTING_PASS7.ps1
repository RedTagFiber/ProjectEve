$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$path = Join-Path $root 'Conversations\ConversationManager.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}
if (-not (Test-Path $path)) {
    throw 'Conversations\ConversationManager.cs was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass7' $stamp
$archiveRoot = Join-Path 'D:\ProjectEve\Archive\CanonicalPass7' $stamp

$backupFile = Join-Path $backupRoot 'Conversations\ConversationManager.cs'
$archiveFile = Join-Path $archiveRoot 'Conversations\ConversationManager.pre-history-routing.cs'

New-Item -ItemType Directory -Path (Split-Path $backupFile -Parent) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $archiveFile -Parent) -Force | Out-Null
Copy-Item $path $backupFile -Force
Copy-Item $path $archiveFile -Force

$text = Get-Content $path -Raw
$nl = [Environment]::NewLine

# Add canonical data namespace if missing.
if ($text -notmatch '(?m)^using ProjectEve\.Data;') {
    $insertAt = $text.IndexOf('using System;')
    if ($insertAt -lt 0) { throw 'Could not find using insertion point.' }
    $text = $text.Insert($insertAt, 'using ProjectEve.Data;' + $nl)
}

# Replace legacy DB path with canonical history DB.
$pattern = '(?s)\s*private static string DbPath =>\s*Environment\.GetEnvironmentVariable\("EVE_DB_PATH"\)\s*\?\?\s*System\.IO\.Path\.Combine\(AppContext\.BaseDirectory,\s*"Data",\s*"project_eve\.db"\);'
$replacement = $nl + '        private static string DbPath =>' + $nl +
               '            ProjectEveDatabaseSetup.HistoryDatabasePath;'

if ([regex]::IsMatch($text, $pattern)) {
    $text = [regex]::Replace($text, $pattern, $replacement, 1)
}
elseif ($text -notmatch 'ProjectEveDatabaseSetup\.HistoryDatabasePath') {
    throw 'Could not replace ConversationManager.DbPath.'
}

# Ensure bootstrap now creates canonical DBs before opening history.
$oldInit = @'
        public static void Initialize()
        {
            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            EnsureSchema(conn);
            EnsurePlayerIdMigration(conn);
        }
'@

$newInit = @'
        public static void Initialize()
        {
            ProjectEveDatabaseSetup.EnsureAll();

            using var conn = new SqliteConnection(ConnStr);
            conn.Open();
            EnsureSchema(conn);
            EnsurePlayerIdMigration(conn);
            MigrateLegacyMainConversationDataIfNeeded(conn);
        }
'@

if ($text.Contains($oldInit)) {
    $text = $text.Replace($oldInit, $newInit)
}
elseif ($text -notmatch 'MigrateLegacyMainConversationDataIfNeeded\(conn\);') {
    throw 'Could not replace ConversationManager.Initialize.'
}

# Add idempotent legacy migration helper before EnsureSchema.
if ($text -notmatch 'private static void MigrateLegacyMainConversationDataIfNeeded') {
$helper = @'

        /// <summary>
        /// One-time compatibility migration:
        /// if canonical history conversation tables are empty, copy any existing
        /// conversation rows from the legacy main DB. Legacy rows are never deleted.
        /// </summary>
        private static void MigrateLegacyMainConversationDataIfNeeded(
            SqliteConnection conn)
        {
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM ConversationSession;";
                long existing = Convert.ToInt64(count.ExecuteScalar() ?? 0);
                if (existing > 0)
                    return;
            }

            string legacyMainPath = ProjectEveDatabaseSetup.MainDatabasePath;

            if (!System.IO.File.Exists(legacyMainPath) ||
                string.Equals(
                    legacyMainPath,
                    DbPath,
                    StringComparison.OrdinalIgnoreCase))
                return;

            string escaped = legacyMainPath.Replace("'", "''");

            using var tx = conn.BeginTransaction();

            try
            {
                using (var attach = conn.CreateCommand())
                {
                    attach.Transaction = tx;
                    attach.CommandText = $"ATTACH DATABASE '{escaped}' AS legacy_main;";
                    attach.ExecuteNonQuery();
                }

                if (!LegacyTableExists(conn, tx, "ConversationSession"))
                {
                    tx.Rollback();
                    return;
                }

                CopyLegacyTable(
                    conn, tx, "ConversationSession",
                    "Id,PlayerId,NpcId,NpcName,PlayerName,Channel,Location," +
                    "StartedGameTime,EndedGameTime,StartedUtc,EndedUtc," +
                    "LastMessageUtc,Status,EndReason");

                CopyLegacyTable(
                    conn, tx, "ConversationMessage",
                    "Id,SessionId,Sequence,Role,Speaker,SpeakerNpcId," +
                    "MessageText,GameTime,CreatedUtc");

                CopyLegacyTable(
                    conn, tx, "ConversationEvent",
                    "Id,SessionId,PlayerId,NpcId,NpcName,PlayerName,Channel," +
                    "Location,StartedGameTime,EndedGameTime,Summary," +
                    "EmotionalOutcome,RelationshipOutcome,EndReason,CreatedUtc");

                CopyLegacyTable(
                    conn, tx, "ConversationFact",
                    "Id,EventId,PlayerId,NpcId,PlayerName,Subject,FactKey," +
                    "FactValue,Confidence,SourceType,CreatedUtc");

                CopyLegacyTable(
                    conn, tx, "ConversationPlan",
                    "Id,EventId,PlayerId,NpcId,PlayerName,Description,TimeText," +
                    "Location,Status,CreatedUtc,UpdatedUtc");

                using (var detach = conn.CreateCommand())
                {
                    detach.Transaction = tx;
                    detach.CommandText = "DETACH DATABASE legacy_main;";
                    detach.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                // Migration is compatibility-only. Never block startup or delete legacy data.
            }
        }

        private static bool LegacyTableExists(
            SqliteConnection conn,
            SqliteTransaction tx,
            string tableName)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM legacy_main.sqlite_master
                WHERE type='table' AND name=$name;
                """;
            cmd.Parameters.AddWithValue("$name", tableName);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
        }

        private static void CopyLegacyTable(
            SqliteConnection conn,
            SqliteTransaction tx,
            string tableName,
            string columns)
        {
            if (!LegacyTableExists(conn, tx, tableName))
                return;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                $"INSERT OR IGNORE INTO {tableName} ({columns}) " +
                $"SELECT {columns} FROM legacy_main.{tableName};";
            cmd.ExecuteNonQuery();
        }

'@

    $anchor = '        private static void EnsureSchema(SqliteConnection conn)'
    $idx = $text.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) { throw 'Could not find EnsureSchema insertion point.' }
    $text = $text.Insert($idx, $helper)
}

Set-Content $path $text -Encoding UTF8

# Write a routing report outside active project tree.
$reportRoot = 'D:\ProjectEve\Architecture'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
@'
PASS 7 - CONVERSATION / HISTORY ROUTING
=======================================

ConversationManager active storage:
  D:\ProjectEveData\Database\project_eve_history.db

Moved out of main DB for future writes:
  ConversationSession
  ConversationMessage
  ConversationEvent
  ConversationFact
  ConversationPlan

Compatibility:
- If history conversation tables are empty, existing legacy rows from project_eve.db
  are copied once into project_eve_history.db.
- Legacy main-DB rows are NOT deleted.
- Existing history rows are never overwritten.

NOTE:
ConversationManager still owns its conversation table schema in this pass.
A later ownership pass can move CREATE TABLE responsibility fully into
ProjectEveDatabaseSetup after runtime routing is proven stable.
'@ | Set-Content (Join-Path $reportRoot 'PASS7_CONVERSATION_HISTORY_ROUTING.txt') -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Conversation / History Routing Pass 7 applied.' -ForegroundColor Green
Write-Host ('Backup:  ' + $backupRoot)
Write-Host ('Archive: ' + $archiveRoot)
Write-Host ''
Write-Host 'ConversationManager now writes to project_eve_history.db.'
Write-Host 'Legacy main-DB conversation rows are preserved and can migrate once.'
Write-Host 'No legacy tables were dropped.'
Write-Host 'No NPCs or databases were deleted.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host 'Only if build succeeds:'
Write-Host '  dotnet run -- verify'
