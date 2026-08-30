# ProjectEve.NpcStudio Phase 15 — NPC File System Finalize

Phase 15 makes the image approval workflow safer and closer to the final Project Eve asset pipeline.

## What it adds

- `NpcFileSystemService`
- Creates the full NPC folder layout
- Finds newest Comfy output for an NPC
- Validates local image paths
- Copies an approved Comfy image into the NPC case-file folder
- Saves the approved reference path back through existing image approval logic

## Files to copy

```text
Services/NpcFileSystemService.cs
Components/Pages/ImageApprovalRoom.razor
wwwroot/npcstudio.phase15.additions.css
```

Append the CSS additions to the bottom of your existing:

```text
wwwroot/npcstudio.css
```

## Program.cs registration

Add this near your other services:

```csharp
builder.Services.AddScoped<NpcFileSystemService>();
```

Make sure Program.cs already has:

```csharp
using ProjectEve.NpcStudio.Services;
```

## Approval flow

1. Open `/npc-studio/image-approval`
2. Load NPC 1
3. Use Newest Comfy Output
4. Check Preview
5. Keep `Copy approved image into NPC case-file folder` checked
6. Approve Image
7. Open Eve profile

The approved image will be copied to:

```text
D:\ProjectEveData\NPC\000001_Eve_Sinclair\images\reference\approved_reference.png
```

The database reference path should then point to that approved file.

## Folder layout created

Each NPC receives:

```text
dossier
images/reference
images/profile
images/contact
images/in_person
images/social
images/rejected
images/thumbnails
voice/reference
voice/samples
voice/generated
voice/presets
voice/scripts
relationships
traits
prompts
comfy/workflows
comfy/requests
comfy/outputs
comfy/metadata
notes
revisions
exports
temp
```
