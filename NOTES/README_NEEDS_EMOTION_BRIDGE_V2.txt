PROJECT EVE — NEEDS / EMOTION BRIDGE V2
=======================================

This version is wired to the CURRENT Project Eve trait system.

Confirmed current API:

NpcTraits:
    Get(string)
    Set(string,float)
    Adjust(string,float)
    Has(string)
    DriftTowardSetPoints(...)

TraitEngine:
    FastIds
    ApplyTag(...)
    ApplyTags(...)
    ApplyDailyDrift(...)
    ApplyRelationshipInfluence(...)
    UpdateTraitsDay(...)

The bridge therefore NO LONGER uses reflection.

RULES
-----

NeedsSystem owns body/life pressure.

Fast traits own moment-to-moment emotional gameplay truth.

Mid traits are not directly modified by NeedsEmotionBridge.

Slow traits are never modified by NeedsEmotionBridge.

EmotionalProfile remains a UI/readable projection through SyncFromFast().

WHY USE TraitEngine.ApplyTag
----------------------------

NeedsEmotionBridge could call:

    npc.Traits.Adjust(...)

directly.

Instead it calls:

    TraitEngine.ApplyTag(...)

because TraitEngine already contains Project Eve's centralized rules:

    intensity scaling
    Mid reduction
    Slow rejection
    stronger movement away from extremes

That keeps all trait movement consistent.

EXAMPLE
-------

High stress:

    trait.tension +
    trait.anxiety +
    trait.fear slight +
    trait.patience -

Sleep debt:

    trait.anxiety +
    trait.tension +
    trait.patience -

Loneliness:

    trait.loneliness +
    trait.hurt slight +

Fun / connection:

    trait.playfulness +
    trait.hope +
    trait.loneliness -

Needs do NOT directly change:

    mid.blunt
    mid.entitled
    mid.anxious_attach
    slow.life.*
    slow.music.*
    slow.sports.*

Those should change only through the existing repeated-behavior/history machinery.

IMPORTANT
---------

This version assumes the previously built:

    NeedsSystem.cs

exists in:

    ProjectEve.Worlds.SmallTownSystems

and the existing:

    EmotionalProfile

exists in:

    ProjectEve.Characters.Emotion
