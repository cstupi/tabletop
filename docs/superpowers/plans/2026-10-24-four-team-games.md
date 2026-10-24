# Four-team games Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Four teams of two players, each with its own army from at least four army books, play from creation to deletion
against the stack (`four-teams` lifecycle shape), and the game server's unit tests and the rules suite play the same table.

**Architecture:** Four sides is p.50's free-for-all (*"3-4 players"*) with each side a team, so p.50's Team Games rules hold
within a side: *"players from the same team must deploy their units in the same deployment zone, placing a single unit when
it's their team's turn to deploy"* and *"for each team's turn only one of the players may activate a single unit"*. The
free-for-all clauses order the sides: *"All players roll-off to determine the order in which they will deploy. Then the players
alternate in picking a table corner and deploying one unit each within a 2'x1' area, starting with the player that got the
highest score, and then alternating in descending order"*; *"During the first round, turn order goes from the player that
deployed first to the one that deployed last. Starting from the second round, turn order goes from the player that finished
activating first to the one that finished last."* The mission is p.6's, shared per team (p.50 The Mission).

**Tech Stack:** .NET 10, xUnit, the sim's lifecycle suite and rules suite.

**Spec:** `docs/superpowers/specs/2026-10-24-four-team-games-design.md`

## Global Constraints

- The lifecycle suite goes through `/v1` and the socket only; armies come from `POST /v1/dev/armies/import`.
- Army lists are Army Forge export bodies; the three new ones are cut from the official books (Human Defense Force, Battle
  Brothers, Orc Marauders), three units each, beside the Wormhole Daemons of War fixture.
- Every defect is fixed on the game server with a failing test first.
- `docs/handoff.md` is not touched.

## Task 1: Army lists from four books

**Files:** `sim/Lifecycle/Armies/{z65fgu0l29i4lnlu,78qp9l5alslt6yj8,1wj1ysgxpuuz9bc7}.json`, `sim/Lifecycle/LifecycleArmy.cs`,
`sim/Tabletop.Sim.csproj`, `tests/Tabletop.Sim.Tests/LifecycleArmyTests.cs`.

- [ ] Test: `LifecycleArmy.Books` holds four book uids, `LifecycleArmy.List(book)` for each has three units, every unit's
  `armyId` is that book, and the lists' books are distinct.
- [ ] Embed the three lists (`lifecycle-army-<uid>.json`); `List(int book = 0, bool ambush = false)`, book 0 the fixture.
- [ ] `dotnet test tests/Tabletop.Sim.Tests --filter LifecycleArmy`.

## Task 2: The `four-teams` shape

**Files:** `sim/Lifecycle/LifecycleShape.cs` (`Seat.Book`), `LifecycleRunner.cs` (`Players`: eight), `LifecycleGame.Lobby.cs`
(the seat's book), `LifecycleGame.Play.cs` (the four-team assertions), `tests/Tabletop.Sim.Tests/LifecycleShapeTests.cs`.

- [ ] Test: `four-teams` seats accounts 0..7, two a team on teams 1..4, the creator placing the second of each, books 0..3
  each on two seats, strict.
- [ ] Assertions after play, from the recorder's log (`LogQueries`): the roll-off's `round_started` (round 1) orders all four
  teams; every deploy placement is fully within its team's zone; deployment and round one go round the teams in the roll-off
  order, one unit a turn; every round's activations go round all four teams; `game_ended` names at least one winner when any
  marker is held, and the winners are the teams holding the most markers.
- [ ] `dotnet run --project sim -- lifecycle --only four-teams`, then every shape.

## Task 3: Game-server unit tests

**Files:** `tests/Tabletop.GameServer.Tests/GfFourTeamTests.cs`.

- [ ] Roll-off: four teams' dice rank four places; ties among some roll again among themselves (p.50 *"in descending order"*).
- [ ] Deployment over four teams of two armies: one unit per team turn in the roll-off order, either army's unit (p.50).
- [ ] Round one in deployment order; round two by who finished activating first (p.50 Turn Order).
- [ ] Objectives: a marker contested by two of the four teams goes neutral; the winner is the team with most markers, a tie
  between several of the four teams names each (p.6, p.50 The Mission).
- [ ] Morale: a unit of team 3 fails a test the same as on a two-team table (p.10).

## Task 4: A four-team suite game

**Files:** `sim/Suite/SuiteCatalog.Core.FourTeams.cs`, `sim/Suite/SuiteCatalog.cs`, `sim/Drivers/IWebSocketConnector.cs`
(`PoolSize` follows the largest game: eight armies and the recorder).

- [ ] `core-four-teams`: eight armies, two a team on four teams, `ZoneStyles.FreeForAll`, strict, to the end. Checks: p.50
  deployment rotates over the four teams, each unit fully within its team's zone; activations rotate over the four teams.
- [ ] `dotnet run --project sim -- suite --only core-four-teams`, and `--driver socket`.

## Task 5: Defects

Each found defect: a failing test in `tests/Tabletop.GameServer.Tests`, the root cause fixed on the game server, the shape and
the suite run again. Known before running: the default setup draft (`GameSession.NewDraft`) holds Frontline's two zones
whatever the team count, so teams 3 and 4 have no deployment zone and deploy anywhere. A game of three or four teams starts
setup on p.50's free-for-all corners (`ZoneStyles.FreeForAll`).

## Task 6: Validation and docs

- [ ] `dotnet build && dotnet test`; `dotnet test --settings tests/e2e.runsettings --filter Lifecycle`; `dotnet run --project sim -- suite`.
- [ ] `sim/AGENTS.md` (shapes, accounts, armies) and the lifecycle spec's shape table.
