# ProjectEve.NpcStudio Phase 4

Phase 4 adds the first real AI/Comfy control layer:

- Prompt Engineer Lab route: `/npc-studio/prompt-lab`
- Comfy Control Room route: `/npc-studio/comfy`
- Comfy connection test
- Comfy `/prompt` queue call shape
- Shared workflow request/result models

## Files to copy

Copy these into your project:

```text
Services/NpcStudioWorkflowModels.cs
Services/ComfyWorkflowService.cs
Components/Pages/ComfyControlRoom.razor
Components/Pages/PromptEngineerLab.razor
```

## Add this to Program.cs

```csharp
builder.Services.AddHttpClient<ComfyWorkflowService>();
```

Put it near your existing `AddHttpClient<ComfyStudioService>()` line.

## Add menu links to NavMenu.razor

```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="npc-studio/prompt-lab">
        Prompt Lab
    </NavLink>
</div>

<div class="nav-item px-3">
    <NavLink class="nav-link" href="npc-studio/comfy">
        Comfy Room
    </NavLink>
</div>
```

## Test routes

```text
/npc-studio/prompt-lab
/npc-studio/comfy
```

## Note about Comfy

The Comfy workflow in this phase is a placeholder. It wires the UI and HTTP call correctly, but your real Comfy workflow may use different node IDs/checkpoints/custom nodes. Once you export your real workflow JSON from ComfyUI, replace `BuildWorkflowJson` inside `ComfyWorkflowService.cs`.
