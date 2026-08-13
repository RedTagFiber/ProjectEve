PROJECT EVE — HUMAN EVENT SCHEDULER
====================================

PURPOSE
-------
HumanEventScheduler controls WHEN Tier 1-4 NPCs receive a deep HumanEvent pass.

It does NOT decide behavior itself.

The full chain is:

    Human Event Domains.json
        -> HumanEventEngine
        -> HumanEventConsequenceRouter
        -> ProjectEveHumanEventHooks
        -> Project Eve systems

HumanEventScheduler is the clock that decides when to run that chain.


TIER TIMING
-----------

Default settings in:

    Data/World/Ohio/human_event_scheduler.json

are:

    Tier 1 = every 30 game minutes
    Tier 2 = every 60 game minutes
    Tier 3 = every 120 game minutes
    Tier 4 = every 180-300 game minutes
    Tier 5 = no routine deep update

Tier 1-4 still have the SAME full NPC data.

Tier affects update frequency only.


IMPORTANT: THIS IS NOT THE FAST WORLD LOOP
------------------------------------------

The scheduler should NOT be responsible for every movement step.

The fast world/activity loop still handles things like:

    movement
    commute
    work presence
    sleep
    meals
    arrivals/departures
    who is nearby
    location changes
    opportunity detection

HumanEventScheduler handles deeper choices such as:

    talk to this person?
    apologize?
    argue?
    flirt?
    lie?
    call off?
    quit?
    steal?
    help?
    gossip?
    break up?
    make a major decision?

The fast world loop creates opportunities.
The scheduler gives the NPC a chance to think/choose.


BOOT ORDER
----------

Recommended order:

    DatabaseInitializer.Initialize();

    SmallTownEmploymentSystem.Initialize();
    FamilyFriendWebSystem.Initialize();

    HumanEventEngine.Initialize();
    HumanEventConsequenceRouter.Initialize();
    HumanEventScheduler.Initialize();

    var humanEventHooks =
        new ProjectEveHumanEventHooks();

Then register the existing full population:

    HumanEventScheduler.RegisterExistingFullPopulation(
        gameTime);

This scans:

    Characters

and registers Tier 1-4.

Tier 5 is ignored.


NEW NPCs / PROMOTIONS
---------------------

When a new Tier 1-4 NPC is created:

    HumanEventScheduler.RegisterNpc(
        npc.Id,
        npc.Tier,
        gameTime);

When a Tier 5 person is promoted to Tier 4:

    HumanEventScheduler.RegisterNpc(
        npc.Id,
        4,
        gameTime);

If an NPC changes tier:

    HumanEventScheduler.SetTier(
        npc.Id,
        newTier,
        gameTime);

If somebody becomes Tier 5:

    HumanEventScheduler.SetTier(
        npc.Id,
        5,
        gameTime);

That removes them from routine deep scheduling.


CALL FROM THE GAME CLOCK
------------------------

The game clock can call:

    var result =
        HumanEventScheduler.RunDueUpdates(
            gameTime,
            humanEventHooks,
            worldFactsBuilder);

It is safe to call frequently.

The scheduler only runs NPCs whose due time has arrived.


WORLD FACTS
-----------

The scheduler never invents physical opportunities.

Use worldFactsBuilder to add real facts.

Example:

    var result =
        HumanEventScheduler.RunDueUpdates(
            gameTime,
            humanEventHooks,
            (npc, ctx) =>
            {
                if (npc.Location == "grocery")
                {
                    ctx.Tags.Add("in_store");
                    ctx.Tags.Add("merchandise_accessible");
                }

                if (NpcIsInActiveArgument(npc.Id))
                    ctx.Tags.Add("active_conflict");

                if (NpcHasChildcareEmergency(npc.Id))
                    ctx.Tags.Add("childcare_problem");

                return ctx;
            });

Only add:
    crime_opportunity
    romantic_opportunity
    active_conflict
    patient_present

when the world really supports them.

That keeps the HumanEventEngine grounded.


TARGETS
-------

Some events need another NPC.

The scheduler accepts a targetResolver:

    npc =>
    {
        return FindRelevantNearbyPerson(npc);
    }

That might choose:
    spouse
    coworker
    nearby friend
    current argument target
    person they are talking to

The scheduler does not randomly create a target.


MAJOR EVENT BYPASS
------------------

ProjectEveHumanEventHooks creates:

    NpcDeepUpdateRequest

for important events.

HumanEventScheduler processes those BEFORE routine due updates.

Examples:

    fired
    arrest
    breakup
    marriage
    physical fight
    pregnancy
    birth
    death

So:

    Tier 4 NPC gets fired at 10:02 AM

does NOT wait until their normal 1:00 PM deep pass.

The scheduler picks up the immediate request and gives them a new deep reaction.


NO EVENT IS VALID
-----------------

A deep pass does not have to produce something dramatic.

HumanEventEngine can return:

    no event

That is expected.

A realistic NPC often just keeps doing what they are already doing.


FIRST-DUE RANDOMIZATION
-----------------------

If all 200 NPCs were created at noon and Tier 1 updates every 30 minutes,
we do NOT want all Tier 1 NPCs thinking at exactly 12:30.

RegisterExistingFullPopulation randomizes the first due time.

After that, normal cadence applies.

This spreads CPU load across game time.


PROCESSING LIMIT
----------------

Default:

    maximumNpcUpdatesPerSchedulerPass = 50

This prevents one game-clock call from trying to process hundreds of overdue NPCs.

If 90 NPCs are due:

    first scheduler call handles up to 50
    next call handles the rest

You can tune this in JSON.


DATABASE
--------

The scheduler creates:

    HumanEventSchedule
    HumanEventSchedulerAudit

HumanEventSchedule stores:

    NpcId
    Tier
    last deep update
    next deep update
    last event
    whether last pass produced an event
    enabled flag

HumanEventSchedulerAudit stores:

    NPC
    tier
    game time
    chosen event
    reason
    routine/immediate/failure pass type


DEBUGGING
---------

Example:

    Console.WriteLine(result);

Output is similar to:

    HumanEventScheduler 8/13/2026 10:00 AM:
    processed 17,
    immediate 2,
    events 6,
    no-event 11,
    failed loads 0

This gives a fast health check for the simulation.


TIER 4 EXAMPLE
--------------

Mike is Tier 4.

His next routine deep pass:
    2:00 PM

At 11:05 AM:
    Mike is at work
    Jim insults him
    active conflict starts

The fast world loop knows:
    Mike and Jim are together
    active_conflict = true

If the insult is a major enough event:
    immediate deep request

HumanEventScheduler runs Mike now.

HumanEventEngine evaluates:
    walk away
    argue
    insult back
    apologize
    shove
    physical fight
    etc.

based on:
    Mike's traits
    relationship with Jim
    anger/tension
    world opportunity
    job situation
    recent history

That is exactly why the scheduler and fast world loop are separate.


CONFIG
------

human_event_scheduler.json lets you change cadence without recompiling.

Current defaults:

    tier1:
        min 30
        max 30

    tier2:
        min 60
        max 60

    tier3:
        min 120
        max 120

    tier4:
        min 180
        max 300

You can later tune these based on CPU/GPU use and how alive the town feels.


DEPENDENCIES
------------

This file expects:

    HumanEventEngine.cs
    HumanEventWorldBridge.cs
    HumanEventConsequenceRouter.cs
    ProjectEveHumanEventHooks.cs

and the earlier:

    SmallTownEmploymentSystem.cs
    FamilyFriendWebSystem.cs


NEXT SYSTEM
-----------

The next useful system is probably:

    GossipEngine.cs

because your Family & Friend Web is already designed around:
    real shared identities
    directional relationships
    no omniscience
    telephone-game information spread

The HumanEvent system can already choose:

    hear_rumor
    repeat_rumor
    embellish_rumor
    protect_secret
    betray_secret
    spread_rumor

The GossipEngine would control WHO actually learns WHAT, HOW it changes while
traveling, and whether the original source becomes known.
