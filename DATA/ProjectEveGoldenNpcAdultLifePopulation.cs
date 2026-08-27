using Microsoft.Data.Sqlite;

namespace ProjectEve.Data;

/// <summary>
/// Golden NPC 2B-4: verified current-adult-life canon for Eve Sinclair.
///
/// Established source canon:
/// - Eve rents a room at Adam's house.
/// - Eve manages Sinclair Coffee, owned by Lisa Sinclair.
/// - Workplace registry id for Sinclair Coffee: WP_COFFEE_001.
/// - Cash: 95; bank funds: 1840; debt: 420.
/// - Monthly bills hint: 1800.
/// - Manager schedule/pay remain owned by the existing job profile.
///
/// Intentionally NOT populated:
/// - phone number/device/carrier
/// - vehicle make/model/year/color/VIN/plate
/// because those facts are not yet canonical.
/// </summary>
public static class ProjectEveGoldenNpcAdultLifePopulation
{
    private const int EveId = 1;
    private const string WorkLocationId = "WP_COFFEE_001";

    public static void PopulateEveAdultLife()
    {
        ValidateEve();

        PopulateCharacterCurrentState();
        PopulateFinance();
        PopulateWorkLocationVisit();

        Console.WriteLine();
        Console.WriteLine("Golden NPC 2B-4 populated for Eve Sinclair.");
        Console.WriteLine("  Employer -> Sinclair Coffee (Lisa Sinclair, owner)");
        Console.WriteLine("  Home description -> Adam's house, rents a room");
        Console.WriteLine("  Work location id -> WP_COFFEE_001");
        Console.WriteLine("  Financial account baseline -> bank funds $1,840");
        Console.WriteLine("  Financial obligations -> $420 debt + $1,800 monthly bills hint");
        Console.WriteLine("  Recorded work-location visit baseline");
        Console.WriteLine();
        Console.WriteLine("Phone and vehicle remain intentionally EMPTY until exact canon exists.");
    }

    private static void ValidateEve()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Name, Occupation
            FROM Characters
            WHERE Id = $npcId;
            """;
        command.Parameters.AddWithValue("$npcId", EveId);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
            throw new InvalidOperationException("Eve Sinclair (NpcId=1) is missing.");

        string name = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
        string occupation = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();

        if (!string.Equals(name, "Eve Sinclair", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Expected NpcId=1 to be Eve Sinclair, but found '{name}'.");

        if (!occupation.Contains("coffee", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Eve's occupation is no longer coffee-related ('{occupation}'). " +
                "Adult-life population aborted.");
    }

    private static void PopulateCharacterCurrentState()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Characters
            SET
                Employer = 'Sinclair Coffee (Lisa Sinclair, owner)',
                WorkLocationId = $workLocationId,
                Address = 'Adam''s house — rents a room (in town)',
                UpdatedRealAt = CURRENT_TIMESTAMP
            WHERE Id = $npcId;
            """;

        command.Parameters.AddWithValue("$npcId", EveId);
        command.Parameters.AddWithValue("$workLocationId", WorkLocationId);
        command.ExecuteNonQuery();
    }

    private static void PopulateFinance()
    {
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO FinancialAccounts
                (
                    Id,
                    OwnerType,
                    OwnerId,
                    AccountType,
                    InstitutionName,
                    AccountName,
                    Currency,
                    Status,
                    CreditLimit,
                    InterestRate,
                    OpenedGameTime,
                    CreatedRealAt,
                    UpdatedRealAt
                )
                VALUES
                (
                    'eve-primary-bank-funds',
                    'NPC',
                    $npcId,
                    'Bank',
                    '',
                    'Primary bank funds',
                    'USD',
                    'Open',
                    0,
                    0,
                    '',
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                )
                ON CONFLICT(Id) DO UPDATE SET
                    OwnerType = excluded.OwnerType,
                    OwnerId = excluded.OwnerId,
                    AccountType = excluded.AccountType,
                    AccountName = excluded.AccountName,
                    Currency = excluded.Currency,
                    Status = excluded.Status,
                    UpdatedRealAt = CURRENT_TIMESTAMP;
                """;
            command.Parameters.AddWithValue("$npcId", EveId);
            command.ExecuteNonQuery();
        }

        UpsertObligation(
            connection,
            transaction,
            id: "eve-existing-debt",
            accountId: "eve-primary-bank-funds",
            payeeName: "",
            obligationType: "ExistingDebt",
            amount: 420m,
            frequency: "Outstanding",
            notesStatus: "Active");

        UpsertObligation(
            connection,
            transaction,
            id: "eve-monthly-bills-baseline",
            accountId: "eve-primary-bank-funds",
            payeeName: "",
            obligationType: "MonthlyBillsEstimate",
            amount: 1800m,
            frequency: "Monthly",
            notesStatus: "Active");

        transaction.Commit();

        // FinancialAccounts currently has no current-balance column. Preserve the
        // established $1,840 bank balance and $95 cash as explicit notes instead
        // of inventing fake transactions merely to synthesize a balance.
        EnsureFinanceCanonNotes();
    }

    private static void UpsertObligation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        string accountId,
        string payeeName,
        string obligationType,
        decimal amount,
        string frequency,
        string notesStatus)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            INSERT INTO FinancialObligations
            (
                Id,
                OwnerNpcId,
                AccountId,
                PayeeName,
                ObligationType,
                Amount,
                Frequency,
                DueDay,
                AutoPay,
                Status,
                NextDueGameTime,
                CreatedRealAt,
                UpdatedRealAt
            )
            VALUES
            (
                $id,
                $npcId,
                $accountId,
                $payeeName,
                $obligationType,
                $amount,
                $frequency,
                NULL,
                0,
                $status,
                '',
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                OwnerNpcId = excluded.OwnerNpcId,
                AccountId = excluded.AccountId,
                PayeeName = excluded.PayeeName,
                ObligationType = excluded.ObligationType,
                Amount = excluded.Amount,
                Frequency = excluded.Frequency,
                Status = excluded.Status,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$npcId", EveId);
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$payeeName", payeeName);
        command.Parameters.AddWithValue("$obligationType", obligationType);
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$frequency", frequency);
        command.Parameters.AddWithValue("$status", notesStatus);

        command.ExecuteNonQuery();
    }

    private static void EnsureFinanceCanonNotes()
    {
        // The current finance schema has no balance or freeform Notes column on
        // FinancialAccounts/FinancialObligations. Store exact legacy money canon
        // in a build revision only if that table is available; otherwise leave
        // the structured finance rows conservative.
        using var connection = Open(ProjectEveDatabaseSetup.MainDatabasePath);

        if (!TableExists(connection, "NpcBuildRevisions"))
            return;

        using var check = connection.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*)
            FROM NpcBuildRevisions
            WHERE NpcId = $npcId
              AND RevisionType = 'GoldenNpc2B4FinanceCanon'
              AND Title = 'Eve legacy money canon';
            """;
        check.Parameters.AddWithValue("$npcId", EveId);

        long count = Convert.ToInt64(check.ExecuteScalar());
        if (count > 0)
            return;

        var columns = GetColumns(connection, "NpcBuildRevisions");
        if (!columns.Contains("Id") ||
            !columns.Contains("NpcId") ||
            !columns.Contains("RevisionType") ||
            !columns.Contains("Title") ||
            !columns.Contains("Details"))
            return;

        using var insert = connection.CreateCommand();

        var names = new List<string> { "Id", "NpcId", "RevisionType", "Title", "Details" };
        var values = new List<string> { "$id", "$npcId", "$type", "$title", "$details" };

        if (columns.Contains("OldValue"))
        {
            names.Add("OldValue");
            values.Add("''");
        }

        if (columns.Contains("NewValue"))
        {
            names.Add("NewValue");
            values.Add("''");
        }

        if (columns.Contains("CreatedRealAt"))
        {
            names.Add("CreatedRealAt");
            values.Add("CURRENT_TIMESTAMP");
        }

        insert.CommandText =
            $"INSERT INTO NpcBuildRevisions ({string.Join(", ", names)}) " +
            $"VALUES ({string.Join(", ", values)});";

        insert.Parameters.AddWithValue("$id", "golden-2b4-eve-finance-canon");
        insert.Parameters.AddWithValue("$npcId", EveId);
        insert.Parameters.AddWithValue("$type", "GoldenNpc2B4FinanceCanon");
        insert.Parameters.AddWithValue("$title", "Eve legacy money canon");
        insert.Parameters.AddWithValue(
            "$details",
            "Established canon: cash $95; bank $1,840; debt $420; MonthlyBillsHint $1,800; SavingsRateHint 8%.");

        try
        {
            insert.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // This note is supplementary only. Structured finance population
            // must not fail because a legacy revision-table shape differs.
        }
    }

    private static void PopulateWorkLocationVisit()
    {
        using var connection = Open(ProjectEveDatabaseSetup.LocationDatabasePath);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO LocationVisits
            (
                Id,
                LocationId,
                CharacterId,
                FirstVisitGameTime,
                LastVisitGameTime,
                VisitCount,
                Notes
            )
            VALUES
            (
                'eve-sinclair-coffee-baseline',
                $locationId,
                $npcId,
                '',
                '',
                1,
                'Baseline canonical presence only: Eve works as manager at Sinclair Coffee. VisitCount=1 means at least one known presence, not a reconstructed lifetime visit total.'
            )
            ON CONFLICT(Id) DO UPDATE SET
                LocationId = excluded.LocationId,
                CharacterId = excluded.CharacterId,
                VisitCount = CASE
                    WHEN LocationVisits.VisitCount < 1 THEN 1
                    ELSE LocationVisits.VisitCount
                END,
                Notes = excluded.Notes;
            """;

        command.Parameters.AddWithValue("$locationId", WorkLocationId);
        command.Parameters.AddWithValue("$npcId", EveId);
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND lower(name) = lower($tableName);
            """;
        command.Parameters.AddWithValue("$tableName", tableName);

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static HashSet<string> GetColumns(
        SqliteConnection connection,
        string tableName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(1))
                result.Add(reader.GetString(1));
        }

        return result;
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();

        return connection;
    }
}
