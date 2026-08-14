using ProjectEve.AI.Training;
using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Characters.NPCs;
using ProjectEve.Characters.Traits.Core;
using ProjectEve.Core.Chat;
using ProjectEve.Traits;
using ProjectEve.Traits.Matrix;
using ProjectEve.Relationships;
using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// CharacterFactory lives here in your tree — if build fails, see note at bottom
using ProjectEve.Characters.Characters;
using ProjectEve.AI;
using ProjectEve.AI.Brain;

class Program
{
    static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");
    static string DbPath => Path.Combine(DataDir, "project_eve.db");

    static readonly string TraitJsonRoot =
        @"D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean\Characters\Traits\TraitJson";

    static readonly string FastTraitDir =
        @"D:\ProjectEve\EveData\Traits\Fast\Parents";

    static readonly string TrainingOutDir =
        @"D:\ProjectEve\EveData\training";

    static readonly string LineBankDb =
        @"D:\ProjectEve\EveData\db\linebank.db";

    static readonly string LineBankTool =
        @"D:\ProjectEve\EveData\db\linebank_tool.py";

    static readonly string IntentSeedDir =
        @"D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean\AI\Training\Packs\LineBank\intents";

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0].Equals("train", StringComparison.OrdinalIgnoreCase))
        {
            RunDatasetBuild();
            return;
        }

        if (args.Length > 0 && args[0].Equals("build-thought-data", StringComparison.OrdinalIgnoreCase))
        {
            RunBuildThoughtData(args);
            return;
        }

        if (args.Length > 0 && args[0].Equals("bank-import", StringComparison.OrdinalIgnoreCase))
        {
            RunLineBankImport();
            return;
        }

        bool fresh = args.Any(a => a.Equals("--fresh", StringComparison.OrdinalIgnoreCase));
        RunChat(freshDb: fresh);
    }

    static void RunLineBankImport()
    {
        Console.WriteLine("LineBank import…");
        Console.WriteLine("  tool : " + LineBankTool);
        Console.WriteLine("  db   : " + LineBankDb);
        Console.WriteLine("  json : " + IntentSeedDir);

        if (!File.Exists(LineBankTool))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("linebank_tool.py not found — skip import.");
            Console.ResetColor();
            return;
        }

        if (!Directory.Exists(IntentSeedDir))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Intent seed folder missing — skip import.");
            Console.ResetColor();
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments =
                    "\"" + LineBankTool + "\" " +
                    "--db \"" + LineBankDb + "\" " +
                    "--intent-dir \"" + IntentSeedDir + "\" " +
                    "import-json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                Console.WriteLine("Import error: process failed to start.");
                return;
            }

            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(90_000);

            if (!string.IsNullOrWhiteSpace(stdout))
                Console.WriteLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(stderr.TrimEnd());
                Console.ResetColor();
            }

            Console.ForegroundColor = p.ExitCode == 0 ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(p.ExitCode == 0
                ? "LineBank DB updated from JSON packs."
                : "Import failed (exit " + p.ExitCode + ").");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Import error: " + ex.Message);
            Console.ResetColor();
        }
    }

    static void RunBuildThoughtData(string[] args)
    {
        Console.Title = "Thought Dataset Builder";

        string fastDir = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
            ? args[1]
            : FastTraitDir;

        string outPath = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2])
            ? args[2]
            : Path.Combine(TrainingOutDir, "thought_heat_v1.jsonl");

        int minRows = 400;
        if (args.Length > 3 && int.TryParse(args[3], out int n) && n > 0)
            minRows = n;

        Console.WriteLine("Building Thought heat jsonl...");
        Console.WriteLine("  Fast dir : " + fastDir);
        Console.WriteLine("  Out      : " + outPath);

        if (!Directory.Exists(fastDir))
        {
            Console.WriteLine("Fast trait folder not found: " + fastDir);
            return;
        }

        try
        {
            var outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            string written = ThoughtDatasetBuilder.BuildHeatJsonl(fastDir, outPath, minRows);
            int lines = File.ReadAllLines(written).Length;
            long bytes = new FileInfo(written).Length;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Done. " + lines + " lines, " + bytes + " bytes");
            Console.WriteLine(written);
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Build failed: " + ex.Message);
        }
    }

    static void RunDatasetBuild()
    {
        Console.Title = "Eve Dataset Builder";
        try
        {
            EveLoraTrainer.BuildDatasetOnly();
            Console.WriteLine("Done.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Dataset build failed: " + ex.Message);
        }
    }

    static void RunChat(bool freshDb)
    {
        Console.Title = "Eve Chat — trait system test";

        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("EVE_DB_PATH", DbPath);
        Console.WriteLine("DB → " + DbPath);

        if (freshDb)
        {
            if (!ResetDatabaseFiles(DbPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FRESH FAILED — database was not deleted. Close the app and try again.");
                Console.ResetColor();
                return;
            }
        }

        var eve = BootWorld();

        string voiceOutDir = Path.Combine(DataDir, "voice");
        try { Directory.CreateDirectory(voiceOutDir); } catch { }

        TtsBakeService? tts = null;
        Step("TTS worker");
        try
        {
            tts = new TtsBakeService();
            tts.Start();
            Console.WriteLine("  started");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  skipped: " + ex.Message);
            tts = null;
        }

        PrintReady(eve);
        int lineNum = 0;

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[" + DateTime.Now.ToString("HH:mm") + "] You: ");
            Console.ResetColor();

            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            string lower = input.Trim().ToLowerInvariant();
            if (lower == "exit" || lower == "quit")
                break;

            if (lower == "sheet")
            {
                try { CharacterSheetPrinter.Print(eve); }
                catch (Exception ex) { Console.WriteLine(ex.Message); }
                continue;
            }
            if (lower == "traits") { PrintFastSnapshot(eve); continue; }
            if (lower == "reroll") { ForceRerollTraits(eve); continue; }
            if (lower == "matrix") { PrintMatrixSelf(eve); continue; }
            if (lower == "baseline")
            {
                Console.WriteLine("Current meters:");
                PrintMeters(eve, 0);
                continue;
            }
            if (lower == "bank-import" || lower == "import-lines")
            {
                RunLineBankImport();
                continue;
            }
            if (lower == "fresh" || lower == "reset-db" || lower == "--fresh")
            {
                Console.WriteLine("  Refreshing project_eve.db…");
                Console.WriteLine("  NOTE: current live state is intentionally NOT saved.");

                // Drop references to session-only state before clearing SQLite pools.
                try { eve.Brain?.ClearSessionLog(); } catch { }

                if (!ResetDatabaseFiles(DbPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  FRESH ABORTED — old DB is still in use.");
                    Console.WriteLine("  Nothing was reloaded. Exit ProjectEve and run once with --fresh.");
                    Console.ResetColor();
                    continue;
                }

                eve = BootWorld();
                PrintReady(eve);
                continue;
            }
            if (lower == "clearlog")
            {
                try { eve.Brain?.ClearSessionLog(); } catch { }
                Console.WriteLine("  Session log cleared.");
                continue;
            }
            if (lower == "npcgen" || lower == "gen-npc" || lower == "build-npc")
            {
                RunNpcGenInteractive();
                continue;
            }

            if (lower == "stress")
            {
                input = "(stress) I fucking hate you right now.";
                Console.WriteLine("  [inject] " + input);
            }
            else if (lower == "comfort")
            {
                input = "(comfort) I'm sorry. I love you. Come here.";
                Console.WriteLine("  [inject] " + input);
            }

            float beforeAng = Get(eve, "trait.anger");
            float beforeAff = Get(eve, "trait.affection");
            float beforeHurt = Get(eve, "trait.hurt");
            float beforeTrust = Get(eve, "trait.trust");
            float beforeGuard = Get(eve, "trait.guard");
            float beforeAnx = Get(eve, "trait.anxiety");
            float beforeTen = Get(eve, "trait.tension");
            float beforeOpen = Get(eve, "trait.openness");
            float beforeHope = Get(eve, "trait.hope");
            float beforeAttr = Get(eve, "trait.attraction");

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("  Eve is thinking...");
            Console.ResetColor();

            var sw = Stopwatch.StartNew();
            string reply;

            try
            {
                if (eve.Brain == null)
                {
                    eve.Brain = new Brain();
                    eve.Brain.Owner = eve;
                }
                eve.Brain.Think(input);
                PrintThoughtDebug(eve.Brain.LastThought);
                reply = eve.Brain.Reply(input);
            }
            catch (Exception ex)
            {
                reply = "(brain error: " + ex.Message + ")";
            }

            try { reply = EmotionSpeechEngine.ApplyEmotionTone(eve, reply, inPerson: false); }
            catch { }

            sw.Stop();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm") + "] Eve: " + reply);
            Console.ResetColor();

            if (eve.Brain != null)
                PrintReplyMeta(eve.Brain);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            PrintMeters(eve, sw.Elapsed.TotalSeconds);
            Console.WriteLine(
                "  Δ ang=" + (Get(eve, "trait.anger") - beforeAng).ToString("+0;-0;0") +
                " aff=" + (Get(eve, "trait.affection") - beforeAff).ToString("+0;-0;0") +
                " hurt=" + (Get(eve, "trait.hurt") - beforeHurt).ToString("+0;-0;0") +
                " trust=" + (Get(eve, "trait.trust") - beforeTrust).ToString("+0;-0;0"));
            Console.WriteLine(
                "  Δ guard=" + (Get(eve, "trait.guard") - beforeGuard).ToString("+0;-0;0") +
                " anx=" + (Get(eve, "trait.anxiety") - beforeAnx).ToString("+0;-0;0") +
                " ten=" + (Get(eve, "trait.tension") - beforeTen).ToString("+0;-0;0") +
                " open=" + (Get(eve, "trait.openness") - beforeOpen).ToString("+0;-0;0") +
                " hope=" + (Get(eve, "trait.hope") - beforeHope).ToString("+0;-0;0") +
                " attr=" + (Get(eve, "trait.attraction") - beforeAttr).ToString("+0;-0;0") + "\n");
            Console.ResetColor();

            if (tts != null && !string.IsNullOrWhiteSpace(reply))
            {
                lineNum++;
                try
                {
                    tts.Enqueue(reply, "af_heart",
                        Path.Combine(voiceOutDir, "eve_line_" + lineNum.ToString("000") + ".wav"));
                }
                catch { }
            }
        }

        try
        {
            if (eve.Traits != null)
                CharacterRepository.SaveTraits(eve.Id, eve.Traits);
        }
        catch { }

        Console.WriteLine("Goodbye.");
    }

    // =========================================================
    // NPC GEN
    // =========================================================
    static void RunNpcGenInteractive()
    {
        Console.WriteLine();
        Console.WriteLine("NPC GEN — factory rolls traits; you set count + contact web edge.");
        Console.Write("How many NPCs? ");
        string? nText = Console.ReadLine();
        if (!int.TryParse(nText, out int count) || count < 1 || count > 50)
        {
            Console.WriteLine("  Enter a number 1–50.");
            return;
        }

        Console.WriteLine("Contact / anchor:");
        Console.WriteLine("  1 Eve   2 Adam   3 Lisa   4 Edward   5 none");
        Console.Write("Choice: ");
        string? cText = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

        string contact;
        if (cText == "1" || cText == "eve") contact = "Eve";
        else if (cText == "2" || cText == "adam") contact = "Adam";
        else if (cText == "3" || cText == "lisa") contact = "Lisa";
        else if (cText == "4" || cText == "edward" || cText == "ed") contact = "Edward";
        else contact = "none";

        Console.Write("Lane hint (shop / art / crew / school / casual): ");
        string lane = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lane))
            lane = "casual";

        Console.WriteLine("  Building " + count + " NPC(s), contact=" + contact + ", lane=" + lane + "…");

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var npc = BuildConnectedNpc(contact, lane, i);
                EnsureNpcCore(npc);
                EnsureNpcTraits(npc);

                // 1) Characters FIRST
                try
                {
                    SaveNpcIdentityStub(npc);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  identity: " + ex.Message);
                    continue;
                }

                // 2) memory AFTER identity (FK safe)
                try
                {
                    npc.Remember(
                        "Connected in town to " + contact + " (lane " + lane + "). Relationship is real but not twin-deep.",
                        "Social",
                        4);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  memory: " + ex.Message);
                }

                // 3) state
                try
                {
                    CharacterRepository.SaveCharacterState(npc);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  state: " + ex.Message);
                }

                // 4) edge
                try
                {
                    SaveRelationshipEdge(npc, contact);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  edge: " + ex.Message);
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(
                    "  [" + npc.Id + "] " + npc.Name + " | " + npc.Age + " | " +
                    npc.Occupation + " | tier " + npc.Tier + " | → " + contact);
                Console.ResetColor();
                made++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  fail: " + ex.Message);
            }
        }

        Console.WriteLine("Done. " + made + "/" + count + " created.");
    }

    static SimCharacter BuildConnectedNpc(string contact, string lane, int index)
    {
        var rng = new Random(Environment.TickCount ^ (index * 397) ^ contact.GetHashCode());

        string[] first =
        {
            "Alex", "Jordan", "Sam", "Casey", "Riley", "Taylor", "Morgan", "Quinn",
            "Avery", "Drew", "Jamie", "Cameron", "Parker", "Reese", "Skyler", "Tessa",
            "Maya", "Nadine", "Bree", "Chris", "Olivia", "Hannah", "Derek"
        };
        string[] last =
        {
            "Miller", "Brooks", "Cole", "Hale", "Lang", "Quinn", "Park", "Shaw",
            "Grubb", "Rivera", "Molnar", "Nash", "Bell", "Crowe", "Diaz", "Walsh"
        };

        string name = first[rng.Next(first.Length)] + " " + last[rng.Next(last.Length)];
        int age = rng.Next(22, 36);
        string gender = rng.Next(2) == 0 ? "Female" : "Male";

        string occupation = "Local worker";
        if (lane == "shop") occupation = "Barista";
        else if (lane == "crew") occupation = "Firefighter";
        else if (lane == "art") occupation = "Artist";
        else if (lane == "school") occupation = "Office worker";

        // Prefer factory when available; else plain SimCharacter
        SimCharacter npc;
        try
        {
            npc = CharacterFactory.Create(
                name, age, gender,
                "Bellefontaine / Sidney, Ohio area",
                occupation);
        }
        catch
        {
            npc = new SimCharacter(name, age)
            {
                Gender = gender,
                Location = "Bellefontaine / Sidney, Ohio area",
                Occupation = occupation
            };
        }

        npc.Id = 1000 + Math.Abs((name + index + DateTime.Now.Millisecond).GetHashCode() % 9000);
        if (npc.Id < 1000) npc.Id += 1000;

        npc.Tier = contact == "none" ? 4 : 3;
        npc.Hometown = "Bellefontaine, OH";
        npc.HomeAddress = "in town";
        npc.Goal = "Keep life steady in a small town";
        npc.Need = "People who feel familiar";
        npc.Fear = "Being invisible or used";
        npc.Want = "A place that makes sense";
        npc.PersonalityContext =
            "Generated contact NPC. Lane=" + lane + ". Primary web contact=" + contact + ". " +
            "Not vault-level with the Sinclairs unless play earns it.";

        if (!string.Equals(contact, "none", StringComparison.OrdinalIgnoreCase))
        {
            int strength = 45;
            if (lane == "shop") strength = 60;
            else if (lane == "crew") strength = 65;
            else if (lane == "art") strength = 55;
            else if (lane == "school") strength = 58;

            npc.Relationships.Add(new Relationship
            {
                TargetName = contact,
                Trust = strength,
                Respect = Math.Max(0, strength - 5),
                Affection = Math.Max(0, strength - 10),
                Attraction = 0,
                Tension = 15
            });
        }

        if (npc.Job == null)
            npc.Job = new ProjectEve.Money.JobProfile();

        npc.Job.JobName = occupation;
        npc.Job.Employer = lane == "shop" ? "Sinclair Coffee"
            : lane == "crew" ? "Local fire department"
            : "Local business";
        npc.Job.BossName = lane == "shop" ? "Eve Sinclair" : "";
        npc.Job.HourlyRate = 14m + rng.Next(0, 8);
        npc.Job.WeeklyHours = 30;

        if (npc.Money == null)
            npc.Money = new ProjectEve.Money.MoneyProfile();

        npc.Money.Cash = rng.Next(40, 120);
        npc.Money.Bank = rng.Next(400, 4000);
        npc.Money.Debt = rng.Next(0, 5000);

        // DO NOT Remember here — Characters row does not exist yet (FK fail)
        return npc;
    }

    static void EnsureNpcCore(SimCharacter npc)
    {
        try { CharacterFactory.EnsureCore(npc); }
        catch
        {
            npc.Brain ??= new Brain();
            npc.Brain.Owner = npc;
            npc.Money ??= new ProjectEve.Money.MoneyProfile();
            npc.Job ??= new ProjectEve.Money.JobProfile();
            npc.Traits ??= new NpcTraits();
        }
    }

    static void EnsureNpcTraits(SimCharacter npc)
    {
        try { CharacterFactory.EnsureTraits(npc); }
        catch
        {
            npc.Traits ??= new NpcTraits();
            if (npc.Traits.GetAll().Count > 0) return;
            try { TraitJsonLoader.ApplyRolledLayers(npc.Traits); }
            catch { npc.Traits.InitializeFastDefaults(); }
        }
    }

    static void SaveNpcIdentityStub(SimCharacter npc)
    {
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Characters " +
            "(Id, Name, Age, Gender, Occupation, Location, Goal, Need, Fear, Want, PersonalityContext, Hometown, Address, Tier) " +
            "VALUES ($id, $name, $age, $gender, $occ, $loc, $goal, $need, $fear, $want, $ctx, $home, $addr, $tier) " +
            "ON CONFLICT(Id) DO UPDATE SET " +
            "Name=$name, Age=$age, Gender=$gender, Occupation=$occ, Location=$loc, " +
            "Goal=$goal, Need=$need, Fear=$fear, Want=$want, PersonalityContext=$ctx, " +
            "Hometown=$home, Address=$addr, Tier=$tier";

        cmd.Parameters.AddWithValue("$id", npc.Id);
        cmd.Parameters.AddWithValue("$name", npc.Name ?? "");
        cmd.Parameters.AddWithValue("$age", npc.Age);
        cmd.Parameters.AddWithValue("$gender", npc.Gender ?? "");
        cmd.Parameters.AddWithValue("$occ", npc.Occupation ?? "");
        cmd.Parameters.AddWithValue("$loc", npc.Location ?? "");
        cmd.Parameters.AddWithValue("$goal", npc.Goal ?? "");
        cmd.Parameters.AddWithValue("$need", npc.Need ?? "");
        cmd.Parameters.AddWithValue("$fear", npc.Fear ?? "");
        cmd.Parameters.AddWithValue("$want", npc.Want ?? "");
        cmd.Parameters.AddWithValue("$ctx", npc.PersonalityContext ?? "");
        cmd.Parameters.AddWithValue("$home", npc.Hometown ?? "");
        cmd.Parameters.AddWithValue("$addr", npc.HomeAddress ?? "");
        cmd.Parameters.AddWithValue("$tier", npc.Tier);
        cmd.ExecuteNonQuery();
    }

    static void SaveRelationshipEdge(SimCharacter npc, string contact)
    {
        if (string.Equals(contact, "none", StringComparison.OrdinalIgnoreCase))
            return;
        if (npc.Relationships == null || npc.Relationships.Count == 0)
            return;

        var rel = npc.Relationships[0];
        using var conn = new SqliteConnection("Data Source=" + DbPath);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Relationships " +
            "(NpcId, TargetName, Trust, Respect, Affection, Attraction, Tension, RelationshipType, Notes) " +
            "VALUES ($id, $target, $trust, $respect, $aff, $attr, $ten, $type, $notes)";

        cmd.Parameters.AddWithValue("$id", npc.Id);
        cmd.Parameters.AddWithValue("$target", rel.TargetName ?? contact);
        cmd.Parameters.AddWithValue("$trust", rel.Trust);
        cmd.Parameters.AddWithValue("$respect", rel.Respect);
        cmd.Parameters.AddWithValue("$aff", rel.Affection);
        cmd.Parameters.AddWithValue("$attr", rel.Attraction);
        cmd.Parameters.AddWithValue("$ten", rel.Tension);
        cmd.Parameters.AddWithValue("$type", "contact");
        cmd.Parameters.AddWithValue("$notes", "npcgen web edge");
        cmd.ExecuteNonQuery();
    }

    static SimCharacter BootWorld()
    {
        Step("DB init");
        try
        {
            DatabaseInitializer.Initialize();
            Console.WriteLine("  ok  exists=" + File.Exists(DbPath) +
                "  bytes=" + (File.Exists(DbPath) ? new FileInfo(DbPath).Length : 0));
        }
        catch (Exception ex)
        {
            Console.WriteLine("  warning: " + ex.Message);
        }

        Step("Trait registry + behaviors");
        try
        {
            TraitRegistry.LoadBaseTraits();
            BehaviorRegistry.Load();
            Console.WriteLine("  ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  warning: " + ex.Message);
        }

        Step("TraitJson root");
        try
        {
            TraitJsonLoader.SetRoot(TraitJsonRoot);
            Console.WriteLine("  " + TraitJsonLoader.ResolveDefaultRoot());
        }
        catch (Exception ex)
        {
            Console.WriteLine("  warning: " + ex.Message);
        }

        Step("Relationship matrix");
        try
        {
            RelationshipMatrixLoader.Load(Path.Combine(TraitJsonRoot, "Matrix"));
            Console.WriteLine(
                "  fast=" + RelationshipMatrixLoader.FastRows.Count +
                " mid=" + RelationshipMatrixLoader.MidRows.Count +
                " slow=" + RelationshipMatrixLoader.SlowRows.Count +
                " loaded=" + RelationshipMatrixLoader.Loaded);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  matrix warning: " + ex.Message);
        }

        Step("LineBank import (new JSON packs)");
        RunLineBankImport();

        Step("Load Eve id=1");
        var eve = LoadEveOrFallback();
        eve.Brain ??= new Brain();
        eve.Brain.Owner = eve;

        Step("Ensure traits");
        EnsureTraits(eve);

        Console.WriteLine();
        Console.WriteLine("BASELINE (before chat):");
        PrintMeters(eve, 0);
        return eve;
    }

    static void PrintReady(SimCharacter eve)
    {
        Console.WriteLine("Ready: " + eve.Name + " | age " + eve.Age + " | " + eve.Occupation);
        Console.WriteLine("Location: " + eve.Location);
        Console.WriteLine("Commands: sheet | traits | reroll | stress | comfort | matrix | baseline | bank-import | fresh | clearlog | npcgen | exit");
        Console.WriteLine("AI test display: each normal turn now prints THOUGHT / LEAKS / TAGS before the outward reply.");
        Console.WriteLine("Args: train | build-thought-data | bank-import | --fresh");
        Console.WriteLine("npcgen → how many + contact Eve/Adam/Lisa/Edward/none + lane\n");
    }

    static void PrintReplyMeta(Brain brain)
    {
        string src = string.IsNullOrWhiteSpace(brain.LastReplySource)
            ? "(unknown)"
            : brain.LastReplySource;

        Console.ForegroundColor = ConsoleColor.DarkCyan;

        if (src == "ai_with_bank_seed")
        {
            Console.WriteLine(
                "  source : AI + LINEBANK SEED  (Qwen adapted/ignored a cached candidate)");
            Console.WriteLine(
                "  store  : yes — final AI line tried to grow linebank.db");
        }
        else if (src == "ai_new")
        {
            Console.WriteLine(
                "  source : AI NEW  (no useful LineBank seed)");
            Console.WriteLine(
                "  store  : yes — final AI line tried to grow linebank.db");
        }
        else if (src == "ai_retry_no_seed")
        {
            Console.WriteLine(
                "  source : AI RETRY / NO SEED  (first draft repeated the previous reply)");
            Console.WriteLine(
                "  store  : yes — retry line tried to grow linebank.db");
        }
        else if (src == "bank_error_fallback")
        {
            Console.WriteLine(
                "  source : LINEBANK ERROR FALLBACK  (Qwen call failed)");
            Console.WriteLine(
                "  store  : no");
        }
        else if (src == "llm")
        {
            // Compatibility with any older path still returning "llm".
            Console.WriteLine(
                "  source : AI / DialogueEngine");
            Console.WriteLine(
                "  store  : yes");
        }
        else if (src == "director")
        {
            Console.WriteLine("  source : DIRECTOR command");
            Console.WriteLine("  store  : no");
        }
        else
        {
            Console.WriteLine("  source : " + src);
            Console.WriteLine("  store  : ?");
        }

        Console.ResetColor();
    }

    static float Get(SimCharacter eve, string id)
    {
        try { return eve.Traits != null ? eve.Traits.Get(id) : 0f; }
        catch { return 0f; }
    }

    static void PrintThoughtDebug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            Console.WriteLine("  THOUGHT: (none)");
            Console.WriteLine("  LEAKS: (none)");
            Console.WriteLine("  TAGS: (none)");
            return;
        }

        try
        {
            var packet = ThoughtPacket.Parse(raw);

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("  THOUGHT: " +
                (string.IsNullOrWhiteSpace(packet.Thought) ? "(none)" : packet.Thought));
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  LEAKS: " + packet.LeakLine);
            Console.ResetColor();

            PrintTagLine(raw);
        }
        catch
        {
            PrintTagLine(raw);
        }
    }

    static void PrintTagLine(string? thought)
    {
        if (string.IsNullOrWhiteSpace(thought))
        {
            Console.WriteLine("  TAGS: (no thought)");
            return;
        }
        int i = thought.LastIndexOf("TAGS:", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            Console.WriteLine("  TAGS: (missing from thought)");
            return;
        }
        string line = thought.Substring(i);
        int nl = line.IndexOfAny(new[] { '\r', '\n' });
        if (nl > 0) line = line.Substring(0, nl);
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("  " + line.Trim());
        Console.ResetColor();
    }

    static void PrintMeters(SimCharacter eve, double seconds)
    {
        if (eve.Traits == null)
        {
            Console.WriteLine("  (no traits) (" + seconds.ToString("0.00") + "s)");
            return;
        }
        Console.WriteLine(
            "  ang=" + Get(eve, "trait.anger").ToString("0") +
            " anx=" + Get(eve, "trait.anxiety").ToString("0") +
            " hurt=" + Get(eve, "trait.hurt").ToString("0") +
            " trust=" + Get(eve, "trait.trust").ToString("0") +
            " aff=" + Get(eve, "trait.affection").ToString("0") +
            " des=" + Get(eve, "trait.desire").ToString("0") +
            " ten=" + Get(eve, "trait.tension").ToString("0") +
            " guard=" + Get(eve, "trait.guard").ToString("0") +
            " (" + seconds.ToString("0.00") + "s)");
    }

    static void PrintMatrixSelf(SimCharacter eve)
    {
        if (!RelationshipMatrixLoader.Loaded)
        {
            Console.WriteLine("Matrix not loaded.");
            return;
        }
        try
        {
            var score = LikeScoreService.ScoreLike(eve, eve);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(LikeScoreService.BuildThoughtBlock(score, eve.Name + " (self-test)"));
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine("matrix test: " + ex.Message);
        }
    }

    static bool ResetDatabaseFiles(string mainDb)
    {
        Step("Reset DB (--fresh / fresh)");

        // Microsoft.Data.Sqlite uses connection pooling. A disposed SqliteConnection
        // can leave an underlying SQLite handle pooled, which keeps the DB file locked
        // on Windows. Clear those pooled handles before deleting the database.
        try
        {
            SqliteConnection.ClearAllPools();
        }
        catch (Exception ex)
        {
            Console.WriteLine("  pool clear warning: " + ex.Message);
        }

        // Give finalizers a chance to release any short-lived wrappers that just went
        // out of scope. This is a reset/debug command, so a brief forced collection is OK.
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch { }

        string[] paths =
        {
            mainDb,
            mainDb + "-wal",
            mainDb + "-shm",
            Path.Combine(DataDir, "eve_memory.db"),
            Path.Combine(DataDir, "eve_memory.db-wal"),
            Path.Combine(DataDir, "eve_memory.db-shm")
        };

        bool ok = true;

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            bool deleted = false;
            Exception? lastError = null;

            // Windows can take a moment to release SQLite handles after pool clearing.
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    File.Delete(path);
                    deleted = !File.Exists(path);

                    if (deleted)
                    {
                        Console.WriteLine("  deleted " + Path.GetFileName(path));
                        break;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                try
                {
                    SqliteConnection.ClearAllPools();
                    System.Threading.Thread.Sleep(150);
                }
                catch { }
            }

            if (!deleted)
            {
                ok = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(
                    "  FAILED " + Path.GetFileName(path) +
                    (lastError != null ? " — " + lastError.Message : ""));
                Console.ResetColor();
            }
        }

        // project_eve.db is the critical file. If it still exists, this was not fresh.
        if (File.Exists(mainDb))
            ok = false;

        if (ok)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  reset complete — next boot will create a genuinely new DB");
            Console.ResetColor();
        }

        return ok;
    }

    static SimCharacter LoadEveOrFallback()
    {
        try
        {
            var task = Task.Run(() => CharacterRepository.LoadCharacter(1));
            if (!task.Wait(TimeSpan.FromSeconds(5)))
            {
                Console.WriteLine("  TIMEOUT — fallback Eve()");
                return new Eve { Id = 1 };
            }
            var eve = task.Result;
            if (eve == null)
            {
                Console.WriteLine("  null — fallback Eve()");
                return new Eve { Id = 1 };
            }
            Console.WriteLine("  loaded " + eve.Name + " age=" + eve.Age);
            return eve;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  load error: " + ex.Message);
            return new Eve { Id = 1 };
        }
    }

    static void Step(string label)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("[*] " + label);
        Console.ResetColor();
    }

    static void EnsureTraits(SimCharacter eve)
    {
        eve.Traits ??= new NpcTraits();
        if (eve.Traits.GetAll().Count > 0)
        {
            Console.WriteLine("  " + eve.Traits.GetAll().Count + " keys already present");
            return;
        }
        try
        {
            TraitJsonLoader.ApplyRolledLayers(eve.Traits);
            Console.WriteLine("  rolled → " + eve.Traits.GetAll().Count + " keys");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  roll failed: " + ex.Message);
            eve.Traits.InitializeFastDefaults();
            Console.WriteLine("  Fast defaults → " + eve.Traits.GetAll().Count + " keys");
        }
    }

    static void ForceRerollTraits(SimCharacter eve)
    {
        try
        {
            eve.Traits ??= new NpcTraits();
            TraitJsonLoader.ApplyRolledLayers(eve.Traits);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Rerolled (" + eve.Traits.GetAll().Count + ")");
            Console.WriteLine(eve.Traits.BuildLlmSummary(15));
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            eve.Traits?.InitializeFastDefaults();
        }
    }

    static void PrintFastSnapshot(SimCharacter eve)
    {
        if (eve.Traits == null) { Console.WriteLine("No traits."); return; }
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(eve.Traits.BuildLlmSummary(20));
        Console.WriteLine("keys=" + eve.Traits.GetAll().Count);
        Console.ResetColor();
    }
}