using ProjectEve.AI.Brain;
using ProjectEve.AI.Training;
using ProjectEve.Characters.Base;
using System;
using System.Diagnostics;

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0].Equals("train", StringComparison.OrdinalIgnoreCase))
        {
            RunDatasetBuild();
            return;
        }

        RunChat();
    }

    static void RunDatasetBuild()
    {
        Console.Title = "Eve Dataset Builder";
        Console.WriteLine("Building Eve training dataset from packs...\n");

        try
        {
            EveLoraTrainer.BuildDatasetOnly();
            Console.WriteLine("\nDone. You can close this window.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Dataset build failed:");
            Console.WriteLine(ex.Message);
            Console.ResetColor();
        }
    }

    static void RunChat()
    {
        Console.Title = "Eve Chat";

        try
        {
            DatabaseInitializer.Initialize();
        }
        catch (Exception ex)
        {
            Console.WriteLine("DB init warning: " + ex.Message);
        }

        var eve = CharacterRepository.LoadCharacter(1);
        if (eve == null)
        {
            Console.WriteLine("Eve could not be loaded.");
            return;
        }

        if (eve.Brain == null)
            eve.Brain = new Brain();
        eve.Brain.Owner = eve;

        Console.WriteLine($"Loaded: {eve.Name}, age {eve.Age}");
        Console.WriteLine($"Location: {eve.Location}");
        Console.WriteLine($"Occupation: {eve.Occupation}");
        Console.WriteLine($"Traits: {eve.Traits?.GetAll().Count ?? 0}");
        Console.WriteLine();
        Console.WriteLine("Commands: sheet | exit");
        Console.WriteLine("Chat with Eve.\n");

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"[{DateTime.Now:HH:mm}] You: ");
            Console.ResetColor();

            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            string lower = input.Trim().ToLowerInvariant();

            if (lower is "exit" or "quit")
                break;

            if (lower == "sheet")
            {
                CharacterSheetPrinter.Print(eve);
                continue;
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("   Eve is thinking...");
            Console.ResetColor();

            var sw = Stopwatch.StartNew();
            eve.Brain.Think(input);
            string reply = eve.Brain.Reply(input);
            sw.Stop();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[{DateTime.Now:HH:mm}] Eve: {reply}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"   ({sw.Elapsed.TotalSeconds:0.00}s)\n");
            Console.ResetColor();
        }

        try
        {
            if (eve.Traits != null)
                CharacterRepository.SaveTraits(eve.Id, eve.Traits);
        }
        catch
        {
            // ignore save failures on exit
        }

        Console.WriteLine("Goodbye.");
    }
}