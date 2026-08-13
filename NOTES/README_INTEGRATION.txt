PROJECT EVE — POPULATION / EMPLOYMENT / FAMILY-FRIEND WEB INTEGRATION
=====================================================================

WHAT THIS PACK DOES
-------------------
This is not a second NPC system. It bridges the new job/workplace data into the
ProjectEve structures already in the repository:

- SimCharacter
- JobProfile
- CharacterFactory / CharacterRepository
- Relationships table / Relationship class
- SQLite project_eve.db
- SmallTownScheduleSystem
- SmallTownWorldManager

WHY THIS BRIDGE IS NEEDED
-------------------------
The current ProjectEve code has a useful JobProfile and CharacterFactory job loader,
but npcgen in Program.cs still hard-codes lane -> occupation:

    shop -> Barista
    crew -> Firefighter
    art -> Artist
    school -> Office worker

and then directly invents employer, pay, and 30 weekly hours.

The new system instead:
1. Loads project_eve_all_jobs_master.json.
2. Loads project_eve_town_workplaces.json.
3. Creates every real job slot in SQLite.
4. Assigns an NPC only to a vacant slot.
5. Copies job facts into the existing npc.Job (JobProfile).
6. Saves the exact workplace/job/slot and exact time schedule in NpcEmployment.
7. Uses the normal CharacterRepository.SaveJob(npc).

FILES / WHERE TO PLACE THEM
---------------------------
Data/World/Ohio/project_eve_all_jobs_master.json
Data/World/Ohio/project_eve_town_workplaces.json
Data/World/Ohio/population_rules.json

World/SmallTown/Population/SmallTownEmploymentSystem.cs
World/SmallTown/Population/FamilyFriendWebSystem.cs

Replace:
World/SmallTown/SmallTownSystem/SmallTownScheduleSystem.cs

BOOT CONNECTION
---------------
After DatabaseInitializer.Initialize() in BootWorld(), add:

    SmallTownEmploymentSystem.Initialize();
    FamilyFriendWebSystem.Initialize();

This creates the new slot/employment/web tables and syncs the 2,561 job slots.

NPCGEN CONNECTION
-----------------
In Program.cs, BuildConnectedNpc currently creates a generic SimCharacter and then
manually fills npc.Job. Keep the identity/traits/memory/save flow, but replace the
manual job block with:

    string? preferredCategory = lane switch
    {
        "shop" => "restaurants",
        "crew" => "fireEms",
        "school" => "education",
        _ => null
    };

    bool gotJob = SmallTownEmploymentSystem.AssignOpenJob(
        npc,
        preferredCategory,
        rng);

    if (!gotJob)
    {
        npc.Occupation = "Unemployed";
        npc.Job.JobType = "unemployed";
    }

IMPORTANT:
SaveNpcIdentityStub(npc) must still run BEFORE any database operation that has an
NpcId foreign key. Your current Program.cs already follows this rule.

BEST ORDER FOR A NEW DEEP NPC
-----------------------------
1. Build identity (name, age, gender).
2. Assign permanent NpcId.
3. Set home/town and temporary materialization Tier.
4. Save character identity row.
5. EnsureCore / EnsureTraits.
6. Assign a real open job slot.
7. Save JobProfile + NpcEmployment.
8. Build Family/Friend Web links.
9. Save Relationships mirror rows for Tier 1-4.
10. Add memory/history.
11. Save Character state.

FAMILY AND FRIEND WEB
---------------------
Do NOT use SimCharacter.Tier as the only Family/Friend tier.

Why:
Sarah may see Tom as Tier 1.
John may see the same Tom as Tier 2.
Eve may see the same Tom as Tier 4.

One global integer cannot represent all three.

FamilyFriendWebSystem therefore stores:

    OwnerNpcId
    TargetNpcId
    WebTier
    RelationshipType
    IsHistoryOnly

The same TargetNpcId is reused in every web.

Characters.Tier is kept as a cheap global materialization/load priority only.
FamilyFriendWeb.WebTier is the directional relationship truth.

Example:

    FamilyFriendWebSystem.LinkTwoWay(
        sarah.Id,
        tom.Id,
        1, 1,
        "sibling",
        "sibling");

If Tom is married to John's sister Emily:

    FamilyFriendWebSystem.LinkTwoWay(
        tom.Id,
        emily.Id,
        1, 1,
        "spouse",
        "spouse");

    FamilyFriendWebSystem.LinkTwoWay(
        sarah.Id,
        emily.Id,
        2, 2,
        "sister_in_law",
        "sister_in_law");

    FamilyFriendWebSystem.LinkTwoWay(
        john.Id,
        tom.Id,
        2, 2,
        "brother_in_law",
        "brother_in_law");

TIER 5
------
Tier 5 stays a Characters row with identity/history links but no expensive brain,
traits, or memory database population unless needed.

On direct interaction:

    var promoted = FamilyFriendWebSystem.PromoteHistoryPersonToTier4(
        ownerNpcId,
        targetNpcId);

That promotes the SAME person record to Tier 4 and creates the interactive core.
No name reroll. No new sister. No changed former boss.

CHILDREN
--------
Children can also be Characters rows, but they do not need BrainState/Traits/JobProfile.
Store identity + parent/household links only.

When a child later becomes relevant, use the same materialization idea as Tier 5.

SCHEDULE
--------
The old SmallTownScheduleSystem recognizes only a short list of occupation strings
and defaults unknown jobs to 9-5.

The included replacement reads NpcEmployment. This makes all 240 jobs obey their
assigned schedules, including exact half-hours such as 06:00-14:30.

CALL-OFFS
---------
Use:

    SmallTownEmploymentSystem.RecordCallOff(
        npc,
        DateTime.Today,
        "illness");

Late notice:

    SmallTownEmploymentSystem.RecordCallOff(
        npc,
        DateTime.Today,
        "car would not start",
        lateNotice: true);

No call / no show:

    SmallTownEmploymentSystem.RecordCallOff(
        npc,
        DateTime.Today,
        "did not call",
        noCallNoShow: true);

NpcWorkAbsence makes IsNpcWorking(...) return false for that date.

The supplied implementation automatically fires the NPC at the attendance-policy
hard limit, frees the job slot, and marks the NPC unemployed.

ENVIRONMENT VARIABLES (OPTIONAL)
--------------------------------
EVE_JOB_MASTER
EVE_TOWN_WORKPLACES
EVE_DB_PATH

If EVE_JOB_MASTER / EVE_TOWN_WORKPLACES are not set, the bridge looks under:

    AppContext.BaseDirectory/Data/World/Ohio/

THIS PACK DOES NOT YET AUTO-GENERATE ALL 200 PEOPLE
---------------------------------------------------
That should be the next layer.

This pack deliberately puts the plumbing in first:
- exact jobs
- exact slots
- exact work schedules
- attendance
- firing
- directional Family/Friend Web
- Tier 5 -> Tier 4 materialization

Once this is in place, a PopulationBaker can safely create the 200 core NPCs and
weave family/marriage/friend overlap without making contradictory people.
