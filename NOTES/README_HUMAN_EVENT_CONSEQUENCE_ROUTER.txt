PROJECT EVE — HUMAN EVENT CONSEQUENCE ROUTER
=============================================

PURPOSE
-------
HumanEventEngine decides:

    "What does this NPC do?"

HumanEventConsequenceRouter decides:

    "What changes in Project Eve because they did it?"

This is deliberately separated.

Why?
If HumanEventEngine itself starts changing jobs, money, police cases, health,
pregnancy, gossip, relationships, location and memories, it becomes an
unmaintainable god-class.

The router coordinates the result and lets each owning subsystem stay responsible
for its own rules.


THE PIPELINE
------------

Human Event Domains.json
        |
        v
HumanEventEngine
    hard gates
    job/role fit
    traits
    needs/emotions
    relationship
    opportunity
        |
        v
HumanEventDecision
        |
        v
HumanEventConsequenceRouter
        |
        +--> relationship
        +--> employment
        +--> money
        +--> law/police
        +--> health
        +--> family
        +--> gossip
        +--> reputation
        +--> location/activity
        +--> memory
        +--> history
        +--> force deep update


FILES
-----

World/SmallTown/HumanEvents/HumanEventConsequenceRouter.cs

Data/World/Ohio/human_event_consequences.json

The router requires the HumanEventEngine pack that was built immediately before
this one.


BOOT
----

After:

    DatabaseInitializer.Initialize();
    HumanEventEngine.Initialize();

add:

    HumanEventConsequenceRouter.Initialize();


DATABASE TABLES
---------------

The router creates:

    HumanEventConsequenceQueue
    HumanEventConsequenceAudit

HumanEventConsequenceQueue:
    persistent requests for specialized systems

HumanEventConsequenceAudit:
    debugging record of what the router did


WHY A QUEUE?
------------

Some consequences can be safely applied by the router immediately.

Example:
    insult
        -> trust -2
        -> respect -5
        -> affection -3
        -> tension +8

Relationship.cs already provides:
    AdjustTrust
    AdjustRespect
    AdjustAffection
    AdjustAttraction
    AdjustTension

So the router can update the live Relationship object safely.

Other consequences belong to another Project Eve system.

Example:
    call_off

The router should NOT recreate:
    attendance points
    firing thresholds
    schedule coverage
    job slot status
    manager logic

Those belong to SmallTownEmploymentSystem.

Therefore the router sends/queues:
    route = employment
    event = call_off

and your employment adapter handles it.


NO INVENTED FACTS
-----------------

This is a major design rule.

If Sarah steals money, the router DOES NOT decide:

    "$287 was stolen"

unless the world already supplied that amount.

Instead:

    context.Amount = 287m;

If no amount exists:
    the money consequence remains queued

Same for:
    injury severity
    medical diagnosis
    pregnancy details
    legal charge
    item value
    police case
    witness list

The simulation should create facts from the responsible system, not from random
router guesses.


HIDDEN BEHAVIOR
---------------

This is one of the most important parts.

BAD ACTION DOES NOT EQUAL INSTANT KNOWLEDGE.

Example:

    Sarah cheats on John.

John does not magically lose 25 trust at the moment Sarah cheats if John has no
idea it happened.

Instead the router stores:

    partner_damage_if_discovered
    reputation_damage_if_discovered

Later:

    John discovers the affair

Then ResolveDeferred(...) applies the damage.

This supports:
    secrets
    lies
    affairs
    hidden theft
    betrayal
    rumors
    coverups
    private/public personality differences

Exactly the kind of non-omniscient world Project Eve needs.


EXAMPLE — ARGUMENT
------------------

    var ctx = new HumanEventConsequenceRouter.HumanEventConsequenceContext
    {
        TargetIsAware = true,
        TargetName = jim.Name,
        LocationId = mike.Location
    };

    var result = HumanEventConsequenceRouter.Route(
        mike,
        decision,
        gameTime,
        ctx,
        projectEveHooks);

If decision.EventId == "heated_argument":

    relationship changes
    memory requested
    possibly deep update
    audit written


EXAMPLE — HIDDEN CHEATING
-------------------------

    var ctx = new HumanEventConsequenceRouter.HumanEventConsequenceContext
    {
        TargetIsAware = false,
        TargetName = affairTarget.Name
    };

    HumanEventConsequenceRouter.Route(
        sarah,
        decision,
        gameTime,
        ctx,
        projectEveHooks);

The router DOES NOT magically alter the unaware partner.

It queues:
    partner_damage_if_discovered
    reputation_damage_if_discovered

When someone later discovers it, the discovery event can resolve the deferred
consequence.


EXAMPLE — MONEY
---------------

    var ctx = new HumanEventConsequenceRouter.HumanEventConsequenceContext
    {
        Amount = 120m,
        TargetIsAware = true,
        TargetName = "Tom"
    };

    HumanEventConsequenceRouter.Route(
        sarah,
        decision,
        gameTime,
        ctx,
        projectEveHooks);

For:
    lend_money
    borrow_money
    steal_money
    receive_paycheck
    pay_bill

the amount must come from the world/economy system.


CONSEQUENCE RULES
-----------------

human_event_consequences.json has two layers.

1. domainDefaults

Every event in Human Event Domains.json gets a consequence route because it
belongs to a domain.

Examples:

    crime
        -> law
        -> reputation
        -> memory

    family
        -> relationship
        -> family

    work
        -> employment

    health
        -> health

    life_changes
        -> history
        -> memory

That means the giant Human Event Domains list is covered without writing a
custom code block for every single event.

2. eventOverrides

Special events can add precise consequences.

Examples:

    insult
    apologize
    physical_fight
    relationship_started
    break_up
    marry
    divorce
    cheat
    call_off
    fired
    promotion
    robbery
    arrest
    pregnancy
    birth
    death


RELATIONSHIP CONSEQUENCES
-------------------------

Relationship changes are directional because Project Eve Relationship objects
are directional.

That is good.

Sarah can become:
    less trusting of John

without automatically forcing:
    John becomes equally less trusting of Sarah

If an event affects both people, route a consequence for each relevant NPC.

This preserves asymmetric relationships.


MEMORY
------

The router requests memories only when an event deserves one.

Memory importance is 0-10.

Examples:
    clock in          0
    normal shift      0
    compliment        small
    insult            moderate
    argument          moderate
    physical fight    high
    fired             high
    marriage          very high
    birth             very high
    death             very high

This prevents:
    "I clocked into work Tuesday"

from becoming one of an NPC's important memories.


MAJOR EVENTS
------------

Major events request an immediate deep update.

Examples:
    fight
    firing
    arrest
    breakup
    marriage
    divorce
    pregnancy
    birth
    death
    medical emergency
    discovering cheating

This bypasses the normal Tier timer.

So a Tier 4 NPC does not wait 3-5 game hours to react to their spouse dying or
being arrested.


TIER RULE
---------

Tier 1-4:
    same full NPC data

Difference:
    background/deep update frequency

Tier 5:
    lightweight

The consequence router does NOT reduce consequence quality for Tier 4.

If a Tier 4 NPC gets fired:
    they are fired now

Their money/job/family facts should update now.

Tier only changes routine deep thinking frequency.


PROJECT EVE HOOKS
-----------------

HumanEventConsequenceRouter includes:

    IHumanEventConsequenceHooks

This is the bridge to existing/current/future systems.

Methods include:

    OnRelationshipChanged
    OnEmploymentEvent
    OnMoneyEvent
    OnLawEvent
    OnHealthEvent
    OnFamilyEvent
    OnGossipEvent
    OnReputationEvent
    OnLocationEvent
    OnActivityEvent
    OnMemoryRequested
    OnHistoryRequested
    OnDeepUpdateRequested

Why use hooks?

Because we already have or are building separate systems for these areas.
The router should call those systems instead of duplicating them.

Until a hook is wired:
    the consequence stays in HumanEventConsequenceQueue

So building the game in stages does not lose events.


IMPORTANT NEXT INTEGRATION
--------------------------

The next file should be:

    ProjectEveHumanEventHooks.cs

That is NOT another behavior engine.

It will be the concrete adapter that connects the router to the exact methods
already present in Project Eve:

    SmallTownEmploymentSystem
    CharacterRepository
    MemoryDatabase / Remember
    History
    FamilyFriendWebSystem
    MoneyProfile

For police, health, reputation and other systems that do not yet exist, the
adapter can leave the queue pending until those systems are built.


DEBUGGING
---------

If an NPC does something weird:

HumanEventEngine tells us:
    WHY THEY CHOSE IT

HumanEventConsequenceAudit tells us:
    WHAT PROJECT EVE CHANGED BECAUSE OF IT

That gives us both halves of the behavior trace.


DESIGN SUMMARY
--------------

Human Event Domains:
    what humans can do

HumanEventEngine:
    what this person would consider/do

HumanEventConsequenceRouter:
    what the action changes

Owning systems:
    exact rules and long-term simulation

Memory/History:
    what the person remembers and how their life story changes
