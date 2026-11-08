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

- Dumps live at `.superpowers/armyforge/<edition>/` (git-ignored), each with `index.json` and `books/`. `fetch` downloads the official book index and each book from Army Forge into the folder of the edition the index serves (`ArmyForgeJson.EditionOf`: the major.minor of the entries' `versionString`) and refuses a folder that holds another edition. It also GETs `/api/rules/common/2` (game system 2, Grimdark Future) into `common-rules.json` beside the books; `fetch --rules-only` fetches only that file and leaves `index.json` and `books/` alone. The 3.5 dump cannot be fetched again; its backup is `/home/cstupi/backups/armyforge-gf-3.5-2026-10-29/`.
- `GeneratedOutputTests` pins `generate --edition 3.5` to the committed 3.5.1 content; it skips without the dump.
- `generate --edition E` (3.5|3.6, default 3.6) reads `.superpowers/armyforge/E/` (`index.json`, `books/*.json`) and that edition's `core.properties.json` and `army-rules.part.json`, translates, checks, and writes that edition's `army-books/` and `army-book-rules.part.json`, both named for its rules id.
- It fails and writes nothing on an unknown generation shape, a text mismatch, a 3.5 spell naming a rule in neither the core document nor the part, an unresolvable name collision, or a composed document `PropertiesJson` refuses.
- A spell's `inLoS` parameter ("in line of sight") writes `lineOfSight: true` on its targeting (3.6.0: the core Caster rule no longer asks sight of a spell); a 3.5 dump carries none.
- The printed spell text is the authority on range: when `inRange` and the "within N\"" of the printed text disagree, the text's range is written and the run prints `range from text: <book> <spell> a → b` (the 3.6 dump's `inRange` is the 3.5 value, 96 spells).
- Effect `Urj6dU6Tua8B` ("loses N spell tokens") is a `remove_spell_tokens` row in either edition.
- A spell is written text-only (`effects: []`, `played: false`; `GfCasting` does not offer it) for an unknown `attackType`, and for an empty one except on a weapon-rule grant (effect `54xO9EKTj-ix`, written with no `attack_kind` condition), in either edition.
- Under 3.6 only (`BookWriter.DemoteUndefinedGrants`), a spell is also written text-only when it names a rule neither the core nor the hand part defines, or one they define text-only (`played: false`) (`text-only spells (undefined or text-only grant: <rule>)` in the output, each line `undefined grant:` or `text-only grant:`). Playing those rules in the 3.6 hand part and regenerating makes such spells live.
- `generate` also writes the army-book rules part: every rule name on a unit, a weapon, an item or an upgrade that core and `army-rules.part.json` do not define becomes a template wrapper (an Aura, Buff or Mark whose text is its template, X read ignoring case, and whose base the catalog holds) or a `played: false` text-only entry (`kind: weapon` when only weapons carry it); under 3.6 only, a template wrapper whose base core or the hand part defines text-only (`played: false`) is text-only too (group `<pattern>, template text, base text-only`). Two books describing one name differently fail the run. A name no book describes takes the text of `common-rules.json` when the dump folder holds one (3.6: Brace and Parry; the run prints `described by the common rules: <names>`); the names with no description are the rest. It prints the count per group and the names with no description.
- `generate` also writes `server/management/Content/Armies/army-book-units.<edition>.json` (`army-book-units.3.5.json`, `army-book-units.3.6.json`; one units file per edition) (`ReserveUnits`, `--units FILE`): every unit a book's Spawn or Split rating (*"Bot Swarms [3]"*) names, in the TTS export's shape, keyed by book uid and unit name, for the import's reserve units. A rating naming no unit of its book fails the run.
- Output is deterministic: an unchanged dump writes identical bytes.
- References `server/game` for `PropertiesJson` and `PropertySetDocument`.

## Per-edition workflow

1. `./scripts/army-books fetch`: refreshes `.superpowers/armyforge/<edition>/` from Army Forge; the only network call.
2. `./scripts/army-books generate` (3.6; `--edition 3.5` for 3.5.1): writes that edition's `army-books/`, `army-book-rules.part.json` and units file; read the report: `range from text`, `text-only spells`, `no description`, and the per-group counts.
3. A name the report lists under `no description` or as a text-only grant is authored in that edition's hand part (`server/game/Content/<folder>/army-rules.part.json`) with the book's printed text, then `generate` again; the spells that granted it become live. `AwaitingTheir36Text` in `tests/Tabletop.ArmyBooks.Tests/ArmyRules36Tests.cs` lists hand entries still on 3.5.1 text.
4. `dotnet test tests/Tabletop.ArmyBooks.Tests` and `tests/Tabletop.GameServer.Tests` (`ContentFoldersTests` pins the 3.5.1 hash; it must not move).
5. `dotnet run --project sim -- suite --only army:3.6:<uid>,spells:3.6:<uid>` for the books touched, then the full suite: a played rule needs a game that fires it (`sim/AGENTS.md` → The rules suite).

## Commands

```bash
./scripts/army-books fetch [--to DIR]
./scripts/army-books generate [--edition 3.5|3.6] [--from DIR] [--out DIR] [--rules FILE]
dotnet test tests/Tabletop.ArmyBooks.Tests
```

## Conventions and pitfalls

- `fetch` is the only code path that calls Army Forge. It runs by hand and never from a test.
- Tests read `tests/Tabletop.ArmyBooks.Tests/Fixtures/`, a checked-in copy of a few books and, in `Fixtures/rules/common-rules.json`, of the two common rules (Brace, Parry).
- The dump is not checked in; the generated books are. Review a revision as the diff of the book files.
- A book file is never edited by hand: fix the generator and regenerate.
- Editing any book changes the properties hash; old games keep the hash they pinned.
- Run from inside the repo; the tool finds the root by `Tabletop.sln` and the dump by walking up for `.superpowers/armyforge`.

## Docs

- `docs/superpowers/specs/2026-10-02-spell-library-design.md`
