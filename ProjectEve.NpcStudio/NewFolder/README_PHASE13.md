# ProjectEve.NpcStudio Phase 13 — Real SD3.5 Comfy Workflow

This phase replaces the placeholder Comfy workflow with the exported SD3.5 workflow that works manually in ComfyUI.

## Copy / replace

Copy these files into your `ProjectEve.NpcStudio` project:

```text
Services/NpcStudioWorkflowModels.cs
Services/ComfyWorkflowService.cs
Components/Pages/ComfyControlRoom.razor
```

Optional but recommended: copy the workflow JSON to:

```text
D:\ProjectEveData\ComfyWorkflows\ProjectEve_SD35_ReferencePortrait_V01.json
```

The same workflow is also included in this zip under:

```text
ComfyWorkflows/ProjectEve_SD35_ReferencePortrait_V01.json
```

## Working node map

Phase 13 uses your real node ids:

```text
3  = KSampler
4  = CheckpointLoaderSimple
8  = VAE Decode
9  = Save Image
16 = Positive Prompt
40 = Negative Prompt
53 = EmptySD3LatentImage
```

## Default SD3.5 settings

```text
Checkpoint: sd3.5_large_fp8_scaled.safetensors
Width: 768
Height: 1152
Steps: 32
CFG: 5
Sampler: dpm_adaptive
Scheduler: karras
Seed: -1 means random
```

## Test order

1. Start ComfyUI.
2. Start NPC Studio.
3. Open `/npc-studio/comfy`.
4. Click `Test Comfy`.
5. Load NPC 1.
6. Click `Build Starter Prompt From NPC`.
7. Queue photo.
8. Check ComfyUI output folder.
9. Paste final file path into Image Approval.

## Notes

The browser preview only appears after a real image file exists under:

```text
D:\ProjectEveData
```

Comfy itself saves to its output folder unless your workflow uses an output path custom node. This phase queues the correct SD3.5 graph; later phases can add automatic output polling and automatic copying into NPC folders.
