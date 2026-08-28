namespace ProjectEve.NpcStudio.Services;

using ProjectEve.NpcStudio.Models;
public sealed class ComfyStudioService
{
    private readonly HttpClient _http;
    private readonly NpcStudioOptions _options;

    public ComfyStudioService(HttpClient http, NpcStudioOptions options)
    {
        _http = http;
        _options = options;
    }

    // V0.1 is Comfy-ready but does not force a workflow yet.
    // Next step: add your exact Comfy workflow JSON and map prompt/seed into it.
    public Task<string> GetComfyStatusAsync()
    {
        return Task.FromResult($"Comfy endpoint configured: {_options.ComfyBaseUrl}");
    }
}
