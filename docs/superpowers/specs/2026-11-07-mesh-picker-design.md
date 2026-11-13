# Mesh picker

The army page's mesh picker (`/armies/[id]`, a slot row's mesh) for a library of hundreds of miniatures. It replaces the `<select>`
of `AssetPicker` and the page's *Mesh folder* choice (UX redesign part 3 → Army page).

## Owner rulings

- 2026-11-07: "When picking models on the website we need much better search / organization of picking what mesh goes with what
  model. right now you could scroll through hundreds to find what you need."
- Design philosophy: visuals over words; actions live on the thing they act on; lots of spacing, icons and colouring; easy to change.

## The slot row

The row's thumbnail and mesh name are one button; it opens the picker for that row. Base and height stay as they are. A row is labelled
with the slot's name as the import numbers it (`Orc Boyz 3`).

```
┌▌ Orc Boyz ×10 ─────────────────────────────────────────────── 🎯 4+  🛡 5+ ┐
│ Boss 1        [ [img] Orc Boss 02       ▾ ]   ◯ 32 mm round   ↕ 1.90 in   │   one button: thumbnail, mesh name
│ Orc Boyz 2    [ [ ▣ ] No mesh            ▾ ]   ◯ 25 mm round   ↕ 1.50 in   │   no mesh: the mesh icon, dim
└───────────────────────────────────────────────────────────────────────────┘
```

## The picker

A modal `<dialog>`. Desktop (≥ 60rem):

```
┌ ▣ Mesh for Boss · Orc Boyz ──────────────────────────────────────────────────────── [✕] ┐
│ [🔍 orc boss▏                                                                       ]  │  focused on open
│ (◯ Fits 32 mm round · 41) (🛡 In this army · 6) (⏱ Recent · 12)        3 of 842       │  chips toggle; count of matches
├───────────────────┬───────────────────────────────────────────────────────────────────┤
│ ▤ All        842  │ SUGGESTED                                                          │  only with an empty search
│ ⇄ Shared      12  │ ┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐                    │
│ ▸ Orcs       120  │ │ img  ││ img  ││ img  ││ img  ││ img  ││ img  │                    │
│   ▸ Boyz      40  │ │Orc   ││Orc   ││Boss  ││...   ││      ││      │                    │
│   ▸ Bosses    12  │ │Boss 2││Boy 1 ││Nob   ││      ││      ││      │                    │
│ ▸ Elves       90  │ │⊞unit ││⇄Orc B││Aname ││      ││      ││      │                    │  why: this unit / other army / name
│ ▸ Starter      6  │ └──────┘└──────┘└──────┘└──────┘└──────┘└──────┘                    │
│                   │ ORCS › BOYZ · 40                                                    │  the folder in view as a breadcrumb
│                   │ ┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐                    │
│                   │ │ img ◯││ img  ││ img  ││ ...  ││      ││      │   (virtual rows)   │  ◯ on a tile: fits this base
│                   │ └──────┘└──────┘└──────┘└──────┘└──────┘└──────┘                    │
│                   │ 12 more in other folders  [Search all]                              │  when a folder narrows a search
├───────────────────┴───────────────────────────────────────────────────────────────────┤
│ Applies to (● Boss · 1)(○ every Boss in Orc Boyz · 1)(○ every Boss in the army · 3)      │  one choice, default set
│ 3 models in Orc Boyz, Orc Mob · replaces 2 meshes               [⊘ No mesh]  ⏎ pick  esc │  what will change
└───────────────────────────────────────────────────────────────────────────────────────┘
```

390 px: the tree becomes a row of folder chips under the chips (All, Shared, the top-level folders, then the open folder's
sub-folders after its breadcrumb, scrolling sideways); the grid has three columns; the scope choice wraps.

```
┌ ▣ Mesh for Boss ─────── [✕] ┐
│ [🔍 search             ]    │
│ (◯ Fits)(🛡 Army)(⏱ Recent) │
│ (▤ All)(⇄ Shared)(Orcs)(…)  │
│ SUGGESTED                   │
│ [img][img][img]             │
│ ALL · 842                   │
│ [img][img][img]             │
│ [img][img][img]             │
│ Applies to                  │
│ (● Boss 1)(○ Orc Boyz 1)    │
│ (○ the army 3)              │
│ [⊘ No mesh]                 │
└─────────────────────────────┘
```

### Search

- Matches the asset's name and the names of every folder it is filed in, and those folders' parents. Words are split on spaces,
  punctuation, `_`, `-`, camelCase and letter–digit boundaries, lower-cased; a trailing `s` or `z` is dropped on both sides (`Boyz`,
  `boy`, `orcs` match). Numbers match by value (`2` finds `02`) or as a typed prefix (`0` finds `02`).
- Every query word must match some word of the asset: exact, then word prefix, then inside a word, then one edit (words of 4 or
  more letters; two edits from 8; swapping two neighbouring letters is one edit). Name words score above folder words. Results sort by score, then *fits this base*, then recent,
  then name.
- The search covers the folder in view and its sub-folders. With a folder open and a search typed, a line counts the matches in the
  rest of the library and **Search all** switches to All.

### Filters

Chips, each with its count over the folder in view and the search, any combination:

- **Fits** the row's base: the asset's modelled base is the slot's base (`sameBase`). Off by default; fitting assets rank first
  anyway and carry the `base` mark.
- **In this army**: the meshes on any slot of this army.
- **Recent**: the account's last 30 picks in this browser, newest first (the chip orders by recency).
- A chip that is on draws the `ready` mark in place of its icon.

### Suggested

One row of the grid's width, only with an empty search and no chip on, from the whole library whatever folder is open. Scored per
asset, highest first, the row's own mesh left out; the sources add up, so an asset named for the model can outrank another model's
mesh in the unit:

| Source | Mark | Score |
| --- | --- | --- |
| On another slot of this unit | `armies` icon, `this unit` | 8, +4 with the same model name |
| On a slot of a unit of the same name in any of the account's armies (`GET /v1/armies/meshes`) | `copy` icon, the unit name | 5 + slot count (max 5); 3 when only the model name matches |
| Name words matching the model name, then the unit name | `mesh` icon, `name` | per word: 3 exact, 2 prefix, 1 inside, 0.5 edit; model words × 2 |
| Recent pick | — | +1 |

### Apply to

The row's slots S and their model names N. A model name is the slot's name without the number the import gives each slot
(`Orc Boyz 3` is `Orc Boyz`). Scopes, the narrowest first; a scope with the same slots as a narrower one is not shown; the chosen
one draws the `ready` mark:

- **this model** (or **every model** on a unit's shared row): S.
- **every ‹name› in ‹unit›**: the unit's slots named in N.
- **every ‹name› in the army**: the army's slots named in N.

Default: S, for every row: a pick sets the row it was opened from (one model, or the shared row's every model) and nothing else; a wider scope is chosen. The line under the choice names the
model count, the units touched and how many of them now hold a mesh other than the highlighted tile (`replaces 2 meshes`). A pick is one click (or Enter) with
the scope chosen; the patch is one `PATCH /v1/armies/{id}` with a `SlotPatch` per slot (`clearAsset` for **No mesh**).

### Keyboard

Typing goes to the search box, which keeps focus. ↑ ↓ ← → move the highlighted tile through the suggested row and the grid,
PageUp/PageDown move a screen, Home/End the ends, Enter picks, Esc closes. Tab reaches the chips, the tree and the scope choice.

### Remembered

`localStorage`, per account: `tabletop.meshRecent.<accountId>` (asset ids, 30, newest first) and `tabletop.meshFolder.<accountId>` (the
folder the picker opens at; All when missing or deleted).

### Scale

The page loads every miniature once (`GET /v1/assets?kind=miniature`, keyset pages of 200), as today; search, filters and suggestions
run in the browser over an index built once per library load. The grid draws only the rows in view (fixed row height) and thumbnails
load lazily. Target: a search over 1000 assets under 5 ms, a keystroke to an updated grid under 50 ms.

## Server

`GET /v1/armies/meshes` (owner) → `ArmyMeshUse[]` `{ unitName, modelName, assetId, slots }`: per unit name, model name (the slot name
without its trailing number) and mesh, the number of the account's slots holding it, across all its armies; most slots first. One query (`IArmyRepository.MeshUses`); the in-memory fake and the
Postgres repository share a contract test.

## Where it lives

- `web/src/lib/meshpicker/`: `search.ts` (`words`, `stem`, `wordScore`, `buildIndex`, `search`, `fits`), `suggest.ts` (`suggest`), `scope.ts`
  (`modelName`, `scopes`, `defaultScope`, `scopePatch`, `scopeChange`, `changeLine`), `nav.ts` (`gridMove`), `memory.ts` (`loadRecent`,
  `pushRecent`, `loadLastFolder`, `saveLastFolder`).
- `web/src/lib/components/MeshPicker.svelte` (the dialog; icons `search`, `recent`, `suggest`).
- `web/src/routes/armies/[id]/+page.svelte`: the row's mesh button opens the one picker; the page builds the index once per library
  load and fetches `/v1/armies/meshes` on load and after each pick. The page's *Mesh folder* select, `AssetPicker` and
  `lib/armies/folder.ts` are gone.
- Management: `ArmyEndpoints` (`/meshes`), `IArmyRepository.MeshUses`, `ArmyRepository`, `InMemoryArmyRepository`.
