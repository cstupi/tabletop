# Task 15: `CLAUDE.md` and the whole-tree check

**Files:**
- Modify: `CLAUDE.md` (Commands, Layout, Conventions)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing new.

- [ ] **Step 1: Update `CLAUDE.md`**

Under **Commands**, after the `./scripts/check-client` line:

```bash
./scripts/army-books fetch   # download the official army books from Army Forge into .superpowers/armyforge/ (the only command that calls it)
./scripts/army-books generate   # regenerate server/game/Content/army-books/ from that copy; fails on any untranslatable spell
```

Under **Layout**, in the `server/game/` bullet, after the `Properties/` sentence, add: *"`Content/` holds the core document (`grimdark-future-core.properties.json`), the hand-written army-rules part (`grimdark-future-army-rules.part.json`) and the 48 generated army books (`army-books/<uid>.book.json`), composed into one set by `PropertiesSeeder`."* Under the repo-level-support paragraph, add `scripts/ArmyBooks/` (`Tabletop.ArmyBooks`, the generator) to what `scripts/` holds.

Under **Conventions**, replace *"Authored `Properties` are `server/game/Content/*.properties.json`, seeded by hash at startup; editing one makes a new set with a new hash — old games keep theirs."* with *"Authored `Properties` are `server/game/Content/*.properties.json` with every `*.part.json` and `army-books/*.book.json` composed in, seeded by hash at startup; editing any of them makes a new set with a new hash, and old games keep theirs. A book file is never edited by hand: fix the generator and regenerate."*

- [ ] **Step 2: The whole-tree check**

Run:

```bash
dotnet build --no-incremental
./scripts/check-client
docker compose up -d && dotnet test
(cd web && npm test && npx svelte-check)
(cd viewer && npm test && npm run check && npm run build)
./scripts/army-books generate && git status --short server/game/Content/army-books
```

Expected: 0 errors and no new warnings; `check-client` prints nothing; every suite passes; the regeneration leaves `server/game/Content/army-books` unchanged.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "Record the army-books generator and the composed properties in CLAUDE.md"
```
