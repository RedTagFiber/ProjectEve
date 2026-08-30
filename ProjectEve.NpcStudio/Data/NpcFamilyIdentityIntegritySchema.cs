using Microsoft.Data.Sqlite;
using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Data;

/// <summary>
/// Family identity + structural integrity foundation.
///
/// MAIN DB owns identity/name history/provenance.
/// RELATIONSHIPS DB owns structural kinship + relationship depth.
///
/// This class is additive and idempotent. It never deletes family data.
/// </summary>
public static class NpcFamilyIdentityIntegritySchema
{
    public static void Ensure(NpcStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EnsureMain(options.MainDbPath);

        var relationshipsPath =
            GetOptionalStringProperty(options, "RelationshipsDbPath")
            ?? @"D:\ProjectEveData\Database\project_eve_relationships.db";

        EnsureRelationships(relationshipsPath);
    }

    private static void EnsureMain(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using var conn = Open(dbPath);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcNameProfiles
            (
                NpcId INTEGER PRIMARY KEY,
                FirstName TEXT NOT NULL DEFAULT '',
                MiddleName TEXT NOT NULL DEFAULT '',
                CurrentLastName TEXT NOT NULL DEFAULT '',
                BirthLastName TEXT NOT NULL DEFAULT '',
                PreferredName TEXT NOT NULL DEFAULT '',
                Suffix TEXT NOT NULL DEFAULT '',
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcNameHistory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NpcId INTEGER NOT NULL,
                FirstName TEXT NOT NULL DEFAULT '',
                MiddleName TEXT NOT NULL DEFAULT '',
                LastName TEXT NOT NULL DEFAULT '',
                NameType TEXT NOT NULL DEFAULT 'Current',
                StartGameDate TEXT NOT NULL DEFAULT '',
                EndGameDate TEXT NOT NULL DEFAULT '',
                Reason TEXT NOT NULL DEFAULT '',
                RelatedNpcId INTEGER NULL,
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        Execute(conn, """
            CREATE INDEX IF NOT EXISTS IX_NpcNameHistory_NpcId
            ON NpcNameHistory(NpcId);
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS NpcCreationProvenance
            (
                NpcId INTEGER PRIMARY KEY,
                CreationSourceType TEXT NOT NULL DEFAULT 'Unknown',
                CreatedFromNpcId INTEGER NULL,
                CreatedFromNpcName TEXT NOT NULL DEFAULT '',
                OriginalRole TEXT NOT NULL DEFAULT '',
                CreationBatchId TEXT NOT NULL DEFAULT '',
                BuildStatus TEXT NOT NULL DEFAULT 'Draft',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        // Seed a structured name row for every existing Character without
        // changing Characters.Name. Birth surname is intentionally NOT guessed
        // beyond the current surname; builders/history can correct it later.
        if (TableExists(conn, "Characters") &&
            ColumnExists(conn, "Characters", "Id") &&
            ColumnExists(conn, "Characters", "Name"))
        {
            using var read = conn.CreateCommand();
            read.CommandText = "SELECT Id, Name FROM Characters;";
            using var reader = read.ExecuteReader();

            var rows = new List<(int Id, string Name)>();
            while (reader.Read())
                rows.Add((reader.GetInt32(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));

            reader.Close();

            foreach (var row in rows)
            {
                var clean = CleanDraftDisplayName(row.Name);
                var parts = SplitName(clean);

                using var insert = conn.CreateCommand();
                insert.CommandText = """
                    INSERT INTO NpcNameProfiles
                    (
                        NpcId, FirstName, MiddleName, CurrentLastName,
                        BirthLastName, PreferredName, Suffix, UpdatedRealAt
                    )
                    VALUES
                    (
                        $id, $first, $middle, $last,
                        $birth, $preferred, $suffix, CURRENT_TIMESTAMP
                    )
                    ON CONFLICT(NpcId) DO NOTHING;
                    """;
                insert.Parameters.AddWithValue("$id", row.Id);
                insert.Parameters.AddWithValue("$first", parts.First);
                insert.Parameters.AddWithValue("$middle", parts.Middle);
                insert.Parameters.AddWithValue("$last", parts.Last);
                insert.Parameters.AddWithValue("$birth", parts.Last);
                insert.Parameters.AddWithValue("$preferred", parts.First);
                insert.Parameters.AddWithValue("$suffix", parts.Suffix);
                insert.ExecuteNonQuery();
            }
        }
    }

    private static void EnsureRelationships(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        using var conn = Open(dbPath);

        // Canonical parent/child structure. ParentKind keeps biological,
        // adoptive, step, guardian, etc. distinct.
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS FamilyParentChildLinks
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ParentNpcId INTEGER NOT NULL,
                ChildNpcId INTEGER NOT NULL,
                ParentKind TEXT NOT NULL DEFAULT 'Biological',
                ParentSlot TEXT NOT NULL DEFAULT '',
                FamilyLine TEXT NOT NULL DEFAULT '',
                IsCurrent INTEGER NOT NULL DEFAULT 1,
                StartGameDate TEXT NOT NULL DEFAULT '',
                EndGameDate TEXT NOT NULL DEFAULT '',
                Source TEXT NOT NULL DEFAULT 'FamilyBuilder',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CHECK (ParentNpcId <> ChildNpcId)
            );
            """);

        Execute(conn, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_FamilyParentChildLinks_PairKind
            ON FamilyParentChildLinks(ParentNpcId, ChildNpcId, ParentKind);
            """);

        // A child may have at most one active biological Mother slot and one
        // active biological Father slot. Step/adoptive parents are separate.
        Execute(conn, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_FamilyParentChildLinks_BioSlot
            ON FamilyParentChildLinks(ChildNpcId, ParentSlot)
            WHERE IsCurrent = 1
              AND lower(ParentKind) = 'biological'
              AND ParentSlot IN ('Mother','Father');
            """);

        // Canonical spouse/partner history. Store the smaller ID first so a
        // pair cannot be duplicated in reverse order.
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS FamilyUnionLinks
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Person1NpcId INTEGER NOT NULL,
                Person2NpcId INTEGER NOT NULL,
                UnionType TEXT NOT NULL DEFAULT 'Marriage',
                Status TEXT NOT NULL DEFAULT 'Active',
                StartGameDate TEXT NOT NULL DEFAULT '',
                EndGameDate TEXT NOT NULL DEFAULT '',
                Source TEXT NOT NULL DEFAULT 'FamilyBuilder',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CHECK (Person1NpcId < Person2NpcId)
            );
            """);

        Execute(conn, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_FamilyUnionLinks_PairTypeStart
            ON FamilyUnionLinks(Person1NpcId, Person2NpcId, UnionType, StartGameDate);
            """);

        Execute(conn, """
            CREATE INDEX IF NOT EXISTS IX_FamilyUnionLinks_Person1_Status
            ON FamilyUnionLinks(Person1NpcId, Status);
            """);

        Execute(conn, """
            CREATE INDEX IF NOT EXISTS IX_FamilyUnionLinks_Person2_Status
            ON FamilyUnionLinks(Person2NpcId, Status);
            """);

        // Explicit structural kinship that cannot/should not be inferred from
        // parent-child/union edges (for example a story-defined legal kinship).
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS FamilyKinshipOverrides
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceNpcId INTEGER NOT NULL,
                TargetNpcId INTEGER NOT NULL,
                KinshipRole TEXT NOT NULL,
                IsCurrent INTEGER NOT NULL DEFAULT 1,
                Source TEXT NOT NULL DEFAULT 'Manual',
                Notes TEXT NOT NULL DEFAULT '',
                CreatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedRealAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CHECK (SourceNpcId <> TargetNpcId)
            );
            """);

        Execute(conn, """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_FamilyKinshipOverrides_DirectedRole
            ON FamilyKinshipOverrides(SourceNpcId, TargetNpcId, KinshipRole);
            """);

        // Relationship depth is independent from emotional scores.
        if (TableExists(conn, "RelationshipStates"))
        {
            EnsureColumn(conn, "RelationshipStates", "RelationshipTier", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(conn, "RelationshipStates", "KnownSinceYear", "INTEGER NULL");
            EnsureColumn(conn, "RelationshipStates", "RelationshipStartedYear", "INTEGER NULL");
            EnsureColumn(conn, "RelationshipStates", "RelationshipEndedYear", "INTEGER NULL");
            EnsureColumn(conn, "RelationshipStates", "RelationshipLifeStage", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(conn, "RelationshipStates", "IsEstranged", "INTEGER NOT NULL DEFAULT 0");

            Execute(conn, """
                CREATE INDEX IF NOT EXISTS IX_RelationshipStates_Source_Tier
                ON RelationshipStates(SourceCharacterId, RelationshipTier);
                """);
        }
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        return conn;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string definition)
    {
        if (ColumnExists(conn, table, column)) return;
        Execute(conn, $"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};");
    }

    private static string? GetOptionalStringProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        return property?.GetValue(value) as string;
    }

    public static string CleanDraftDisplayName(string? value)
    {
        var text = (value ?? "").Trim();

        const string prefix = "[Family Draft]";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..].Trim();

        return text;
    }

    private static (string First, string Middle, string Last, string Suffix) SplitName(string value)
    {
        // Do not treat "Evelyn Sinclair — Mother" as a surname.
        var dash = value.IndexOf('—');
        if (dash >= 0)
            value = value[..dash].Trim();

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("", "", "", "");
        if (parts.Length == 1) return (parts[0], "", "", "");

        var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Jr.", "Jr", "Sr.", "Sr", "II", "III", "IV", "V"
        };

        var suffix = "";
        var end = parts.Length;
        if (suffixes.Contains(parts[^1]))
        {
            suffix = parts[^1];
            end--;
        }

        if (end <= 1) return (parts[0], "", "", suffix);

        var first = parts[0];
        var last = parts[end - 1];
        var middle = end > 2 ? string.Join(" ", parts.Skip(1).Take(end - 2)) : "";
        return (first, middle, last, suffix);
    }
}
