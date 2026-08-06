using System;
using System.IO;
using ProjectEve.AI.Training;

namespace ProjectEve.AI.Training
{
    public static class EveLoraTrainer
    {
        public class TrainConfig
        {
            public string EveJsonPath { get; set; } =
                @"C:\Users\ryans\source\repos\RedTagFiber\ProjectEve2026\ProjectEve\AI\Training\EveJson\eve.json";

            public string DatasetPath { get; set; } =
                @"C:\AI\eve-dataset\eve_sharegpt.json";
        }

        public static void BuildDatasetOnly(TrainConfig? config = null)
        {
            config ??= new TrainConfig();

            var examples = EvePackLoader.LoadAll(config.EveJsonPath);
            if (examples.Count == 0)
                throw new InvalidOperationException("No examples loaded from packs.");

            EvePackLoader.ExportShareGpt(config.DatasetPath, examples);

            Console.WriteLine("Dataset build complete.");
            Console.WriteLine($"Examples: {examples.Count}");
            Console.WriteLine($"Output: {config.DatasetPath}");
        }

        public static void Train(TrainConfig? config = null)
        {
            BuildDatasetOnly(config);

            throw new NotSupportedException(
                "LM-Kit trainer type not available in this package. Dataset export works.");
        }
    }
}