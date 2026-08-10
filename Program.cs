using ProjectEve.AI.Brain;
using ProjectEve.AI.Training;
using ProjectEve.Characters.Base;
using ProjectEve.Characters.Emotion;
using ProjectEve.Characters.NPCs;
using ProjectEve.Characters.Traits.Core;
using ProjectEve.Core.Chat;
using ProjectEve.Traits;
using ProjectEve.Traits.Matrix;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

class Program
{
    static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");
    static string DbPath => Path.Combine(DataDir, "project_eve.db");

    static readonly string TraitJsonRoot =
     @"D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean\Characters\Traits\TraitJson";

    // Runtime / game JSON (code tree) — keep for chat roll if you still use it
    // Training catalogs live on the data drive:
    static readonly string FastTraitDir =
        @"D:\ProjectEve\EveData\Traits\Fast\Parents";

    static readonly string TrainingOutDir =
        @"D:\ProjectEve\EveData\training";

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0].Equals("train", StringComparison.OrdinalIgnoreCase))
        {
            RunDatasetBuild();
            return;
        }

        // Build Thought heat jsonl from Fast trait JSONs → Unsloth
        // Usage:
        //   build-thought-data
        //   build-thought-data "D:\path\to\Fast" "D:\ProjectEve\EveData\training\thought_heat_v1.jsonl" 400
        if (args.Length > 0 && args[0].Equals("build-thought-data", StringComparison.OrdinalIgnoreCase))
        {
            RunBuildThoughtData(args);
            return;
        }

        bool fresh = args.Any(a => a.Equals("--fresh", StringComparison.OrdinalIgnoreCase));
        RunChat(freshDb: fresh);
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

        Console.WriteLine("Building Thought heat jsonl from Fast trait catalogs...");
        Console.WriteLine("  Fast dir : " + fastDir);
        Console.WriteLine("  Out      : " + outPath);
        Console.WriteLine("  Min rows : " + minRows);
        Console.WriteLine();

        if (!Directory.Exists(fastDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Fast trait folder not found:");
            Console.WriteLine("  " + fastDir);
            Console.WriteLine("Fix FastTraitDir / pass path as arg 1.");
            Console.ResetColor();
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            string written = ThoughtDatasetBuilder.BuildHeatJsonl(fastDir, outPath, minRows);
            int lines = File.ReadAllLines(written).Length;
            long bytes = new FileInfo(written).Length;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Done. {lines} lines, {bytes} bytes");
            Console.WriteLine(written);
            Console.ResetColor();

            // Preview first line for Unsloth sanity
            string? first = File.ReadLines(written).FirstOrDefault();
            if (!string.IsNullOrEmpty(first))
            {
                Console.WriteLine();
                Console.WriteLine("First line preview:");
                Console.WriteLine(first.Length > 180 ? first[..180] + "..." : first);
            }

            Console.WriteLine();
            Console.WriteLine("Next: Unsloth → Browse this jsonl → HF base model (not GGUF) → QLoRA → Start");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Build failed: " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
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
            ResetDatabaseFiles(DbPath);

        Step("DB init");
        try
        {
            DatabaseInitializer.Initialize();
            Console.WriteLine("   ok  exists=" + File.Exists(DbPath) +
                "  bytes=" + (File.Exists(DbPath) ? new FileInfo(DbPath).Length : 0));
        }
        catch (Exception ex)
        {
            Console.WriteLine("   warning: " + ex.Message);
        }

        Step("Trait registry + behaviors");
        try
        {
            TraitRegistry.LoadBaseTraits();
            BehaviorRegistry.Load();
            Console.WriteLine("   ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine("   warning: " + ex.Message);
        }

        Step("TraitJson root");
        try
        {
            TraitJsonLoader.SetRoot(TraitJsonRoot);
            Console.WriteLine("   " + TraitJsonLoader.ResolveDefaultRoot());
        }
        catch (Exception ex)
        {
            Console.WriteLine("   warning: " + ex.Message);
        }

        Step("Relationship matrix");
        try
        {
            RelationshipMatrixLoader.Load(Path.Combine(TraitJsonRoot, "Matrix"));
            Console.WriteLine(
                $"   fast={RelationshipMatrixLoader.FastRows.Count} " +
                $"mid={RelationshipMatrixLoader.MidRows.Count} " +
                $"slow={RelationshipMatrixLoader.SlowRows.Count} " +
                $"loaded={RelationshipMatrixLoader.Loaded}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("   matrix warning: " + ex.Message);
        }

        TtsBakeService? tts = null;
        Step("TTS worker");
        try
        {
            tts = new TtsBakeService();
            tts.Start();
            Console.WriteLine("   started");
        }
        catch (Exception ex)
        {
            Console.WriteLine("   skipped: " + ex.Message);
            tts = null;
        }

        Step("Load Eve id=1");
        var eve = LoadEveOrFallback();
        eve.Brain ??= new Brain();
        eve.Brain.Owner = eve;

        Step("Ensure traits");
        EnsureTraits(eve);

        Console.WriteLine();
        Console.WriteLine("BASELINE (before chat):");
        PrintMeters(eve, 0);

        string voiceOutDir = Path.Combine(DataDir, "voice");
        try { Directory.CreateDirectory(voiceOutDir); } catch { }

        Console.WriteLine($"Ready: {eve.Name} | age {eve.Age} | {eve.Occupation}");
        Console.WriteLine($"Location: {eve.Location}");
        Console.WriteLine("Commands: sheet | traits | reroll | stress | comfort | matrix | baseline | exit");
        Console.WriteLine("Args: train | build-thought-data | --fresh");
        Console.WriteLine("Test path: stress → comfort → compliment → hello\n");

        int lineNum = 0;

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"[{DateTime.Now:HH:mm}] You: ");
            Console.ResetColor();

            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            string lower = input.Trim().ToLowerInvariant();
            if (lower is "exit" or "quit") break;

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

            if (lower == "stress")
            {
                input = "(stress) I fucking hate you right now.";
                Console.WriteLine("   [inject] " + input);
            }
            else if (lower == "comfort")
            {
                input = "(comfort) I'm sorry. I love you. Come here.";
                Console.WriteLine("   [inject] " + input);
            }

            float beforeAng = Get(eve, "trait.anger");
            float beforeAff = Get(eve, "trait.affection");
            float beforeHurt = Get(eve, "trait.hurt");
            float beforeTrust = Get(eve, "trait.trust");

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("   Eve is thinking...");
            Console.ResetColor();

            var sw = Stopwatch.StartNew();
            string reply;

            try
            {
                eve.Brain.Think(input);
                PrintTagLine(eve.Brain.LastThought);
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
            Console.WriteLine($"[{DateTime.Now:HH:mm}] Eve: {reply}");
            Console.ResetColor();

            float dAng = Get(eve, "trait.anger") - beforeAng;
            float dAff = Get(eve, "trait.affection") - beforeAff;
            float dHurt = Get(eve, "trait.hurt") - beforeHurt;
            float dTrust = Get(eve, "trait.trust") - beforeTrust;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            PrintMeters(eve, sw.Elapsed.TotalSeconds);
            Console.WriteLine(
                $"   Δ ang={dAng:+0;-0;0} aff={dAff:+0;-0;0} " +
                $"hurt={dHurt:+0;-0;0} trust={dTrust:+0;-0;0}\n");
            Console.ResetColor();

            if (tts != null && !string.IsNullOrWhiteSpace(reply))
            {
                lineNum++;
                try
                {
                    tts.Enqueue(reply, "af_heart",
                        Path.Combine(voiceOutDir, $"eve_line_{lineNum:000}.wav"));
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

    static float Get(SimCharacter eve, string id)
    {
        try { return eve.Traits?.Get(id) ?? 0f; }
        catch { return 0f; }
    }

    static void PrintTagLine(string? thought)
    {
        if (string.IsNullOrWhiteSpace(thought))
        {
            Console.WriteLine("   TAGS: (no thought)");
            return;
        }
        int i = thought.LastIndexOf("TAGS:", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            Console.WriteLine("   TAGS: (missing from thought)");
            return;
        }
        string line = thought[i..];
        int nl = line.IndexOfAny(new[] { '\r', '\n' });
        if (nl > 0) line = line[..nl];
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("   " + line.Trim());
        Console.ResetColor();
    }

    static void PrintMeters(SimCharacter eve, double seconds)
    {
        if (eve.Traits == null)
        {
            Console.WriteLine($"   (no traits) ({seconds:0.00}s)");
            return;
        }
        Console.WriteLine(
            $"   ang={Get(eve, "trait.anger"):0} anx={Get(eve, "trait.anxiety"):0} " +
            $"hurt={Get(eve, "trait.hurt"):0} trust={Get(eve, "trait.trust"):0} " +
            $"aff={Get(eve, "trait.affection"):0} des={Get(eve, "trait.desire"):0} " +
            $"ten={Get(eve, "trait.tension"):0} guard={Get(eve, "trait.guard"):0} " +
            $"({seconds:0.00}s)");
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
            Console.WriteLine(
                $"fast={RelationshipMatrixLoader.FastRows.Count} " +
                $"mid={RelationshipMatrixLoader.MidRows.Count} " +
                $"slow={RelationshipMatrixLoader.SlowRows.Count}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.WriteLine("matrix test: " + ex.Message);
        }
    }

    static void ResetDatabaseFiles(string mainDb)
    {
        Step("Reset DB (--fresh)");
        foreach (var path in new[]
        {
            mainDb, mainDb + "-wal", mainDb + "-shm",
            Path.Combine(DataDir, "eve_memory.db")
        })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Console.WriteLine("   deleted " + Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("   " + ex.Message);
            }
        }
    }

    static SimCharacter LoadEveOrFallback()
    {
        try
        {
            var task = Task.Run(() => CharacterRepository.LoadCharacter(1));
            if (!task.Wait(TimeSpan.FromSeconds(5)))
            {
                Console.WriteLine("   TIMEOUT — fallback Eve()");
                return new Eve { Id = 1 };
            }
            var eve = task.Result;
            if (eve == null)
            {
                Console.WriteLine("   null — fallback Eve()");
                return new Eve { Id = 1 };
            }
            Console.WriteLine($"   loaded {eve.Name} age={eve.Age}");
            return eve;
        }
        catch (Exception ex)
        {
            Console.WriteLine("   load error: " + ex.Message);
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
            Console.WriteLine($"   {eve.Traits.GetAll().Count} keys already present");
            return;
        }
        try
        {
            TraitJsonLoader.ApplyRolledLayers(eve.Traits);
            Console.WriteLine($"   rolled → {eve.Traits.GetAll().Count} keys");
        }
        catch (Exception ex)
        {
            Console.WriteLine("   roll failed: " + ex.Message);
            eve.Traits.InitializeFastDefaults();
            Console.WriteLine($"   Fast defaults → {eve.Traits.GetAll().Count} keys");
        }
    }

    static void ForceRerollTraits(SimCharacter eve)
    {
        try
        {
            eve.Traits ??= new NpcTraits();
            TraitJsonLoader.ApplyRolledLayers(eve.Traits);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Rerolled ({eve.Traits.GetAll().Count})");
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
        Console.WriteLine($"keys={eve.Traits.GetAll().Count}");
        Console.ResetColor();
    }
}