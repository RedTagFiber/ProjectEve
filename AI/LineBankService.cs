using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectEve.AI;

/// <summary>
/// Texting-first line catalog. Pull bank hit before LLM; store live lines after.
/// DB: D:\ProjectEve\EveData\db\linebank.db
/// </summary>
public sealed class LineBankService
{
    private readonly string _dbPath;
    private static readonly Regex TagPiece = new(
        @"trait\.(?<id>[a-zA-Z0-9_]+)\s*(?<op>[+\-=])\s*(?<delta>\d+)\s*@\s*(?<inten>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LineBankService(string? dbPath = null)
    {
        _dbPath = dbPath
            ?? Path.Combine(@"D:\ProjectEve\EveData\db", "linebank.db");
    }

    public bool DbExists => File.Exists(_dbPath);

    public static (List<string> Traits, int Intensity) ParseTags(string? thought)
    {
        var traits = new List<string>();
        int intensity = 5;
        if (string.IsNullOrWhiteSpace(thought))
            return (traits, intensity);

        var idx = thought.IndexOf("TAGS:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return (traits, intensity);

        var line = thought[idx..];
        var nl = line.IndexOfAny(new[] { '\r', '\n' });
        if (nl > 0) line = line[..nl];

        if (line.Contains("none", StringComparison.OrdinalIgnoreCase)
            && !TagPiece.IsMatch(line))
            return (traits, intensity);

        int maxInten = 0;
        foreach (Match m in TagPiece.Matches(line))
        {
            var id = "trait." + m.Groups["id"].Value.ToLowerInvariant();
            traits.Add(id);
            if (int.TryParse(m.Groups["inten"].Value, out var inten))
                maxInten = Math.Max(maxInten, inten);
        }

        if (maxInten > 0)
            intensity = Math.Clamp(maxInten, 1, 10);

        return (traits.Distinct().ToList(), intensity);
    }

    public static string? GuessIntent(string? playerMessage)
    {
        var s = (playerMessage ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (Contains(s, "i love you", "love you", "love u"))
            return "intent.love.you";
        if (Contains(s, "miss you", "missed you", "i miss"))
            return "intent.greet.missedyou";
        if (Contains(s, "good morning", "morning babe", "gm "))
            return "intent.goodmorning";
        if (Contains(s, "good night", "goodnight", "gn ", "sleep well"))
            return "intent.goodnight";
        if (Contains(s, "where are you", "where r you", "you at"))
            return "intent.where.are.you";
        if (Contains(s, "come over", "come here", "come thru"))
            return "intent.come.over";
        if (Contains(s, "tonight", "plans", "hang out", "what are we doing"))
            return "intent.plans.tonight";
        if (Contains(s, "kiss", "make out"))
            return "intent.intimacy.kiss";
        if (Contains(s, "truth or dare", "truthordare"))
            return "intent.game.truthordare";
        if (Contains(s, "are we", "what are we", "boyfriend", "girlfriend", "official"))
            return "intent.relationship.status";
        if (Contains(s, "hello", "hey", "hi ", "hiya", "what's up", "whats up", "wyd"))
            return "intent.greet.hello";
        if (Contains(s, "bye", "later", "gotta go", "talk later"))
            return "intent.greet.bye";
        if (Contains(s, "beautiful", "sexy", "hot", "pretty", "handsome", "you look"))
            return "intent.compliment.give";
        if (Contains(s, "fuck me", "want you", "so wet", "hard for", "dirty"))
            return "intent.sex.dirtytalk.f2m";
        if (Contains(s, "what do you mean", "say that again", "huh", "wait what", "???"))
            return "intent.meta.clarify";
        if (Contains(s, "how are you", "how you doing", "how r you", "you good", "you okay", "you alright"))
            return "intent.status.howareyou";
        if (Contains(s, "what have you been up to", "what you been up to", "what's new", "whats new", "wyd", "what you doing", "what are you doing", "been up to"))
            return "intent.status.uptono";
        // ——— high-value emotional / relationship ———
        if (Contains(s, "i love you", "love you", "love u"))
            return "intent.love.you";
        if (Contains(s, "i miss you", "miss you", "missed you", "i miss"))
            return "intent.greet.missedyou";
        if (Contains(s, "do you still love me", "still love me", "you still love"))
            return "intent.reassurance.love";
        if (Contains(s, "are you mad", "you mad", "are you pissed", "you upset", "you okay with me"))
            return "intent.conflict.check";
        if (Contains(s, "i'm sorry", "im sorry", "i was wrong", "my bad"))
            return "intent.apology.give";
        if (Contains(s, "thank you", "thanks", "thx", "appreciate it"))
            return "intent.thanks";

        // ——— status / check-in (openers) ———
        if (Contains(s, "how are you", "how you doing", "how r you", "you good", "you okay", "you alright", "you doing ok"))
            return "intent.status.howareyou";
        if (Contains(s, "what have you been up to", "what you been up to", "what's new", "whats new",
                         "what you doing", "what are you doing", "been up to", "wyd"))
            return "intent.status.uptono";
        if (Contains(s, "you busy", "are you busy", "you free", "got a minute", "you around"))
            return "intent.status.busy";
        if (Contains(s, "i'm home", "im home", "just got home", "made it home"))
            return "intent.status.home";
        if (Contains(s, "i'm tired", "im tired", "so tired", "exhausted", "drained"))
            return "intent.status.tired";
        if (Contains(s, "i'm bored", "im bored", "so bored", "nothing to do"))
            return "intent.status.bored";

        // ——— time of day ———
        if (Contains(s, "good morning", "morning babe", "gm ", "morning beautiful"))
            return "intent.goodmorning";
        if (Contains(s, "good night", "goodnight", "gn ", "sleep well", "night babe"))
            return "intent.goodnight";

        // ——— plans / logistics ———
        if (Contains(s, "where are you", "where r you", "you at", "where you at"))
            return "intent.where.are.you";
        if (Contains(s, "come over", "come here", "come thru", "come by"))
            return "intent.come.over";
        if (Contains(s, "tonight", "plans", "hang out", "what are we doing", "you free later"))
            return "intent.plans.tonight";
        if (Contains(s, "call me", "can you call", "facetime", "video call"))
            return "intent.call.me";
        if (Contains(s, "on my way", "leaving now", "headed over", "be there soon"))
            return "intent.travel.otw";

        // ——— intimacy / flirty ———
        if (Contains(s, "kiss", "make out", "kiss me"))
            return "intent.intimacy.kiss";
        if (Contains(s, "what are you wearing", "what you wearing", "you naked"))
            return "intent.flirty.wearing";
        if (Contains(s, "thinking about you", "can't stop thinking", "cant stop thinking"))
            return "intent.affection.thinking";
        if (Contains(s, "i need you", "need you", "want you here"))
            return "intent.affection.need";
        if (Contains(s, "fuck me", "want you", "so wet", "hard for", "dirty", "horny"))
            return "intent.sex.dirtytalk.f2m";

        // ——— social / reactions ———
        if (Contains(s, "lol", "haha", "lmao", "😂", "🤣", "that's funny", "thats funny"))
            return "intent.react.laugh";
        if (Contains(s, "beautiful", "sexy", "hot", "pretty", "handsome", "you look"))
            return "intent.compliment.give";
        if (Contains(s, "send a pic", "send pic", "picture", "selfie", "show me"))
            return "intent.request.pic";

        // ——— relationship / games ———
        if (Contains(s, "are we", "what are we", "boyfriend", "girlfriend", "official", "dating"))
            return "intent.relationship.status";
        if (Contains(s, "truth or dare", "truthordare"))
            return "intent.game.truthordare";
        if (Contains(s, "who is that", "who's that", "who was that", "who you with"))
            return "intent.jealousy.who";

        // ——— meta / repair ———
        if (Contains(s, "what do you mean", "say that again", "huh", "wait what", "???", "come again"))
            return "intent.meta.clarify";

        // ——— broad greets (keep last) ———
        if (Contains(s, "hello", "hey", "hi ", "hiya", "what's up", "whats up", "sup"))
            return "intent.greet.hello";
        if (Contains(s, "bye", "later", "gotta go", "talk later", "ttyl", "see you"))
            return "intent.greet.bye";





        return null;
    }

    static bool Contains(string s, params string[] words)
    {
        foreach (var w in words)
            if (s.Contains(w, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public LineHit? TryPull(
        string speaker,
        string? intentId,
        IEnumerable<string>? hotTraits,
        int intensity = 5,
        string channel = "text",
        string maxRating = "plus18",
        int? excludeRowId = null)
    {
        if (!DbExists) return null;
        speaker = NormSpeaker(speaker);

        using var conn = Open();
        if (!string.IsNullOrWhiteSpace(intentId))
        {
            var hit = PullByIntent(conn, speaker, intentId!, intensity, channel, excludeRowId);
            if (hit != null) return hit;
        }

        var traits = (hotTraits ?? Array.Empty<string>())
            .Select(NormTrait)
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();
        if (traits.Count == 0) return null;

        var intent = BestIntentForTraits(conn, traits);
        if (intent == null) return null;
        return PullByIntent(conn, speaker, intent, intensity, channel, excludeRowId);
    }

    public ComboHit? TryPullCombo(
        string speaker,
        string? intentId,
        int intensity = 5,
        string channel = "text",
        int preferBubbles = 2)
    {
        if (!DbExists) return null;
        preferBubbles = Math.Clamp(preferBubbles, 2, 5);
        speaker = NormSpeaker(speaker);

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.combo_id, c.intent_id, c.bubble_count
            FROM line_combos c
            WHERE c.speaker = $sp
              AND c.active = 1
              AND c.quality != 'hide'
              AND c.channel IN ($ch, 'either', 'text')
              AND c.bubble_count BETWEEN 2 AND 5
              AND ($intent IS NULL OR c.intent_id = $intent)
              AND (c.intensity_min IS NULL OR c.intensity_min <= $inten)
              AND (c.intensity_max IS NULL OR c.intensity_max >= $inten)
            ORDER BY
              CASE WHEN c.bubble_count = $pref THEN 0 ELSE 1 END,
              c.success_score DESC,
              c.use_count DESC,
              RANDOM()
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$sp", speaker);
        cmd.Parameters.AddWithValue("$ch", channel);
        cmd.Parameters.AddWithValue("$intent", (object?)intentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inten", intensity);
        cmd.Parameters.AddWithValue("$pref", preferBubbles);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var comboId = r.GetInt32(0);
        var iid = r.IsDBNull(1) ? null : r.GetString(1);
        r.Close();

        var texts = new List<string>();
        using var m = conn.CreateCommand();
        m.CommandText = """
            SELECT l.text FROM line_combo_members m
            JOIN lines l ON l.id = m.line_row_id
            WHERE m.combo_id = $c
            ORDER BY m.seq
            """;
        m.Parameters.AddWithValue("$c", comboId);
        using var mr = m.ExecuteReader();
        while (mr.Read())
            texts.Add(mr.GetString(0));

        if (texts.Count < 2) return null;
        BumpCombo(conn, comboId);
        return new ComboHit(comboId, iid, texts);
    }

    public int StoreLiveLine(
        string speaker,
        string intentId,
        string text,
        string? style = null,
        int? intensity = null,
        string channel = "text",
        string? lineKey = null,
        IReadOnlyDictionary<string, double>? traitWeights = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        if (text.StartsWith("(dialogue error", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (text.StartsWith("(brain error", StringComparison.OrdinalIgnoreCase))
            return 0;

        speaker = NormSpeaker(speaker);
        intentId = NormIntent(intentId);
        lineKey ??= $"live_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

        using var conn = Open();
        EnsureIntent(conn, intentId, channel);

        var norm = text.Trim().ToLowerInvariant();
        var hash = Hash(speaker, intentId, norm);

        using (var dupe = conn.CreateCommand())
        {
            dupe.CommandText = "SELECT id FROM lines WHERE text_hash = $h AND speaker = $sp LIMIT 1";
            dupe.Parameters.AddWithValue("$h", hash);
            dupe.Parameters.AddWithValue("$sp", speaker);
            var existing = dupe.ExecuteScalar();
            if (existing != null)
                return Convert.ToInt32(existing);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO lines (
                intent_id, line_key, speaker, channel, text, text_norm, style, intensity,
                source, text_hash, updated_at
            ) VALUES (
                $i, $k, $sp, $ch, $t, $tn, $st, $in, 'live', $h, datetime('now')
            )
            ON CONFLICT(speaker, intent_id, line_key) DO UPDATE SET
                text = excluded.text,
                text_norm = excluded.text_norm,
                style = COALESCE(excluded.style, lines.style),
                intensity = COALESCE(excluded.intensity, lines.intensity),
                updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("$i", intentId);
        cmd.Parameters.AddWithValue("$k", lineKey);
        cmd.Parameters.AddWithValue("$sp", speaker);
        cmd.Parameters.AddWithValue("$ch", channel);
        cmd.Parameters.AddWithValue("$t", text.Trim());
        cmd.Parameters.AddWithValue("$tn", norm);
        cmd.Parameters.AddWithValue("$st", (object?)style ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$in", (object?)intensity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.ExecuteNonQuery();

        long id;
        using (var idCmd = conn.CreateCommand())
        {
            idCmd.CommandText = "SELECT id FROM lines WHERE speaker=$sp AND intent_id=$i AND line_key=$k";
            idCmd.Parameters.AddWithValue("$sp", speaker);
            idCmd.Parameters.AddWithValue("$i", intentId);
            idCmd.Parameters.AddWithValue("$k", lineKey);
            id = Convert.ToInt64(idCmd.ExecuteScalar());
        }

        if (traitWeights != null)
        {
            foreach (var (t, w) in traitWeights)
            {
                using var tc = conn.CreateCommand();
                tc.CommandText = """
                    INSERT OR REPLACE INTO line_traits(line_row_id, trait_id, weight)
                    VALUES ($id, $t, $w)
                    """;
                tc.Parameters.AddWithValue("$id", id);
                tc.Parameters.AddWithValue("$t", NormTrait(t));
                tc.Parameters.AddWithValue("$w", w);
                tc.ExecuteNonQuery();
            }
        }

        using var ev = conn.CreateCommand();
        ev.CommandText = """
            INSERT INTO line_events(line_row_id, speaker, intent_id, channel, source_path)
            VALUES ($id, $sp, $i, $ch, 'live_store')
            """;
        ev.Parameters.AddWithValue("$id", id);
        ev.Parameters.AddWithValue("$sp", speaker);
        ev.Parameters.AddWithValue("$i", intentId);
        ev.Parameters.AddWithValue("$ch", channel);
        ev.ExecuteNonQuery();

        return (int)id;
    }

    public int StoreLiveCombo(
        string speaker,
        string intentId,
        IReadOnlyList<string> bubbles,
        string channel = "text")
    {
        if (bubbles.Count is < 2 or > 5)
            throw new ArgumentOutOfRangeException(nameof(bubbles), "Combo must be 2–5 bubbles.");

        speaker = NormSpeaker(speaker);
        intentId = NormIntent(intentId);
        using var conn = Open();
        EnsureIntent(conn, intentId, channel);

        var ids = new List<int>();
        var baseKey = $"live_{DateTime.UtcNow:yyyyMMddHHmmss}";
        for (var i = 0; i < bubbles.Count; i++)
        {
            var id = StoreLiveLine(speaker, intentId, bubbles[i], channel: channel,
                lineKey: $"{baseKey}_{i + 1}");
            if (id > 0) ids.Add(id);
        }

        if (ids.Count < 2) return 0;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO line_combos (speaker, intent_id, channel, bubble_count, source, combo_hash)
            VALUES ($sp, $i, $ch, $n, 'live', $h)
            """;
        cmd.Parameters.AddWithValue("$sp", speaker);
        cmd.Parameters.AddWithValue("$i", intentId);
        cmd.Parameters.AddWithValue("$ch", channel);
        cmd.Parameters.AddWithValue("$n", ids.Count);
        cmd.Parameters.AddWithValue("$h", Hash(speaker, intentId, string.Join("|", ids)));
        cmd.ExecuteNonQuery();

        long comboId;
        using (var idCmd = conn.CreateCommand())
        {
            idCmd.CommandText = "SELECT last_insert_rowid()";
            comboId = Convert.ToInt64(idCmd.ExecuteScalar());
        }

        for (var seq = 0; seq < ids.Count; seq++)
        {
            using var m = conn.CreateCommand();
            m.CommandText = """
                INSERT INTO line_combo_members(combo_id, seq, line_row_id)
                VALUES ($c, $s, $l)
                """;
            m.Parameters.AddWithValue("$c", comboId);
            m.Parameters.AddWithValue("$s", seq + 1);
            m.Parameters.AddWithValue("$l", ids[seq]);
            m.ExecuteNonQuery();
        }

        return (int)comboId;
    }

    public void MarkVoice(string speaker, string intentId, string lineKey, string wavPath)
    {
        if (!File.Exists(wavPath)) return;
        speaker = NormSpeaker(speaker);
        intentId = NormIntent(intentId);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (speaker == "eve2")
        {
            cmd.CommandText = """
                UPDATE lines SET wav_eve2=$w, baked_eve2=1, updated_at=datetime('now')
                WHERE speaker=$sp AND intent_id=$i AND line_key=$k
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE lines SET wav_adam=$w, baked_adam=1, updated_at=datetime('now')
                WHERE speaker=$sp AND intent_id=$i AND line_key=$k
                """;
        }
        cmd.Parameters.AddWithValue("$w", wavPath);
        cmd.Parameters.AddWithValue("$sp", speaker);
        cmd.Parameters.AddWithValue("$i", intentId);
        cmd.Parameters.AddWithValue("$k", lineKey);
        cmd.ExecuteNonQuery();
    }

    LineHit? PullByIntent(
        SqliteConnection conn,
        string speaker,
        string intentId,
        int intensity,
        string channel,
        int? excludeRowId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, intent_id, line_key, text, style, intensity,
                   wav_eve2, wav_adam, baked_eve2, baked_adam
            FROM lines
            WHERE speaker = $sp
              AND intent_id = $i
              AND active = 1
              AND quality != 'hide'
              AND ($exclude IS NULL OR id != $exclude)
              AND channel IN ($ch, 'either', 'text')
              AND (intensity IS NULL OR ABS(intensity - $inten) <= 3)
            ORDER BY success_score DESC, use_count DESC, RANDOM()
            LIMIT 8
            """;
        cmd.Parameters.AddWithValue("$sp", speaker);
        cmd.Parameters.AddWithValue("$i", NormIntent(intentId));
        cmd.Parameters.AddWithValue("$ch", channel);
        cmd.Parameters.AddWithValue("$inten", intensity);
        cmd.Parameters.AddWithValue("$exclude", (object?)excludeRowId ?? DBNull.Value);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var id = r.GetInt32(0);
        var hit = new LineHit(
            id,
            r.GetString(1),
            r.GetString(2),
            r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetInt32(5),
            WavFor(speaker, r));
        r.Close();
        BumpLine(conn, id);
        return hit;
    }

    static string? BestIntentForTraits(SqliteConnection conn, List<string> traits)
    {
        using var cmd = conn.CreateCommand();
        var placeholders = string.Join(",", traits.Select((_, i) => $"$t{i}"));
        cmd.CommandText = $"""
            SELECT it.intent_id, SUM(it.weight) AS score
            FROM intent_traits it
            JOIN intents i ON i.intent_id = it.intent_id
            WHERE it.trait_id IN ({placeholders})
              AND it.weight > 0
            GROUP BY it.intent_id
            ORDER BY score DESC
            LIMIT 1
            """;
        for (var i = 0; i < traits.Count; i++)
            cmd.Parameters.AddWithValue($"$t{i}", traits[i]);

        return cmd.ExecuteScalar()?.ToString();
    }

    static void BumpLine(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE lines SET use_count = use_count + 1, last_used_at = datetime('now')
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();

        using var ev = conn.CreateCommand();
        ev.CommandText = """
            INSERT INTO line_events(line_row_id, speaker, intent_id, channel, source_path)
            SELECT id, speaker, intent_id, channel, 'bank_hit' FROM lines WHERE id = $id
            """;
        ev.Parameters.AddWithValue("$id", id);
        ev.ExecuteNonQuery();
    }

    static void BumpCombo(SqliteConnection conn, int comboId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE line_combos SET use_count = use_count + 1, last_used_at = datetime('now')
            WHERE combo_id = $id
            """;
        cmd.Parameters.AddWithValue("$id", comboId);
        cmd.ExecuteNonQuery();
    }

    static void EnsureIntent(SqliteConnection conn, string intentId, string channel)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO intents(intent_id, pack_folder, channel, description, source_file)
            VALUES ($i, $p, $ch, 'live/auto', 'live')
            """;
        cmd.Parameters.AddWithValue("$i", intentId);
        cmd.Parameters.AddWithValue("$p", intentId.StartsWith("intent.") ? intentId[7..] : intentId);
        cmd.Parameters.AddWithValue("$ch", channel);
        cmd.ExecuteNonQuery();
    }

    SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var p = conn.CreateCommand();
        p.CommandText = "PRAGMA foreign_keys = ON";
        p.ExecuteNonQuery();
        return conn;
    }

    static string? WavFor(string speaker, SqliteDataReader r)
    {
        if (speaker == "eve2" && !r.IsDBNull(8) && r.GetInt32(8) == 1 && !r.IsDBNull(6))
            return r.GetString(6);
        if (speaker == "adam" && !r.IsDBNull(9) && r.GetInt32(9) == 1 && !r.IsDBNull(7))
            return r.GetString(7);
        return null;
    }

    static string NormSpeaker(string s) =>
        string.IsNullOrWhiteSpace(s) ? "eve2" : s.Trim().ToLowerInvariant();

    static string NormIntent(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "intent.meta.nothing";
        return s.StartsWith("intent.") ? s : "intent." + s;
    }

    static string NormTrait(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return s;
        return s.StartsWith("trait.") ? s : "trait." + s;
    }

    static string Hash(string a, string b, string c)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{a}|{b}|{c}"));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..24];
    }
}

public sealed record LineHit(
    int RowId,
    string IntentId,
    string LineKey,
    string Text,
    string? Style,
    int? Intensity,
    string? WavPath);

public sealed record ComboHit(
    int ComboId,
    string? IntentId,
    IReadOnlyList<string> Texts);
