# Project Eve

Project Eve is a persistent-world NPC simulation project built around coherent character lives, objective world history, subjective knowledge, personal memory, relationships, institutions, and long-term continuity.

## Repository Purpose

This repository contains source code, structured source assets, configuration, and architecture needed to build Project Eve.

Runtime world data, local databases, generated media, AI models, backups, and machine-specific files are intentionally excluded from Git.

## Local Development Paths

Core source repository:

D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean

Runtime data root:

D:\ProjectEveData

Database root:

D:\ProjectEveData\Database

NPC asset root:

D:\ProjectEveData\NPC

## Core Databases

Project Eve uses multiple canonical SQLite databases.

### MAIN
project_eve.db

Owns current-world character and gameplay truth, including current NPC/build state, traits/personality, current occupation/employer, account state, and other current canonical facts.

### HISTORY
project_eve_history.db

Owns objective / god-view immutable history.

Examples:
- WorldEvents
- EventParticipants
- EventFacts
- ConversationTurns
- Communications
- SceneActions

HISTORY answers:

What objectively happened?

### RELATIONSHIPS / MEMORY
project_eve_relationships.db

Owns subjective human truth.

Examples:
- RelationshipStates
- RelationshipReasons
- PersonalMemories
- KnowledgeItems
- NpcTraitReasons
- NpcTraitChangeHistory

This database answers:

What does this person know, believe, remember, feel, or interpret?

### LOCATIONS
project_eve_locations.db

Owns canonical location, room, scene, and world-place data.

## Core Architectural Rule

Objective truth and subjective truth must remain separate.

HISTORY knows what actually happened.

Relationship / Memory data knows what each person believes, remembers, or feels about what happened.

An NPC must never receive god-view knowledge simply because HISTORY contains the truth.

## Character Factory

Character Factory is responsible for generating 100% of the first-pass NPC.

A generated NPC should be complete and playable without manual data entry.

Character Factory must generate coherent:

- identity
- family
- childhood
- education
- profession
- qualifications
- competencies
- relationships
- health
- finances
- homes
- vehicles
- phones
- possessions
- institutions
- routines
- life history
- memories
- knowledge
- current state
- relationship web

Current facts should emerge from life history rather than being independently randomized.

## Life History Rules

NPCs should receive meaningful annual life events.

Typical target:

- Age 0-5: 1-2 meaningful events per year
- Age 6-12: 1-3 per year
- Age 13-17: 2-4 per year
- Age 18+: 1-5 per year

Adults should also have approximately 1-7 major life events across adulthood.

Major events may be positive, negative, or mixed.

History should be causal and continuous.

## Historical People

If an important person appears in an NPC's history, that person should exist as a real persistent NPC.

Examples:

- parents
- grandparents
- siblings
- ex-partners
- childhood friends
- coworkers
- teachers
- supervisors
- doctors
- neighbors
- clergy
- people involved in major events

Do not leave important historical people as anonymous prose placeholders.

## NPC Tier

Global NPC Tier controls simulation and asset depth.

Tier does not determine whether a person is real.

Tier 5 NPCs are persistent real people used to make history, family, institutions, professions, and social networks coherent.

A Tier 5 NPC may later be promoted while retaining the same CharacterId, history, memories, and relationships.

## Personal Relationship Tier

Personal importance is separate from global NPC tier.

Each NPC may rank another person by personal importance:

- Tier 1 - Core person
- Tier 2 - Very important
- Tier 3 - Meaningful
- Tier 4 - Peripheral
- Tier 5 - Historical / weak / background connection

The player is not automatically Tier 1.

A globally Tier 5 NPC may still be Personal Tier 1 to another character.

## Relationship Status

Relationships may also have current status such as:

- Active
- Distant
- Estranged
- Past
- Deceased
- Unknown whereabouts

Death does not automatically reduce emotional importance.

A deceased parent or grandparent may remain Personal Tier 1 and continue to exert strong lasting influence.

## Memory Architecture

A shared event may have:

1. TRUE HISTORY
   - objective facts

2. KNOWN HISTORY
   - what a specific NPC knows or believes happened

3. PERSONAL MEMORY
   - how that NPC remembers, interprets, and emotionally experiences the event

Memory strength, confidence, importance, and emotional meaning may change over time.

Strong memory does not necessarily mean perfect accuracy.

## Professional NPCs

Professional identity must be supported by a believable qualification path.

Examples include:

- doctor
- nurse
- police officer
- detective
- firefighter
- paramedic
- attorney
- judge
- teacher
- banker
- mechanic
- pastor

Qualified for a role does not mean good at the role.

Actual competence and professional reputation must remain distinct.

## Institutions

Project Eve is intended to support interconnected institutions such as:

- Police
- Fire / EMS
- Hospital
- Court
- Legal
- Church / support
- Bank
- Car dealerships
- Mechanics / towing
- Phone store

Systems own specialized operational data.

HISTORY owns what objectively happened.

NPCs inside institutions only act on evidence, knowledge, and information available to them.

## NPC Studio

NPC Studio is a refinement and authoring tool.

Character Factory creates the full first-pass NPC.

NPC Studio is used to:

- inspect
- refine
- correct
- deepen
- edit relationships
- edit history
- adjust personal importance
- inspect relationship webs
- add family/friends/coworkers
- generate connected people
- review memories and known history

NPC Studio should not be required to finish basic missing character data.

## Generated Media

Generated and canonical runtime media should live outside the Git repository.

Examples:

- NPC portraits
- voice
- video
- music
- ComfyUI outputs
- generated security footage
- generated scene media

## AI Models

Large local AI models are not stored in Git.

Examples:

- GGUF
- ONNX
- safetensors
- checkpoints
- local model files

## Files That Must Not Be Committed

Do not commit:

- bin/
- obj/
- .vs/
- runtime databases
- database WAL/SHM files
- database backups
- generated media
- AI model files
- temporary exports
- scratch files
- secrets
- tokens
- API keys
- machine-specific local configuration

## Build

Project Eve currently targets .NET 10.

Typical build:

dotnet restore
dotnet build

## Current Development Direction

The current major redesign focuses on:

1. canonical person-domain architecture
2. complete Character Factory generation
3. deep coherent life history
4. family and relationship graph
5. personal relationship tiers
6. professional qualification and competency systems
7. vehicles / phones / homes / finance
8. objective history vs subjective knowledge and memory
9. one fully completed Golden NPC
10. scaling only after the data model is proven

## Design Principle

The goal is not to generate a collection of random fields.

The goal is to generate a person whose current life makes sense because of the life they have already lived.
