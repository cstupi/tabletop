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

Pending.
