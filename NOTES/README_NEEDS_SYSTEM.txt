PROJECT EVE — NEEDS SYSTEM
===========================

PURPOSE
-------
NeedsSystem gives NPCs changing internal life pressures that rise and fall automatically.

It answers:

    "What does this person currently need?"

This is different from traits.

Traits are:
    who the person tends to be

Needs are:
    what their body/life is demanding right now


CURRENT NEEDS
-------------

The first version tracks:

    Hunger
        0 = full
        100 = starving

    Energy
        0 = exhausted
        100 = fully energized

    Social
        0 = very lonely
        100 = socially satisfied

    Fun
        0 = very bored
        100 = fulfilled

    Stress
        0 = calm
        100 = overwhelmed

    Hygiene
        0 = filthy
        100 = clean

    SleepDebt
        0 = rested
        100 = badly sleep deprived

    Groceries
        0 = empty pantry
        100 = stocked

    Comfort
        0 = highly uncomfortable
        100 = comfortable


WHY THIS MATTERS
----------------

Before NeedsSystem, ActivityPlanner understood tags such as:

    hungry
    needs_groceries
    lonely
    stressed

but something had to supply those tags.

Now the NPC's life creates them automatically.


EXAMPLE
-------

8:00 AM

Sarah:
    Hunger = 28
    Energy = 75
    Social = 60
    Stress = 20

She works all morning.

12:00 PM:

    Hunger rises
    Energy falls
    Stress rises

Eventually:

    Hunger >= 60

NeedsSystem emits:

    hungry

ActivityPlanner now scores:

    meal_home
    restaurant_meal

more strongly.

Sarah does not eat because a random event fired.

She eats because:
    time passed
    work consumed energy
    hunger grew
    meal opportunity became attractive


FAST LOOP
---------

Call this around every 5 game minutes:

    NeedsWorldBridge.TickPopulation(
        gameTime,
        loadedTier1to4Npcs);

It reads the NPC's current activity from:

    WorldActivityEngine

or:

    ActivityPlanner

Then applies activity effects.


ACTIVITY EFFECT EXAMPLES
------------------------

sleep:
    Energy rises
    SleepDebt falls
    Stress falls

work:
    Energy falls
    Stress rises
    Hunger rises

meal_home:
    Hunger falls
    Groceries fall

restaurant_meal:
    Hunger falls
    Fun/social rise slightly

visit_friend:
    Social rises
    Fun rises
    Stress falls

exercise:
    Energy falls immediately
    Stress falls
    Hygiene falls
    Fun can rise

grocery_shopping:
    Groceries rise sharply

hobby:
    Fun rises
    Stress falls

All values are configurable in:

    Data/World/Ohio/needs_system.json


ACTIVITY PLANNER INTEGRATION
----------------------------

Instead of manually creating need tags:

    var ctx =
        new ActivityPlanner.PlannerContext();

use:

    var ctx =
        NeedsWorldBridge.BuildPlannerContext(
            npc);

That automatically adds current need tags.

You can still add actual world facts:

    var ctx =
        NeedsWorldBridge.BuildPlannerContext(
            npc,
            c =>
            {
                c.AvailableFriendNpcIds.Add(jessica.Id);

                c.TargetHomeLocationByNpcId[jessica.Id] =
                    jessica.HomeAddress;
            });

Then:

    ActivityPlanner.PlanNext(
        npc,
        gameTime,
        ctx);

can naturally choose a friend visit if the NPC is lonely and the friend is actually available.


HUMAN EVENT INTEGRATION
-----------------------

Before HumanEventEngine.Decide:

    var ctx =
        HumanEventWorldBridge.CreateContext(
            npc,
            gameTime,
            tier);

    NeedsWorldBridge.AddHumanEventNeeds(
        npc,
        ctx);

Now HumanEventEngine can see:

    hungry
    stressed
    overwhelmed
    lonely
    very_lonely
    bored
    exhausted
    sleep_deprived
    needs_groceries

Needs can also create small domain score biases.

Example:

high stress:
    conflict choices become somewhat easier to trigger

low social satisfaction:
    friendship/social behavior becomes somewhat more attractive

This is a modifier, not destiny.


MAJOR EVENTS CHANGE NEEDS
-------------------------

After an event actually happens:

    NeedsWorldBridge.ApplyHumanEvent(
        npc.Id,
        decision,
        gameTime);

Current configured examples:

fired:
    stress +30
    comfort -10
    fun -8

break_up:
    stress +30
    social -20
    fun -15

physical_fight:
    stress +25
    energy -15
    comfort -10

arrested:
    stress +35
    comfort -25
    social -10

death_of_close_person:
    stress +45
    social -15
    fun -30

receive_paycheck:
    stress -4
    comfort +3

miss_bill:
    stress +12
    comfort -8

These values are simulation tuning defaults.


IMPORTANT DESIGN RULE
---------------------

Needs should never directly command behavior.

BAD:

    stress > 60
        -> NPC punches someone

GOOD:

    stress high
        -> conflict tolerance changes
        -> relaxation activities become more attractive
        -> personality and current relationships still determine response

A patient, empathetic NPC may:
    go home
    exercise
    call a friend
    watch TV
    apologize

An aggressive, impulsive NPC under the same pressure may:
    argue
    drink
    lash out
    make a bad decision

Same need.
Different human.


MONEY / LIFE CONNECTION
-----------------------

NeedsSystem itself does not replace the money system.

Money consequences should influence:
    Stress
    Comfort

through HumanEvents or later household/economy bridges.

Example:

missed rent:
    money system creates fact
        ->
    NeedsSystem raises stress
        ->
    ActivityPlanner/HumanEventEngine respond

This preserves system boundaries.


TIER RULE
---------

Tier 1-4:
    same full needs model

Tier affects:
    deep HumanEvent thinking frequency

It does NOT affect:
    hunger
    sleep
    stress
    loneliness
    groceries

A Tier 4 NPC still gets hungry at the same biological rate as Tier 1.


DATABASE
--------

NeedsSystem creates:

    NpcNeeds

Each NPC has one persistent current-state row.

That means needs survive:
    game saves
    program restart
    NPC leaving player view


FILES
-----

World/SmallTown/Needs/NeedsSystem.cs
World/SmallTown/Needs/NeedsWorldBridge.cs
Data/World/Ohio/needs_system.json
README_NEEDS_SYSTEM.txt


RECOMMENDED FAST CLOCK ORDER
----------------------------

Every ~5 game minutes:

1.
    ActivityPlannerWorldBridge.Tick(...)

2.
    NeedsWorldBridge.TickPopulation(...)

3.
    SocialEncounterEngine evaluates co-located NPCs

4.
    HumanEventScheduler.RunDueUpdates(...)

5.
    LivingTownPendingWorkBridge.Process(...)


WHY ACTIVITY BEFORE NEED TICK?
------------------------------

NeedsSystem should know what the NPC was doing during the elapsed period.

For a more exact simulation later we can store activity intervals and integrate needs
across each interval, but this version is cheap and appropriate for a 200-NPC town.


NEXT USEFUL SYSTEM
------------------

The strongest next layer is probably:

    EmotionSystem.cs

Needs are slow/current pressures:

    hungry
    lonely
    stressed
    tired

Emotions are shorter-lived reactions:

    angry
    embarrassed
    afraid
    happy
    jealous
    guilty
    ashamed
    excited
    grief
    resentment

Then Project Eve gets:

    traits
        +
    needs
        +
    emotions
        +
    relationships
        +
    history
        +
    opportunity

feeding HumanEventEngine.

That would make NPC behavior much more human without relying on constant LLM calls.
