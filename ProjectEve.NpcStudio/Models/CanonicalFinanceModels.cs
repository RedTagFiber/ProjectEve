namespace ProjectEve.NpcStudio.Models;

public sealed class CanonicalTableColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool NotNull { get; set; }
    public bool IsPrimaryKey { get; set; }
}

public sealed class CanonicalDynamicRow
{
    public string TableName { get; set; } = "";
    public Dictionary<string, string?> Values { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string PrimaryKeyColumn { get; set; } = "Id";

    public string PrimaryKeyValue
        => Values.TryGetValue(PrimaryKeyColumn, out var value)
            ? value ?? ""
            : "";
}

public sealed class CanonicalFinanceBundle
{
    public int NpcId { get; set; }
    public string NpcName { get; set; } = "";

    public List<CanonicalTableColumn> AccountColumns { get; set; } = new();
    public List<CanonicalDynamicRow> Accounts { get; set; } = new();

    public List<CanonicalTableColumn> ObligationColumns { get; set; } = new();
    public List<CanonicalDynamicRow> Obligations { get; set; } = new();
}
