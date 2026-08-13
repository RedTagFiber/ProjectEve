PROJECT EVE — HUMAN EVENT ENGINE
================================

WHAT THIS SYSTEM IS
-------------------
Human Event Domains.json is the MASTER HUMAN EXPERIENCE VOCABULARY.

It answers:
    What can humans do or experience?

HumanEventEngine answers:
    Which of those events fits THIS NPC in THIS situation?

The engine is deliberately NOT:
    random event -> NPC does it

The engine is:
    event exists
        -> hard permission gate
        -> world opportunity gate
        -> job/role gate
        -> trait fit
        -> current need/emotion pressure
        -> relationship fit
        -> history/cooldown
        -> weighted choice OR no event

CORE RULE
---------
A bad/ugly event is allowed to exist in the catalog.

That does NOT make every NPC likely to do it.

Example:
    robbery exists in Human Event Domains.json

NPC A:
    criminal_tendency 8
    empathy 78
    rule_following 81
    aggression 10
    no robbery opportunity

Result:
    ROBBERY REJECTED / essentially unavailable.

NPC B:
    criminal_tendency 72
    impulsivity 83
    financial pressure
    robbery opportunity exists
    low observation
    prior criminal history

Result:
    robbery may become a viable candidate.
    It STILL competes against doing nothing / leaving / another action.

PROFESSIONAL AUTHORITY
----------------------
Job/role-specific events are HARD gated.

Examples:
    arrest
        -> police category
        -> on duty
        -> lawful arrest basis
        -> suspect present

    police_search
        -> police category
        -> on duty
        -> lawful search basis
        -> search target present

    teach_class
        -> education job
        -> appropriate teacher title
        -> on duty
        -> class in session

    perform_medical_treatment
        -> allowed healthcare / elderCare / fireEMS role
        -> appropriate job title
        -> on duty
        -> patient present
        -> medical need

    fire_employee
        -> management career level
        -> on duty
        -> subordinate present
        -> termination reason exists

A normal cashier therefore cannot select "arrest" simply because "arrest" is in
the human event list.

IMPORTANT:
"arrest" and "arrested" are different.
"arrest" is an authority ACTION.
"arrested" is something that can HAPPEN TO a civilian.

FILES
-----
Place:

    World/SmallTown/HumanEvents/HumanEventEngine.cs
    World/SmallTown/HumanEvents/HumanEventWorldBridge.cs

    Data/World/Ohio/human_event_rules.json

Keep your existing file:

    Memory/Human Event Domains.json

The engine also accepts these names:
    Memory/HumanEventDomains.json
    Memory/human_event_domains.json

Or set:
    EVE_HUMAN_EVENT_DOMAINS=<full path>

BOOT
----
After DatabaseInitializer.Initialize():

    HumanEventEngine.Initialize();

This creates:
    HumanEventHistory

HumanEventHistory is used for:
    repeat cooldowns
    debugging why behavior happened
    later history/memory routing

DO NOT RUN THIS AS THE 5-MINUTE MOVEMENT LOOP
---------------------------------------------
Project Eve should have two clocks.

FAST WORLD LOOP:
    movement
    commute
    schedule
    work presence
    sleep
    basic meals
    arrivals/departures
    nearby-person detection
    opportunity detection

DEEP EVENT DELIBERATION:
    HumanEventEngine

Suggested deep timing from our current design:
    Tier 1 = every 30 game minutes
    Tier 2 = every 60 game minutes
    Tier 3 = every 120 game minutes
    Tier 4 = every 180-300 game minutes
    Tier 5 = no routine deep deliberation

IMPORTANT EVENTS BYPASS THAT TIMER.

TIER 1-4
--------
Tier 1-4 have the SAME FULL NPC DATA:
    identity
    appearance
    brain
    traits
    money
    job
    relationships
    memory
    history

Tier is processing priority / update frequency only.

Tier 5 is the only lightweight person type.

HOW THE WORLD GIVES THE ENGINE OPPORTUNITIES
--------------------------------------------
The engine DOES NOT invent physical facts.

Example — store:

    var ctx = HumanEventWorldBridge.CreateContext(
        sarah,
        gameTime,
        tier: 2,
        locationId: "WP_GROCERY_001");

    ctx.Tags.Add("in_store");
    ctx.Tags.Add("merchandise_accessible");

    // Only add this if the actual world situation creates a crime opportunity.
    ctx.Tags.Add("crime_opportunity");

    var decision = HumanEventEngine.Decide(sarah, ctx);

Example — argument:

    var ctx = HumanEventWorldBridge.CreateContext(
        mike,
        gameTime,
        tier: 4,
        target: jim);

    ctx.Tags.Add("active_conflict");
    ctx.DomainScoreBias["conflict"] = 10;

    var decision = HumanEventEngine.Decide(mike, ctx);

A low-aggression/high-patience Mike may choose:
    walk_away
    apologize
    de_escalate

A high-aggression/high-impulsivity Mike may make:
    yelling
    shove
    physical_fight

viable.

The world situation opens the door.
The NPC's traits help decide whether they walk through it.

DEBUGGING
---------
This is extremely important for Project Eve.

Use:

    var test = HumanEventEngine.EvaluateEvent(
        npc,
        "robbery",
        context);

    Console.WriteLine(test);

Possible output:

    robbery: REJECTED — crime event without crime opportunity

Or:

    robbery: 34.8 —
    base -8
    criminal_tendency 81 => +14.9
    aggression 73 => +5.5
    risk_taking 78 => +4.5
    empathy 18 => +5.8
    rule_following 20 => +5.4
    world state => +8
    human variance -0.3

This lets us catch "why did this normal NPC do THAT?" problems.

TRAIT ALIASES
-------------
human_event_rules.json contains canonical behavior traits:
    empathy
    conscientiousness
    impulsivity
    aggression
    honesty
    loyalty
    jealousy
    possessiveness
    romance
    sociability
    courage
    caution
    rule_following
    criminal_tendency
    greed
    generosity
    work_ethic
    ambition
    patience
    forgiveness
    vindictiveness
    secrecy
    manipulativeness
    anxiety
    risk_taking
    authority_respect
    caregiving

Each canonical trait has aliases.

As we inspect/expand your actual TraitRegistry, add the real Project Eve trait IDs
to the aliases. You do NOT have to rewrite HumanEventEngine.

UNKNOWN TRAITS
--------------
Unknown traits are treated as neutral 50.

That is intentional.

A newly added event should not suddenly make every NPC saintly or criminal just
because its matching trait ID has not been created yet.

HOW TO ADD A NEW HUMAN EVENT
----------------------------
Example:
    "hide_coworkers_mistake"

Step 1:
Add it to the correct array in:
    Memory/Human Event Domains.json

The engine automatically discovers it.

Step 2:
If domain behavior is enough:
    DONE.

Step 3:
If it needs a special hard gate or special trait mix, add an eventOverrides entry:

    "hide_coworkers_mistake": {
      "allowedAnyJob": true,
      "requiresTarget": true,
      "requiredContextTags": [
        "coworker_made_mistake",
        "actor_knows_about_mistake"
      ],
      "traitWeights": {
        "loyalty": 0.20,
        "honesty": -0.10,
        "rule_following": -0.08
      }
    }

This is why the engine scales to a huge human-event list.

EVENT EXECUTION
---------------
HumanEventEngine CHOOSES.

It should NOT become one giant class that directly owns:
    money
    jobs
    police
    pregnancy
    health
    relationships
    memory
    housing
    crime consequences

After a decision is accepted:

    HumanEventEngine.RecordExecuted(decision, gameTime);

Then route the event to the appropriate Project Eve system.

Examples:
    call_off
        -> SmallTownEmploymentSystem.RecordCallOff(...)

    fired
        -> employment/job-slot system

    borrow_money
        -> money/household system

    physical_fight
        -> conflict/health/relationship systems

    spread_rumor
        -> gossip / Family Friend Web propagation

    relationship_started
        -> relationship system

    major event
        -> memory + history
        -> force deep NPC update

This keeps HumanEventEngine from becoming an unmaintainable 20,000-line god class.

NEXT PIECE
----------
The next logical piece is HumanEventConsequenceRouter.

That will translate:
    chosen event
into:
    actual Project Eve state changes.

But it should call your existing systems rather than duplicating them.
