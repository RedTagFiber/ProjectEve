using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

public sealed partial class NpcStudioRepository
{
    public Task<CanonicalFinanceBundle?> GetCanonicalFinanceBundleAsync(int npcId)
    {
        using var conn = Open();

        var name = CanonicalFinanceScalar(
            conn,
            "SELECT IFNULL(Name, '') FROM Characters WHERE Id = $npcId;",
            npcId);

        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult<CanonicalFinanceBundle?>(null);

        var bundle = new CanonicalFinanceBundle
        {
            NpcId = npcId,
            NpcName = name,
            AccountColumns = GetColumns(conn, "FinancialAccounts"),
            ObligationColumns = GetColumns(conn, "FinancialObligations")
        };

        bundle.Accounts = GetFinanceRows(
            conn,
            "FinancialAccounts",
            bundle.AccountColumns,
            "OwnerId",
            npcId,
            "OwnerType",
            "NPC");

        bundle.Obligations = GetFinanceRows(
            conn,
            "FinancialObligations",
            bundle.ObligationColumns,
            "OwnerNpcId",
            npcId);

        return Task.FromResult<CanonicalFinanceBundle?>(bundle);
    }

    public Task SaveCanonicalFinanceRowAsync(
        CanonicalDynamicRow row,
        int npcId)
    {
        using var conn = Open();

        var columns = GetColumns(conn, row.TableName);

        if (columns.Count == 0)
            throw new InvalidOperationException(
                $"Table '{row.TableName}' does not exist.");

        var allowed = columns
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pk = columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name
                 ?? row.PrimaryKeyColumn;

        if (!allowed.Contains(pk))
            throw new InvalidOperationException(
                $"Primary key '{pk}' is not valid for table '{row.TableName}'.");

        row.PrimaryKeyColumn = pk;

        if (!row.Values.TryGetValue(pk, out var id) ||
            string.IsNullOrWhiteSpace(id))
        {
            id = $"studio-{row.TableName.ToLowerInvariant()}-{Guid.NewGuid():N}";
            row.Values[pk] = id;
        }

        ApplyCanonicalOwnerDefaults(row, npcId, allowed);

        var writable = row.Values
            .Where(pair => allowed.Contains(pair.Key))
            .ToList();

        if (writable.Count == 0)
            throw new InvalidOperationException("No writable values were supplied.");

        var insertColumns = string.Join(
            ", ",
            writable.Select(pair => QuoteIdentifier(pair.Key)));

        var parameterNames = writable
            .Select((pair, i) => (pair, parameter: $"$p{i}"))
            .ToList();

        var insertParams = string.Join(
            ", ",
            parameterNames.Select(x => x.parameter));

        var updateSet = string.Join(
            ", ",
            parameterNames
                .Where(x => !x.pair.Key.Equals(pk, StringComparison.OrdinalIgnoreCase))
                .Select(x =>
                    $"{QuoteIdentifier(x.pair.Key)} = excluded.{QuoteIdentifier(x.pair.Key)}"));

        using var cmd = conn.CreateCommand();

        cmd.CommandText = $"""
            INSERT INTO {QuoteIdentifier(row.TableName)}
            ({insertColumns})
            VALUES
            ({insertParams})
            ON CONFLICT({QuoteIdentifier(pk)}) DO UPDATE SET
            {updateSet};
            """;

        foreach (var entry in parameterNames)
        {
            cmd.Parameters.AddWithValue(
                entry.parameter,
                ConvertFinanceValue(
                    columns.First(c =>
                        c.Name.Equals(
                            entry.pair.Key,
                            StringComparison.OrdinalIgnoreCase)),
                    entry.pair.Value));
        }

        cmd.ExecuteNonQuery();

        var verified = ReadFinanceRowByPk(
            conn,
            row.TableName,
            columns,
            pk,
            id!);

        if (verified is null)
            throw new InvalidOperationException(
                $"Save verification failed: row '{id}' was not found after write.");

        AddRevision(
            conn,
            npcId,
            "Canonical Finance",
            $"{row.TableName} saved",
            $"{pk}={id}");

        return Task.CompletedTask;
    }

    private static void ApplyCanonicalOwnerDefaults(
        CanonicalDynamicRow row,
        int npcId,
        HashSet<string> allowed)
    {
        if (row.TableName.Equals(
            "FinancialAccounts",
            StringComparison.OrdinalIgnoreCase))
        {
            if (allowed.Contains("OwnerType"))
                row.Values["OwnerType"] = "NPC";

            if (allowed.Contains("OwnerId"))
                row.Values["OwnerId"] = npcId.ToString();
        }

        if (row.TableName.Equals(
            "FinancialObligations",
            StringComparison.OrdinalIgnoreCase))
        {
            if (allowed.Contains("OwnerNpcId"))
                row.Values["OwnerNpcId"] = npcId.ToString();
        }
    }

    private static List<CanonicalTableColumn> GetColumns(
        SqliteConnection conn,
        string tableName)
    {
        if (!IsAllowedFinanceTable(tableName))
            throw new InvalidOperationException(
                $"Finance table '{tableName}' is not allowed.");

        var list = new List<CanonicalTableColumn>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"PRAGMA table_info({QuoteIdentifier(tableName)});";

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            list.Add(new CanonicalTableColumn
            {
                Name = Convert.ToString(r["name"]) ?? "",
                Type = Convert.ToString(r["type"]) ?? "",
                NotNull = Convert.ToInt32(r["notnull"]) != 0,
                IsPrimaryKey = Convert.ToInt32(r["pk"]) != 0
            });
        }

        return list;
    }

    private static List<CanonicalDynamicRow> GetFinanceRows(
        SqliteConnection conn,
        string tableName,
        List<CanonicalTableColumn> columns,
        string ownerColumn,
        int npcId,
        string? extraColumn = null,
        string? extraValue = null)
    {
        var list = new List<CanonicalDynamicRow>();

        if (columns.Count == 0)
            return list;

        if (!columns.Any(c =>
            c.Name.Equals(ownerColumn, StringComparison.OrdinalIgnoreCase)))
            return list;

        var sql =
            $"SELECT * FROM {QuoteIdentifier(tableName)} " +
            $"WHERE {QuoteIdentifier(ownerColumn)} = $npcId";

        if (!string.IsNullOrWhiteSpace(extraColumn) &&
            columns.Any(c =>
                c.Name.Equals(extraColumn, StringComparison.OrdinalIgnoreCase)))
        {
            sql += $" AND {QuoteIdentifier(extraColumn)} = $extra";
        }

        sql += ";";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        if (sql.Contains("$extra"))
            cmd.Parameters.AddWithValue("$extra", extraValue ?? "");

        using var r = cmd.ExecuteReader();

        var pk = columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name ?? "Id";

        while (r.Read())
        {
            var row = new CanonicalDynamicRow
            {
                TableName = tableName,
                PrimaryKeyColumn = pk
            };

            foreach (var col in columns)
            {
                var ordinal = r.GetOrdinal(col.Name);
                row.Values[col.Name] =
                    r.IsDBNull(ordinal)
                        ? null
                        : Convert.ToString(r.GetValue(ordinal));
            }

            list.Add(row);
        }

        return list;
    }

    private static CanonicalDynamicRow? ReadFinanceRowByPk(
        SqliteConnection conn,
        string tableName,
        List<CanonicalTableColumn> columns,
        string pk,
        string id)
    {
        using var cmd = conn.CreateCommand();

        cmd.CommandText = $"""
            SELECT *
            FROM {QuoteIdentifier(tableName)}
            WHERE {QuoteIdentifier(pk)} = $id;
            """;

        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
            return null;

        var row = new CanonicalDynamicRow
        {
            TableName = tableName,
            PrimaryKeyColumn = pk
        };

        foreach (var col in columns)
        {
            var ordinal = r.GetOrdinal(col.Name);
            row.Values[col.Name] =
                r.IsDBNull(ordinal)
                    ? null
                    : Convert.ToString(r.GetValue(ordinal));
        }

        return row;
    }

    private static object ConvertFinanceValue(
        CanonicalTableColumn column,
        string? value)
    {
        if (value is null)
            return DBNull.Value;

        var type = (column.Type ?? "").ToUpperInvariant();

        if (type.Contains("INT") &&
            long.TryParse(value, out var intValue))
            return intValue;

        if ((type.Contains("REAL") ||
             type.Contains("FLOA") ||
             type.Contains("DOUB") ||
             type.Contains("NUM")) &&
            double.TryParse(
                value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var realValue))
            return realValue;

        return value;
    }

    private static bool IsAllowedFinanceTable(string tableName)
        => tableName.Equals(
               "FinancialAccounts",
               StringComparison.OrdinalIgnoreCase)
        || tableName.Equals(
               "FinancialObligations",
               StringComparison.OrdinalIgnoreCase);

    private static string QuoteIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Any(ch =>
                !(char.IsLetterOrDigit(ch) || ch == '_')))
        {
            throw new InvalidOperationException(
                $"Unsafe SQLite identifier '{name}'.");
        }

        return "\"" + name + "\"";
    }

    private static string CanonicalFinanceScalar(
        SqliteConnection conn,
        string sql,
        int npcId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
}
