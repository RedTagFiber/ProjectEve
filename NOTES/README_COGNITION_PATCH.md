# Project Eve — Cognition / IQ / Education Patch

This folder is laid out like the ProjectEve source tree so the files can be copied
into the matching locations.

## What this patch adds

- Stable `CognitiveProfile` on every `SimCharacter`
- Conventional IQ-style baseline generated once
- Separate 0–100:
  - verbal reasoning
  - working memory
  - processing speed
  - practical reasoning
  - vocabulary
  - reading exposure
  - cognitive maturity
- Education generated from age + job/life context
- Speech profile:
  - sentence complexity
  - vocabulary use
  - slang
  - formality
  - verbosity
  - profanity
  - code switching
  - regional style
- Expandable domain knowledge
- SQLite persistence in:
  - `CognitiveProfile`
  - `NpcDomainKnowledge`
- Character sheet debug display
- Prompt-safe cognition context that does NOT normally hand the raw IQ number to the LLM

## Project Eve realism rules locked into the code

1. IQ is not morality, wisdom, kindness, education, or job skill.
2. Education does not simply raise IQ.
3. Age changes maturity/experience and can affect processing speed; age does not equal intelligence.
4. Job experience creates domain knowledge.
5. A lower-IQ NPC can be highly skilled in a learned domain.
6. A high-IQ NPC does not automatically know facts they never learned.
7. Slang is NOT "low IQ." Slang/formality depend on age, education, personality, region,
   social context, and code switching.
8. Stress/fatigue can temporarily reduce effective working memory without changing IQ.
9. An existing NPC changing jobs does NOT magically gain a required degree.
10. The Brain rebuild should consume `CognitionPromptContext.BuildForNpc(npc)` later.

## New files

Copy as new files:

- `ProjectEve/Characters/Cognition/CognitiveProfile.cs`
- `ProjectEve/Characters/Cognition/CognitiveProfileGenerator.cs`
- `ProjectEve/Characters/Cognition/CognitionPromptContext.cs`

## Replacement files

Replace your current versions with:

- `ProjectEve/Characters/Base/SimCharacter.cs`
- `ProjectEve/Characters/Base/CharacterRepository.cs`
- `ProjectEve/Characters/Base/CharacterFactor.cs`
- `ProjectEve/Characters/Base/CharacterSheetPrinter.cs`
- `ProjectEve/Characters/Characters/CharacterFactory.cs`

## Database

No manual SQL migration should be needed for this patch.

`CharacterRepository` creates these tables when needed:

- `CognitiveProfile`
- `NpcDomainKnowledge`

Existing NPCs that do not have a cognitive row will receive one the first time
`CharacterFactory.LoadCharacter()` runs and `EnsureCognition()` sees the profile is missing.

The generated profile is then saved for persisted NPCs (`Id > 0`) so IQ is not rerolled
every time they load.

## Optional town_jobs.json fields

The replacement `CharacterFactory.cs` adds these OPTIONAL fields to `TownJob`:

```json
{
  "minimumEducation": "high_school",
  "typicalEducation": "associate_degree",
  "fieldOfStudy": "nursing"
}
```

Existing job JSON remains compatible if these fields are missing. The cognition generator
contains conservative occupation-based fallback inference.

Supported education strings include:

- `grade_school`
- `some_high_school`
- `high_school`
- `ged`
- `trade_certificate`
- `some_college`
- `associate_degree`
- `bachelor_degree`
- `master_degree`
- `doctorate`
- `professional_degree`

## Suggested first test

1. Copy the three new Cognition files.
2. Replace the five existing files.
3. Build.
4. Fix compile errors one at a time if your local tree has API differences.
5. Run your existing console app.
6. Use `sheet`.
7. Confirm a `COGNITION / EDUCATION` section appears.
8. Restart the app and confirm the same saved NPC keeps the same IQ.
9. Generate several NPCs with different ages/jobs and compare:
   - education
   - speech profile
   - domain knowledge
10. After this is stable, wire `CognitionPromptContext.BuildForNpc(npc)` into the new
    Thought / Dialogue Brain path.

## Important note for CharacterFactory ordering

A new NPC gets a stable base cognition first. When `CreateWithJob()` applies the job,
`ApplyJob()` finalizes education and domain knowledge. That lets age and job requirements
shape education without rerolling IQ.

For an already-finalized NPC, changing jobs refreshes domain knowledge but does not
automatically award a new degree.
