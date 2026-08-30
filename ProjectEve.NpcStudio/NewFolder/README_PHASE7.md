# ProjectEve.NpcStudio Phase 7

Phase 7 connects the creative workflow back into the database.

## What this phase adds

- Prompt Engineer Lab can save Ollama output to the NPC's AI Ideas.
- Prompt Engineer Lab also saves prompt history to `NpcPromptGenerations`.
- Prompt Engineer Lab can save extracted Comfy positive/negative prompts to the NPC appearance profile.
- Comfy Control Room now saves queued Comfy requests to `NpcImageGenerations`.
- Repository/service methods added for prompt history, appearance prompt saving, and image generation history.
- `Models/NpcStudioModels.cs` is included again and includes `NpcPromptGeneration`.

## Files to replace

```text
Models/NpcStudioModels.cs
Data/NpcStudioRepository.cs
Services/NpcStudioService.cs
Components/Pages/PromptEngineerLab.razor
Components/Pages/ComfyControlRoom.razor
```

## Test order

1. Build solution.
2. Open `/npc-studio/prompt-lab`.
3. Analyze NPC 1.
4. Click `Save AI Idea to NPC`.
5. Click `Save Comfy Prompt to Appearance`.
6. Open `/npc-studio/npc/1` and check the Prompt Engineer/Comfy tabs.
7. Open `/npc-studio/comfy`.
8. Queue a test request.
9. Open `/npc-studio/npc/1` and check image history/revisions.

## Notes

The Comfy workflow JSON is still the placeholder workflow from Phase 4. This phase saves the queue request and its metadata. The next phase can wire your exported Comfy workflow and real image-file retrieval.
