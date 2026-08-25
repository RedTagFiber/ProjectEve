using Microsoft.Data.Sqlite;
using ProjectEve.Data;

namespace ProjectEve.Characters.Social;

/// <summary>
/// Canonical persistence gateway for NPC social-posting behavior.
///
/// Current scores and last-action game times live in MAIN / NpcSocialBehavior.
/// This is behavioral propensity/state only; actual posts/comments belong to
/// their social/history systems.
/// </summary>
public static class NpcSocialBehaviorRepository
{
    public static NpcSocialBehaviorState Load(int npcId)
    {
        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                BookPostScore,
                GramPostScore,
                CommentScore,
                TrollScore,
                LastBookPostGameTime,
                LastGramPostGameTime,
                LastCommentGameTime,
                LastTrollActionGameTime
            FROM NpcSocialBehavior
            WHERE NpcId = $npcId
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$npcId", npcId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return new NpcSocialBehaviorState { NpcId = npcId };

        return new NpcSocialBehaviorState
        {
            NpcId = npcId,
            BookPostScore = Clamp(reader.GetInt32(0)),
            GramPostScore = Clamp(reader.GetInt32(1)),
            CommentScore = Clamp(reader.GetInt32(2)),
            TrollScore = Clamp(reader.GetInt32(3)),
            LastBookPostGameTime = reader.GetString(4),
            LastGramPostGameTime = reader.GetString(5),
            LastCommentGameTime = reader.GetString(6),
            LastTrollActionGameTime = reader.GetString(7)
        };
    }

    public static void SaveScores(
        int npcId,
        int bookPostScore,
        int gramPostScore,
        int commentScore,
        int trollScore)
    {
        ProjectEveDatabaseSetup.EnsureAll();

        using var conn = ProjectEveDatabaseConnections.OpenMain();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO NpcSocialBehavior
            (
                NpcId,
                BookPostScore,
                GramPostScore,
                CommentScore,
                TrollScore,
                UpdatedRealAt
            )
            VALUES
            (
                $npcId,
                $book,
                $gram,
                $comment,
                $troll,
                CURRENT_TIMESTAMP
            )
            ON CONFLICT(NpcId) DO UPDATE SET
                BookPostScore = excluded.BookPostScore,
                GramPostScore = excluded.GramPostScore,
                CommentScore = excluded.CommentScore,
                TrollScore = excluded.TrollScore,
                UpdatedRealAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.Parameters.AddWithValue("$book", Clamp(bookPostScore));
        cmd.Parameters.AddWithValue("$gram", Clamp(gramPostScore));
        cmd.Parameters.AddWithValue("$comment", Clamp(commentScore));
        cmd.Parameters.AddWithValue("$troll", Clamp(trollScore));
        cmd.ExecuteNonQuery();
    }

    public static void MarkAction(
        int npcId,
        SocialActionKind action,
        DateTime gameTime)
    {
        ProjectEveDatabaseSetup.EnsureAll();

        string column = action switch
        {
            SocialActionKind.BookPost => "LastBookPostGameTime",
            SocialActionKind.GramPost => "LastGramPostGameTime",
            SocialActionKind.Comment => "LastCommentGameTime",
            SocialActionKind.TrollAction => "LastTrollActionGameTime",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        using var conn = ProjectEveDatabaseConnections.OpenMain();

        // Ensure the canonical row exists before updating the selected timestamp.
        using (var ensure = conn.CreateCommand())
        {
            ensure.CommandText = """
                INSERT OR IGNORE INTO NpcSocialBehavior (NpcId)
                VALUES ($npcId);
                """;
            ensure.Parameters.AddWithValue("$npcId", npcId);
            ensure.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"UPDATE NpcSocialBehavior " +
            $"SET {column} = $gameTime, UpdatedRealAt = CURRENT_TIMESTAMP " +
            "WHERE NpcId = $npcId;";
        cmd.Parameters.AddWithValue("$gameTime", gameTime.ToString("o"));
        cmd.Parameters.AddWithValue("$npcId", npcId);
        cmd.ExecuteNonQuery();
    }

    public static bool IsCooldownReady(
        string lastGameTime,
        DateTime gameTime,
        TimeSpan cooldown)
    {
        if (string.IsNullOrWhiteSpace(lastGameTime))
            return true;

        if (!DateTime.TryParse(lastGameTime, out var last))
            return true;

        return gameTime >= last + cooldown;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}

public sealed class NpcSocialBehaviorState
{
    public int NpcId { get; init; }

    public int BookPostScore { get; init; } = 50;
    public int GramPostScore { get; init; } = 50;
    public int CommentScore { get; init; } = 50;
    public int TrollScore { get; init; } = 50;

    public string LastBookPostGameTime { get; init; } = "";
    public string LastGramPostGameTime { get; init; } = "";
    public string LastCommentGameTime { get; init; } = "";
    public string LastTrollActionGameTime { get; init; } = "";
}

public enum SocialActionKind
{
    BookPost,
    GramPost,
    Comment,
    TrollAction
}
