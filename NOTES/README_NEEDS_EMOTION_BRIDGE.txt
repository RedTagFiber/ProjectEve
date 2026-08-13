PROJECT EVE — NEEDS / EMOTION BRIDGE
====================================

PURPOSE
-------
This pack connects the NeedsSystem built earlier to Project Eve's EXISTING
Fast-trait emotional model instead of creating a second competing EmotionSystem.

SOURCE OF TRUTH
---------------

NeedsSystem owns:
    Hunger
    Energy
    Social satisfaction
    Fun
    Stress
    Hygiene
    SleepDebt
    Groceries
    Comfort

NpcTraits Fast values own current emotion:
    anger
    anxiety
    fear
    tension
    affection
    desire
    resentment
    shame
    guilt
    hurt
    loneliness
    hope
    playfulness
    patience
    etc.

EmotionalProfile owns:
    readable/UI compatibility projection
    dominant State
    Mood
    derived emotion meters

This prevents duplicate truths.


MAIN FILE
---------

World/SmallTown/Needs/NeedsEmotionBridge.cs


PATCHED EXISTING FILE
---------------------

Characters/Emotion/EmotionalProfile.cs

This is the user's existing EmotionalProfile with only two targeted changes:

1. Numb is checked before Tired so Numb can actually be reached.

Old:
    if Energy <= 20 -> Tired
    if Energy <= 10 && Sadness >= 40 -> Numb

The second condition could never run.

New:
    if Energy <= 10 && Sadness >= 40 -> Numb
    if Energy <= 20 -> Tired

2. RecalculateFromMeters is explicitly documented as choosing the dominant
   outward/current mood. Other emotional meters still remain active.


NORMAL FAST LOOP
----------------

After NeedsSystem updates an NPC:

    var state =
        NeedsSystem.TickNpc(
            npc,
            gameTime,
            currentActivity);

then sync:

    NeedsEmotionBridge.Sync(
        npc,
        emotionalProfile,
        gameTime);

If EmotionalProfile is stored on your SimCharacter under a property whose name may
change, use the accessor overload:

    NeedsEmotionBridge.Sync(
        npc,
        x => x.Emotion,
        gameTime);

Change x.Emotion to the actual property name in your branch.


WHAT THE BRIDGE DOES
--------------------

It converts current need pressure into SMALL Fast-trait nudges.

Example:

Stress = 85

may push:
    trait.tension up
    trait.anxiety up
    trait.fear slightly up
    trait.patience down

But it does NOT say:
    stress means anger

That distinction matters.


SAME NEED, DIFFERENT HUMAN
--------------------------

NPC A:
    high patience
    high empathy
    low aggression

NPC B:
    low patience
    high aggression
    impulsive

Both:
    Stress = 85
    Energy = 25

NeedsEmotionBridge creates similar pressure:
    tension up
    patience down
    anxiety up

But their existing Fast traits are different.

So NPC A may become:
    Uneasy
    Stressed
    withdrawn

NPC B may become:
    Irritated
    angry
    more conflict-prone

Same life pressure.
Different person.


SOCIAL NEED
-----------

Low Social satisfaction raises:
    trait.loneliness
    trait.hurt slightly

High Social satisfaction lowers:
    trait.loneliness
    may raise hope slightly

This does not force someone to seek romance.
ActivityPlanner and HumanEventEngine still decide what they do.


ENERGY / SLEEP
--------------

NeedsSystem is the body authority.

NeedsEmotionBridge uses:
    Energy
    SleepDebt

to nudge:
    patience
    tension
    anxiety
    playfulness

Then it synchronizes EmotionalProfile.Energy toward NeedsSystem.Energy.

That removes the old split where emotional Energy could drift separately from
the body.


HUNGER
------

High Hunger can:
    lower patience
    slightly raise tension

It does NOT directly create:
    anger
    crime
    cruelty

The personality still matters.


HYGIENE / COMFORT
-----------------

Low Hygiene can slightly increase:
    guard
    shame

Low Comfort can increase:
    tension
    anxiety

High Comfort can lower:
    tension


HUMAN EVENT FLOW
----------------

After a HumanEvent:

    NeedsEmotionBridge.ApplyHumanEventAndSync(
        npc,
        emotionalProfile,
        decision,
        gameTime);

This first applies any configured NeedsSystem event effect.

Examples from needs_system.json:
    fired
    break_up
    physical_fight
    arrested
    medical_emergency
    receive_paycheck
    miss_bill

Then current Needs pressure is projected into Fast emotional traits.

HumanEvent-specific emotional effects can still be applied by your TraitEngine or
event-specific trait code.

The bridge only owns the NEEDS -> FAST emotion connection.


IMPORTANT
---------

Do not make EmotionalProfile the source of gameplay truth again.

Preferred flow:

    Need changes
        ->
    Fast trait changes
        ->
    EmotionalProfile.SyncFromFast()
        ->
    dominant display mood

Not:

    EmotionalProfile meter changes
        ->
    overwrite Fast traits


ABOUT FAST TRAIT SETTING
------------------------

The user's current NpcTraits getter is known:

    traits.Get(id)

The exact public setter name was not present in the uploaded EmotionalProfile file.

Therefore NeedsEmotionBridge safely looks for common public setters:

    Set
    SetTrait
    SetValue
    Update
    UpdateTrait

Once you provide or compile against the exact NpcTraits implementation, we can replace
that reflection helper with the exact direct method call.

That would be the only part I expect might need a tiny compile-specific adjustment.


RECOMMENDED INTEGRATION ORDER
-----------------------------

Every fast world tick:

1.
    ActivityPlannerWorldBridge.Tick(...)

2.
    NeedsWorldBridge.TickPopulation(...)

3.
    NeedsEmotionBridge.Sync(...) for each updated full NPC

4.
    SocialEncounterEngine

5.
    HumanEventScheduler

6.
    LivingTownPendingWorkBridge


WHY THIS IS BETTER THAN A SECOND EMOTION SYSTEM
-----------------------------------------------

Project Eve already has:
    Fast emotion-like traits
    EmotionalProfile
    dominant moods

Building a new EmotionSystem with a second Anger/Stress/Sadness database would create:

    Fast Anger = 72
    EmotionSystem Anger = 35
    EmotionalProfile Anger = 58

and then we would have to decide which one is real.

This bridge avoids that problem entirely.
