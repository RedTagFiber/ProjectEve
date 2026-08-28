using Microsoft.Extensions.Options;
using ProjectEve.NpcStudio.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectEve.NpcStudio.Services;

/// <summary>
/// Phase 13 Comfy bridge.
/// This version uses the user's exported working SD3.5 workflow node map:
/// 3  = KSampler
/// 4  = CheckpointLoaderSimple
/// 9  = SaveImage
/// 16 = Positive Prompt CLIPTextEncode
/// 40 = Negative Prompt CLIPTextEncode
/// 53 = EmptySD3LatentImage
///
/// It loads the workflow JSON, patches the prompts/settings, and queues it to ComfyUI /prompt.
/// </summary>
public sealed class ComfyWorkflowService
{
    private const string DefaultWorkflowPath = @"D:\ProjectEveData\ComfyWorkflows\ProjectEve_SD35_ReferencePortrait_V01.json";
    private const string DefaultCheckpoint = "sd3.5_large_fp8_scaled.safetensors";

    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;

    public ComfyWorkflowService(HttpClient http, IOptions<NpcStudioOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<bool> CanReachComfyAsync()
    {
        try
        {
            using var response = await _http.GetAsync(BuildUrl("/system_stats"));
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<NpcComfyGenerationResult> QueueReferencePortraitAsync(NpcComfyGenerationRequest request)
    {
        try
        {
            var workflow = BuildWorkflowJson(request, out var workflowUsed, out var savePrefix);

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = "ProjectEve.NpcStudio"
            };

            using var response = await _http.PostAsJsonAsync(BuildUrl("/prompt"), payload);
            var raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new NpcComfyGenerationResult
                {
                    Success = false,
                    Message = $"Comfy returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                    RawResponse = raw,
                    WorkflowUsed = workflowUsed,
                    SavePrefix = savePrefix
                };
            }

            var promptId = ExtractPromptId(raw);

            return new NpcComfyGenerationResult
            {
                Success = true,
                Message = "Queued SD3.5 Comfy generation.",
                PromptId = promptId,
                RawResponse = raw,
                WorkflowUsed = workflowUsed,
                SavePrefix = savePrefix
            };
        }
        catch (Exception ex)
        {
            return new NpcComfyGenerationResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private string BuildUrl(string path)
    {
        var baseUrl = (_options.ComfyBaseUrl ?? "http://127.0.0.1:8188").TrimEnd('/');
        return baseUrl + path;
    }

    private static string ExtractPromptId(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("prompt_id", out var promptIdElement))
                return promptIdElement.GetString() ?? "";
        }
        catch
        {
            // Raw response remains available for debugging.
        }

        return "";
    }

    private static JsonObject BuildWorkflowJson(NpcComfyGenerationRequest request, out string workflowUsed, out string savePrefix)
    {
        var workflow = LoadWorkflowTemplate(request, out workflowUsed);

        var positivePrompt = RequireObject(workflow, "16", "Positive Prompt");
        var positiveInputs = RequireObject(positivePrompt, "inputs", "Positive Prompt inputs");
        positiveInputs["text"] = request.PositivePrompt ?? "";

        var negativePrompt = RequireObject(workflow, "40", "Negative Prompt");
        var negativeInputs = RequireObject(negativePrompt, "inputs", "Negative Prompt inputs");
        negativeInputs["text"] = request.NegativePrompt ?? "";

        var checkpoint = RequireObject(workflow, "4", "Load Checkpoint");
        var checkpointInputs = RequireObject(checkpoint, "inputs", "Load Checkpoint inputs");
        checkpointInputs["ckpt_name"] = string.IsNullOrWhiteSpace(request.Checkpoint)
            ? DefaultCheckpoint
            : request.Checkpoint.Trim();

        var latent = RequireObject(workflow, "53", "EmptySD3LatentImage");
        var latentInputs = RequireObject(latent, "inputs", "EmptySD3LatentImage inputs");
        latentInputs["width"] = request.Width <= 0 ? 768 : request.Width;
        latentInputs["height"] = request.Height <= 0 ? 1152 : request.Height;
        latentInputs["batch_size"] = 1;

        var sampler = RequireObject(workflow, "3", "KSampler");
        var samplerInputs = RequireObject(sampler, "inputs", "KSampler inputs");
        samplerInputs["seed"] = ResolveSeed(request.Seed);
        samplerInputs["steps"] = request.Steps <= 0 ? 32 : request.Steps;
        samplerInputs["cfg"] = request.Cfg <= 0 ? 5.0 : request.Cfg;
        samplerInputs["sampler_name"] = string.IsNullOrWhiteSpace(request.Sampler) ? "dpm_adaptive" : request.Sampler.Trim();
        samplerInputs["scheduler"] = string.IsNullOrWhiteSpace(request.Scheduler) ? "karras" : request.Scheduler.Trim();
        samplerInputs["denoise"] = 1.0;

        savePrefix = BuildSavePrefix(request);
        var save = RequireObject(workflow, "9", "Save Image");
        var saveInputs = RequireObject(save, "inputs", "Save Image inputs");
        saveInputs["filename_prefix"] = savePrefix;

        return workflow;
    }

    private static JsonObject LoadWorkflowTemplate(NpcComfyGenerationRequest request, out string workflowUsed)
    {
        var explicitPath = request.WorkflowTemplatePath;
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            workflowUsed = explicitPath;
            return ParseWorkflow(File.ReadAllText(explicitPath));
        }

        if (File.Exists(DefaultWorkflowPath))
        {
            workflowUsed = DefaultWorkflowPath;
            return ParseWorkflow(File.ReadAllText(DefaultWorkflowPath));
        }

        workflowUsed = "embedded Phase 13 SD3.5 workflow template";
        return ParseWorkflow(EmbeddedSd35WorkflowJson);
    }

    private static JsonObject ParseWorkflow(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        if (node is null)
            throw new InvalidOperationException("Could not parse Comfy workflow JSON.");

        return node;
    }

    private static JsonObject RequireObject(JsonObject root, string key, string label)
    {
        if (!root.TryGetPropertyValue(key, out var node) || node is null)
            throw new InvalidOperationException($"Workflow is missing node/key '{key}' for {label}.");

        var obj = node.AsObject();
        if (obj is null)
            throw new InvalidOperationException($"Workflow node/key '{key}' for {label} is not an object.");

        return obj;
    }

    private static long ResolveSeed(string? seedText)
    {
        if (string.IsNullOrWhiteSpace(seedText) || seedText.Trim() == "-1")
            return Random.Shared.NextInt64(1, long.MaxValue);

        if (long.TryParse(seedText.Trim(), out var parsed) && parsed > 0)
            return parsed;

        return Random.Shared.NextInt64(1, long.MaxValue);
    }

    private static string BuildSavePrefix(NpcComfyGenerationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SavePrefix))
            return request.SavePrefix.Trim();

        var imageType = string.IsNullOrWhiteSpace(request.ImageType) ? "ReferencePortrait" : request.ImageType.Trim();
        return $"ProjectEve/NPC_{request.NpcId:D6}/{imageType}";
    }

    private const string EmbeddedSd35WorkflowJson = """
{
  "3": {"inputs": {"seed": 228925952556307, "steps": 32, "cfg": 5, "sampler_name": "dpm_adaptive", "scheduler": "karras", "denoise": 1, "model": ["4", 0], "positive": ["16", 0], "negative": ["40", 0], "latent_image": ["53", 0]}, "class_type": "KSampler", "_meta": {"title": "KSampler"}},
  "4": {"inputs": {"ckpt_name": "sd3.5_large_fp8_scaled.safetensors"}, "class_type": "CheckpointLoaderSimple", "_meta": {"title": "Load Checkpoint"}},
  "8": {"inputs": {"samples": ["3", 0], "vae": ["4", 2]}, "class_type": "VAEDecode", "_meta": {"title": "VAE Decode"}},
  "9": {"inputs": {"filename_prefix": "ComfyUI", "images": ["8", 0]}, "class_type": "SaveImage", "_meta": {"title": "Save Image"}},
  "16": {"inputs": {"text": "Project Eve reference portrait", "clip": ["4", 1]}, "class_type": "CLIPTextEncode", "_meta": {"title": "Positive Prompt"}},
  "40": {"inputs": {"text": "blurry, low quality", "clip": ["4", 1]}, "class_type": "CLIPTextEncode", "_meta": {"title": "Negative Prompt"}},
  "53": {"inputs": {"width": 768, "height": 1152, "batch_size": 1}, "class_type": "EmptySD3LatentImage", "_meta": {"title": "EmptySD3LatentImage"}}
}
""";
}
