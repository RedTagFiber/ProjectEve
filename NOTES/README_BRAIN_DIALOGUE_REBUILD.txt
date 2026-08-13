PROJECT EVE — BRAIN / THOUGHT / DIALOGUE REBUILD V1
===================================================

GOAL
----
Keep one private Thought model and split outward communication:

    ThoughtEngine
        -> private THOUGHT
        -> involuntary LEAKS
        -> Fast TAGS

    DialogueEngineText
        -> typed words only

    DialogueEngineInPerson
        -> conscious PRESENTATION
        -> SAY

This matches Project Eve's existing current trait architecture.

FILES
-----
AI/Brain/ThoughtPacket.cs
AI/Brain/BodyLanguageRuleContext.cs
AI/Brain/ThoughtEngine.cs
AI/Brain/DialoguePromptContext.cs
AI/Brain/DialogueEngineText.cs
AI/Brain/DialogueEngineInPerson.cs
AI/Brain/BrainDialogueIntegration.cs

BODY LANGUAGE JSON
------------------
Copy the previously generated:

    project_eve_body_language_rules_v1.json

to:

    Data/World/Ohio/body_language_rules.json

or set environment variable:

    EVE_BODY_LANGUAGE_RULES=<full path>

The file is loaded once and cached.

HISTORY / MEMORY
----------------
Both Thought and Dialogue now receive actual history/memory text.

History uses:
    HistoryRecord.Summary
    HistoryRecord.Age
    HistoryRecord.Category
    HistoryRecord.Importance

This fixes the old use of HistoryRecord.ToString().

Memory uses:
    MemoryDatabase.GetMemories(owner.Id, ...)
    Summary
    Category
    Importance
    Strength
    EventId

NpcId is authoritative.

LINEBANK POLICY
---------------
OLD:
    every text reply tries LineBank first.

NEW:
    LLM starts first.

    If LLM answers quickly:
        use fresh LLM response
        store it into LineBank

    If LLM is slow beyond softTimeoutMs:
        pull a matching LineBank response if one exists
        return it immediately
        late LLM result may be harvested into LineBank for future use

    If LLM errors:
        LineBank is fallback

Default soft timeout:
    3500 ms

Change this depending on local hardware.

IMPORTANT
---------
The late-line harvest is a GAME RUNTIME async task. It is not required for correctness.
If you do not want fire-and-forget work, remove HarvestLateLineAsync and simply discard
the late LLM result after a bank timeout.

BRAIN THINK FLOW
----------------
Recommended:

    var thought = ThoughtEngine.GeneratePacket(
        situation,
        Owner,
        channel: inPerson ? "in_person" : "text",
        audience: audienceDescription);

    LastThought = thought.Raw;

    TraitEngine.ApplyTags(Owner, thought.Tags);

Then:

    Text:
        BrainDialogueIntegration.ReplyTextAsync(...)

    In person:
        BrainDialogueIntegration.ReplyInPersonAsync(...)

PLAYER-VISIBLE IN-PERSON
------------------------
Thought LEAKS:
    eye_break_short
    jaw_tight

Dialogue PRESENTATION:
    folds her arms and steadies her voice

Dialogue SAY:
    I'm fine.

Player gets:

    *eye break short, jaw tight. folds her arms and steadies her voice.*
    **I'm fine.**

Later, replace the simple HumanizeCue renderer with a richer non-LLM phrase catalog
so cue ids become polished prose without another model call.

GROUP CHAT / PARTY
------------------
These engines are already compatible with groups because both accept:

    relationshipContext
    recentChat

and Thought accepts:

    channel
    audience

The future ConversationManager should decide:
    who heard the message
    who was addressed
    who is present
    who thinks
    who responds
    who stays silent
    who can overhear

Do NOT let one Dialogue call write multiple NPC minds.
