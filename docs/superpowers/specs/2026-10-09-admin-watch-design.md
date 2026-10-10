# Admin watch: the viewer reads any game

Owner decisions (2026-10-09): the viewer is an admin tool. An admin watches any game, finished or running, without joining
it. Admin is a Stytch role. The watcher connects on the game socket.

## The role

- **Where it lives.** Stytch consumer RBAC: a role `admin` in the project's RBAC policy, assigned to the user in Stytch. The
  repository holds no role table and no admin list.
- **How it reaches the servers.** `Sessions.Complete` (`server/management/Identity/Sessions.cs`) already stamps `account_id`
  on each new session as a session custom claim (`StytchClient`'s refresh with custom claims). It stamps `roles` beside it:
  the Stytch user's role ids from the authenticate response's user object. Every JWT the session mints carries both. A role
  change takes effect at the next login; a refresh keeps the claims it has.
- **Reading it.** Game server: `VerifiedSession` gains `IReadOnlySet<string> Roles`, read from the same claim by `StytchJwtVerifier`
  (`server/game/Auth/SessionVerification.cs`). A token without the claim has no roles.
- **Dev sessions.** `POST /v1/dev/sessions` (Development only) takes an optional `roles` so tests and `.superpowers/e2e/`
  mint an admin without Stytch.
- **Owner setup, outside the repository.** In the Stytch dashboard: add the `admin` role to the RBAC policy and assign it to
  the owner's user. Recorded in `docs/handoff.md` → Environment, beside the reset-password redirect.

## The watcher

- **Connect.** `ConnectionHandshake` (`server/game/Session/ConnectionHandshake.cs`) resolves the account to a participant of
  the game. An account that is not a participant and whose session has the `admin` role joins as a watcher; anyone else
  is refused as today.
- **What a watcher gets.** The same frames a participant with no team gets: welcome, board, the event log from the start,
  then live events, presence of others, game info. It has no `ParticipantId` in the game and appears in no participant list,
  lobby or presence.
- **What a watcher may send.** Nothing that changes the game: a command, an answer, an edit, presence, a ping or an undo is
  refused (`Refused` with reason `watcher`). A sight query is answered, since it changes nothing.
- **Game session.** `GameSession` holds watchers apart from participants, so code that reads `client.ParticipantId`
  (lobby, presence, asks) never meets a watcher.

## Listing games

`GET /v1/games` is the public game list players browse and join from (the client's Home screen), so it already lists every
game to every account, an admin included. Each row carries id, name, status, created time, the participant count and the
participants' names (`ParticipantNames`). Management has no admin-only endpoint.

## The viewer

- **Login.** One login (email and password, or a pasted JWT), kept for the page's session.
- **Game list.** After login, the source picker shows the games from `GET /v1/games`: name, status, players, created.
  Clicking one opens the live feed on it: the log replays from the start on the timeline, and a running game follows live.
- **Removed.** The pasted game id and the "must already be a participant with no team" text.
- **Unchanged.** Recording files and URLs, and the Tests page's Watch and Open live.

## Checks

- Management tests: the `roles` claim stamped from the user's roles and kept across a refresh; a dev session minted with `roles`.
- Game server tests: an admin non-participant connects and receives welcome, board and the full log; a non-admin
  non-participant is refused; a watcher's command, answer, presence and ping are refused; a watcher is absent from game info.
- `.superpowers/e2e/`: a scenario that plays a few moves as two players, then connects an admin dev session and receives
  the whole log.
- Viewer: Vitest for the game list model; the live feed's existing tests.

## Documents this changes

`docs/design/viewer/design.md`, `viewer/AGENTS.md`, `docs/design/game-server/design.md` (connect and watchers),
`docs/design/management/design.md` (roles claim, games list), `docs/handoff.md` (Environment: the Stytch role).
