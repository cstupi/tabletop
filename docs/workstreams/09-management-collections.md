> **Superseded 2026-09-06 — do not execute.** Written 2026-08-24, before the first design
> session, and describing a data model that no longer exists. Replaced by
> `docs/superpowers/plans/2026-09-05-management-api.md` and `2026-09-05-web-app.md`. See
> [README.md](README.md) for the full fate of this file. Kept only as a record of how the
> project was first scoped.

# 09 — Management: collections (army management)

**Owns:** `server/management/`, `web/`
**Depends on:** 03 (identity), 01 (`Tabletop.Content` types)
**Branch:** `ws/09-management-collections`

Read `docs/workstreams/README.md`, `docs/architecture.md`, and `CLAUDE.md` first.

## Goal

Users import One Page Rules Army Forge lists, keep them, and bind their own art to them.

This is phase 2 of the management service. Phase 1 (identity) must exist first — you are adding
to a running service with real accounts, not starting one.

## Deliverable

- Army Forge list import, producing armies stored against an account
- Army list, view, rename, delete
- Art profiles: a user's binding of their own assets to units in a list
- Svelte UI for all of the above

## The three decisions that shape this

**The imported format is authoritative.** Army Forge is the source, and its internal format is
treated as the canonical shape. Do not invent a normalised schema of your own to translate into
— that guarantees drift and a lossy round trip. A native list builder is a later feature that
must produce the same format, so design as though it already exists.

**Art binding is user-owned and separate from army data.** The chain is assets → art profiles →
army loadouts. The same list looks different for different users, and **bindings must survive a
re-import.** If a user re-imports an updated list, their art stays attached. This is the
requirement most likely to be got wrong; design the binding key before writing the import.

**Armies are snapshotted into a game.** Editing a saved list must not mutate a live match. The
snapshot format lives in `Tabletop.Content` because the game server and client both read it —
you may consume those types but not change them. If they are wrong, stop and report.

## Stat blocks are named stats

Do not model `Quality`, `Defense`, or `Tough` as fields. OPR uses those; Warhammer uses
M/T/Sv/W/Ld/OC with weapons carrying BS/A/S/AP/D; a third game differs again. A fixed stat type
hardcodes OPR into the product.

Stats are **named and defined by the rule pack**. Storage is a generic named-stat store. The
ergonomics cost is paid back later by ruleset-specific typed accessors over the generic store.

## Out of scope — do not build

Asset upload, S3, blob storage, mesh processing — that is workstream 10, and this workstream
binds to assets that already exist. Lobby, rooms, games. Rules, or any interpretation of what a
stat *means*. The game server. Collision volume authoring for terrain — that lives in the
in-game editor, not the web app.

Base sizes are a preset dropdown (25/32/40mm round, oval sizes), which covers effectively every
model. That much belongs here; nothing more.

## Done when

1. A real Army Forge list imports and is stored against the logged-in account.
2. Art can be bound to units in that list.
3. Re-importing an updated version of the same list preserves the art bindings — proven by a
   test, not by clicking.
4. An army can be serialized to the `Tabletop.Content` snapshot shape.
5. `dotnet build` from the repo root succeeds; `dotnet test` passes.

Paste the commands and their output.

## Decisions

Record here, one line of reasoning each.
