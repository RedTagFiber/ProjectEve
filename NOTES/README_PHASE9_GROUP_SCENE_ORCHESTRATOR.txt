PROJECT EVE — PHASE 9
MULTI-NPC GROUP SCENE ORCHESTRATOR

PURPOSE
Phase 6 answered: who physically saw/heard this?
Phase 7 answered: what does each NPC personally know/believe?
Phase 8 answered: would an NPC share/hide/gossip/confront about known information?
Phase 9 answers: in a live scene with several people, WHO responds, in what order,
and what does each separate mind actually perceive before it responds?

LOCKED RULES IMPLEMENTED
- Practical scene target: 1–5 NPCs normally, up to 10 NPCs + 2 players.
- Up to 10 NPCs can be cheap-scored for response interest.
- Default maximum full Brain calls per player turn: 3.
- One Brain call = one NPC mind only.
- Responders are generated sequentially so later NPCs can perceive earlier NPC replies.
- Exact world transcript is stored separately from observer-specific perception.
- Hidden NPCs can overhear but are not automatically exposed to a player's view.
- An NPC's Brain context is reconstructed from only that NPC's perceived scene history.
- Phase 7 personal knowledge context is added separately.
- No Brain.cs replacement.
- No app.css replacement.
- No InPerson.razor replacement in this phase.

WHY INPERSON.RAZOR IS NOT REPLACED YET
The current In Person page still owns a player-scoped direct-Eve scene. Replacing it
immediately with a shared multi-person scene would create a presence-ownership bug:
Player 1 leaving a shared scene could accidentally remove an NPC that Player 2 is
still talking to. Phase 9 therefore builds and persists the server orchestrator first.

NEXT PHASE
Phase 10 should wire the approved In Person UI to this orchestrator AND introduce
shared scene-presence ownership/leases so:
- two players can occupy the same location safely,
- NPC presence is server/world-owned instead of page-owned,
- left-panel distance comes from the shared scene,
- the current plain-text colored scene stream can display multiple NPC replies.

DATABASE TABLES CREATED AUTOMATICALLY
- GroupSceneSession
- GroupSceneEntry
- GroupScenePerception

NO MANUAL SQL MIGRATION IS REQUIRED.
