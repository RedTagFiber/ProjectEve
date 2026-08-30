# ProjectEve.NpcStudio Phase 8

Phase 8 adds the first approval workflow for Comfy images.

## New route

`/npc-studio/image-approval`

## What it does

- Loads an NPC's image generation history from `NpcImageGenerations`.
- Lets you select a queued/saved image generation record.
- Lets you paste the final image path from Comfy.
- Approves the image record.
- Optionally marks it as the current reference image.
- If marked as current reference, it updates `NpcAppearanceProfiles.ReferenceImagePath`, sets `AppearanceStatus = Approved`, and sets `Approved = 1`.
- Adds a revision row so you can see the image approval history.

## Files included

- `Data/NpcStudioRepository.cs`
- `Services/NpcStudioService.cs`
- `Components/Pages/ImageApprovalRoom.razor`
- `Components/Layout/NavMenu.razor`
- `Models/NpcStudioModels.cs`

## Test path

1. Build solution.
2. Open `/npc-studio/image-approval`.
3. Load NPC `1`.
4. Select an image generation record.
5. Paste a final image path.
6. Approve it as current reference.
7. Open `/npc-studio/npc/1` and check Appearance / Comfy.
