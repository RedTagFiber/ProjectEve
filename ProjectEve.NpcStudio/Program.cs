using ProjectEve.NpcStudio.Components;
using ProjectEve.NpcStudio.Data;
using ProjectEve.NpcStudio.Models;
using ProjectEve.NpcStudio.Services;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------
// NPC Studio V0.2
// Separate from the console seeder.
// Seeder builds the town; NPC Studio reads and manages it.
// ------------------------------------------------------------

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton(new NpcStudioOptions
{
    MainDbPath = @"D:\ProjectEveData\Database\project_eve.db",
    HistoryDbPath = @"D:\ProjectEveData\Database\project_eve_history.db",
    NpcRoot = @"D:\ProjectEveData\NPC",

    OllamaBaseUrl = "http://localhost:11434",
    OllamaModel = "qwen2.5",

    ComfyBaseUrl = "http://127.0.0.1:8188"
});

builder.Services.AddSingleton<NpcStudioSchema>();
builder.Services.AddScoped<NpcStudioRepository>();
builder.Services.AddScoped<NpcStudioService>();
builder.Services.AddScoped<SchoolSystemService>();
builder.Services.AddScoped<BhsStaffDraftService>();
builder.Services.AddScoped<BhsStaffPersistenceService>();
builder.Services.AddScoped<AiEducationProfessionalAssistService>();
builder.Services.AddScoped<FirstNpcCreationService>();
builder.Services.AddScoped<FamilyIntegrityGuardService>();
builder.Services.AddScoped<CanonicalFamilyMigrationService>();
builder.Services.AddScoped<CanonicalFamilyGraphService>();
builder.Services.AddScoped<CanonicalKinshipTitleService>();
builder.Services.AddScoped<FullFamilyTreePreviewService>();
builder.Services.AddScoped<FullFamilyTreeBuildService>();
builder.Services.AddScoped<CanonicalKinshipCacheRepairService>();
builder.Services.AddScoped<FamilyBuildOrchestratorService>();
builder.Services.AddScoped<BellefontaineHousingInventoryService>();
builder.Services.AddScoped<NpcFoundationBuildService>();
builder.Services.AddScoped<CanonicalFamilyRepairService>();
builder.Services.AddScoped<NpcFamilyBuilderService>();
builder.Services.AddScoped<RelationshipCandidateService>();
builder.Services.AddScoped<FamilyGraphResolverService>();
builder.Services.AddScoped<FamilyNpcFactoryPreviewService>();

builder.Services.AddHttpClient<OllamaPromptEngineerService>();
builder.Services.AddHttpClient<AiNpcProfileBuilderService>();
builder.Services.AddHttpClient<AiAppearanceProfileAssistService>();
builder.Services.AddHttpClient<ComfyStudioService>();
builder.Services.AddHttpClient<ComfyWorkflowService>();
builder.Services.AddScoped<NpcFileSystemService>();


builder.Services.AddSingleton<ProjectEve.NpcStudio.Services.MediaStorageService>();

builder.Services.AddSingleton<ProjectEve.NpcStudio.Services.NpcAppearanceDetailService>();
builder.Services.AddSingleton<TraitCompatibilityService>();
builder.Services.AddSingleton<AiTraitPopulationService>();
builder.Services.AddSingleton<AiTraitReasonService>();
builder.Services.AddSingleton<AiFastTraitAlignmentService>();

var app = builder.Build();

// Make sure all V0.2 tables and columns exist at startup.
// This does not erase existing seed data.
using (var scope = app.Services.CreateScope())
{
    var schema = scope.ServiceProvider.GetRequiredService<NpcStudioSchema>();
    schema.Ensure();

    var options = scope.ServiceProvider.GetRequiredService<NpcStudioOptions>();
    NpcStudioCompleteProfileSchema.Ensure(options);
    NpcStudioSchoolSchema.Ensure(options);
    NpcStudioRelationshipSchema.Ensure(options);
    NpcStudioFamilySchemaCompatibility.Ensure(options);
    var schoolSystem = scope.ServiceProvider.GetRequiredService<SchoolSystemService>();
    schoolSystem.SeedMaster();
    NpcFamilyIdentityIntegritySchema.Ensure(options);
    // CLEAN FAMILY SYSTEM: legacy migration/repair is no longer run automatically.
}


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();


// Preview approved/generated NPC media without copying it into wwwroot.
// Security: only files under D:\ProjectEveData can be served.
app.MapGet("/npc-media", (string path) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest("Missing path.");

    var root = Path.GetFullPath(@"D:\ProjectEveData").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    var full = Path.GetFullPath(path);
    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        return Results.NotFound();

    var ext = Path.GetExtension(full).ToLowerInvariant();
    var contentType = ext switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream"
    };

    return Results.File(full, contentType, enableRangeProcessing: true);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();





















