PROJECT EVE — CONVERSATION MEMORY v1

WHAT THIS DOES
==============
ACTIVE SECTION
- stores every exact player/NPC/system line
- can feed the WHOLE active section back into Thought and Dialogue

WHEN SECTION ENDS
- exact transcript stays permanently
- local eve-thought creates a compact ConversationEvent summary
- direct learned facts are indexed
- agreed/pending plans are indexed

NEXT SECTION / DIFFERENT CHANNEL
- text -> phone -> in-person can retrieve the previous event
- unresolved plans are carried forward
- the old exact transcript remains available for later evidence / telephone-game provenance

FILES TO ADD
============
Conversations/ConversationManager.cs
Conversations/ConversationSummaryEngine.cs
Conversations/ConversationPromptContext.cs

Optional reference:
Data/World/Conversation/conversation_memory_v1.sql

BASIC FLOW
==========
Start:
long sessionId = ConversationManager.StartOrResume(
    eve.Id,
    eve.Name,
    "Ryan Slayback",
    "text",
    "phone");

Before Brain handles player line:
ConversationManager.AppendPlayer(sessionId, "Ryan Slayback", input);

After final Eve reply:
ConversationManager.AppendNpc(sessionId, eve.Id, eve.Name, reply);

Build AI conversation context:
string conversationContext = ConversationPromptContext.Build(
    eve,
    "Ryan Slayback",
    sessionId,
    "text",
    "phone");

Feed conversationContext to BOTH Thought and Dialogue.

End section:
var result = await ConversationManager.EndSectionAsync(
    sessionId,
    "text conversation ended");

Later in-person:
long newSession = ConversationManager.StartOrResume(
    eve.Id,
    eve.Name,
    "Ryan Slayback",
    "in_person",
    "Sinclair Coffee");

string continuity = ConversationPromptContext.Build(
    eve,
    "Ryan Slayback",
    newSession,
    "in_person",
    "Sinclair Coffee");

If the old text section contained:
"Let's meet at Sinclair Coffee at 7."
and Eve agreed,
the new context can contain that unresolved/agreed plan.

TELEPHONE GAME
==============
Do not give Adam Eve's exact transcript automatically.

The original ConversationMessage rows are truth/evidence.
When Eve later tells Adam something, create a separate gossip/transmitted-belief record sourced from Eve and optionally point it to ConversationEvent.Id.

That preserves:
- what was actually said
- what Eve remembers
- what Eve tells Adam
- what Adam tells Lisa
- what Lisa believes

SECTION BOUNDARIES
==================
End/start a new section when:
- channel changes
- meaningful scene/location changes
- participants materially change
- meaningful time gap
- hangup / leaves / goes to sleep
- explicit scene end
- natural context limit boundary

IMPORTANT MODEL LIMIT
=====================
Your local Qwen context is about 4096 tokens.

Project Eve stores the COMPLETE transcript regardless of size.

v1 exposes the complete active section as requested.
The next improvement should be a ContextBudgetManager that closes/summarizes a section at a natural beat before the model context ceiling, then immediately starts a continuation section.

That gives you complete history WITHOUT silent truncation.

IMPORTANT
=========
I did not overwrite Brain.cs because the exact current Brain.cs was not attached.
The existing project notes explicitly say not to rewrite Brain blindly because it owns director/OOC/session-log/PsyHierarchy and other working behavior.

NEXT:
Upload current AI/Brain.cs and wire this into Think(), Reply(), text/in-person channel changes, and section ending.
