using Microsoft.Extensions.Options;
using ProjectEve.NpcStudio.Models;
using System.Net.Http.Json;
using System.Net.Http.Headers;
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
            string comfyReferenceName = "";

            if (!string.IsNullOrWhiteSpace(request.ReferenceImagePath))
            {
                if (!File.Exists(request.ReferenceImagePath))
                {
                    throw new FileNotFoundException(
                        "Canonical reference image does not exist.",
                        request.ReferenceImagePath);
                }

                comfyReferenceName =
                    await UploadReferenceImageAsync(request.ReferenceImagePath);
            }
            else if (RequiresRealReference(request.ImageType))
            {
                throw new InvalidOperationException(
                    $"{request.ImageType} requires a real approved Profile/Front reference image. " +
                    "Project Eve will not generate this reference from text alone.");
            }

            var workflow = BuildWorkflowJson(
                request,
                comfyReferenceName,
                out var workflowUsed,
                out var savePrefix);

            var payload = new JsonObject
            {
                ["prompt"] = workflow,
                ["client_id"] = "ProjectEve.NpcStudio"
            };

            using var response =
                await _http.PostAsJsonAsync(BuildUrl("/prompt"), payload);

            var raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new NpcComfyGenerationResult
                {
                    Success = false,
                    Message =
                        $"Comfy returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                    RawResponse = raw,
                    WorkflowUsed = workflowUsed,
                    SavePrefix = savePrefix
                };
            }

            return new NpcComfyGenerationResult
            {
                Success = true,
                Message = string.IsNullOrWhiteSpace(comfyReferenceName)
                    ? "Queued Comfy generation."
                    : $"Queued reference-conditioned generation using {Path.GetFileName(request.ReferenceImagePath)}.",
                PromptId = ExtractPromptId(raw),
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

    private async Task<string> UploadReferenceImageAsync(string absolutePath)
    {
        await using var stream = File.OpenRead(absolutePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);

        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("image/png");

        content.Add(
            fileContent,
            "image",
            Path.GetFileName(absolutePath));

        content.Add(
            new StringContent("overwrite"),
            "overwrite");

        content.Add(
            new StringContent("input"),
            "type");

        using var response =
            await _http.PostAsync(BuildUrl("/upload/image"), content);

        var raw = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var name =
            root.TryGetProperty("name", out var nameNode)
                ? nameNode.GetString() ?? ""
                : "";

        var subfolder =
            root.TryGetProperty("subfolder", out var subNode)
                ? subNode.GetString() ?? ""
                : "";

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                "Comfy did not return the uploaded reference filename.");
        }

        return string.IsNullOrWhiteSpace(subfolder)
            ? name
            : subfolder.TrimEnd('/', '\\') + "/" + name;
    }

    private static bool RequiresRealReference(string? imageType) =>
        imageType is "SideReference" or "BodyReference";

    public async Task<string> GetPromptStatusAsync(string promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId))
            return "Ready";

        try
        {
            using var response = await _http.GetAsync(
                BuildUrl("/history/" + Uri.EscapeDataString(promptId)));

            if (!response.IsSuccessStatusCode)
                return "Making";

            var raw = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(raw) || raw.Trim() == "{}")
                return "Making";

            using var doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty(promptId, out var history))
                return "Making";

            if (history.TryGetProperty("status", out var status))
            {
                if (status.TryGetProperty("status_str", out var statusText))
                {
                    var value = statusText.GetString() ?? "";

                    if (value.Contains(
                        "error",
                        StringComparison.OrdinalIgnoreCase))
                        return "Failed";
                }

                if (status.TryGetProperty("completed", out var completed) &&
                    completed.ValueKind == JsonValueKind.True)
                    return "Complete";
            }

            if (history.TryGetProperty("outputs", out var outputs) &&
                outputs.ValueKind == JsonValueKind.Object &&
                outputs.EnumerateObject().Any())
                return "Complete";

            return "Making";
        }
        catch
        {
            // A temporary history/read failure should not mark the image failed.
            return "Making";
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

    private static JsonObject BuildWorkflowJson(
        NpcComfyGenerationRequest request,
        string comfyReferenceName,
        out string workflowUsed,
        out string savePrefix)
    {
        var workflow = LoadWorkflowTemplate(request, out workflowUsed);

        JsonObject? FindByClass(params string[] names)
        {
            foreach (var pair in workflow)
            {
                if (pair.Value is not JsonObject obj)
                    continue;

                var classType = obj["class_type"]?.GetValue<string>() ?? "";
                if (names.Any(x => string.Equals(x, classType, StringComparison.OrdinalIgnoreCase)))
                    return obj;
            }

            return null;
        }

        JsonObject? FindByTitle(string text)
        {
            foreach (var pair in workflow)
            {
                if (pair.Value is not JsonObject obj)
                    continue;

                var title = obj["_meta"]?["title"]?.GetValue<string>() ?? "";
                if (title.Contains(text, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }

            return null;
        }

        JsonObject Inputs(JsonObject node, string label) =>
            RequireObject(node, "inputs", label + " inputs");

        var positive = FindByTitle("Positive") ?? FindByClass("CLIPTextEncode", "TextEncodeQwenImageEditPlus");
        if (positive is null)
            throw new InvalidOperationException("Workflow has no positive prompt node.");

        var pi = Inputs(positive, "Positive Prompt");
        if (pi.ContainsKey("text"))
            pi["text"] = request.PositivePrompt ?? "";
        else if (pi.ContainsKey("prompt"))
            pi["prompt"] = request.PositivePrompt ?? "";

        var negative = FindByTitle("Negative");

        if (negative is null &&
            string.Equals(
                positive["class_type"]?.GetValue<string>(),
                "TextEncodeQwenImageEditPlus",
                StringComparison.OrdinalIgnoreCase))
        {
            negative = workflow
                .Select(x => x.Value as JsonObject)
                .Where(x => x is not null && !ReferenceEquals(x, positive))
                .FirstOrDefault(x =>
                    string.Equals(
                        x!["class_type"]?.GetValue<string>(),
                        "TextEncodeQwenImageEditPlus",
                        StringComparison.OrdinalIgnoreCase));
        }

        if (negative is not null)
        {
            var ni = Inputs(negative, "Negative Prompt");

            if (ni.ContainsKey("text"))
                ni["text"] = request.NegativePrompt ?? "";
            else if (ni.ContainsKey("prompt"))
                ni["prompt"] = request.NegativePrompt ?? "";
        }

        var sampler = FindByClass("KSampler")
            ?? throw new InvalidOperationException("Workflow has no KSampler.");

        var si = Inputs(sampler, "KSampler");
        si["seed"] = ResolveSeed(request.Seed);

        if (si["steps"] is JsonValue && request.Steps > 0)
            si["steps"] = request.Steps;

        if (si["cfg"] is JsonValue && request.Cfg > 0)
            si["cfg"] = request.Cfg;

        savePrefix = BuildSavePrefix(request);

        var save = FindByClass("SaveImage", "SaveImageAdvanced")
            ?? throw new InvalidOperationException("Workflow has no SaveImage node.");

        Inputs(save, "Save Image")["filename_prefix"] = savePrefix;

        if (!string.IsNullOrWhiteSpace(comfyReferenceName))
        {
            var loadImage = FindByClass("LoadImage");

            if (loadImage is not null)
            {
                Inputs(loadImage, "Load Image")["image"] = comfyReferenceName;
            }
            else
            {
                InjectReferenceImageConditioning(workflow, request, comfyReferenceName);
            }
        }

        var latent = FindByClass("EmptySD3LatentImage");

        if (latent is not null)
        {
            var li = Inputs(latent, "Empty Latent");

            if (request.Width > 0)
                li["width"] = request.Width;

            if (request.Height > 0)
                li["height"] = request.Height;

            li["batch_size"] = 1;
        }

        return workflow;
    }

    private static void InjectReferenceImageConditioning(
        JsonObject workflow,
        NpcComfyGenerationRequest request,
        string comfyReferenceName)
    {
        var samplerPair = workflow.FirstOrDefault(x =>
            x.Value is JsonObject obj &&
            string.Equals(
                obj["class_type"]?.GetValue<string>(),
                "KSampler",
                StringComparison.OrdinalIgnoreCase));

        if (samplerPair.Value is not JsonObject sampler)
        {
            throw new InvalidOperationException(
                "Reference workflow has no KSampler node.");
        }

        JsonArray? vaeConnection = null;

        foreach (var pair in workflow)
        {
            if (pair.Value is not JsonObject obj)
                continue;

            if (!string.Equals(
                    obj["class_type"]?.GetValue<string>(),
                    "VAEDecode",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (obj["inputs"] is JsonObject inputs &&
                inputs["vae"] is JsonArray vae)
            {
                vaeConnection = vae.DeepClone().AsArray();
                break;
            }
        }

        if (vaeConnection is null)
        {
            throw new InvalidOperationException(
                "Reference workflow has no reusable VAE connection.");
        }

        var loadId = NextNumericNodeId(workflow);

        var encodeNumber = long.Parse(loadId) + 1;
        while (workflow.ContainsKey(encodeNumber.ToString()))
            encodeNumber++;

        var encodeId = encodeNumber.ToString();

        workflow[loadId] = new JsonObject
        {
            ["inputs"] = new JsonObject
            {
                ["image"] = comfyReferenceName
            },
            ["class_type"] = "LoadImage",
            ["_meta"] = new JsonObject
            {
                ["title"] =
                    "ProjectEve Canonical Identity Reference"
            }
        };

        workflow[encodeId] = new JsonObject
        {
            ["inputs"] = new JsonObject
            {
                ["pixels"] = new JsonArray(loadId, 0),
                ["vae"] = vaeConnection
            },
            ["class_type"] = "VAEEncode",
            ["_meta"] = new JsonObject
            {
                ["title"] =
                    "ProjectEve Reference VAE Encode"
            }
        };

        var samplerInputs =
            RequireObject(
                sampler,
                "inputs",
                "KSampler inputs");

        samplerInputs["latent_image"] =
            new JsonArray(encodeId, 0);

        samplerInputs["denoise"] =
            ResolveReferenceDenoise(request);
    }

    private static double ResolveReferenceDenoise(
        NpcComfyGenerationRequest request)
    {
        if (request.ReferenceDenoise > 0 &&
            request.ReferenceDenoise <= 1)
        {
            return request.ReferenceDenoise;
        }

        return request.ImageType switch
        {
            "FrontReference" => 0.48,
            "SideReference" => 0.72,
            "BodyReference" => 0.80,
            _ => 0.65
        };
    }

    private static string NextNumericNodeId(
        JsonObject workflow)
    {
        long max = 1000;

        foreach (var key in workflow.Select(x => x.Key))
        {
            if (long.TryParse(key, out var parsed) &&
                parsed > max)
            {
                max = parsed;
            }
        }

        var candidate = max + 1;

        while (workflow.ContainsKey(candidate.ToString()))
            candidate++;

        return candidate.ToString();
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
