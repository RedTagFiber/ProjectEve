# NPC Studio V0.3 Phase 3

This phase turns the working V0.2 app into a more usable Studio.

## Copy these files into your project

- `Components/Layout/NavMenu.razor`
- `Components/Pages/NpcStudioDashboard.razor`
- `Components/Pages/NpcBrowser.razor`
- `Components/Pages/NpcQueuePage.razor`
- `Components/Pages/RelationshipMap.razor`
- `wwwroot/npcstudio.css`

## New routes

- `/npc-studio`
- `/npc-studio/browser`
- `/npc-studio/needs-image`
- `/npc-studio/needs-voice`
- `/npc-studio/relationship-map`
- `/npc-studio/npc/1`

## What changed

- Replaces the default left menu with NPC Studio links.
- Adds Needs Image and Needs Voice queues.
- Adds Relationship Map page.
- Improves Dashboard layout.
- Improves Browser filters and status badges.
- Adds stronger CSS polish for V0.3.

## After copying

Build the solution, run the app, then open:

`https://localhost:7067/npc-studio`
