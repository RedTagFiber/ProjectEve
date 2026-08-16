# ProjectEve Social Rules

## Main rule

Meeting an NPC makes them discoverable. It does not automatically add them to Book or Gram.

## Book

Book is friend-based.

States:

- none
- request_sent_by_player
- request_sent_by_npc
- friends
- declined

Only friends appear in the normal Book feed.

## Gram

Gram is follow-based.

The player must follow the NPC to see the NPC's Gram posts.

## App-specific mute/block

Book controls affect Book only.

Gram controls affect Gram only.

Global controls affect everything.

## Block examples

- Block on Book: hides Book posts/comments/requests, but Gram can still show.
- Block on Gram: hides Gram posts/comments/follow suggestions, but Book can still show.
- Block Everywhere: hides normal Book, Gram, messages, calls, suggestions, and NPC-initiated contact.

## Mute examples

- Mute on Book: hides Book posts, but keeps friendship.
- Mute on Gram: hides Gram posts, but keeps follow state.
- Mute Everywhere: hides normal feed activity everywhere, but is softer than blocking.

## Multiple players

Social state is per player.

One player can block Eve while another player is Book friends with her.

## NPC understanding

NPC dialogue should understand social requests, but the social graph should only update after NPC agreement.

Conversation flow:

1. Player asks to add/follow NPC.
2. NPC social request policy checks trust and preferences.
3. NPC says yes/no.
4. If yes, update social graph.
5. If no, nothing changes.
