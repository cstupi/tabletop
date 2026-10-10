# scripts/ArmyBooks

`Tabletop.ArmyBooks`: the console generator for the official Grimdark Future army books. Repo-level support, in `Tabletop.sln`, neither shipped nor referenced by a deliverable.
Writes `server/game/Content/army-books/<uid>.book.json` and `server/game/Content/grimdark-future-army-book-rules.part.json`, which the game server composes into its properties set.

## Layout

- `Program.cs`: the `fetch` and `generate` commands.
- `ArmyForgeBook.cs`: `AfIndexEntry`, `AfBook` (spells, rule texts, units, upgrade packages), `AfSpell` and `ArmyForgeJson` (reads Army Forge's JSON).
- `RuleTemplates.cs`: the Aura, Buff and Mark template texts and the wrapper entry each becomes.
- `RuleWriter.cs`: names in scope, a wrapper or a text-only entry per name core and the hand part leave undefined, the report.
- `SpellTranslator.cs`: Army Forge's `generation` to a spell entry.
- `TextCheck.cs`: every fact the translation took from `generation` must appear in the spell's printed `effect`.
- `BookWriter.cs`: makes spell names unique across the corpus, checks against the core document and the army-rules part, writes the books.

## How it works

- `fetch` downloads the official book index and each book from Army Forge into `.superpowers/armyforge/` (git-ignored).
- `generate` reads `index.json` and `books/*.json` from there, translates, checks, and rewrites the book files.
- It fails and writes nothing on an unknown generation shape, a text mismatch, a rule named in neither the core document nor the part, an unresolvable name collision, or a composed document `PropertiesJson` refuses.
- `generate` also writes the army-book rules part: every rule name on a unit, a weapon, an item or an upgrade that core and `grimdark-future-army-rules.part.json` do not define becomes a template wrapper (an Aura, Buff or Mark whose text is its template and whose base the catalog holds) or a `played: false` text-only entry (`kind: weapon` when only weapons carry it). Two books describing one name differently fail the run. It prints the count per group and the names with no description.
- Output is deterministic: an unchanged dump writes identical bytes.
- References `server/game` for `PropertiesJson` and `PropertySetDocument`.

## Commands

```bash
./scripts/army-books fetch [--to DIR]
./scripts/army-books generate [--from DIR] [--out DIR] [--rules FILE]
dotnet test tests/Tabletop.ArmyBooks.Tests
```

## Conventions and pitfalls

- `fetch` is the only code path that calls Army Forge. It runs by hand and never from a test.
- Tests read `tests/Tabletop.ArmyBooks.Tests/Fixtures/`, a checked-in copy of a few books.
- The dump is not checked in; the generated books are. Review a revision as the diff of the book files.
- A book file is never edited by hand: fix the generator and regenerate.
- Editing any book changes the properties hash; old games keep the hash they pinned.
- Run from inside the repo; the tool finds the root by `Tabletop.sln` and the dump by walking up for `.superpowers/armyforge`.

## Docs

- `docs/superpowers/specs/2026-10-02-spell-library-design.md`
- `docs/superpowers/plans/2026-10-02-spell-library/README.md`
