using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ProjectEve.Traits.Matrix
{
    public sealed class MatrixFile
    {
        [JsonPropertyName("rows")]
        public List<MatrixRow> Rows { get; set; } = new();
    }

    public sealed class MatrixRow
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("layer")] public string Layer { get; set; } = "";
        [JsonPropertyName("weight")] public float Weight { get; set; } = 0.5f;
        [JsonPropertyName("minParent")] public float MinParent { get; set; } = 0f;
        [JsonPropertyName("strangerBase")] public float StrangerBase { get; set; } = 0f;
        [JsonPropertyName("targetHigh")] public ScoreDelta? TargetHigh { get; set; }
        [JsonPropertyName("targetLow")] public ScoreDelta? TargetLow { get; set; }
        [JsonPropertyName("sharedHigh")] public ScoreDelta? SharedHigh { get; set; }
        [JsonPropertyName("rivalIds")] public List<string>? RivalIds { get; set; }
        [JsonPropertyName("rivalHigh")] public ScoreDelta? RivalHigh { get; set; }
    }

    public sealed class ScoreDelta
    {
        [JsonPropertyName("like")] public float Like { get; set; }
        [JsonPropertyName("trust")] public float Trust { get; set; }
        [JsonPropertyName("affection")] public float Affection { get; set; }
        [JsonPropertyName("attraction")] public float Attraction { get; set; }
        [JsonPropertyName("tension")] public float Tension { get; set; }
        [JsonPropertyName("growthMult")] public float GrowthMult { get; set; } = 1f;
    }

    public sealed class OppositeFile
    {
        [JsonPropertyName("pairs")]
        public List<OppositePair> Pairs { get; set; } = new();
    }

    public sealed class OppositePair
    {
        [JsonPropertyName("a")] public string A { get; set; } = "";
        [JsonPropertyName("b")] public string B { get; set; } = "";
        [JsonPropertyName("requiresMin")] public float RequiresMin { get; set; } = 50f;
        [JsonPropertyName("like")] public float Like { get; set; }
        [JsonPropertyName("attraction")] public float Attraction { get; set; }
        [JsonPropertyName("cap")] public float Cap { get; set; } = 4f;
    }

    public sealed class LikeBandFile
    {
        [JsonPropertyName("bands")]
        public List<LikeBand> Bands { get; set; } = new();
    }

    public sealed class LikeBand
    {
        [JsonPropertyName("min")] public int Min { get; set; }
        [JsonPropertyName("max")] public int Max { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("warmScale")] public float WarmScale { get; set; } = 1f;
        [JsonPropertyName("insultScale")] public float InsultScale { get; set; } = 1f;
        [JsonPropertyName("trustLossScale")] public float TrustLossScale { get; set; } = 1f;
    }

    public sealed class StandingScore
    {
        public float Like;
        public float Trust;
        public float Affection;
        public float Attraction;
        public float Tension;
        public float GrowthMult = 1f;
        public string Band = "neutral";
        public List<string> Notes = new();
    }
}