PROJECT EVE — LIVING TOWN PACK
===============================

WHAT THIS ADDS
--------------

1. WorldActivityEngine.cs
   Cheap 5-minute physical world/activity loop.

2. SocialEncounterEngine.cs
   Detects when real people are actually together and creates social opportunities.

3. GossipEngine.cs
   Full non-omniscient TELEPHONE GAME.

4. CrimePoliceEngine.cs
   Persistent crimes, witnesses, reports, police cases, probable cause and arrests.

5. HealthSystem.cs
   Persistent injury/health incidents without inventing diagnoses.

6. LivingTownPendingWorkBridge.cs
   Connects pending HumanEvent hook work into Gossip, Crime/Police and Health.


THE CORE LOOP
-------------

FAST WORLD LOOP — every ~5 game minutes:

    WorldActivityEngine.Tick(...)

This updates:
    sleep
    home
    work
    physical location
    busy/not busy

Then evaluate actual co-location:

    SocialEncounterEngine.Evaluate(sarah, jessica, gameTime)

If both are really at the same location and available, this can create:

    conversation_opportunity
    romantic_opportunity
    active_conflict

Those become tags for HumanEventEngine.


DEEP LOOP
---------

HumanEventScheduler still controls:

    T1 = 30 minutes
    T2 = 60 minutes
    T3 = 120 minutes
    T4 = 180-300 minutes

Major events bypass the timer.

The fast world loop and deep loop are intentionally different.


GOSSIP / TELEPHONE GAME
-----------------------

This system never does:

    rumor exists
    -> whole town knows it

Instead:

    Sarah knows fact A.

Sarah actually encounters Jessica.
Sarah chooses/gets opportunity to gossip.

    Sarah -> Jessica

Jessica receives a VERSION of fact A.

Later Jessica encounters Mike.

    Jessica -> Mike

Mike gets Jessica's version, not magically Sarah's original version.

Every transmission stores:

    RumorId
    speaker
    listener
    before text
    after text
    confidence before
    confidence after
    distortion type
    retell depth
    location
    game time

Possible distortion:

    preserved
    detail_lost
    exaggerated
    softened
    misremembered
    speaker_bias

So a chain can look like:

ORIGINAL:
    "John left work early Tuesday because his daughter was sick."

Sarah -> Jessica:
    preserved

Jessica -> Mike:
    "John left work early Tuesday because his daughter was sick."

Mike -> Tom:
    detail_lost

    "John left work early Tuesday."

Tom -> Rachel:
    speaker_bias

    "The way I heard it, John just left work early Tuesday."

Rachel -> Lisa:
    exaggerated

    "John left work early Tuesday — and apparently it was worse than people first said."

Nobody became omniscient.
The fact changed through actual people.


TELEPHONE CHAIN DEBUGGING
-------------------------

Use:

    GossipEngine.GetTelephoneChain(rumorId)

You can see every hop and exactly where the story changed.


WHO KNOWS WHAT
--------------

Use:

    GossipEngine.GetKnownRumors(npcId)

A person only appears as knowing a rumor if:

    they originated it
or
    somebody transmitted it to them

This is critical to Project Eve.


SECRET LEVEL
------------

Rumors have SecretLevel 0-100.

High secrecy reduces retelling probability.

Traits also matter:

    sociability high
        -> more likely to tell

    secrecy high
        -> less likely to tell

    manipulativeness high
        -> more exaggeration/bias

    honesty high
        -> slightly more softening / cautious phrasing

Relationship closeness can make somebody more likely to confide in a close friend.


CRIME / POLICE
--------------

HumanEventEngine decides:
    steal?
    robbery?
    assault?

CrimePoliceEngine records what actually happened.

It stores:
    suspect
    victim
    location
    time
    severity
    witnesses

A witness can report the crime.

Only then is a PoliceCase opened.

Arrest is NOT automatic.

A PoliceCase needs:

    ProbableCause = true

before:

    TryRecordArrest(...)

can succeed.

This preserves the rule:

    crime happened
does NOT mean
    police magically know who did it.


HEALTH
------

HealthSystem stores:

    general health
    pain
    hospitalization
    health incidents

It never invents a diagnosis.

The event/world supplies:

    injury severity
    medical fact

Example:

    physical_fight
    target = Jim
    InjurySeverity = "bruised ribs"
    severity = 5

HealthSystem records that fact.


PENDING WORK BRIDGE
-------------------

Earlier ProjectEveHumanEventHooks intentionally queued systems that did not exist yet:

    law
    health
    gossip

Now:

    LivingTownPendingWorkBridge.Process(gameTime)

can consume them.

This means you do NOT have to rewrite the core HumanEvent pipeline every time we add
a specialized system.


BOOT ORDER
----------

Recommended:

    DatabaseInitializer.Initialize();

    SmallTownEmploymentSystem.Initialize();
    FamilyFriendWebSystem.Initialize();

    HumanEventEngine.Initialize();
    HumanEventConsequenceRouter.Initialize();
    HumanEventScheduler.Initialize();

    WorldActivityEngine.Initialize();
    SocialEncounterEngine.Initialize();
    GossipEngine.Initialize();
    CrimePoliceEngine.Initialize();
    HealthSystem.Initialize();
    LivingTownPendingWorkBridge.Initialize();

    var humanEventHooks = new ProjectEveHumanEventHooks();


GAME CLOCK EXAMPLE
------------------

Every 5 game minutes:

    WorldActivityEngine.Tick(gameTime, loadedTier1to4Npcs);

Then:
    detect people sharing locations
    SocialEncounterEngine.Evaluate(...)

Then:
    HumanEventScheduler.RunDueUpdates(...)

Then:
    LivingTownPendingWorkBridge.Process(gameTime)

You do not need to run all 200 NPCs through deep AI every five minutes.


IMPORTANT LIMITATION OF THIS FIRST WORLD ACTIVITY VERSION
--------------------------------------------------------

WorldActivityEngine currently provides the cheap foundation:
    work
    sleep
    home
    idle
    location

The next refinement should add a proper ActivityPlanner with:
    meals
    shopping
    errands
    visiting friends
    church
    bars
    appointments
    hobbies
    spontaneous trips

using the NPC's needs, schedule and available town locations.

That should remain mostly deterministic/rule-based so it stays fast.


DESIGN RULE
-----------

Project Eve now has:

    WORLD:
        where can this happen?

    HUMAN EVENT:
        would this person do it?

    CONSEQUENCE:
        what changes because it happened?

    GOSSIP:
        who actually learns about it?

Those four questions stay separate.

That separation is what prevents:
    omniscience
    random crime
    frozen NPCs
    everyone acting the same
    Happyville
    Chaosville
