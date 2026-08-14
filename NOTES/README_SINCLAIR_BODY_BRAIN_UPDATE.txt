PROJECT EVE — SINCLAIR FAMILY BODY + BRAIN UPDATE v1

ADD:
- Characters/NPCs/Body/SinclairBodyCanon.cs
- AI/Brain/SelfBodyFactContext.cs

REPLACE:
- Characters/NPCs/Eve.cs
- Characters/NPCs/Adam.cs
- Characters/NPCs/Lisa.cs
- Characters/NPCs/Edward.cs
- AI/Brain/ThoughtEngine.cs
- AI/Brain/DialoguePromptContext.cs
- AI/Brain/DialogueEngineText.cs
- AI/Brain/DialogueEngineInPerson.cs

ANCHOR FAMILY RULE:
Eve/Adam/Lisa/Edward are authored NPCs. Their stable body canon is hard-baked.
Do not reroll their stable body through BodyGenerator.

IMPORTANT IF NpcBodyProfile ALREADY HAS OLD RANDOM ROWS:
Run ONCE:
DELETE FROM NpcBodyProfile WHERE NpcId IN (1,2,3,4);

Then launch and save. After that, persistence owns their evolving BodyState.

BODY QUERY BEHAVIOR:
Ordinary self fact (eyes/hair/height/etc.):
- Thought event strength forced to 0
- TAGS normally none
- Dialogue gets exact canon fact
- no hallucinated replacement values

Private adult body question:
- NPC knows their own canonical fact
- privacy/boundary/relationship controls disclosure
- refusal is valid
- inventing another size/detail is not

EVE BODY TEST CANON:
Eye color: Hazel
Hair color: Light Brown
Height: 165 cm
Weight: 68 kg
Build: Curvy
Adult-private bra: 36C, full, natural
Nipple piercings: none

ANTI-ECHO:
Text dialogue now retries if Qwen copies the player's sentence, not only when it repeats its own prior reply.
