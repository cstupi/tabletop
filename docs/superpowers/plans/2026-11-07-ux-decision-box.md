# UX redesign: decision box

Spec: `docs/superpowers/specs/2026-11-07-ux-redesign-2-play-hud.md` → Decision box (index `2026-11-07-ux-redesign-design.md`). One card per
Ask of mine, a quiet band for an Ask that is not. Nothing new on the wire; `calls.json` unchanged.

## Tasks

1. **Core model** `client/src/Core/DecisionCard.cs` (test-first, `tests/Tabletop.Client.Tests/DecisionCardTests.cs`):
   - `Head(ask, p, n)`: the Ask's icon key, its title (the prompt when short, else the question's words with the prompt as a body line),
     and the subject (the acting unit for a move and for targets; a move's title is the activation's action word).
   - `Groups(ask, shown, numbered, p, game, n, maxWounds)`: activation options grouped by unit (consecutive runs naming the same pieces and
     note), the unit's wound pips and team ordinal, a tile per option with the word after the label's colon, its icon (`hold`, `advance`,
     `rush`, `charge`, else `activation`) and its key; numbering continues across groups. Any other Options Ask is one group of tiles.
   - `Bar(readouts, allowance, minimum)`: the farthest ghost's distance over the allowance, `7.5" of 12"`.
   - `Quiet(p, game, mine)`: one band per team an open Ask waits on (team, ordinal, what it is doing).
   - `SealedReveals` queues each reveal as `RevealChip`s (team, ordinal, choice) instead of a sentence.
   - `DecisionView` drops the hint line.
2. **Theme**: `Variation.KeyCap`, `Tile`, `Card`, `Quiet`; icons `activation`, `hold`, `advance`, `rush`, `melee`, `cast`, `wound`, `key`
   per theme (SVG), `UiThemes` icon list and its test counts.
3. **Godot** `client/src/Hud/DecisionBox*.cs` (+ `DecisionTiles.cs`): the card (title band in my team's colour, icon, display-font
   title, `⌨` opening the key overlay), tiles with key caps, the note once under its group, flat secondary buttons, the allowance bar, gate
   reasons only while they hold, Commit / Cast here / Pass, target chips with the target's band, the casualty counter and pips,
   token steppers, deploy rows with pips, the quiet band.
4. **Viewer** `viewer/src/lib/play/decisionCard.ts` (port with tests), `decisionBox.ts` / `decisionBox.move.ts` publish the card,
   `DecisionBox.svelte` draws it with `.card`, `.tile`, `kbd`, `.quiet`, `.team-band`.
5. Docs: `client/AGENTS.md`, `viewer/AGENTS.md` where they describe the box.

## Validation

| Command | Pass |
| --- | --- |
| `dotnet build` | 0 errors |
| `dotnet test tests/Tabletop.Client.Tests` | all pass |
| `./scripts/check-client` | prints nothing |
| `cd viewer && npm run check && npm test` | 0 errors, all pass |
| Godot screenshots 1280×720 and 1920×1080, both themes for one | activate, move with a drag, targets, casualty pick, another team's Ask match the spec's drawings |

Before: `/tmp/claude-1000/ux-audit/18-ask-1.png` … `25-move-ask.png`, `26-move-drag.png`.

## Results

- `dotnet build`: 0 errors. `dotnet test tests/Tabletop.Client.Tests`: 1037 passed. `./scripts/check-client`: empty.
- `cd viewer && npm run check`: 0 errors, 0 warnings; `npm test`: 1808 passed, 6 skipped (the E2E suites). `calls.agreement` unchanged.
- Screenshots (two clients, `sim-player-0` Sci-fi and `sim-player-1` Fantasy, on a `skirmish --rounds 3` game over the dev stack):
  activate, move with a drag, targets, casualty pick, another team's Ask, a no-unit options Ask and the round boundary at 1280×720 in
  both themes, and activate, move with a drag, casualties and another team's Ask at 1920×1080 (Sci-fi). They match the spec's drawings:
  the title band in my team's colour with the Ask's icon and the unit, tiles grouped by unit with pips and key caps continuing across
  groups, the note once under its group, the allowance bar (`6" of 12"`), Commit lit with its ⏎ cap and Pass flat, target chips with the
  target's band, `REMOVE 1 MODEL` with `1 of 1` and pips, and `▌TEAM 2 removing casualties` as a quiet band.
- Not built: the viewer has no key overlay, so its card has no ⌨.
