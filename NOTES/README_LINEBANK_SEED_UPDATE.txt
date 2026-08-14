PROJECT EVE — LINEBANK SEED UPDATE
2026-08-13

WHY THIS PATCH EXISTS
The console test showed the old Brain returned a LineBank hit immediately.
That caused:
  compliment -> "Ask, don't steamroll."
  next message -> same cached line again

Qwen was never allowed to write those replies.

NEW TEXT FLOW
Player message
  -> ThoughtEngine
  -> ThoughtPacket / Fast tags
  -> search LineBank for a candidate phrase
  -> candidate goes into DialogueEngineText as OPTIONAL SEED
  -> Qwen sees current NPC + cognition + relationship + thought + recent chat + seed
  -> Qwen may IGNORE / ADAPT / EXPAND the seed
  -> Qwen's final line is sent
  -> good final line is stored back into LineBank

DIRECT BANK SPEECH
Only used if the Qwen dialogue call actually fails.

FILES TO REPLACE
ProjectEve/AI/Brain/Brain.cs
ProjectEve/AI/Brain/BrainDialogueIntegration.cs
ProjectEve/AI/Brain/DialogueEngineText.cs
ProjectEve/Program.cs

EXPECTED CONSOLE SOURCES
AI + LINEBANK SEED
  Qwen received a cached candidate and wrote the final reply.

AI NEW
  No useful cached candidate existed; Qwen wrote normally.

LINEBANK ERROR FALLBACK
  Qwen failed, so the cached line was used as emergency output.

IMPORTANT
This patch preserves the existing director commands, Think/TraitEngine flow,
session log, relationship interim block, formatting, typing delay, and LineBank DB.

FIRST TEST
Run the same conversation again:

hello
yes it does. your looking lovely today
I am not trying to do anything but have a nice txt
you kind of on guard, sorry I make you feel this way. just trying to get to know you

The second and third replies should NOT repeat "Ask, don't steamroll." unless
Qwen independently decides that wording genuinely fits.

NEXT ISSUE AFTER THIS TEST
ThoughtEngine is still over-reading harmless small talk/compliments:
  "hello" -> attraction+3 / hope+2
  compliment -> guard+3

Do NOT tune that until the LineBank routing is verified.
