namespace ProjectEve.AI.Training
{
    internal class LoraTrainingParameters
    {
        public int LoraRank { get; set; }
        public int LoraAlpha { get; set; }
        public float AdamAlpha { get; set; }
        public float AdamBeta1 { get; set; }
        public float AdamBeta2 { get; set; }
        public float AdamDecay { get; set; }
        public int GradientAccumulation { get; set; }
    }
}