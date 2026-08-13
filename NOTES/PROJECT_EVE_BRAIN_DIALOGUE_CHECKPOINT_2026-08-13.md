# PROJECT EVE — BRAIN / THOUGHT / DIALOGUE CHECKPOINT
Date: 2026-08-13
Status: LOCKED STARTING POINT

## CORE DECISION

Project Eve will keep a shared NPC Brain and a shared ThoughtEngine, but split outward communication by channel.

Architecture:

WORLD / PLAYER
      ↓
NPC BRAIN
      ↓
THOUGHT ENGINE
      ├─ PRIVATE THOUGHT
      ├─ FAST TRAIT TAGS
      └─ INVOLUNTARY BODY LEAKS
              ↓
         TraitEngine
              ↓
     COMMUNICATION MODE
       /             \
      /               \
TEXT DIALOGUE      IN-PERSON DIALOGUE
      ↓               ↓
typed words       conscious body presentation
                  + spoken words

Future:
- Phone/voice can become a third presentation layer.
- Group text / parties / multi-NPC scenes will eventually be coordinated by a ConversationManager.
- Each NPC keeps its own Brain/Thought pass. One model call should NOT write multiple NPC minds at once.

---

## MODELS

Current intended setup:

Thought model:
- Qwen 8B-class local model
- Ollama model name currently: eve-thought

Dialogue model:
- current local dialogue model (Mistral-family / existing eve-dialogue setup)
- Ollama model name currently: eve-dialogue

No third AI model is required for body language.

The Thought model decides involuntary body leakage.
The In-Person Dialogue model decides deliberate/conscious body presentation plus spoken words.

---

## THOUGHT ENGINE CONTRACT

ThoughtEngine should output exactly:

THOUGHT: <private internal interpretation>
LEAKS: <0-3 involuntary body cues OR none>
TAGS: <Fast trait changes OR none>

Example:

THOUGHT: Don't let him see how badly that landed.
LEAKS: eye_break_short; jaw_tight
TAGS: trait.hurt+3@7; trait.guard+2@6

Rules:
- THOUGHT is private. Player never sees it.
- LEAKS are unconscious/involuntary.
- TAGS only modify Fast traits.
- Never directly modify Mid or Slow traits here.
- Text mode forces LEAKS: none because the recipient cannot see the NPC's body.
- Body leakage is NOT a lie detector.
- A cue may mean anxiety, shame, hurt, attraction, fear, anger, deception, etc.
- NPC traits and personal baseline influence which tells appear.

ThoughtEngine is responsible for:
1. interpreting the moment,
2. deciding small emotional changes,
3. deciding what physically leaks before conscious control.

---

## BODY LANGUAGE DESIGN

We created:

project_eve_body_language_rules_v1.json

Target project location:

Data/World/Ohio/body_language_rules.json

The JSON covers all 20 Fast traits:

trait.anger
trait.anxiety
trait.fear
trait.shame
trait.guilt
trait.hurt
trait.jealousy
trait.resentment
trait.trust
trait.affection
trait.desire
trait.attraction
trait.tension
trait.playfulness
trait.pride
trait.patience
trait.guard
trait.openness
trait.loneliness
trait.hope

The rules include:
- intensity bands
- common physical cues
- conscious cover behaviors
- trait-combination rules
- ambiguity rules
- lie-skill concepts
- personal tell profile concepts

Important principle:

NO universal "lying animation."

Body language comes from:

personal baseline
+ Fast traits
+ Mid traits
+ relationship
+ current needs/stress
+ stakes
+ lie skill
+ emotional control
+ context

Example:

NPC A:
anxiety 78
guilt 72
guard 25
lie skill 20

May:
- fidget
- hesitate
- overexplain
- break eye contact

NPC B:
anxiety 78
guilt 72
guard 84
pride 77
lie skill 88

May:
- tiny eye break
- jaw tight
- regain control
- hold eye contact deliberately
- answer calmly

Player never gets:
"She is lying."

Player gets observable evidence and must interpret it.

---

## PERSONAL TELL PROFILE

Future/full NPC hidden data should include something like:

lie_skill
emotional_control
expressiveness
self_monitoring

default_tells
stress_tells
hurt_tells
attraction_tells
anger_tells
deception_tells

Traits should bias/generate these values rather than making them completely random.

Long-term idea:
Close relationships can learn another NPC's tells.

Example memory/knowledge:

target=Eve
cue=goes_very_still
interpretation=possibly_hiding_something
confidence=74

This makes strangers harder to read than spouses/best friends.

---

## IN-PERSON DIALOGUE DESIGN

Thought provides involuntary leakage.

DialogueEngineInPerson receives:
- character identity
- Fast/Mid/Slow relevant state
- private thought
- involuntary LEAKS
- relationship context
- memory/history
- scene
- audience/present NPCs
- body-language rule hints

DialogueEngineInPerson decides:

PRESENTATION: <0-2 conscious body/social actions>
SAY: <spoken words>

Example:

Thought:
LEAKS: eye_break_short; jaw_tight

Dialogue:
PRESENTATION: folds her arms and steadies her voice
SAY: I'm fine.

Player-facing result:

*Her eyes flick away for a second and her jaw tightens. She folds her arms and steadies herself.*
**"I'm fine."**

Critical distinction:

LEAKS = unconscious body
PRESENTATION = conscious body management
SAY = chosen speech

These may contradict each other.

That contradiction is intentional and important.

---

## TEXT DIALOGUE DESIGN

DialogueEngineText is separate from in-person dialogue.

Text receives:
- character
- relationship
- private thought
- current Fast state
- memory/history
- recent chat
- group members if group text

Text does NOT receive player-visible physical body cues.

Text realism comes through:
- length
- fragments
- punctuation
- warmth/coldness
- emoji choice
- delay feel
- short answers
- message bursts
- avoidance
- sudden formality
- deleted/retyped behavior later if supported by UI

Text output is typed words only.

---

## HISTORY / MEMORY INTEGRATION

Architecture stays:

WORLD TRUTH
HistoryEventRow
facts / participants / beats / peaks
      ↓
PERSONAL LIFE HISTORY
HistoryRecord
meaning / trait links / trust gate / telephone noise / Tier5 actors
      ↓
MEMORY DATABASE
MemoryRecord
retrievable/decaying personal memory
      ↓
Thought / Dialogue / Behavior

HistoryRecord is personal life-history meaning.

HistoryEventRow is objective-ish world-event truth.

MemoryRecord is current recallable memory.

Gossip is transmitted belief and may differ from truth.

Important fix:
Do NOT use HistoryRecord.ToString() for prompts.

Use actual fields:
- Summary
- Age
- Category
- Importance

Memory prompt retrieval should use:
MemoryDatabase.GetMemories(owner.Id, ...)

NpcId is authoritative identity.

Relevant memory/history should be fed to both Thought and Dialogue.

Do not dump all memories.
Use a small relevant/high-importance set.

---

## LINEBANK POLICY — LOCKED

OLD:
Text always tried LineBank first.

NEW:
LLM is primary.

Flow:

start Dialogue LLM
      ↓
if response is fast
      ↓
use new LLM response
      ↓
store successful response in LineBank

if LLM takes too long
      ↓
ask LineBank for a matching cached line
      ↓
if available, send cached line immediately
      ↓
late LLM result can still be added to LineBank for future use

if LLM errors
      ↓
LineBank can be fallback

Default soft fallback timeout in rebuild:
3500 ms

Can later tune to local hardware.

Purpose of LineBank:

It is NOT the NPC's primary brain.

It is:
- latency fallback
- growing dialogue cache
- common-line accelerator
- offline performance helper

As play continues:
rare/new situations generate fresh lines,
fresh lines enter bank,
future similar moments become cheaper/faster.

---

## NEW REBUILD PACKAGE

Created artifact:

project_eve_brain_dialogue_rebuild_v1.zip

Contains:

AI/Brain/ThoughtPacket.cs
AI/Brain/ThoughtEngine.cs
AI/Brain/BodyLanguageRuleContext.cs
AI/Brain/DialoguePromptContext.cs
AI/Brain/DialogueEngineText.cs
AI/Brain/DialogueEngineInPerson.cs
AI/Brain/BrainDialogueIntegration.cs

Data/World/Ohio/body_language_rules.json

README_BRAIN_DIALOGUE_REBUILD.txt

This package is the starting implementation, NOT yet guaranteed compile-clean against every current repo file.

---

## CURRENT BRAIN BEHAVIOR TO PRESERVE

Existing Brain.cs already contains useful working systems that should NOT be thrown away:

- Owner
- director commands
- OOC
- SCENE
- TONE
- FACT
- REL
- TRAIT commands
- session log
- PsyHierarchy behavioral pull
- ToneInference
- EmotionSpeechEngine
- MessageFormatter
- typing delay
- relationship block
- line-bank speaker selection
- recent chat handling

Do not rewrite the whole Brain blindly.

Integrate the new engines into the existing Brain incrementally.

---

## IMPORTANT EXISTING ISSUES TO FIX DURING INTEGRATION

1. ThoughtEngine had static global change signatures:
   _lastSceneSignature
   _lastAppearanceSignature
   _lastEmotionSignature

These are shared across all NPCs and should NOT remain global.
Move to per-NPC Brain state or key by NpcId.

2. Brain Energy is not currently synchronized from NeedsSystem.
Eventually use NeedsSystem Energy rather than a floating default.

3. BuildRecentMemoryBlock in old Brain was placeholder text.
New context builder uses actual memory rows.

4. Old Thought history used h.ToString().
New rebuild uses HistoryRecord.Summary/Age/Category/Importance.

5. Old single DialogueEngine had conflicting rules:
Brain said spoken words only,
DialogueEngine in-person said ACTION/SAY.
Split Text/InPerson solves this.

6. Current relationship block uses Fast traits as interim relationship values and hardcodes Ryan in text.
Eventually use the actual directional Relationship edge for the active target.

---

## RELATIONSHIP BOUNDARY

Keep strict ownership:

Fast traits:
current emotional/reactive state

Relationship:
per-target bond

Example:

Sarah may have:
trust Ryan 80
trust John 15
trust Lisa 72

One global trait.trust cannot represent that.

Dialogue context eventually needs active target relationship data.

---

## FUTURE GROUP CONVERSATION ARCHITECTURE

Later add:

ConversationManager / ConversationOrchestrator

It decides:
- channel
- who is present
- who was addressed
- who heard a line
- who can overhear
- who responds
- who stays silent
- interruptions
- response order

Example party:

Ryan speaks to Sarah.

Sarah hears it.
Lisa sees them but cannot hear.
John is across the room and misses it.
Edward enters later.

Each NPC only processes what they actually perceived.

Do NOT generate all NPCs in one dialogue model call.

Each NPC:
perception
→ Thought
→ decision to respond or not
→ correct Dialogue engine

---

## CORE OWNERSHIP RULES

NeedsSystem owns:
body/life pressure

Fast traits own:
current emotional/reactive state

Relationship owns:
per-target social state

Mid traits own:
recurring behavioral pattern

Slow traits own:
identity/preferences

EmotionalProfile owns:
display/compatibility projection only

HumanEventEngine owns:
event/decision selection

HistoryEventRow owns:
world event truth

HistoryRecord owns:
personal life-history meaning

MemoryRecord owns:
retrievable personal memory

Gossip owns:
transmitted belief/distortion

Dialogue owns:
expression of current state, NOT truth storage

Thought owns:
private interpretation and immediate Fast reaction, NOT history ownership

Main danger:
Do not let multiple systems write the same truth.

---

## NEXT WORK SESSION — START HERE

1. Open current Brain.cs.

2. Add ThoughtPacket use into Brain.Think():
   - generate packet
   - LastThought = packet.Raw
   - apply packet Fast tags through TraitEngine
   - retain packet for Reply()

3. Replace current one DialogueEngine path with:
   - DialogueEngineText
   - DialogueEngineInPerson

4. Keep all current director commands/session logging.

5. Change Text flow:
   - LLM first
   - LineBank only after soft timeout/error
   - successful fresh LLM output stored into bank

6. Compile.

7. Fix Visual Studio errors ONE AT A TIME against the exact current project APIs.
   Do not invent broad rewrites.

8. Once one-on-one text and one-on-one in-person work:
   - improve body cue renderer from cue ids to natural non-AI phrase catalog.

9. Then:
   - real per-target Relationship context
   - NeedsSystem Energy
   - relevant memory retrieval
   - ConversationManager for multi-NPC/group scenes

---

## STARTING PHILOSOPHY

The simulation creates the person.

The LLM does not decide who the person is.

World state
+ needs
+ traits
+ relationships
+ history
+ memory
+ current situation
      ↓
private thought
      ↓
emotional change
      ↓
involuntary leakage
      ↓
conscious social behavior
      ↓
speech/text

The dialogue model gives the simulated person a voice.

That is the locked direction for Project Eve.
