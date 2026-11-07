# No page citations in the UI

Owner ruling 2026-11-07: "we dont need page citations in the ui." No player-visible string carries a rulebook page
citation (`(p.7)`, `(p.11, p.7)`, `(3.6.0 p.14)`, `Label (Page)`). Code comments, doc comments, docs and the sim's
developer-only suite names keep theirs.

## Inventory

| Layer | File | Strings |
| --- | --- | --- |
| Godot client | `client/src/Core/ContactCheck.cs` `Sentence` | 4 (base overlap, through a model, impassable, within 1") |
| Godot client | `client/src/Core/WorkedLines.cs` | 1 ("a 6 always succeeds, a 1 always fails") |
| Godot client | `client/src/Panels/LobbyPanel.cs` | 1 (option label `Label (Page)`) |
| Viewer | `viewer/src/lib/play/contactCheck.ts`, `workedLines.ts` | 4 + 1 (ports of the above) |
| Viewer | `viewer/src/lib/lobby/LobbyPage.svelte` | 1 (option label's page span) |
| Web app | none found | 0 |
| Game server | `GfMoves.Breaches.cs` (8), `GfMoves.Contact.cs` (3), `GfMoves.Coherency.cs` (1), `GfMoves.Charge.cs` (1), `GfRules.Deployment.cs` (4, two through `ScoutPage`), `GfRules.Moves.cs` (1), `GfRules.MissionLength.cs` (1), `GfRules.Scheduling.cs` (5 notes and the activation refusal), `GfEdition.cs` (`ArtilleryNote`, `CumbersomeNote`), `GfOptions.cs` (3 option pages), `Properties/PropertySetDocument.cs` (1 validation problem) | refusal/breach details, Ask notes, option pages, property-set validation |

## Wire decision

`RulesOption.Page` is read only to print it beside the label (Godot lobby, viewer lobby). It is dropped from the
wire: `RulesOption(Key, Label, Values, Default)`, `wire.generated.ts` regenerated, `GfEdition.HybridSightPage` and
`GfEdition.ScoutPage` deleted. JSON stays tolerant both ways (unmapped members are skipped, a missing constructor
parameter reads as null), so `ProtocolVersion` does not move. The optional-rules spec's wire record and lobby label
follow.

## Steps

1. Tests first, per layer: a guard asserting no output matches `/\bp\.\d/`.
2. Strip each citation; the sentence keeps its own punctuation (`… (p.11); X moved` → `…; X moved`).
3. Drop `RulesOption.Page`; regenerate `wire.generated.ts` (`TABLETOP_WRITE_GENERATED=1 dotnet test tests/Tabletop.Viewer.Tests`)
   and the viewer fixtures that carry a welcome (`TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests`,
   then `--filter CallsViewerFixture` and `--filter CombatLogViewerFixture` in `Tabletop.Client.Tests`).
4. Update test expectations and suite expectations that match on the old text.
5. Specs: the UX redesign mockups drop `(p.10)`; the client UX spec states UI text carries no page citations.

## Validation

- Core: `NoPageCitationsTests` (Client.Tests) runs `ContactCheck.Sentence` over every word it maps and `WorkedLines`
  over a recorded log; none matches `\bp\.\d`.
- Server: `NoPageCitationsTests` (GameServer.Tests) checks every `GfRules.Scheduling` note, both editions'
  `ArtilleryNote`/`CumbersomeNote`, `MissionLengthDetail`, and the refusal details the move/deploy tests produce;
  existing refusal tests assert the uncited text.
- Viewer: `noPageCitations.test.ts` runs `contactSentence` and `workedLines` over the same inputs.
- Suites: `dotnet build`, `dotnet test`, `./scripts/check-client`, `cd viewer && npm run check && npm test`,
  `cd web && npm test && npm run check`, `dotnet run --project sim -- suite`.

### Results

Guard tests: `tests/Tabletop.Client.Tests/NoPageCitationsTests.cs` (contact sentences, a worked roll, every string literal under
`client/src`), `tests/Tabletop.GameServer.Tests/NoPageCitationsTests.cs` (notes, mission-length detail, both editions' option
catalogs, every string literal under `server/game`, `server/management`, `libs`), `viewer/tests/noPageCitations.test.ts` and
`web/tests/noPageCitations.test.ts` (contact sentences; every `src` line outside comments).

The viewer's call logs (`viewer/tests/fixtures/calls/*.json`) had their `page` fields and cited notes stripped in place:
regenerating them (`--filter CallLogFixture`) drops every `confirm` case, which `Every_required_case_kind_is_present` then
fails on, a drift that predates this change. `calls.json` and `combatlog.json` were regenerated from them.

- `dotnet build`: succeeded.
- `dotnet test`: all green. Client 1036, GameServer 1865, Management 348, Sim 345, Geometry 303, Protocol 66, Content 71,
  TerrainMeshes 114, Sounds 89, ArmyBooks 60, Geometry.Wasm 44, TableTextures 29, Viewer 16.
- `./scripts/check-client`: exit 0.
- `cd viewer && npm run check && npm test`: 0 errors; 1805 passed, 6 skipped (119 files).
- `cd web && npm test && npm run check`: 142 passed, 1 skipped; 0 errors.
- `dotnet run --project sim -- suite`: `PASSED: 685 of 685 games passed, 0 failing on known defects, 0 unexpected; 1122 of 1122
  entries covered, 0 not automated`.
