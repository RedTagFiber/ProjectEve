PROJECT EVE — THOUGHT + LINEBANK TUNING / COOLDOWN UPDATE
2026-08-13

REPLACE THESE FILES:
- AI/Brain/Brain.cs
- AI/Brain/BrainDialogueIntegration.cs
- AI/Brain/DialogueEngineText.cs
- AI/Brain/ThoughtEngine.cs
- AI/LineBankService.cs
- Program.cs

WHAT CHANGED

1. THOUGHTENGINE NEUTRAL-EVENT CALIBRATION
Normal hello/small talk is strength 0.
Normal compliment/apology/reassurance is strength 1.
Meaningful argument/accusation/vulnerable disclosure is strength 2.
Betrayal/threat/violence/major confession is strength 3.

2. MAX FAST-TRAIT MOVEMENT BY EVENT STRENGTH
Strength 0: max 1 tag, max +/-1, intensity <= 4
Strength 1: max 2 tags, max +/-1, intensity <= 5
Strength 2: max 3 tags, max +/-3, intensity <= 8
Strength 3: max 4 tags, max +/-5, intensity <= 10

This is enforced in code after Qwen returns, not prompt-only.

3. LINEBANK STRONG-MATCH GATE
No recognized player intent = no LineBank seed.
A returned LineBank row must match the requested intent exactly.
Trait-only fallback is not accepted as a seed.

4. LINEBANK COOLDOWN
- Never use the same LineBank row on back-to-back seed turns.
- Duplicate wording is rejected too, even if it is stored under a different row.
- Maximum 2 LineBank-assisted replies in a row.
- After 2, one reply is forced AI_NEW. Then the streak resets.
- Cooldown state lives in each Brain/NPC, not globally.

5. RECENT REPLY REPETITION
The previous NPC reply is passed into DialogueEngineText.
Qwen is told not to repeat it.
If the first draft exactly repeats it, the engine retries once with NO LineBank seed.

6. PROGRAM DEBUG DELTAS
Now also prints:
guard / anxiety / tension / openness / hope / attraction

EXPECTED SOURCES
AI + LINEBANK SEED
AI NEW
AI RETRY / NO SEED
LINEBANK ERROR FALLBACK

TEST THE SAME FOUR LINES:
hello
yes it does, your looking lovely today
I am not trying to do anything but have a nice txt
your kind of on guard, sorry I make you feel this way, just trying to get to know you

EXPECTED:
- hello should be neutral or tiny trait movement
- compliment should not automatically mean manipulation
- same LineBank line cannot seed back-to-back
- after two seed-assisted replies, next turn is AI_NEW
- exact repeated NPC reply triggers one no-seed retry


IMPORTANT FIX
Your project already owns LineBankService at:
  ProjectEve/AI/LineBankService.cs

Do NOT keep a second copy at:
  ProjectEve/Narrative/Texting/LineBankService.cs

If that duplicate file was copied into the project from the previous ZIP, delete it.
The duplicate creates CS0121 ambiguous calls because both files declare the same
ProjectEve.AI.LineBankService class and StoreLiveLine method.
