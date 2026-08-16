# ProjectEve Social Graph Setup

This package adds the first real player social graph layer for Project Eve.

It is designed around these rules:

- NPC posts are global.
- Each player has their own social graph.
- Book is friend-request based.
- Gram is follow based.
- Book mute/block is separate from Gram mute/block.
- Global mute/block overrides normal app behavior.
- NPC social requests can be accepted/refused based on trust.
- No player social code is hardcoded to Ryan.

## Folder layout

Copy the folders into your existing solution:

```text
ProjectEve.MediaSystem/
  SocialGraph/
    BookFriendStatus.cs
    SocialAppPlatform.cs
    SocialRequestType.cs
    PlayerNpcSocialState.cs
    PlayerSocialGraphService.cs
    NpcSocialPreference.cs
    NpcSocialRequestDecision.cs
    NpcSocialRequestPolicy.cs

ProjectEve.PhoneOS/
  Services/
    PhonePlayerIdentity.cs
    InstantGramPhoneService.cs
```

## Install steps

1. Copy `ProjectEve.MediaSystem/SocialGraph` into your real `ProjectEve.MediaSystem` project.
2. Copy `ProjectEve.PhoneOS/Services/PhonePlayerIdentity.cs` into your real `ProjectEve.PhoneOS/Services` folder.
3. Replace your existing `ProjectEve.PhoneOS/Services/InstantGramPhoneService.cs` with the included one.
4. Build the solution.

The new service will create this SQLite table inside:

```text
D:\ProjectEveData\Database\projecteve_runtime.db
```

Table:

```text
PlayerNpcSocialStates
```

## What this gives you now

### Per-player social state

Every social row uses:

```text
PlayerId + NpcId
```

So multiple players can have different Book friends, Gram follows, blocks, and mutes.

### Book visibility

Book posts show only when:

```text
BookFriendStatus = friends
not globally blocked
not blocked on Book
not globally muted
not muted on Book
```

### Gram visibility

Gram posts show only when:

```text
IsFollowingOnGram = true
not globally blocked
not blocked on Gram
not globally muted
not muted on Gram
```

### Starter setup

Each player automatically starts with Eve as:

```text
known
Book friend
Gram followed
```

NPC id:

```text
000001
```

## Important later hookup

When your `PlayerProfileService` gives you the current player, call this before loading feeds:

```csharp
_social.SetPlayerIdentity(
    playerId: currentPlayer.PlayerId,
    displayName: currentPlayer.DisplayName,
    gramHandle: currentPlayer.GramHandle,
    bookHandle: currentPlayer.BookHandle
);
```

Right now the service has safe defaults:

```text
player_001
Player
@player
```

So it is no longer hardcoded to Ryan.

## Conversation/NPC social request design

Use `NpcSocialRequestPolicy` when the player asks things like:

```text
Can I add you on Gram?
Can I add you on Book?
Can I follow you?
Send me a friend request.
```

The policy checks:

```text
NPC uses the app
NPC is not blocked
trust level is high enough
NPC privacy preference allows it
```

Trust levels are intentionally low:

```text
2 = friendly enough for Gram
3 = friendly enough for Book
4 = NPC may ask the player first
```

If the NPC accepts, your conversation system should call:

```text
FollowNpcOnGram(...)
AcceptBookFriendRequest(...)
```

If the NPC refuses, do not update the social graph.

## Next likely files to update after this

- `Book.razor`: add buttons for mute/block/unblock on Book.
- `Gram.razor`: add buttons for mute/block/unblock on Gram.
- `PlayerProfileService`: add stable `PlayerId`, `BookHandle`, and `GramHandle` if not already present.
- NPC conversation system: detect Book/Gram add requests and apply the social graph only if the NPC agrees.
