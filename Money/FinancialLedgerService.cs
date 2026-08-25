using Microsoft.Data.Sqlite;
using ProjectEve.Data;

namespace ProjectEve.Money;

/// <summary>
/// Canonical ProjectEve finance gateway.
///
/// Account definitions live in project_eve.db.
/// Posted money movements live in project_eve_history.db.
/// A displayed balance is SUM(FinancialTransactions.Amount), never a second source of truth.
/// </summary>
public static class FinancialLedgerService
{
    private const string CashType = "Cash";
    private const string CheckingType = "Checking";
    private const string DebtType = "Debt";

    public static NpcFinancialSnapshot GetNpcSnapshot(int npcId)
    {
        EnsureAccounts(npcId);

        decimal cash = GetBalance(AccountId(npcId, CashType));
        decimal bank = GetBalance(AccountId(npcId, CheckingType));
        decimal debtLedger = GetBalance(AccountId(npcId, DebtType));

        return new NpcFinancialSnapshot(
            Cash: Math.Max(0m, cash),
            Bank: Math.Max(0m, bank),
            Debt: Math.Max(0m, -debtLedger)
        );
    }

    public static void EnsureAccounts(int npcId)
    {
        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        UpsertAccount(conn, npcId, CashType, "Cash Wallet", "");
        UpsertAccount(conn, npcId, CheckingType, "Checking", "Bellefontaine Community Bank");
        UpsertAccount(conn, npcId, DebtType, "Debt", "");
    }

    /// <summary>
    /// One-time bridge from the legacy MoneyProfile snapshot into the canonical ledger.
    /// It only runs when this NPC has no finance transactions yet.
    /// </summary>
    public static bool TryMigrateLegacyMoney(int npcId, decimal cash, decimal bank, decimal debt)
    {
        EnsureAccounts(npcId);

        using var history = ProjectEveDatabaseConnections.OpenHistory();
        using (var check = history.CreateCommand())
        {
            check.CommandText = """
                SELECT COUNT(*)
                FROM FinancialTransactions
                WHERE OwnerType = 'NPC' AND OwnerId = $npc;
                """;
            check.Parameters.AddWithValue("$npc", npcId);
            long count = Convert.ToInt64(check.ExecuteScalar() ?? 0L);
            if (count > 0)
                return false;
        }

        var group = "legacy-money:" + npcId;
        if (cash != 0m)
            PostDelta(npcId, CashType, cash, "OpeningBalance", "Legacy cash opening balance", group);
        if (bank != 0m)
            PostDelta(npcId, CheckingType, bank, "OpeningBalance", "Legacy bank opening balance", group);
        if (debt != 0m)
            PostDelta(npcId, DebtType, -Math.Abs(debt), "OpeningBalance", "Legacy debt opening balance", group);

        return true;
    }

    /// <summary>
    /// Compatibility bridge for older code that still edits MoneyProfile in memory.
    /// Differences are recorded as explicit Adjustment transactions rather than overwriting a balance.
    /// </summary>
    public static void ReconcileNpcSnapshot(
        int npcId,
        decimal desiredCash,
        decimal desiredBank,
        decimal desiredDebt,
        string reason)
    {
        EnsureAccounts(npcId);
        var current = GetNpcSnapshot(npcId);

        decimal cashDelta = desiredCash - current.Cash;
        decimal bankDelta = desiredBank - current.Bank;
        decimal desiredDebtLedger = -Math.Abs(desiredDebt);
        decimal currentDebtLedger = -current.Debt;
        decimal debtDelta = desiredDebtLedger - currentDebtLedger;

        var group = "reconcile:" + Guid.NewGuid().ToString("N");

        if (cashDelta != 0m)
            PostDelta(npcId, CashType, cashDelta, "Adjustment", reason, group);
        if (bankDelta != 0m)
            PostDelta(npcId, CheckingType, bankDelta, "Adjustment", reason, group);
        if (debtDelta != 0m)
            PostDelta(npcId, DebtType, debtDelta, "Adjustment", reason, group);
    }

    public static string PostDelta(
        int npcId,
        string accountType,
        decimal amount,
        string transactionType,
        string description,
        string? transferGroupId = null,
        string? gameTime = null,
        int? merchantId = null,
        int? locationId = null,
        string? relatedEventId = null,
        string? category = null)
    {
        EnsureAccounts(npcId);

        string accountId = AccountId(npcId, accountType);
        string id = "txn:" + Guid.NewGuid().ToString("N");

        using var conn = ProjectEveDatabaseConnections.OpenHistory();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FinancialTransactions
            (
                Id, AccountId, OwnerType, OwnerId, TransferGroupId,
                TransactionType, Amount, CounterpartyAccountId,
                MerchantId, LocationId, Category, Description,
                GameTime, RelatedEventId, Status
            )
            VALUES
            (
                $id, $account, 'NPC', $owner, $group,
                $type, $amount, '',
                $merchant, $location, $category, $description,
                $gameTime, $event, 'Posted'
            );
            """;

        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$account", accountId);
        cmd.Parameters.AddWithValue("$owner", npcId);
        cmd.Parameters.AddWithValue("$group", transferGroupId ?? "");
        cmd.Parameters.AddWithValue("$type", transactionType ?? "");
        cmd.Parameters.AddWithValue("$amount", Convert.ToDouble(amount));
        cmd.Parameters.AddWithValue("$merchant", (object?)merchantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$location", (object?)locationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$category", category ?? "");
        cmd.Parameters.AddWithValue("$description", description ?? "");
        cmd.Parameters.AddWithValue("$gameTime", gameTime ?? "");
        cmd.Parameters.AddWithValue("$event", relatedEventId ?? "");
        cmd.ExecuteNonQuery();

        return id;
    }

    /// <summary>
    /// Double-entry style transfer. The source receives a negative transaction
    /// and the destination receives an equal positive transaction.
    /// </summary>
    public static string Transfer(
        int fromNpcId,
        string fromAccountType,
        int toNpcId,
        string toAccountType,
        decimal amount,
        string description,
        string? gameTime = null)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Transfer amount must be positive.");

        EnsureAccounts(fromNpcId);
        EnsureAccounts(toNpcId);

        string group = "transfer:" + Guid.NewGuid().ToString("N");
        string fromId = AccountId(fromNpcId, fromAccountType);
        string toId = AccountId(toNpcId, toAccountType);

        using var conn = ProjectEveDatabaseConnections.OpenHistory();
        using var tx = conn.BeginTransaction();

        InsertTransferLeg(conn, tx, fromNpcId, fromId, toId, -amount, description, group, gameTime);
        InsertTransferLeg(conn, tx, toNpcId, toId, fromId, amount, description, group, gameTime);

        tx.Commit();
        return group;
    }

    public static decimal GetBalance(string accountId)
    {
        using var conn = ProjectEveDatabaseConnections.OpenHistory();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(Amount), 0)
            FROM FinancialTransactions
            WHERE AccountId = $account AND Status = 'Posted';
            """;
        cmd.Parameters.AddWithValue("$account", accountId);
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value
            ? 0m
            : Convert.ToDecimal(value);
    }

    public static IReadOnlyList<FinancialTransactionRecord> GetRecentTransactions(
        int npcId,
        int limit = 50)
    {
        using var conn = ProjectEveDatabaseConnections.OpenHistory();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, AccountId, TransferGroupId, TransactionType, Amount,
                   CounterpartyAccountId, MerchantId, LocationId, Category,
                   Description, GameTime, RelatedEventId, Status
            FROM FinancialTransactions
            WHERE OwnerType = 'NPC' AND OwnerId = $owner
            ORDER BY CreatedRealAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$owner", npcId);
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        var rows = new List<FinancialTransactionRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new FinancialTransactionRecord
            {
                Id = reader.GetString(0),
                AccountId = reader.GetString(1),
                TransferGroupId = reader.IsDBNull(2) ? "" : reader.GetString(2),
                TransactionType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Amount = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4)),
                CounterpartyAccountId = reader.IsDBNull(5) ? "" : reader.GetString(5),
                MerchantId = reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetValue(6)),
                LocationId = reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7)),
                Category = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Description = reader.IsDBNull(9) ? "" : reader.GetString(9),
                GameTime = reader.IsDBNull(10) ? "" : reader.GetString(10),
                RelatedEventId = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Status = reader.IsDBNull(12) ? "" : reader.GetString(12)
            });
        }

        return rows;
    }

    private static void InsertTransferLeg(
        SqliteConnection conn,
        SqliteTransaction tx,
        int ownerId,
        string accountId,
        string counterpartyAccountId,
        decimal amount,
        string description,
        string group,
        string? gameTime)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO FinancialTransactions
            (
                Id, AccountId, OwnerType, OwnerId, TransferGroupId,
                TransactionType, Amount, CounterpartyAccountId,
                Category, Description, GameTime, Status
            )
            VALUES
            (
                $id, $account, 'NPC', $owner, $group,
                'Transfer', $amount, $counterparty,
                'Transfer', $description, $gameTime, 'Posted'
            );
            """;
        cmd.Parameters.AddWithValue("$id", "txn:" + Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$account", accountId);
        cmd.Parameters.AddWithValue("$owner", ownerId);
        cmd.Parameters.AddWithValue("$group", group);
        cmd.Parameters.AddWithValue("$amount", Convert.ToDouble(amount));
        cmd.Parameters.AddWithValue("$counterparty", counterpartyAccountId);
        cmd.Parameters.AddWithValue("$description", description ?? "");
        cmd.Parameters.AddWithValue("$gameTime", gameTime ?? "");
        cmd.ExecuteNonQuery();
    }

    private static string AccountId(int npcId, string accountType)
        => $"npc:{npcId}:finance:{NormalizeAccountType(accountType)}";

    private static string NormalizeAccountType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "checking";

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray());
    }

    private static void UpsertAccount(
        SqliteConnection conn,
        int npcId,
        string accountType,
        string accountName,
        string institutionName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FinancialAccounts
            (
                Id, OwnerType, OwnerId, AccountType,
                InstitutionName, AccountName, Currency, Status
            )
            VALUES
            (
                $id, 'NPC', $owner, $type,
                $institution, $name, 'USD', 'Open'
            )
            ON CONFLICT(Id) DO UPDATE SET
                AccountType = excluded.AccountType,
                InstitutionName = CASE
                    WHEN FinancialAccounts.InstitutionName = '' THEN excluded.InstitutionName
                    ELSE FinancialAccounts.InstitutionName
                END,
                AccountName = CASE
                    WHEN FinancialAccounts.AccountName = '' THEN excluded.AccountName
                    ELSE FinancialAccounts.AccountName
                END,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;
        cmd.Parameters.AddWithValue("$id", AccountId(npcId, accountType));
        cmd.Parameters.AddWithValue("$owner", npcId);
        cmd.Parameters.AddWithValue("$type", accountType);
        cmd.Parameters.AddWithValue("$institution", institutionName);
        cmd.Parameters.AddWithValue("$name", accountName);
        cmd.ExecuteNonQuery();
    }
}
