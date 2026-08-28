# ProjectEve.NpcStudio V0.1

This is the first ground-up NPC / Voice Studio scaffold.

## What this first round includes

- Dashboard
- NPC Browser
- NPC Character Sheet
- Relationship Viewer
- Comfy/Image prep tab
- Voice prep tab
- Prompt Engineer tab using Ollama
- Traits view
- Revision history
- Schema updater for Studio tables/columns

## Database used

```text
D:\ProjectEveData\Database\project_eve.db
```

## Important

The console seeder still builds the town.  
NPC Studio reads and manages the town after it exists.

## Required NuGet package

Install this in the ProjectEve.NpcStudio project if it is not already installed:

```powershell
dotnet add package Microsoft.Data.Sqlite
```

## Ollama

Default model in Program.cs:

```text
qwen2.5
```

Run:

```powershell
ollama pull qwen2.5
ollama serve
```

You can change the model in Program.cs.

## Routes

```text
/npc-studio
/npc-studio/browser
/npc-studio/npc/1
```

## CSS

Add this to your Components/App.razor or main layout if needed:

```html
<link rel="stylesheet" href="npcstudio.css" />
```

Some Blazor templates already load site CSS in `Components/App.razor`.
