PROJECT EVE — ACTIVITY PLANNER
===============================

WHAT THIS SYSTEM DOES
---------------------

ActivityPlanner gives NPCs believable free-time lives between hard obligations.

It answers:

    "What is this person doing next, and where?"

It does NOT answer:

    "What morally/socially complex choice do they make?"

That still belongs to HumanEventEngine.


THE NEW FLOW
------------

GAME CLOCK — every ~5 game minutes

    ActivityPlannerWorldBridge.Tick(...)
        |
        v
    NPC gets/keeps an activity plan
        |
        v
    NpcWorldActivity is updated
        |
        v
    SocialEncounterEngine sees who is really together
        |
        v
    HumanEventScheduler / HumanEventEngine
        |
        v
    conversation / gossip / flirt / argument / help / crime / etc.


HARD OBLIGATIONS WIN
---------------------

The planner will not send someone shopping when they are supposed to be at work.

Priority examples:

    medical emergency
    police detention
    work shift
    school
    sleep

Only after those are clear does optional life fill the gap.


OPTIONAL LIFE
-------------

Current activity vocabulary includes:

    stay_home
    meal_home
    restaurant_meal
    grocery_shopping
    shopping
    errand
    visit_friend
    visit_family
    bar
    church
    hobby
    exercise
    appointment
    date
    movie_entertainment
    coffee_cafe
    walk_around_town
    spontaneous_trip

Staying home is intentionally common.

Project Eve should contain plenty of:

    leftovers
    TV
    scrolling
    sitting around
    chores
    quiet nights

not just nonstop drama.


TRAITS SHAPE ACTIVITY
---------------------

Examples:

high sociability:
    more friend visits
    cafes
    bars
    dates

high conscientiousness:
    errands
    groceries
    exercise
    less impulsive wandering

high impulsivity:
    shopping
    restaurant
    spontaneous trip

high romance:
    date activity more attractive

Traits only shape the score.
They do not guarantee an activity.


MONEY MATTERS
-------------

Activities have cost levels.

A cash-strapped NPC is pushed toward:

    home meal
    staying home
    walks
    low-cost errands

A comfortable NPC can more easily choose:

    restaurant
    entertainment
    shopping
    date
    spontaneous trip

The planner still allows occasional financially bad decisions when the NPC's
traits/context make them plausible.


REAL DESTINATIONS
-----------------

This pack includes:

    town_activity_venues.json

It was generated from the existing:

    project_eve_town_workplaces.json

and includes all 56 current Project Eve workplaces/locations as reusable venues.

Examples include:

    Sinclair Coffee
    grocery stores
    restaurants
    bar/grill
    church
    library
    hospital
    banks
    retail stores
    schools
    government/service locations

The planner does not invent a generic unnamed location when a real registered
venue is available.


BOOT
----

After DatabaseInitializer.Initialize():

    WorldActivityEngine.Initialize();
    ActivityPlanner.Initialize();

Then import the actual town venues:

    int venueCount =
        ActivityPlannerBootstrap.ImportTownVenues();

The current pack imports 56 workplaces/venues plus known Project Eve seeded locations.


FAST GAME CLOCK
---------------

Instead of relying only on the old generic WorldActivityEngine.Tick for Tier 1-4,
use:

    ActivityPlannerWorldBridge.Tick(
        gameTime,
        loadedTier1to4Npcs,
        npc =>
        {
            var ctx =
                new ActivityPlanner.PlannerContext();

            // Add only REAL current needs/facts.

            if (NpcIsHungry(npc))
                ctx.Tags.Add("hungry");

            if (NpcNeedsGroceries(npc))
                ctx.Tags.Add("needs_groceries");

            if (NpcHasErrand(npc))
                ctx.Tags.Add("has_errand");

            return ctx;
        });

The bridge:
    keeps an existing active plan
or
    creates a new plan when the previous activity ends

then writes the result into:

    NpcWorldActivity

which SocialEncounterEngine already reads.


FRIENDS / FAMILY
----------------

For visits, supply actual people:

    ctx.AvailableFriendNpcIds.Add(jessica.Id);
    ctx.AvailableFamilyNpcIds.Add(mom.Id);

And their homes:

    ctx.TargetHomeLocationByNpcId[jessica.Id] =
        jessica.HomeAddress;

This means:

    Sarah decides to visit Jessica
        ->
    Sarah physically appears at Jessica's actual home
        ->
    Jessica may also be there
        ->
    SocialEncounterEngine detects them together


DATES
-----

A date does not appear from nowhere.

The relationship/social system supplies:

    ctx.Tags.Add("date_available");
    ctx.AvailableDateNpcIds.Add(john.Id);

Then the planner can choose:

    cafe
    restaurant
    park
    entertainment
    bar if age allows

The deeper romantic behavior still belongs to HumanEventEngine.


APPOINTMENTS
------------

Appointments are not randomly invented.

Supply:

    ctx.Tags.Add("has_appointment");
    ctx.RequiredLocationId = "WP_HOSPITAL_001";

Then ActivityPlanner treats it as a valid appointment option.


CHURCH
------

Church is naturally more likely on Sunday morning.

A regular attendee can also have:

    ctx.Tags.Add("church_attender");

Later we can give NPCs explicit belief/religion profiles if desired.


SPONTANEOUS TRIPS
-----------------

Spontaneous trips are deliberately rare.

They rise with:

    impulsivity
    risk taking
    available money

and fall with:

    conscientiousness
    financial pressure

They are optional life variation, not random teleportation.


HOW THIS CREATES HUMAN EVENTS
-----------------------------

Example:

8:00 AM
    Sarah goes to work.

12:10 PM
    Jessica chooses Sinclair Coffee for lunch.

12:20 PM
    Sarah is on break at Sinclair Coffee.

Now both are physically in the same place.

SocialEncounterEngine creates:

    conversation_opportunity

HumanEventEngine may choose:

    small talk
    complain about work
    gossip
    ask favor
    share secret
    flirt
    argue
    ignore

Then GossipEngine can move real information person-to-person.


ANOTHER EXAMPLE
---------------

Friday 8:00 PM

Mike:
    high sociability
    moderate money
    no work tomorrow

Planner:
    bar

Jim:
    also chooses same bar

They have:
    high relationship tension

SocialEncounterEngine:
    active_conflict opportunity

HumanEventEngine:
    maybe ignore
    maybe argue
    maybe insult
    maybe fight

The bar did not CAUSE the fight.

The activity system created the believable opportunity.


IMPORTANT SEPARATION
--------------------

ActivityPlanner:
    where / broad activity

SocialEncounterEngine:
    who is actually together

HumanEventEngine:
    what the person chooses

ConsequenceRouter:
    what changes

GossipEngine:
    who learns what

This keeps the world believable instead of making one random system control everything.


FILES
-----

World/SmallTown/Activity/ActivityPlanner.cs
World/SmallTown/Activity/ActivityPlannerBootstrap.cs
World/SmallTown/Activity/ActivityPlannerWorldBridge.cs

Data/World/Ohio/activity_planner.json
Data/World/Ohio/town_activity_venues.json

README_ACTIVITY_PLANNER.txt


NEXT STRONGEST PIECE
--------------------

Now that people can physically choose places, the next useful refinement is a:

    NeedsSystem

It would maintain changing values such as:

    hunger
    energy
    social need
    loneliness
    fun
    stress
    hygiene
    errands/groceries
    sleep debt

Those values can feed ActivityPlanner automatically.

Then:

    hungry
        -> meal opportunity

    lonely
        -> friend/family/social places

    stressed
        -> home, hobby, bar, exercise, talk to friend depending on personality

    low energy
        -> stay home / sleep

That will make activities arise from the NPC's life rather than requiring us to
manually add context tags.
