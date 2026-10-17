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

**The flow runs through management.** Whatever is logging in posts to our endpoints;
management calls Stytch; a **Stytch session JWT carrying our account UUID** comes back. Nothing
outside management holds a Stytch secret, and no Stytch identifier is stored in our schema.

*Corrected 2026-09-06.* Stytch's session JWT puts its own `user_id` in `sub` and does not carry
`external_id`. The account UUID arrives as the top-level custom claim `account_id`, which management sets on each new session with `session_custom_claims`; Stytch keeps it on the session across refreshes. Stytch
reserves `sub` and the `https://stytch.com/*` names, and custom claims are merged at the top
level. The game server reads `account_id` and nothing else.

*Revised 2026-08-30.* This session had management issue an opaque token of its own, stored in a
`session` table. That table is gone: both services verify the JWT locally against Stytch's
public JWKS, so nothing needs to be looked up and nothing calls Stytch on the request path.

~~All of Stytch's methods are supported — email OTP, magic link, password, OAuth.~~ Two are:
email OTP and password, both Basic-auth endpoints that run in management. Magic links and OAuth
are not offered (ruled 09-04, [the client shell](2026-09-04-client-shell-design.md)).

*Revised 2026-08-29 by [the web app](2026-08-29-web-app-design.md): **login lives in the
browser**, and the client acquires a session from it. This section originally put login in the
client on the reasoning that no browser existed to host it.*

*Revised again 2026-09-04 by [the client shell](2026-09-04-client-shell-design.md): no social
login and no magic links, so the two remaining methods — password and email one-time code —
run through management and **the client has its own login screen**. This section's original
routing stands after all; only the redirect-based methods are gone.*

**Replacing Stytch is a change inside one module.** That is the constraint the routing
exists to satisfy, and it is why the vendor is not allowed to appear in the client, in the
schema, or in any other module. ~~The one place confinement is nominal rather than literal
is an OAuth consent page.~~ With no OAuth, confinement is literal.

~~`architecture.md`'s original position — the browser owns login, the client acquires a token
from that session — stands after all.~~ Superseded 09-04: the client has its own login screen
and acquires nothing from the browser.

**Playing requires an account.** There are no guests, and there is no offline mode. An owned
army is needed to be on a team that deploys, not to join — a participant with no team watches
([the setup phase](2026-09-04-setup-phase-design.md), 09-04).

## What an account owns

Armies, assets and their blobs, folders, layouts, and the games it takes part in.

**Single owner throughout, and no graph.** There are no friends, no handles, no following,
and no way to browse or search another user's library. Two accounts meet in a game and nowhere
else.

**Private for discovery, not for access.** The participants in a game download the assets that
game uses and render them locally. That is a download, not a share: no record is created on
either side, nothing enters anybody's library, and per-user dedup is untouched.

A relationship graph is a later addition. Nothing here is shaped to prevent one, and
nothing here needs one.

**Folders are the asset library's, and only the asset library's.** Five hundred meshes is
the problem folders solve, and nobody has five hundred armies. Armies are a flat list you
open to see details; layouts likewise.

There is no entity grouping owned things together. `architecture.md` used the word
*collection* for one — as a module name and in one stray sentence about collections being
private — and it never meant anything. It is dropped.

## Deletion is total

Deleting an account destroys its armies, assets, blobs, folders and layouts, deletes **every
game it hosted** (`game.created_by`) — the game record and its event log — **leaves every game
it joined** exactly as `POST /v1/games/{id}/leave` does, and then deletes the account row
(ruled by the owner 2026-10-16). Leaving removes its `participant` row; the game, its teams and
its log stay, and the last participant's leave ends the game.

No tombstone, no scrubbed name, no deactivated account, no extra game status. Nothing
survives that would have to render a user who no longer exists: a game it joined loses it as a
game loses any player who leaves, and the log names teams, not accounts.

The collateral is the games it hosted: they disappear for everyone in them.

Because nothing outlives a user, **`Participant` is an ordinary reference to `Account`**
rather than a copy of a name. This is the one place the project does not hold its own copy,
and it holds only because deletion removes every `participant` row the account has.

## A game is a lobby

One record with a status, as `architecture.md` already had it. A room is a game that has
not started.

**Games are public.** The client lists every game, whatever its status, and anyone logged in
may join one.
Passwords on games and friend-joining are later, and they change who is allowed in rather
than what a game is.

**Creating and joining are plain endpoints.** A user hits management, which creates the game
and the `participant` row that puts them in it — on no team until they pick one in the lobby
(09-04), and there is a team to pick only while the game is `lobby` or `setup` (ruled by the
owner 09-06). The client then opens its WebSocket to the game server with that JWT and the game
id. If no session for that game is in memory, the
game server hydrates it and starts serving.

No M2M call is involved and management never learns which task holds which game. This
settles the question `architecture.md` left open about who instantiates a session, and it
settles it by having nobody do it: the session comes into being because a client arrived.

**This session invented a join token for that handoff, and it was dropped on 08-30.** The
argument for it was that confining Stytch to management makes management the token issuer, so
a bespoke token became a seam between two servers that never call each other. What that missed
is that the seam needed no new token: a game has an id, a session says who you are, and
`participant` already records who is allowed in. The token restated all three and added an
expiry that could lapse mid-game.

## Ending is a flag, not a state

A game ends when someone ends it explicitly, when the last participant leaves, or, under `strict`, when its ruleset's final round has been played (the game server marks it ended; [the enforcement engine](2026-10-02-enforcement-engine-design.md)).

Leaving is deliberate. **Disconnecting is not leaving**: a disconnected player is still in the
game, and reconnecting rehydrates from the log — into the team slot on their own `participant`
row, unchanged (ruled by the owner 09-06).

**Ending and deleting are the creator's alone** (ruled by the owner 2026-09-06, overturning any
participant). `POST /v1/games/{id}/end` and `DELETE /v1/games/{id}` answer `404` to anyone else,
and the right stays with the creator after they leave the game. An account deletion destroys
every game the account hosted, so a game whose creator is gone is gone with them.

**An ended game is just one marked ended.** Because state is replayed from the event log,
restarting an ended game is the identical operation to recovering a crashed task: hydrate
from the log and serve. So there is no terminal state, no separate recovery mechanism, and
no reaper for abandoned games. Any participant can pick an ended game back up.

Ended games persist with their logs until deleted. That is also what makes a replay
viewer possible later, and it is what account deletion has to reach into.

## Deliberately not decided

- ~~The web app: whether it exists.~~ Settled by
  [the web app](2026-08-29-web-app-design.md): it exists, it owns preparation, and the client
  owns play. Browsing and joining games stay in the client.
- What an account looks like to another account, beyond a name.
- Per-account defaults.
- The game record's own fields beyond status and participants — table, mission, rule pack
  and setup configuration belong to that module.
- Password-protected games and friend-joining.

## Still open

- ~~Whether a game already playing can be joined.~~ **Settled on 08-30: yes.** The game browser
  lists **every** status — `lobby`, `setup`, `playing`, `ended` — and filtering is the user's,
  not the server's. "Open games" was the wrong phrase; nothing is hidden by status.

  A game in progress is joinable because joining is only a `participant` row plus a socket, and
  the arriving client hydrates from the log like any reconnect. An ended game is listed for the
  same reason `ended` is reversible: picking one back up is the identical operation.

  **Clients connect at `lobby`**, not at `setup`, and may rejoin at any point afterwards. There
  is one path in — hydrate from the log and serve — whether it is a first join, a rejoin, a
  reconnect or a crashed task coming back.

  *Amended 2026-09-06, ruled by the owner.* Joining a game at any status stays true. **Taking a
  team does not**: teams are created and taken only while the game is `lobby` or `setup`, and
  the team list freezes at the flip to `playing`. This section allowed joining at any status
  without distinguishing the two, which is one of the readings that made teams keep coming up. A
  player who joined before the flip **rejoins the slot recorded on their `participant` row** —
  same team, no re-pick, not a spectator. One who arrives after it has no slot and therefore no
  team, which is the whole definition of a spectator. Stated in full in
  [game flow](2026-08-28-game-flow-design.md), *Teams are frozen at the flip, and a team is a
  slot*.
