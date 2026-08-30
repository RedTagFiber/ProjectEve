using ProjectEve.NpcStudio.Models;

namespace ProjectEve.NpcStudio.Services;

// ------------------------------------------------------------
// NPC Studio File System Service
// Phase 15
//
// Purpose:
// - Keep every NPC's creative assets in one predictable folder.
// - Copy approved Comfy outputs into the NPC case-file folder.
// - Create image/voice/prompt/relationship/trait/revision folders.
// - Help Studio find the newest Comfy output for a specific NPC.
// ------------------------------------------------------------
public sealed class NpcFileSystemService
{
    public const string ProjectEveDataRoot = @"D:\ProjectEveData";
    public const string NpcRoot = @"D:\ProjectEveData\NPC";
    public const string ComfyTempRoot = @"D:\ProjectEveData\Comfy\Temp";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };

    public Task EnsureNpcFolderLayoutAsync(NpcCharacterSheet sheet)
    {
        var root = GetNpcFolderPath(sheet);

        // Main case-file sections.
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "dossier"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        Directory.CreateDirectory(Path.Combine(root, "voice"));
        Directory.CreateDirectory(Path.Combine(root, "relationships"));
        Directory.CreateDirectory(Path.Combine(root, "traits"));
        Directory.CreateDirectory(Path.Combine(root, "prompts"));
        Directory.CreateDirectory(Path.Combine(root, "comfy"));
        Directory.CreateDirectory(Path.Combine(root, "notes"));
        Directory.CreateDirectory(Path.Combine(root, "revisions"));
        Directory.CreateDirectory(Path.Combine(root, "exports"));
        Directory.CreateDirectory(Path.Combine(root, "temp"));

        // Image slots.
        Directory.CreateDirectory(Path.Combine(root, "images", "reference"));
        Directory.CreateDirectory(Path.Combine(root, "images", "profile"));
        Directory.CreateDirectory(Path.Combine(root, "images", "contact"));
        Directory.CreateDirectory(Path.Combine(root, "images", "in_person"));
        Directory.CreateDirectory(Path.Combine(root, "images", "social"));
        Directory.CreateDirectory(Path.Combine(root, "images", "rejected"));
        Directory.CreateDirectory(Path.Combine(root, "images", "thumbnails"));

        // Voice slots.
        Directory.CreateDirectory(Path.Combine(root, "voice", "reference"));
        Directory.CreateDirectory(Path.Combine(root, "voice", "samples"));
        Directory.CreateDirectory(Path.Combine(root, "voice", "generated"));
        Directory.CreateDirectory(Path.Combine(root, "voice", "presets"));
        Directory.CreateDirectory(Path.Combine(root, "voice", "scripts"));

        // Comfy work area inside the NPC folder.
        Directory.CreateDirectory(Path.Combine(root, "comfy", "workflows"));
        Directory.CreateDirectory(Path.Combine(root, "comfy", "requests"));
        Directory.CreateDirectory(Path.Combine(root, "comfy", "outputs"));
        Directory.CreateDirectory(Path.Combine(root, "comfy", "metadata"));

        return Task.CompletedTask;
    }

    public string GetNpcFolderPath(NpcCharacterSheet sheet)
    {
        if (!string.IsNullOrWhiteSpace(sheet.FolderPath))
        {
            return sheet.FolderPath.Trim();
        }

        return Path.Combine(NpcRoot, $"{sheet.Id:D6}_{SafeName(sheet.Name)}");
    }

    public string GetReferenceFolderPath(NpcCharacterSheet sheet)
    {
        return Path.Combine(GetNpcFolderPath(sheet), "images", "reference");
    }

    public async Task<string> CopyImageToNpcReferenceAsync(NpcCharacterSheet sheet, string sourceImagePath)
    {
        var validation = ValidateImagePath(sourceImagePath);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        await EnsureNpcFolderLayoutAsync(sheet);

        var ext = Path.GetExtension(validation.FullPath).ToLowerInvariant();
        var referenceFolder = GetReferenceFolderPath(sheet);
        Directory.CreateDirectory(referenceFolder);

        var safeName = SafeName(sheet.Name).ToLowerInvariant();
        var datedFile = Path.Combine(referenceFolder, $"{safeName}_reference_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
        var approvedFile = Path.Combine(referenceFolder, $"approved_reference{ext}");

        File.Copy(validation.FullPath, datedFile, overwrite: true);
        File.Copy(validation.FullPath, approvedFile, overwrite: true);

        await WriteTextSafeAsync(
            Path.Combine(GetNpcFolderPath(sheet), "images", "reference", "last_approved_source.txt"),
            $"Approved source: {validation.FullPath}{Environment.NewLine}Copied at: {DateTime.Now:O}{Environment.NewLine}");

        return approvedFile;
    }

    public async Task WritePromptFileAsync(NpcCharacterSheet sheet, string fileName, string text)
    {
        await EnsureNpcFolderLayoutAsync(sheet);
        var folder = Path.Combine(GetNpcFolderPath(sheet), "prompts");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, SafeName(fileName)), text ?? "");
    }

    public string FindNewestComfyOutput(int npcId)
    {
        var npcToken = $"NPC_{npcId:D6}";

        // Old layout first: D:\ProjectEveData\Comfy\Temp\ProjectEve\NPC_000002
        var legacyNpcFolder = Path.Combine(ComfyTempRoot, "ProjectEve", npcToken);
        var found = FindNewestImageFile(legacyNpcFolder);
        if (!string.IsNullOrWhiteSpace(found))
            return found;

        // New School/Family generation layouts can place NPC folders deeper:
        // ProjectEve\SchoolSystem\BHS\NPC_000002\...
        // ProjectEve\Families\Household_000001\NPC_000002\...
        // Search only paths that contain THIS NPC token so another household
        // member's newer image cannot be mistaken for this NPC.
        try
        {
            if (Directory.Exists(ComfyTempRoot))
            {
                var npcSpecific = Directory
                    .EnumerateFiles(ComfyTempRoot, "*.*", SearchOption.AllDirectories)
                    .Where(path =>
                        ImageExtensions.Contains(Path.GetExtension(path)) &&
                        path.Contains(npcToken, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(npcSpecific))
                    return npcSpecific;
            }
        }
        catch
        {
            // Approval room will report that no NPC-specific output was found.
        }

        return "";
    }

    public ImagePathValidation ValidateImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ImagePathValidation.Fail("Paste a full image file path first.");

        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return ImagePathValidation.Fail("Use a local file path, not a web URL.");

        string fullPath;
        string allowedRoot;

        try
        {
            fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            allowedRoot = Path.GetFullPath(ProjectEveDataRoot);
        }
        catch (Exception ex)
        {
            return ImagePathValidation.Fail("Invalid path: " + ex.Message);
        }

        if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            return ImagePathValidation.Fail("Blocked path. Images must be under " + ProjectEveDataRoot + ".");

        var ext = Path.GetExtension(fullPath);
        if (!ImageExtensions.Contains(ext))
            return ImagePathValidation.Fail("Unsupported image extension. Use .png, .jpg, .jpeg, .webp, or .gif.");

        if (!File.Exists(fullPath))
            return ImagePathValidation.Fail("File not found yet: " + fullPath);

        return ImagePathValidation.Ok(fullPath);
    }

    private static string FindNewestImageFile(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return "";

            return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static async Task WriteTextSafeAsync(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ProjectEveDataRoot);
        await File.WriteAllTextAsync(path, text);
    }

    public static string SafeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "npc";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        cleaned = cleaned.Replace(' ', '_');

        while (cleaned.Contains("__", StringComparison.Ordinal))
            cleaned = cleaned.Replace("__", "_");

        return cleaned.Trim('_');
    }
}

public sealed class ImagePathValidation
{
    public bool IsValid { get; init; }
    public string FullPath { get; init; } = "";
    public string Message { get; init; } = "";

    public static ImagePathValidation Ok(string fullPath) => new()
    {
        IsValid = true,
        FullPath = fullPath,
        Message = "OK"
    };

    public static ImagePathValidation Fail(string message) => new()
    {
        IsValid = false,
        Message = message
    };
}

