namespace ProjectEve.NpcStudio.Services;

public sealed class NpcPromptBuildRequest
{
    public int NpcId { get; set; }
    public string PromptType { get; set; } = "ReferencePortrait";
    public string ExtraDirection { get; set; } = "";
}

public sealed class NpcComfyGenerationRequest
{
    public int NpcId { get; set; }
    public string ImageType { get; set; } = "ReferencePortrait";
    public string PositivePrompt { get; set; } = "";
    public string NegativePrompt { get; set; } = "cartoon, anime, plastic skin, distorted face, bad eyes, blurry, extra fingers, watermark, text";

    // Phase 13 default is the user's working SD3.5 workflow.
    public string WorkflowName { get; set; } = "ProjectEve_SD35_ReferencePortrait_V01";
    public string WorkflowTemplatePath { get; set; } = @"D:\ProjectEveData\ComfyWorkflows\ProjectEve_SD35_ReferencePortrait_V01.json";

    public string Checkpoint { get; set; } = "sd3.5_large_fp8_scaled.safetensors";
    public int Width { get; set; } = 768;
    public int Height { get; set; } = 1152;
    public int Steps { get; set; } = 32;
    public double Cfg { get; set; } = 5.0;
    public string Sampler { get; set; } = "dpm_adaptive";
    public string Scheduler { get; set; } = "karras";
    public string Seed { get; set; } = "-1";
    public string SavePrefix { get; set; } = "";
}

public sealed class NpcComfyGenerationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string PromptId { get; set; } = "";
    public string RawResponse { get; set; } = "";
    public string WorkflowUsed { get; set; } = "";
    public string SavePrefix { get; set; } = "";
}
