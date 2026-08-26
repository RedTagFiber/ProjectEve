using Microsoft.Data.Sqlite;
using ProjectEve.Data;
using ProjectEve.Traits;

namespace ProjectEve.Characters.Traits;

/// <summary>
/// The single persistence gateway for NPC trait values.
///
/// Canonical table: project_eve.db / NpcTraitValues
/// Schema ownership: ProjectEveDatabaseSetup
///
/// No other repository should create or maintain a second live Traits table.
/// </summary>
public static class NpcTraitRepository
{
    public static Dictionary<string, float> LoadAll(int npcId)
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TraitId, CurrentValue
            FROM NpcTraitValues
            WHERE NpcId = $id AND IsEnabled <> 0
            ORDER BY TraitId;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string traitId = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(traitId))
                continue;

            float value = reader.IsDBNull(1)
                ? 50f
                : Convert.ToSingle(reader.GetValue(1));

            result[traitId] = value;
        }

        return result;
    }

    public static void SaveAll(int npcId, NpcTraits traits)
    {
        if (traits == null)
            return;

        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var tx = conn.BeginTransaction();

        var all = traits.GetAll();

        // Disable rows no longer present rather than deleting build history.
        using (var disable = conn.CreateCommand())
        {
            disable.Transaction = tx;
            disable.CommandText = """
                UPDATE NpcTraitValues
                SET IsEnabled = 0,
                    UpdatedRealAt = CURRENT_TIMESTAMP
                WHERE NpcId = $id;
                """;
            disable.Parameters.AddWithValue("$id", npcId);
            disable.ExecuteNonQuery();
        }

        foreach (var pair in all)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            Upsert(conn, tx, npcId, pair.Key, pair.Value, controlOnly: false);
        }

        tx.Commit();
    }

    public static void SaveOne(int npcId, string traitId, float value)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            return;

        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var tx = conn.BeginTransaction();
        Upsert(conn, tx, npcId, traitId, value, controlOnly: false);
        tx.Commit();
    }


    /// <summary>
    /// Saves the editable Fast20 dossier state.
    /// Current intensity, set-point and expression style are canonical in MAIN.
    /// Stage/Wildcard/Deviation remain derived.
    /// </summary>

    /// <summary>
    /// Saves one explicitly assigned Mid personality trait.
    /// Missing Mid traits remain missing until the author assigns them.
    /// </summary>

    /// <summary>
    /// Saves one explicitly authored Slow trait/preference.
    /// Slow traits remain absent until intentionally assigned.
    /// </summary>

    /// <summary>
    /// Loads explicitly authored SubSlow values from MAIN.
    /// Definition defaults are not persisted here.
    /// </summary>
    public static IReadOnlyList<NpcSubSlowValueRecord> LoadSubSlowValues(int npcId)
    {
        if (npcId <= 0)
            return Array.Empty<NpcSubSlowValueRecord>();

        ProjectEveDatabaseSetup.EnsureAll();

        var rows = new List<NpcSubSlowValueRecord>();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                ParentTraitId,
                SubTraitId,
                SubTraitName,
                ValueType,
                ValueText,
                IsEnabled
            FROM NpcSubSlowValues
            WHERE NpcId = $npcId
              AND IsEnabled = 1
            ORDER BY ParentTraitId, SubTraitName, SubTraitId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new NpcSubSlowValueRecord
            {
                ParentTraitId = reader.IsDBNull(0) ? "" : reader.GetString(0),
                SubTraitId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                SubTraitName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                ValueType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ValueText = reader.IsDBNull(4) ? "" : reader.GetString(4),
                IsEnabled = !reader.IsDBNull(5) && reader.GetInt32(5) != 0
            });
        }

        return rows;
    }

    /// <summary>
    /// Saves one explicitly authored typed SubSlow value.
    /// Bool and enum values are stored losslessly as text.
    /// </summary>
    public static void SaveSubSlowValue(
        int npcId,
        string parentTraitId,
        string subTraitId,
        string subTraitName,
        string valueType,
        string valueText)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        if (string.IsNullOrWhiteSpace(parentTraitId))
            throw new ArgumentException("ParentTraitId is required.", nameof(parentTraitId));

        if (string.IsNullOrWhiteSpace(subTraitId))
            throw new ArgumentException("SubTraitId is required.", nameof(subTraitId));

        if (string.IsNullOrWhiteSpace(valueText))
        {
            DeactivateSubSlowValue(npcId, parentTraitId, subTraitId);
            return;
        }

        ProjectEveDatabaseSetup.EnsureAll();

        string parent = parentTraitId.Trim();
        string sub = subTraitId.Trim();
        string name = string.IsNullOrWhiteSpace(subTraitName)
            ? sub
            : subTraitName.Trim();
        string type = valueType?.Trim() ?? "";
        string value = valueText.Trim();
        string rowId = $"{npcId}_{parent}_{sub}";

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcSubSlowValues
            (
                Id,
                NpcId,
                ParentTraitId,
                SubTraitId,
                SubTraitName,
                ValueType,
                ValueText,
                IsEnabled,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $id,
                $npcId,
                $parentTraitId,
                $subTraitId,
                $subTraitName,
                $valueType,
                $valueText,
                1,
                '',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId, ParentTraitId, SubTraitId) DO UPDATE SET
                SubTraitName = excluded.SubTraitName,
                ValueType = excluded.ValueType,
                ValueText = excluded.ValueText,
                IsEnabled = 1,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", rowId);
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$parentTraitId", parent);
        cmd.Parameters.AddWithValue("$subTraitId", sub);
        cmd.Parameters.AddWithValue("$subTraitName", name);
        cmd.Parameters.AddWithValue("$valueType", type);
        cmd.Parameters.AddWithValue("$valueText", value);
        cmd.ExecuteNonQuery();
    }

    public static void DeactivateSubSlowValue(
        int npcId,
        string parentTraitId,
        string subTraitId)
    {
        if (npcId <= 0 ||
            string.IsNullOrWhiteSpace(parentTraitId) ||
            string.IsNullOrWhiteSpace(subTraitId))
        {
            return;
        }

        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcSubSlowValues
            SET IsEnabled = 0,
                UpdatedRealAt = CURRENT_TIMESTAMP
            WHERE NpcId = $npcId
              AND ParentTraitId = $parentTraitId
              AND SubTraitId = $subTraitId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$parentTraitId", parentTraitId.Trim());
        cmd.Parameters.AddWithValue("$subTraitId", subTraitId.Trim());
        cmd.ExecuteNonQuery();
    }

    public static void SaveSlowDossierState(
        int npcId,
        string traitId,
        string traitName,
        float currentValue,
        float setPointValue,
        string expressionStyle)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        if (string.IsNullOrWhiteSpace(traitId))
            throw new ArgumentException("TraitId is required.", nameof(traitId));

        ProjectEveDatabaseSetup.EnsureAll();

        int current = Math.Clamp(
            (int)Math.Round(currentValue),
            0,
            100);

        int setPoint = Math.Clamp(
            (int)Math.Round(setPointValue),
            0,
            100);

        string cleanId = traitId.Trim();
        string cleanName = string.IsNullOrWhiteSpace(traitName)
            ? PrettyName(cleanId)
            : traitName.Trim();

        string cleanStyle = expressionStyle?.Trim() ?? "";
        string rowId = $"{npcId}_{cleanId}";

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcTraitValues
            (
                Id,
                NpcId,
                MainGroup,
                SubGroup,
                SubSubGroup,
                TraitId,
                TraitName,
                IsEnabled,
                StartingValue,
                SetPointValue,
                CurrentValue,
                ExpressionStyle,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $rowId,
                $npcId,
                'Preference',
                'Slow',
                '',
                $traitId,
                $traitName,
                1,
                $starting,
                $setPoint,
                $current,
                $style,
                '',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                MainGroup = 'Preference',
                SubGroup = 'Slow',
                TraitName = excluded.TraitName,
                IsEnabled = 1,
                SetPointValue = excluded.SetPointValue,
                CurrentValue = excluded.CurrentValue,
                ExpressionStyle = excluded.ExpressionStyle,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$rowId", rowId);
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$traitId", cleanId);
        cmd.Parameters.AddWithValue("$traitName", cleanName);
        cmd.Parameters.AddWithValue("$starting", setPoint);
        cmd.Parameters.AddWithValue("$setPoint", setPoint);
        cmd.Parameters.AddWithValue("$current", current);
        cmd.Parameters.AddWithValue("$style", cleanStyle);
        cmd.ExecuteNonQuery();
    }

    public static void SaveMidDossierState(
        int npcId,
        string traitId,
        string traitName,
        float currentValue,
        float setPointValue,
        string expressionStyle)
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        if (string.IsNullOrWhiteSpace(traitId))
            throw new ArgumentException("TraitId is required.", nameof(traitId));

        ProjectEveDatabaseSetup.EnsureAll();

        int current = Math.Clamp(
            (int)Math.Round(currentValue),
            0,
            100);

        int setPoint = Math.Clamp(
            (int)Math.Round(setPointValue),
            0,
            100);

        string cleanId = traitId.Trim();
        string cleanName = string.IsNullOrWhiteSpace(traitName)
            ? PrettyName(cleanId)
            : traitName.Trim();

        string cleanStyle = expressionStyle?.Trim() ?? "";
        string rowId = $"{npcId}_{cleanId}";

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcTraitValues
            (
                Id,
                NpcId,
                MainGroup,
                SubGroup,
                SubSubGroup,
                TraitId,
                TraitName,
                IsEnabled,
                StartingValue,
                SetPointValue,
                CurrentValue,
                ExpressionStyle,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $rowId,
                $npcId,
                'Personality',
                'Mid',
                '',
                $traitId,
                $traitName,
                1,
                $starting,
                $setPoint,
                $current,
                $style,
                '',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                MainGroup = 'Personality',
                SubGroup = 'Mid',
                TraitName = excluded.TraitName,
                IsEnabled = 1,
                SetPointValue = excluded.SetPointValue,
                CurrentValue = excluded.CurrentValue,
                ExpressionStyle = excluded.ExpressionStyle,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$rowId", rowId);
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$traitId", cleanId);
        cmd.Parameters.AddWithValue("$traitName", cleanName);
        cmd.Parameters.AddWithValue("$starting", setPoint);
        cmd.Parameters.AddWithValue("$setPoint", setPoint);
        cmd.Parameters.AddWithValue("$current", current);
        cmd.Parameters.AddWithValue("$style", cleanStyle);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Deactivates one assigned trait without deleting its canonical row.
    /// </summary>
    public static void DeactivateTrait(int npcId, string traitId)
    {
        if (npcId <= 0 || string.IsNullOrWhiteSpace(traitId))
            return;

        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcTraitValues
            SET IsEnabled = 0,
                UpdatedRealAt = CURRENT_TIMESTAMP
            WHERE NpcId = $npcId
              AND TraitId = $traitId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$traitId", traitId.Trim());
        cmd.ExecuteNonQuery();
    }

    public static void SaveFastDossierState(
        int npcId,
        string traitId,
        float currentValue,
        float setPointValue,
        string expressionStyle)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            return;

        ProjectEveDatabaseSetup.EnsureAll();

        int current = Math.Clamp(
            (int)Math.Round(currentValue),
            0,
            100);

        int setPoint = Math.Clamp(
            (int)Math.Round(setPointValue),
            0,
            100);

        string style = expressionStyle?.Trim() ?? "";
        string rowId = $"{npcId}_{traitId}";

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcTraitValues
            (
                Id,
                NpcId,
                MainGroup,
                SubGroup,
                SubSubGroup,
                TraitId,
                TraitName,
                IsEnabled,
                StartingValue,
                SetPointValue,
                CurrentValue,
                ExpressionStyle,
                Notes,
                UpdatedRealAt
            )
            VALUES
            (
                $rowId,
                $npcId,
                $main,
                $sub,
                '',
                $traitId,
                $traitName,
                1,
                $starting,
                $setPoint,
                $current,
                $style,
                '',
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                IsEnabled = 1,
                SetPointValue = excluded.SetPointValue,
                CurrentValue = excluded.CurrentValue,
                ExpressionStyle = excluded.ExpressionStyle,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$rowId", rowId);
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$main", GuessMainGroup(traitId));
        cmd.Parameters.AddWithValue("$sub", GuessSubGroup(traitId));
        cmd.Parameters.AddWithValue("$traitId", traitId);
        cmd.Parameters.AddWithValue("$traitName", PrettyName(traitId));
        cmd.Parameters.AddWithValue("$starting", setPoint);
        cmd.Parameters.AddWithValue("$setPoint", setPoint);
        cmd.Parameters.AddWithValue("$current", current);
        cmd.Parameters.AddWithValue("$style", style);

        cmd.ExecuteNonQuery();
    }

    public static int LoadControl(int npcId, string traitId, int fallback = 50)
    {
        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Control
            FROM NpcTraitControl
            WHERE NpcId = $id AND TraitId = $trait
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$trait", traitId);

        object? value = cmd.ExecuteScalar();
        if (value == null || value == DBNull.Value)
            return fallback;

        return Math.Clamp(Convert.ToInt32(value), 0, 100);
    }

    public static void SaveControl(int npcId, string traitId, int control, DateTime lastUpdated)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            return;

        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcTraitControl
            (NpcId, TraitId, Control, LastUpdatedRealAt)
            VALUES ($id, $trait, $control, $updated)
            ON CONFLICT(NpcId, TraitId) DO UPDATE SET
                Control = excluded.Control,
                LastUpdatedRealAt = excluded.LastUpdatedRealAt;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$trait", traitId);
        cmd.Parameters.AddWithValue("$control", Math.Clamp(control, 0, 100));
        cmd.Parameters.AddWithValue("$updated", lastUpdated.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public static (float Value, int Control, DateTime LastUpdated)? LoadOne(int npcId, string traitId)
    {
        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT v.CurrentValue,
                   COALESCE(c.Control, 50),
                   c.LastUpdatedRealAt
            FROM NpcTraitValues v
            LEFT JOIN NpcTraitControl c
              ON c.NpcId = v.NpcId AND c.TraitId = v.TraitId
            WHERE v.NpcId = $id
              AND v.TraitId = $trait
              AND v.IsEnabled <> 0
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", npcId);
        cmd.Parameters.AddWithValue("$trait", traitId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        float value = reader.IsDBNull(0) ? 50f : Convert.ToSingle(reader.GetValue(0));
        int control = reader.IsDBNull(1) ? 50 : Convert.ToInt32(reader.GetValue(1));
        DateTime updated = DateTime.UtcNow;

        if (!reader.IsDBNull(2) && DateTime.TryParse(reader.GetString(2), out var parsed))
            updated = parsed;

        return (value, Math.Clamp(control, 0, 100), updated);
    }


    /// <summary>
    /// Reads canonical persisted trait state for dossier/context use.
    ///
    /// StartingValue is the original persisted seed value and is not updated by
    /// later CurrentValue writes. Live runtime set-points/styles, when available,
    /// are supplied separately by FastTraitDossierService.
    /// </summary>
    public static IReadOnlyList<TraitValueRecord> LoadValueRecords(int npcId)
    {
        var result = new List<TraitValueRecord>();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                TraitId,
                TraitName,
                StartingValue,
                SetPointValue,
                CurrentValue,
                ExpressionStyle
            FROM NpcTraitValues
            WHERE NpcId = $npcId
              AND IsEnabled <> 0
            ORDER BY TraitId;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string traitId = reader.IsDBNull(0) ? "" : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(traitId))
                continue;

            result.Add(new TraitValueRecord
            {
                TraitId = traitId,
                TraitName = reader.IsDBNull(1) ? traitId : reader.GetString(1),
                StartingValue = reader.IsDBNull(2)
                    ? 50f
                    : Convert.ToSingle(reader.GetValue(2)),
                SetPointValue = reader.IsDBNull(3)
                    ? (reader.IsDBNull(2)
                        ? 50f
                        : Convert.ToSingle(reader.GetValue(2)))
                    : Convert.ToSingle(reader.GetValue(3)),
                CurrentValue = reader.IsDBNull(4)
                    ? 50f
                    : Convert.ToSingle(reader.GetValue(4)),
                ExpressionStyle = reader.IsDBNull(5)
                    ? ""
                    : reader.GetString(5)
            });
        }

        return result;
    }

    public static TraitChangeRecord? LoadLastMeaningfulChange(
        int npcId,
        string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            return null;

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.RelationshipDatabasePath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                Id,
                NpcId,
                TraitId,
                TargetCharacterId,
                BeforeValue,
                AfterValue,
                Delta,
                ReasonType,
                Reason,
                SourceType,
                SourceEventId,
                SourceMemoryId,
                GameTime,
                CreatedRealAt
            FROM NpcTraitChangeHistory
            WHERE NpcId = $npcId
              AND TraitId = $traitId
            ORDER BY GameTime DESC, CreatedRealAt DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$traitId", traitId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new TraitChangeRecord
        {
            Id = reader.GetString(0),
            NpcId = reader.GetInt32(1),
            TraitId = reader.GetString(2),
            TargetCharacterId = reader.GetInt32(3),
            BeforeValue = Convert.ToSingle(reader.GetValue(4)),
            AfterValue = Convert.ToSingle(reader.GetValue(5)),
            Delta = Convert.ToSingle(reader.GetValue(6)),
            ReasonType = reader.GetString(7),
            Reason = reader.GetString(8),
            SourceType = reader.GetString(9),
            SourceEventId = reader.GetString(10),
            SourceMemoryId = reader.GetString(11),
            GameTime = reader.GetString(12),
            CreatedRealAt = reader.GetString(13)
        };
    }

    public sealed class TraitValueRecord
    {
        public string TraitId { get; init; } = "";
        public string TraitName { get; init; } = "";
        public float StartingValue { get; init; } = 50f;
        public float SetPointValue { get; init; } = 50f;
        public float CurrentValue { get; init; } = 50f;
        public string ExpressionStyle { get; init; } = "";
        public string UpdatedRealAt { get; init; } = "";
    }

    public sealed class TraitChangeRecord
    {
        public string Id { get; init; } = "";
        public int NpcId { get; init; }
        public string TraitId { get; init; } = "";
        public int TargetCharacterId { get; init; } = -1;
        public float BeforeValue { get; init; }
        public float AfterValue { get; init; }
        public float Delta { get; init; }
        public string ReasonType { get; init; } = "";
        public string Reason { get; init; } = "";
        public string SourceType { get; init; } = "";
        public string SourceEventId { get; init; } = "";
        public string SourceMemoryId { get; init; } = "";
        public string GameTime { get; init; } = "";
        public string CreatedRealAt { get; init; } = "";
    }


    /// <summary>
    /// Records the causal trail behind a Fast-trait change.
    ///
    /// Current trait intensity remains canonical in MAIN / NpcTraitValues.
    /// Subjective reasons and trait-change history live in RELATIONSHIPS.
    /// Source event/memory ids are references only; their full records stay in
    /// their canonical databases.
    /// </summary>
    public static void RecordCausalChange(
        int npcId,
        string traitId,
        float before,
        float after,
        DateTime gameTime,
        string reasonType,
        string reason,
        string sourceType = "",
        string sourceEventId = "",
        string sourceMemoryId = "",
        int? targetCharacterId = null,
        int confidence = 100)
    {
        if (string.IsNullOrWhiteSpace(traitId))
            return;

        float delta = after - before;
        if (Math.Abs(delta) < 0.001f)
            return;

        ProjectEveDatabaseSetup.EnsureAll();

        int targetId = targetCharacterId ?? -1;
        string cleanReasonType = reasonType?.Trim() ?? "";
        string cleanReason = reason?.Trim() ?? "";
        string cleanSourceType = sourceType?.Trim() ?? "";
        string cleanEvent = sourceEventId?.Trim() ?? "";
        string cleanMemory = sourceMemoryId?.Trim() ?? "";

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.RelationshipDatabasePath}");
        conn.Open();

        using var tx = conn.BeginTransaction();

        // Current active evidence/reason. Stable reasons are updated rather than
        // duplicated every simulation tick.
        using (var reasonCmd = conn.CreateCommand())
        {
            reasonCmd.Transaction = tx;
            reasonCmd.CommandText = """
                INSERT INTO NpcTraitReasons
                (
                    Id,
                    NpcId,
                    TraitId,
                    TargetCharacterId,
                    ReasonType,
                    Reason,
                    Impact,
                    SourceType,
                    SourceEventId,
                    SourceMemoryId,
                    Confidence,
                    IsActive,
                    GameTime,
                    CreatedRealAt,
                    UpdatedRealAt
                )
                VALUES
                (
                    $id,
                    $npcId,
                    $traitId,
                    $targetId,
                    $reasonType,
                    $reason,
                    $impact,
                    $sourceType,
                    $eventId,
                    $memoryId,
                    $confidence,
                    1,
                    $gameTime,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                )
                ON CONFLICT
                (
                    NpcId,
                    TraitId,
                    TargetCharacterId,
                    ReasonType,
                    Reason,
                    SourceType,
                    SourceEventId,
                    SourceMemoryId
                )
                DO UPDATE SET
                    Impact = excluded.Impact,
                    Confidence = excluded.Confidence,
                    IsActive = 1,
                    GameTime = excluded.GameTime,
                    UpdatedRealAt = CURRENT_TIMESTAMP;
                """;

            reasonCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            reasonCmd.Parameters.AddWithValue("$npcId", npcId);
            reasonCmd.Parameters.AddWithValue("$traitId", traitId);
            reasonCmd.Parameters.AddWithValue("$targetId", targetId);
            reasonCmd.Parameters.AddWithValue("$reasonType", cleanReasonType);
            reasonCmd.Parameters.AddWithValue("$reason", cleanReason);
            reasonCmd.Parameters.AddWithValue("$impact", delta);
            reasonCmd.Parameters.AddWithValue("$sourceType", cleanSourceType);
            reasonCmd.Parameters.AddWithValue("$eventId", cleanEvent);
            reasonCmd.Parameters.AddWithValue("$memoryId", cleanMemory);
            reasonCmd.Parameters.AddWithValue("$confidence", Math.Clamp(confidence, 0, 100));
            reasonCmd.Parameters.AddWithValue("$gameTime", gameTime.ToString("o"));
            reasonCmd.ExecuteNonQuery();
        }

        // Append-only subjective trait-change history.
        using (var historyCmd = conn.CreateCommand())
        {
            historyCmd.Transaction = tx;
            historyCmd.CommandText = """
                INSERT INTO NpcTraitChangeHistory
                (
                    Id,
                    NpcId,
                    TraitId,
                    TargetCharacterId,
                    BeforeValue,
                    AfterValue,
                    Delta,
                    ReasonType,
                    Reason,
                    SourceType,
                    SourceEventId,
                    SourceMemoryId,
                    GameTime,
                    CreatedRealAt
                )
                VALUES
                (
                    $id,
                    $npcId,
                    $traitId,
                    $targetId,
                    $before,
                    $after,
                    $delta,
                    $reasonType,
                    $reason,
                    $sourceType,
                    $eventId,
                    $memoryId,
                    $gameTime,
                    CURRENT_TIMESTAMP
                );
                """;

            historyCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            historyCmd.Parameters.AddWithValue("$npcId", npcId);
            historyCmd.Parameters.AddWithValue("$traitId", traitId);
            historyCmd.Parameters.AddWithValue("$targetId", targetId);
            historyCmd.Parameters.AddWithValue("$before", before);
            historyCmd.Parameters.AddWithValue("$after", after);
            historyCmd.Parameters.AddWithValue("$delta", delta);
            historyCmd.Parameters.AddWithValue("$reasonType", cleanReasonType);
            historyCmd.Parameters.AddWithValue("$reason", cleanReason);
            historyCmd.Parameters.AddWithValue("$sourceType", cleanSourceType);
            historyCmd.Parameters.AddWithValue("$eventId", cleanEvent);
            historyCmd.Parameters.AddWithValue("$memoryId", cleanMemory);
            historyCmd.Parameters.AddWithValue("$gameTime", gameTime.ToString("o"));
            historyCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }


    /// <summary>
    /// Creates or updates one active causal reason for a trait.
    /// This edits subjective causal evidence only; it does not itself move the
    /// canonical trait value or create trait-change history.
    /// </summary>
    public static string SaveTraitReason(
        string? id,
        int npcId,
        string traitId,
        int targetCharacterId,
        string reasonType,
        string reason,
        float impact,
        string sourceType = "",
        string sourceEventId = "",
        string sourceMemoryId = "",
        int confidence = 100,
        string gameTime = "")
    {
        if (npcId <= 0)
            throw new ArgumentOutOfRangeException(nameof(npcId));

        if (string.IsNullOrWhiteSpace(traitId))
            throw new ArgumentException("TraitId is required.", nameof(traitId));

        string cleanReason = reason?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(cleanReason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        ProjectEveDatabaseSetup.EnsureAll();

        string reasonId = string.IsNullOrWhiteSpace(id)
            ? Guid.NewGuid().ToString("N")
            : id.Trim();

        string cleanReasonType = reasonType?.Trim() ?? "";
        string cleanSourceType = sourceType?.Trim() ?? "";
        string cleanEvent = sourceEventId?.Trim() ?? "";
        string cleanMemory = sourceMemoryId?.Trim() ?? "";
        string cleanGameTime = gameTime?.Trim() ?? "";

        float cleanImpact = Math.Clamp(impact, -100f, 100f);
        int cleanConfidence = Math.Clamp(confidence, 0, 100);

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.RelationshipDatabasePath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcTraitReasons
            (
                Id,
                NpcId,
                TraitId,
                TargetCharacterId,
                ReasonType,
                Reason,
                Impact,
                SourceType,
                SourceEventId,
                SourceMemoryId,
                Confidence,
                IsActive,
                GameTime,
                CreatedRealAt,
                UpdatedRealAt
            )
            VALUES
            (
                $id,
                $npcId,
                $traitId,
                $targetId,
                $reasonType,
                $reason,
                $impact,
                $sourceType,
                $eventId,
                $memoryId,
                $confidence,
                1,
                $gameTime,
                CURRENT_TIMESTAMP,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                TargetCharacterId = excluded.TargetCharacterId,
                ReasonType = excluded.ReasonType,
                Reason = excluded.Reason,
                Impact = excluded.Impact,
                SourceType = excluded.SourceType,
                SourceEventId = excluded.SourceEventId,
                SourceMemoryId = excluded.SourceMemoryId,
                Confidence = excluded.Confidence,
                IsActive = 1,
                GameTime = excluded.GameTime,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$id", reasonId);
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$traitId", traitId.Trim());
        cmd.Parameters.AddWithValue("$targetId", targetCharacterId);
        cmd.Parameters.AddWithValue("$reasonType", cleanReasonType);
        cmd.Parameters.AddWithValue("$reason", cleanReason);
        cmd.Parameters.AddWithValue("$impact", cleanImpact);
        cmd.Parameters.AddWithValue("$sourceType", cleanSourceType);
        cmd.Parameters.AddWithValue("$eventId", cleanEvent);
        cmd.Parameters.AddWithValue("$memoryId", cleanMemory);
        cmd.Parameters.AddWithValue("$confidence", cleanConfidence);
        cmd.Parameters.AddWithValue("$gameTime", cleanGameTime);
        cmd.ExecuteNonQuery();

        return reasonId;
    }

    /// <summary>
    /// Deactivates a current causal reason without deleting its row.
    /// </summary>
    public static void DeactivateTraitReason(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.RelationshipDatabasePath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE NpcTraitReasons
            SET IsActive = 0,
                UpdatedRealAt = CURRENT_TIMESTAMP
            WHERE Id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id.Trim());
        cmd.ExecuteNonQuery();
    }

    public static IReadOnlyList<TraitReasonRecord> LoadActiveReasons(
        int npcId,
        string traitId,
        int maxRows = 12)
    {
        var result = new List<TraitReasonRecord>();

        using var conn = new SqliteConnection(
            $"Data Source={ProjectEveDatabaseSetup.RelationshipDatabasePath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                Id,
                NpcId,
                TraitId,
                TargetCharacterId,
                ReasonType,
                Reason,
                Impact,
                SourceType,
                SourceEventId,
                SourceMemoryId,
                Confidence,
                GameTime,
                UpdatedRealAt
            FROM NpcTraitReasons
            WHERE NpcId = $npcId
              AND TraitId = $traitId
              AND IsActive <> 0
            ORDER BY ABS(Impact) DESC, UpdatedRealAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$traitId", traitId);
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(maxRows, 1, 100));

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new TraitReasonRecord
            {
                Id = reader.GetString(0),
                NpcId = reader.GetInt32(1),
                TraitId = reader.GetString(2),
                TargetCharacterId = reader.GetInt32(3),
                ReasonType = reader.GetString(4),
                Reason = reader.GetString(5),
                Impact = Convert.ToSingle(reader.GetValue(6)),
                SourceType = reader.GetString(7),
                SourceEventId = reader.GetString(8),
                SourceMemoryId = reader.GetString(9),
                Confidence = reader.GetInt32(10),
                GameTime = reader.GetString(11),
                UpdatedRealAt = reader.GetString(12)
            });
        }

        return result;
    }

    public sealed class TraitReasonRecord
    {
        public string Id { get; init; } = "";
        public int NpcId { get; init; }
        public string TraitId { get; init; } = "";
        public int TargetCharacterId { get; init; } = -1;
        public string ReasonType { get; init; } = "";
        public string Reason { get; init; } = "";
        public float Impact { get; init; }
        public string SourceType { get; init; } = "";
        public string SourceEventId { get; init; } = "";
        public string SourceMemoryId { get; init; } = "";
        public int Confidence { get; init; } = 100;
        public string GameTime { get; init; } = "";
        public string UpdatedRealAt { get; init; } = "";
    }

    private static void Upsert(
        SqliteConnection conn,
        SqliteTransaction tx,
        int npcId,
        string traitId,
        float value,
        bool controlOnly)
    {
        int rounded = Math.Clamp((int)Math.Round(value), 0, 100);
        string rowId = $"{npcId}_{traitId}";

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO NpcTraitValues
            (
                Id, NpcId, MainGroup, SubGroup, SubSubGroup,
                TraitId, TraitName, IsEnabled,
                StartingValue, SetPointValue, CurrentValue,
                ExpressionStyle, Notes, UpdatedRealAt
            )
            VALUES
            (
                $rowId, $npcId, $main, $sub, '',
                $traitId, $traitName, 1,
                $starting, $starting, $current,
                '', '', CURRENT_TIMESTAMP
            )
            ON CONFLICT(Id) DO UPDATE SET
                MainGroup = excluded.MainGroup,
                SubGroup = excluded.SubGroup,
                TraitName = excluded.TraitName,
                IsEnabled = 1,
                CurrentValue = excluded.CurrentValue,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$rowId", rowId);
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$main", GuessMainGroup(traitId));
        cmd.Parameters.AddWithValue("$sub", GuessSubGroup(traitId));
        cmd.Parameters.AddWithValue("$traitId", traitId);
        cmd.Parameters.AddWithValue("$traitName", PrettyName(traitId));
        cmd.Parameters.AddWithValue("$starting", rounded);
        cmd.Parameters.AddWithValue("$current", rounded);
        cmd.ExecuteNonQuery();
    }

    private static string GuessMainGroup(string traitId)
    {
        string clean = traitId.ToLowerInvariant();

        if (clean.Contains("anger") || clean.Contains("anxiety") ||
            clean.Contains("hurt") || clean.Contains("fear"))
            return "Emotional";

        if (clean.Contains("trust") || clean.Contains("affection") ||
            clean.Contains("attraction") || clean.Contains("tension"))
            return "Relationship";

        if (clean.Contains("openness") || clean.Contains("guard") ||
            clean.Contains("hope") || clean.Contains("desire"))
            return "Personality";

        return "General";
    }

    private static string GuessSubGroup(string traitId)
    {
        string clean = traitId.ToLowerInvariant();

        if (clean.Contains("anger")) return "Anger";
        if (clean.Contains("anxiety")) return "Anxiety";
        if (clean.Contains("hurt")) return "Hurt";
        if (clean.Contains("trust")) return "Trust";
        if (clean.Contains("affection")) return "Affection";
        if (clean.Contains("guard")) return "Guard";

        return "";
    }

    private static string PrettyName(string traitId)
    {
        string value = traitId;

        if (value.StartsWith("trait.", StringComparison.OrdinalIgnoreCase))
            value = value["trait.".Length..];

        value = value.Replace("_", " ").Replace(".", " ").Trim();

        if (string.IsNullOrWhiteSpace(value))
            return traitId;

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}


public sealed class NpcSubSlowValueRecord
{
    public string ParentTraitId { get; init; } = "";
    public string SubTraitId { get; init; } = "";
    public string SubTraitName { get; init; } = "";
    public string ValueType { get; init; } = "";
    public string ValueText { get; init; } = "";
    public bool IsEnabled { get; init; }
}

