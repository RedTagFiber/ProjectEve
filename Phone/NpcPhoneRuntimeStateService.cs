using System;
using System.IO;

using Microsoft.Data.Sqlite;

namespace ProjectEve.Phone;

/// <summary>
/// Hidden bridge from the living-world simulation into phone behavior.
///
/// Other ProjectEve world systems may update this when they KNOW an NPC is:
/// sleeping, driving, in an emergency, in a meeting/customer-facing moment,
/// working, or temporarily has poor phone access.
///
/// The scheduler prefers this explicit state over heuristics.
/// PhoneOS must not display these fields directly.
/// </summary>
public static class NpcPhoneRuntimeStateService
{
    private static string DbPath =>
        Environment.GetEnvironmentVariable("EVE_DB_PATH")
        ?? Path.Combine(
            AppContext.BaseDirectory,
            "Data",
            "project_eve.db");

    private static string ConnStr =>
        "Data Source=" + DbPath;

    public static void Update(
        NpcPhoneRuntimeState state)
    {
        if (state == null || state.NpcId <= 0)
            return;

        EnsureSchema();

        using var conn =
            new SqliteConnection(ConnStr);

        conn.Open();

        using var cmd =
            conn.CreateCommand();

        cmd.CommandText = """
            INSERT INTO NpcPhoneRuntimeState
            (NpcId,IsSleeping,IsDriving,IsEmergency,
             IsInMeeting,IsWorking,PhoneAccess,Workload,
             Activity,UpdatedUtc,ValidUntilUtc)
            VALUES
            ($npc,$sleep,$drive,$emergency,
             $meeting,$working,$access,$workload,
             $activity,$updated,$valid)
            ON CONFLICT(NpcId) DO UPDATE SET
                IsSleeping=excluded.IsSleeping,
                IsDriving=excluded.IsDriving,
                IsEmergency=excluded.IsEmergency,
                IsInMeeting=excluded.IsInMeeting,
                IsWorking=excluded.IsWorking,
                PhoneAccess=excluded.PhoneAccess,
                Workload=excluded.Workload,
                Activity=excluded.Activity,
                UpdatedUtc=excluded.UpdatedUtc,
                ValidUntilUtc=excluded.ValidUntilUtc;
            """;

        cmd.Parameters.AddWithValue(
            "$npc",
            state.NpcId);

        cmd.Parameters.AddWithValue(
            "$sleep",
            DbBool(state.IsSleeping));

        cmd.Parameters.AddWithValue(
            "$drive",
            DbBool(state.IsDriving));

        cmd.Parameters.AddWithValue(
            "$emergency",
            DbBool(state.IsEmergency));

        cmd.Parameters.AddWithValue(
            "$meeting",
            DbBool(state.IsInMeeting));

        cmd.Parameters.AddWithValue(
            "$working",
            DbBool(state.IsWorking));

        cmd.Parameters.AddWithValue(
            "$access",
            DbInt(state.PhoneAccess));

        cmd.Parameters.AddWithValue(
            "$workload",
            DbInt(state.Workload));

        cmd.Parameters.AddWithValue(
            "$activity",
            state.Activity ?? "");

        cmd.Parameters.AddWithValue(
            "$updated",
            DateTime.UtcNow.ToString("O"));

        cmd.Parameters.AddWithValue(
            "$valid",
            (object?)state.ValidUntilUtc?.ToString("O")
            ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    public static NpcPhoneRuntimeState? GetActive(
        int npcId,
        DateTime utcNow)
    {
        if (npcId <= 0)
            return null;

        EnsureSchema();

        using var conn =
            new SqliteConnection(ConnStr);

        conn.Open();

        using var cmd =
            conn.CreateCommand();

        cmd.CommandText = """
            SELECT NpcId,
                   IsSleeping,IsDriving,IsEmergency,
                   IsInMeeting,IsWorking,
                   PhoneAccess,Workload,
                   Activity,UpdatedUtc,ValidUntilUtc
            FROM NpcPhoneRuntimeState
            WHERE NpcId=$npc
            LIMIT 1;
            """;

        cmd.Parameters.AddWithValue(
            "$npc",
            npcId);

        using var r =
            cmd.ExecuteReader();

        if (!r.Read())
            return null;

        DateTime? validUntil =
            r.IsDBNull(10)
                ? null
                : ParseUtc(r.GetString(10));

        if (validUntil.HasValue &&
            validUntil.Value < utcNow)
            return null;

        return new NpcPhoneRuntimeState
        {
            NpcId = r.GetInt32(0),
            IsSleeping = ReadBool(r, 1),
            IsDriving = ReadBool(r, 2),
            IsEmergency = ReadBool(r, 3),
            IsInMeeting = ReadBool(r, 4),
            IsWorking = ReadBool(r, 5),
            PhoneAccess = ReadInt(r, 6),
            Workload = ReadInt(r, 7),
            Activity =
                r.IsDBNull(8)
                    ? ""
                    : r.GetString(8),
            UpdatedUtc =
                r.IsDBNull(9)
                    ? null
                    : ParseUtc(r.GetString(9)),
            ValidUntilUtc =
                validUntil
        };
    }

    public static void Clear(int npcId)
    {
        EnsureSchema();

        using var conn =
            new SqliteConnection(ConnStr);

        conn.Open();

        using var cmd =
            conn.CreateCommand();

        cmd.CommandText =
            "DELETE FROM NpcPhoneRuntimeState WHERE NpcId=$npc;";

        cmd.Parameters.AddWithValue(
            "$npc",
            npcId);

        cmd.ExecuteNonQuery();
    }

    public static void SetDriving(
        int npcId,
        bool driving,
        TimeSpan? duration = null)
    {
        var current =
            GetActive(
                npcId,
                DateTime.UtcNow)
            ?? new NpcPhoneRuntimeState
            {
                NpcId = npcId
            };

        current.IsDriving = driving;
        current.ValidUntilUtc =
            duration.HasValue
                ? DateTime.UtcNow + duration.Value
                : current.ValidUntilUtc;

        Update(current);
    }

    public static void SetSleeping(
        int npcId,
        bool sleeping,
        TimeSpan? duration = null)
    {
        var current =
            GetActive(
                npcId,
                DateTime.UtcNow)
            ?? new NpcPhoneRuntimeState
            {
                NpcId = npcId
            };

        current.IsSleeping = sleeping;
        current.ValidUntilUtc =
            duration.HasValue
                ? DateTime.UtcNow + duration.Value
                : current.ValidUntilUtc;

        Update(current);
    }

    public static void SetEmergency(
        int npcId,
        bool emergency,
        TimeSpan? duration = null)
    {
        var current =
            GetActive(
                npcId,
                DateTime.UtcNow)
            ?? new NpcPhoneRuntimeState
            {
                NpcId = npcId
            };

        current.IsEmergency = emergency;
        current.ValidUntilUtc =
            duration.HasValue
                ? DateTime.UtcNow + duration.Value
                : current.ValidUntilUtc;

        Update(current);
    }

    private static void EnsureSchema()
    {
        var dir =
            Path.GetDirectoryName(DbPath);

        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        using var conn =
            new SqliteConnection(ConnStr);

        conn.Open();

        using var cmd =
            conn.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS NpcPhoneRuntimeState(
                NpcId INTEGER PRIMARY KEY,
                IsSleeping INTEGER NULL,
                IsDriving INTEGER NULL,
                IsEmergency INTEGER NULL,
                IsInMeeting INTEGER NULL,
                IsWorking INTEGER NULL,
                PhoneAccess INTEGER NULL,
                Workload INTEGER NULL,
                Activity TEXT NOT NULL DEFAULT '',
                UpdatedUtc TEXT NOT NULL,
                ValidUntilUtc TEXT NULL
            );
            """;

        cmd.ExecuteNonQuery();
    }

    private static object DbBool(bool? value)
        => value.HasValue
            ? (value.Value ? 1 : 0)
            : DBNull.Value;

    private static object DbInt(int? value)
        => value.HasValue
            ? Math.Clamp(value.Value, 0, 100)
            : DBNull.Value;

    private static bool? ReadBool(
        SqliteDataReader r,
        int index)
    {
        if (r.IsDBNull(index))
            return null;

        return r.GetInt32(index) != 0;
    }

    private static int? ReadInt(
        SqliteDataReader r,
        int index)
    {
        if (r.IsDBNull(index))
            return null;

        return Math.Clamp(
            r.GetInt32(index),
            0,
            100);
    }

    private static DateTime? ParseUtc(
        string value)
        => DateTime.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var dt)
            ? dt
            : null;
}

public sealed class NpcPhoneRuntimeState
{
    public int NpcId { get; set; }

    public bool? IsSleeping { get; set; }
    public bool? IsDriving { get; set; }
    public bool? IsEmergency { get; set; }
    public bool? IsInMeeting { get; set; }
    public bool? IsWorking { get; set; }

    /// <summary>0=no access, 100=phone readily available.</summary>
    public int? PhoneAccess { get; set; }

    /// <summary>0=idle, 100=fully occupied.</summary>
    public int? Workload { get; set; }

    public string Activity { get; set; } = "";

    public DateTime? UpdatedUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
}
