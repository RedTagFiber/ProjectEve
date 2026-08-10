using ProjectEve.Characters.Base;
using System;
using System.Collections.Generic;

namespace ProjectEve.Traits.Matrix
{
    public static class LikeScoreService
    {
        private const float HighThreshold = 60f;
        private const float LowThreshold = 35f;

        /// <summary>First-meet seed from matrix. Does not write DB.</summary>
        public static StandingScore ScoreLike(SimCharacter observer, SimCharacter target)
        {
            if (!RelationshipMatrixLoader.Loaded)
                return Neutral();

            var score = new StandingScore
            {
                Like = 50f,
                Trust = 50f,
                Affection = 50f,
                Attraction = 50f,
                Tension = 15f,
                GrowthMult = 1f
            };

            ApplyLayer(observer, target, RelationshipMatrixLoader.FastRows, score);
            ApplyLayer(observer, target, RelationshipMatrixLoader.MidRows, score);
            ApplyLayer(observer, target, RelationshipMatrixLoader.SlowRows, score);
            ApplyOpposites(observer, target, score);

            score.Like = Clamp(score.Like);
            score.Trust = Clamp(score.Trust);
            score.Affection = Clamp(score.Affection);
            score.Attraction = Clamp(score.Attraction);
            score.Tension = Clamp(score.Tension);
            score.GrowthMult = Math.Clamp(score.GrowthMult, 0.4f, 1.6f);
            score.Band = RelationshipMatrixLoader.GetBand(score.Like).Name;
            return score;
        }

        public static string BuildThoughtBlock(StandingScore s, string targetName)
        {
            string notes = s.Notes.Count == 0 ? "none" : string.Join("; ", s.Notes);
            return
                $"ABOUT {targetName}:\n" +
                $"- LikeScore {s.Like:0} ({s.Band})\n" +
                $"- Trust {s.Trust:0} Affection {s.Affection:0} Attraction {s.Attraction:0} Tension {s.Tension:0}\n" +
                $"- Notes: {notes}";
        }

        private static void ApplyLayer(
            SimCharacter observer, SimCharacter target,
            List<MatrixRow> rows, StandingScore score)
        {
            if (observer.Traits == null || target.Traits == null) return;

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Id)) continue;

                float tVal = target.Traits.Get(row.Id);
                float oVal = observer.Traits.Get(row.Id);
                float w = row.Weight <= 0 ? 0.5f : row.Weight;

                // Slow/mid "not identity" skip
                if (row.MinParent > 0 && tVal < row.MinParent && oVal < row.MinParent)
                    continue;

                // Stranger base from observer Mid
                if (row.StrangerBase != 0 && oVal >= HighThreshold)
                    score.Like += row.StrangerBase * 0.5f;

                if (tVal >= HighThreshold && row.TargetHigh != null)
                {
                    AddDelta(score, row.TargetHigh, w);
                    if (Math.Abs(row.TargetHigh.Like) >= 3)
                        score.Notes.Add($"{row.Id} high");
                }
                else if (tVal <= LowThreshold && row.TargetLow != null)
                {
                    AddDelta(score, row.TargetLow, w);
                }

                // Shared
                if (tVal >= HighThreshold && oVal >= HighThreshold && row.SharedHigh != null)
                {
                    AddDelta(score, row.SharedHigh, w);
                    if (row.SharedHigh.GrowthMult > 1f)
                        score.GrowthMult *= row.SharedHigh.GrowthMult;
                    score.Notes.Add($"shared {row.Id}");
                }

                // Rival
                if (row.RivalIds != null && row.RivalHigh != null && oVal >= HighThreshold)
                {
                    foreach (var rivalId in row.RivalIds)
                    {
                        if (target.Traits.Get(rivalId) >= HighThreshold)
                        {
                            AddDelta(score, row.RivalHigh, w);
                            if (row.RivalHigh.GrowthMult > 0f && row.RivalHigh.GrowthMult < 1f)
                                score.GrowthMult *= row.RivalHigh.GrowthMult;
                            score.Notes.Add($"rival {row.Id} vs {rivalId}");
                        }
                    }
                }
            }
        }

        private static void ApplyOpposites(SimCharacter observer, SimCharacter target, StandingScore score)
        {
            if (observer.Traits == null || target.Traits == null) return;

            foreach (var p in RelationshipMatrixLoader.OppositePairs)
            {
                float oA = observer.Traits.Get(p.A);
                float tB = target.Traits.Get(p.B);
                if (oA < p.RequiresMin || tB < HighThreshold) continue;

                float like = Math.Min(p.Like, p.Cap);
                score.Like += like;
                score.Attraction += Math.Min(p.Attraction, p.Cap);
                score.Notes.Add($"opposite {p.A}/{p.B}");
            }
        }

        private static void AddDelta(StandingScore s, ScoreDelta d, float w)
        {
            s.Like += d.Like * w;
            s.Trust += d.Trust * w;
            s.Affection += d.Affection * w;
            s.Attraction += d.Attraction * w;
            s.Tension += d.Tension * w;
        }

        private static StandingScore Neutral() => new()
        {
            Like = 50,
            Trust = 50,
            Affection = 50,
            Attraction = 50,
            Tension = 15,
            Band = "neutral",
            GrowthMult = 1f
        };

        private static float Clamp(float v) => Math.Clamp(v, 0f, 100f);
    }
}