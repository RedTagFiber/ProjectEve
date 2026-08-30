PROJECT EVE - CLEAN NPC RESTART

WHAT THIS DOES
- Creates a timestamped backup of D:\ProjectEveData\Database\project_eve.db
- Permanently removes every old NPC record and NPC-linked record from project_eve.db
- Removes D:\ProjectEveData\NPC and recreates it clean
- Removes old D:\ProjectEveData\Comfy\Temp\ProjectEve\NPC_* temp folders
- Keeps World Builder code and Locations data
- Seeds exactly one NPC: Eve Sinclair, NPC ID 1
- Creates Eve's new empty NPC folder structure

SAFETY
The utility uses the locked 3-warning purge design:
1. Type DELETE
2. Type NPC
3. Type PURGE ALL NPCS

RUN
1. Close Project Eve / NPC Studio / Visual Studio debugging.
2. Extract this ZIP somewhere convenient.
3. Right-click RESET_ALL_NPCS.ps1 -> Run with PowerShell
   OR from PowerShell:
      .\RESET_ALL_NPCS.ps1
4. After it finishes, reopen NPC Studio.

NEXT BUILD ORDER
1. Eve only - complete the full dossier and verify every system.
2. Add Eve's brother.
3. Add Eve's mother.
4. Add Eve's father.
5. Once those four are correct, add NPCs in batches of 10 and validate each batch.
