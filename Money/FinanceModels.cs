namespace ProjectEve.Money;

/// <summary>
/// Current account definition. The balance is NOT stored here as canonical truth.
/// Balance is derived from FinancialTransactions in project_eve_history.db.
/// </summary>
public sealed class FinancialAccountRecord
{
    public string Id { get; set; } = "";
    public string OwnerType { get; set; } = "NPC";
    public int OwnerId { get; set; }
    public string AccountType { get; set; } = "";
    public string InstitutionName { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Open";
    public decimal CreditLimit { get; set; }
    public decimal InterestRate { get; set; }
    public string OpenedGameTime { get; set; } = "";
}

public sealed class FinancialTransactionRecord
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string TransferGroupId { get; set; } = "";
    public string TransactionType { get; set; } = "";
    /// <summary>Signed delta. Positive adds value to the account; negative removes value.</summary>
    public decimal Amount { get; set; }
    public string CounterpartyAccountId { get; set; } = "";
    public int? MerchantId { get; set; }
    public int? LocationId { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string GameTime { get; set; } = "";
    public string RelatedEventId { get; set; } = "";
    public string Status { get; set; } = "Posted";
}

public readonly record struct NpcFinancialSnapshot(
    decimal Cash,
    decimal Bank,
    decimal Debt
);
