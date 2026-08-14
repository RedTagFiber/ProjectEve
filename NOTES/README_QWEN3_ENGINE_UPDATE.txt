PROJECT EVE — QWEN3 ENGINE UPDATE

REPLACE:
- AI/Brain/ThoughtEngine.cs
- AI/Brain/ThoughtPacket.cs
- AI/Brain/DialoguePromptContext.cs
- AI/Brain/DialogueEngineText.cs
- AI/Brain/DialogueEngineInPerson.cs
- Program.cs

WHAT CHANGED
1. Qwen3 thinking mode is disabled in every Ollama /api/chat request with:
   think = false

2. ThoughtEngine output contract is stricter:
   THOUGHT:
   LEAKS:
   TAGS:
   Invalid word-only TAGS are discarded to TAGS: none.
   Bad counted/prose LEAKS such as "2 (...)" are discarded.

3. CognitionPromptContext is now fed to Thought and Dialogue when available.

4. Dialogue prompts explicitly allow the full human range when supported by character state:
   kind / truthful / loving / deceptive / cruel / manipulative / profane / etc.
   They are also told NOT to force darkness and NOT to reveal private truth automatically.

5. Text dialogue strips an accidental "<character name>:" prefix and wrapping quotes.

6. Program now prints THOUGHT / LEAKS / TAGS before the outward reply on every normal test turn.

IMPORTANT
- This patch does not change your Ollama aliases. You already rebuilt:
  eve-thought  -> Qwen3 Abliterated 8B Q4_K_M
  eve-dialogue -> Qwen3 Abliterated 8B Q4_K_M
- Brain.cs is not included here because the current Brain was not among the files uploaded for this patch.
- LineBank seed-first behavior is a separate next change; this patch does not alter that policy.

FIRST TEST
Build Solution.
Then run Program normally and enter:
I know you lied to me about where you were last night.

Because that accusation may not be canon for Eve, a correct result may reject it rather than confess.
The important checks are:
- no visible "Thinking..." reasoning
- THOUGHT is private/person-specific
- LEAKS is none in text mode
- TAGS uses valid trait.*+delta@intensity format or none
- outward reply sounds human and does not automatically expose private thought
