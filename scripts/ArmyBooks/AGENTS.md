# scripts/ArmyBooks

`Tabletop.ArmyBooks`: the console generator for the official Grimdark Future army books. Repo-level support, in `Tabletop.sln`, neither shipped nor referenced by a deliverable.
Writes `server/game/Content/<folder>/army-books/<uid>.book.json` and `server/game/Content/<folder>/army-book-rules.part.json` (`<folder>` is `grimdark-future-3.5.1` or `grimdark-future-3.6`), which the game server composes into its properties set.

## Layout

- `Program.cs`: the `fetch` and `generate` commands.
- `ArmyForgeBook.cs`: `AfIndexEntry`, `AfBook` (spells, rule texts, units, upgrade packages), `AfSpell` and `ArmyForgeJson` (reads Army Forge's JSON).
- `RuleTemplates.cs`: the Aura, Buff and Mark template texts and the wrapper entry each becomes.
- `RuleWriter.cs`: names in scope, a wrapper or a text-only entry per name core and the hand part leave undefined, the report.
- `SpellTranslator.cs`: Army Forge's `generation` to a spell entry.
- `TextCheck.cs`: every fact the translation took from `generation` must appear in the spell's printed `effect`.
- `BookWriter.cs`: makes spell names unique across the corpus, checks against the core document and the army-rules part, writes the books.

## How it works

- Dumps live at `.superpowers/armyforge/<edition>/` (git-ignored), each with `index.json` and `books/`. `fetch` downloads the official book index and each book from Army Forge into the folder of the edition the index serves (`ArmyForgeJson.EditionOf`: the major.minor of the entries' `versionString`) and refuses a folder that holds another edition. The 3.5 dump cannot be fetched again; its backup is `/home/cstupi/backups/armyforge-gf-3.5-2026-10-29/`.
- `GeneratedOutputTests` pins `generate --edition 3.5` to the committed 3.5.1 content; it skips without the dump.
- `generate --edition E` (3.5|3.6, default 3.5) reads `.superpowers/armyforge/E/` (`index.json`, `books/*.json`) and that edition's `core.properties.json` and `army-rules.part.json`, translates, checks, and writes that edition's `army-books/` and `army-book-rules.part.json`, both named for its rules id.
- It fails and writes nothing on an unknown generation shape, a text mismatch, a 3.5 spell naming a rule in neither the core document nor the part, an unresolvable name collision, or a composed document `PropertiesJson` refuses.
- A spell's `inLoS` parameter ("in line of sight") writes `lineOfSight: true` on its targeting (3.6.0: the core Caster rule no longer asks sight of a spell); a 3.5 dump carries none.
- The printed spell text is the authority on range: when `inRange` and the "within N\"" of the printed text disagree, the text's range is written and the run prints `range from text: <book> <spell> a → b` (the 3.6 dump's `inRange` is the 3.5 value, 96 spells).
- A spell is written text-only (`effects: []`, `played: false`; `GfCasting` does not offer it) for effect id `Urj6dU6Tua8B` ("loses N spell tokens", no grammar kind) and for an empty or unknown `attackType`, and, under 3.6 only, when it grants a rule neither the core nor the hand part defines (`text-only spells (undefined grant: <rule>)` in the output). Defining those rules in the 3.6 hand part and regenerating makes such spells live.
- `server/management/Content/Armies/army-book-units.json` is one file for both editions: a 3.6 run overwrites it with 3.6 units. Revert it (`git checkout`) until Task 7.3 splits it per edition.
- `generate` also writes the army-book rules part: every rule name on a unit, a weapon, an item or an upgrade that core and `army-rules.part.json` do not define becomes a template wrapper (an Aura, Buff or Mark whose text is its template, X read ignoring case, and whose base the catalog holds) or a `played: false` text-only entry (`kind: weapon` when only weapons carry it). Two books describing one name differently fail the run. It prints the count per group and the names with no description.
- `generate` also writes `server/management/Content/Armies/army-book-units.json` (`ReserveUnits`, `--units FILE`): every unit a book's Spawn or Split rating (*"Bot Swarms [3]"*) names, in the TTS export's shape, keyed by book uid and unit name, for the import's reserve units. A rating naming no unit of its book fails the run.
- Output is deterministic: an unchanged dump writes identical bytes.
- References `server/game` for `PropertiesJson` and `PropertySetDocument`.

## Commands

```bash
./scripts/army-books fetch [--to DIR]
./scripts/army-books generate [--edition 3.5|3.6] [--from DIR] [--out DIR] [--rules FILE]
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
