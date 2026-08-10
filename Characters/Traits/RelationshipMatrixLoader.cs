using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ProjectEve.Traits.Matrix
{
    public static class RelationshipMatrixLoader
    {
        private static readonly JsonSerializerOptions Opts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static List<MatrixRow> FastRows { get; private set; } = new();
        public static List<MatrixRow> MidRows { get; private set; } = new();
        public static List<MatrixRow> SlowRows { get; private set; } = new();
        public static List<OppositePair> OppositePairs { get; private set; } = new();
        public static List<LikeBand> Bands { get; private set; } = new();
        public static bool Loaded { get; private set; }

        public static void Load(string matrixFolder)
        {
            FastRows = LoadRows(Path.Combine(matrixFolder, "fast_matrix.json"));
            MidRows = LoadRows(Path.Combine(matrixFolder, "mid_matrix.json"));
            SlowRows = LoadRows(Path.Combine(matrixFolder, "slow_matrix.json"));
            OppositePairs = LoadOpposites(Path.Combine(matrixFolder, "opposite_pairs.json"));
            Bands = LoadBands(Path.Combine(matrixFolder, "like_band_scales.json"));
            Loaded = true;
            Console.WriteLine(
                $"Matrix loaded: fast={FastRows.Count} mid={MidRows.Count} " +
                $"slow={SlowRows.Count} opposite={OppositePairs.Count} bands={Bands.Count}");
        }

        private static List<MatrixRow> LoadRows(string path)
        {
            if (!File.Exists(path)) return new List<MatrixRow>();
            var file = JsonSerializer.Deserialize<MatrixFile>(File.ReadAllText(path), Opts);
            return file?.Rows ?? new List<MatrixRow>();
        }

        private static List<OppositePair> LoadOpposites(string path)
        {
            if (!File.Exists(path)) return new List<OppositePair>();
            var file = JsonSerializer.Deserialize<OppositeFile>(File.ReadAllText(path), Opts);
            return file?.Pairs ?? new List<OppositePair>();
        }

        private static List<LikeBand> LoadBands(string path)
        {
            if (!File.Exists(path)) return new List<LikeBand>();
            var file = JsonSerializer.Deserialize<LikeBandFile>(File.ReadAllText(path), Opts);
            return file?.Bands ?? new List<LikeBand>();
        }

        public static LikeBand GetBand(float likeScore)
        {
            int v = (int)Math.Clamp(likeScore, 0, 100);
            foreach (var b in Bands)
                if (v >= b.Min && v <= b.Max) return b;
            return new LikeBand { Name = "neutral", WarmScale = 1f, InsultScale = 1f, TrustLossScale = 1f };
        }
    }
}