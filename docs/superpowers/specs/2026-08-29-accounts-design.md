# Accounts, and everything outside the game

Design session, 2026-08-29. What a user is, what they own, how they get in, and the whole
life of a game record before and after the game server holds it.

Sixth module. It follows [the asset library](2026-08-28-asset-library-design.md) and
[the army](2026-08-28-army-design.md), which described the two things a user brings to a
table. This is the account those hang off, and the surface a client talks to when it is
not in a game.

Scope is the management side. The client is where this API gets consumed, but the client's
own screens are not designed here.

## An account is two fields

```
Account   id     UUIDv7, generated in application code
          name   display name, non-unique
```

Nothing else. Per-account defaults — a preferred rule pack, default base sizes — can be
added later and change nothing about what an account is.

Names are not unique because there is no reason for them to be. Nothing addresses a user
by name; everything addresses them by id.

## Auth is a module, not a vendor

Stytch answers exactly one question: **is this session valid, and whose is it.** Its
`external_id` holds the account's GUID, and that is the entire link between the two
systems. No Stytch identifier is stored anywhere in our schema.

**The flow runs through management.** The client posts to our endpoints; management calls
Stytch; our session token comes back. The client never holds a Stytch public token and
never calls Stytch directly.

All of Stytch's methods are supported — email OTP, magic link, password, OAuth. Email OTP
and password are plain API calls with no browser in them. Magic link and OAuth are
click-through by nature, so the client opens the system browser or a webview and catches
the redirect.

**Replacing Stytch is a change inside one module.** That is the constraint the routing
exists to satisfy, and it is why the vendor is not allowed to appear in the client, in the
schema, or in any other module. The one place confinement is nominal rather than literal
is an OAuth consent page, which has Stytch in the URL bar. That costs nothing.

This supersedes `architecture.md`, which said the browser owns login and the game client
acquires a token from that session. That was written when the web app was going to be the
front door. It is not.

**Playing requires an account.** There are no guests: you need an owned army to bring, and
there is no offline mode.

## What an account owns

Armies, assets and their blobs, folders, layouts, and the games it takes part in.

**Single owner throughout, and no graph.** There are no friends, no handles, no following,
no sharing, and no way to browse another user's library. Two accounts meet in a game and
nowhere else. Everything designed so far already assumed this — an army holds its own copy
of every value, an asset is deduped per user, and sharing was always described as copying.

A relationship graph is a later addition. Nothing here is shaped to prevent one, and
nothing here needs one.

**Folders are the asset library's, and only the asset library's.** Five hundred meshes is
the problem folders solve, and nobody has five hundred armies. Armies are a flat list you
open to see details; layouts likewise.

There is no entity grouping owned things together. `architecture.md` used the word
*collection* for one — as a module name and in one stray sentence about collections being
private — and it never meant anything. It is dropped.

## Deletion is total

Deleting an account destroys its armies, assets, blobs, folders and layouts, and deletes
**every game it participated in** — the game record and its event log — and then the
account row.

No tombstone, no scrubbed name, no deactivated account, no extra game status. Nothing
survives that would have to render a user who no longer exists.

The collateral is real and accepted: a game other people played in disappears because one
participant left.

Because nothing outlives a user, **`Participant` is an ordinary reference to `Account`**
rather than a copy of a name. This is the one place the project does not hold its own copy,
and it holds only because the cascade removes the thing a copy would have protected
against.

## A game is a lobby

One record with a status, as `architecture.md` already had it. A room is a game that has
not started.

**Games are public.** The browser lists open games and anyone logged in may join one.
Passwords on games and friend-joining are later, and they change who is allowed in rather
than what a game is.

**Creating and joining are plain endpoints.** A user hits management, which creates the
game and returns a **join token**. The client presents that token to the game server over
its WebSocket. If no session for that game is in memory, the game server hydrates it and
starts serving.

No M2M call is involved and management never learns which task holds which game. This
settles the question `architecture.md` left open about who instantiates a session, and it
settles it by having nobody do it: the session comes into being because a client arrived.

It also creates a contract that did not exist before. `architecture.md` argued auth needed no
agreed format because Stytch was the authority for both servers; confining Stytch to management
means management issues the tokens, so **the join token is a seam between the two servers** —
the one thing they share without ever calling each other.

## Ending is a flag, not a state

Nothing in the rules ends a game — there are no victory conditions, because players count
their own points.

A game ends when someone ends it explicitly, or when the last participant leaves. Leaving
is deliberate. **Disconnecting is not leaving**: a disconnected player is still in the
game, and reconnecting rehydrates from the log.

**An ended game is just one marked ended.** Because state is replayed from the event log,
restarting an ended game is the identical operation to recovering a crashed task: hydrate
from the log and serve. So there is no terminal state, no separate recovery mechanism, and
no reaper for abandoned games. Any participant can pick an ended game back up.

Ended games persist with their logs until deleted. That is also what makes a replay
viewer possible later, and it is what account deletion has to reach into.

## Deliberately not decided

- The web app: whether it exists, and what it does if it does. Browsing and joining games
  live in the game client.
- What an account looks like to another account, beyond a name.
- Per-account defaults.
- The game record's own fields beyond status and participants — table, mission, rule pack
  and setup configuration belong to that module.
- Password-protected games and friend-joining.

## Still open

- **What is in a join token and how long it lives.** It is now a contract between the two
  servers, so it has to be agreed before either is built against the other.
- **Whether a game already playing can be joined**, or only one still in its lobby. The
  browser lists open games; nothing yet says what open means.
