# Task 13: The 48 books, and every spell is cast

**Files:**
- Create: `server/game/Content/army-books/<uid>.book.json`, 48 files (generated); delete `server/game/Content/army-books/.gitkeep`
- Modify: `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs` (the shipped library, and the every-spell theory)

**Interfaces:**
- Consumes: `./scripts/army-books generate` (Task 12); `Armies.InBook` (Task 1); every engine addition of Tasks 5 to 11.
- Produces: the shipped set with 48 books and 190 spell entries; `SpellLibraryTests.EverySpell` (`TheoryData<string, string>`: book uid, spell name), which the army-rule hooks plan extends.

Spec → *Testing*, *Every spell is cast*: a theory over every spell entry of the shipped set fields a caster of a book listing it and a legal target, casts it with scripted dice, and asserts the outcome its family promises. It fails the day a regeneration produces a spell the engine cannot cast.

- [ ] **Step 1: Generate the books**

Run: `./scripts/army-books generate` (if `.superpowers/armyforge/index.json` is missing, `./scripts/army-books fetch` first; it is the only command that calls Army Forge).
Expected: `wrote 48 books, 190 spells`, and 48 files under `server/game/Content/army-books/`. Run it a second time and check `git status --short server/game/Content/army-books` shows the same files with no further change.

Delete `server/game/Content/army-books/.gitkeep`.

- [ ] **Step 2: Write the tests**

Add to `tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs` (the class from Task 2), with the usings `Tabletop.GameServer.Rules.GrimdarkFuture`, `Tabletop.Protocol`, `Tabletop.Protocol.Tests` and `static Tabletop.GameServer.Tests.Infrastructure.Armies`:

```csharp
    [Fact]
    public void The_shipped_set_holds_every_official_book_and_its_spells()
    {
        var core = TestProperties.Core;
        Assert.Equal(48, core.Books.Count);
        Assert.Equal(190, core.Entries.Count(e => e.Kind == CatalogKind.Spell));
        Assert.All(core.Books.Values.Where(b => b.Uid != "i4ZEAmqeFSfAcw3f"), b => Assert.Equal(6, core.SpellsOf(b.Uid).Count));
        Assert.Equal([1, 1, 2, 2, 3, 3], core.SpellsOf("z65fgu0l29i4lnlu").Select(s => s.Cost!.Value).Order());
        Assert.All(core.Entries.Where(e => e.Kind == CatalogKind.Spell), e => Assert.Null(e.Faction));
        Assert.Equal("3.5.3", core.Books["1wj1ysgxpuuz9bc7"].Version);
    }

    public static TheoryData<string, string> EverySpell()
    {
        var data = new TheoryData<string, string>();
        foreach (var spell in TestProperties.Core.Entries.Where(e => e.Kind == CatalogKind.Spell).OrderBy(e => e.Name, StringComparer.Ordinal))
            data.Add(TestProperties.Core.Books.Values.OrderBy(b => b.Uid, StringComparer.Ordinal).First(b => b.Spells.Contains(spell.Name)).Uid, spell.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(EverySpell))]
    public void Every_spell_is_cast_and_does_what_its_family_promises(string book, string name)
    {
        var props = TestProperties.Core; var spell = props.Entry(name)!;
        var wizard = Armies.InBook(Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]), book);
        var friend = Unit("Friend", "4+", "4+", [], 3, []);
        var enemy = Unit("Enemy", "4+", "4+", [], 5, [P("Tough", 3)]);
        var state = State([wizard, friend], [enemy], x1: -2, x2: 2);
        var sim = new Sim(state, new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), props, new ScriptedDice([6, .. Enumerable.Repeat(4, 300)]));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"{name}|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());                                                         // the spend window: no tokens for or against
        var targets = Assert.IsType<PieceSelection>(sim.AskFor(Samples.Team1).Legal);
        var target = spell.Targeting!.Side == TargetSide.Friendly ? wizard.Id : targets.Eligible[0];
        sim.Answer(Samples.Team1, new SelectedPieces([target]));
        for (var i = 0; i < 50 && sim.State.Projection.OpenAsks.Count > 0; i++)
        {
            var open = sim.State.Projection.OpenAsks.Values.First();
            var team = open.Ask.Teams.First(t => !open.Answers.ContainsKey(t));
            sim.Answer(team, open.Ask.Legal switch
            {
                Allocation a => new Allocated(new Dictionary<Guid, int> { [a.Targets[0]] = a.Amount }),
                _ => new Passed(),
            });
        }
        Assert.Empty(sim.State.Projection.OpenAsks);
        Assert.Empty(sim.State.Projection.Stack);
        var targetUnit = sim.State.Projection.Units.ContainsKey(target) ? target : sim.Model(target).UnitId;
        foreach (var row in spell.Effects)
        {
            switch (row.Kind)
            {
                case EffectKind.AddHit:
                    var hit = sim.Steps("hit").Single();
                    if (row.TargetRoll is null) { Assert.Empty(hit.Rolls); Assert.Equal(row.Amount, hit.Values!["hits"]); }
                    else Assert.Equal(row.Amount, hit.Rolls.Single().Dice.Count);
                    break;
                case EffectKind.TakeTest:
                    Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.MoraleTest && s.Context.ActorUnitId == targetUnit);
                    break;
                default:
                    Assert.Equal(EffectDuration.Once, row.Duration);
                    Assert.Contains(sim.Unit(targetUnit).Grants, g => g.EffectId == row.Id && g.EntryName == name);
                    break;
            }
        }
    }
```

The wizard and its friend stand at x = −2, the enemy at x = 2: four inches between centres, inside every range in the corpus (the shortest is 6"). A friendly-side spell targets the wizard's own unit, which every friendly spell may (`GfCasting.SpellTargets` counts the caster's unit among its friends at distance 0). The Allocation answer puts every wound on the first eligible model; every other Ask is passed, which declines *Deep Hypnosis*'s move.

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Tabletop.GameServer.Tests --filter FullyQualifiedName~SpellLibraryTests`
Expected: PASS, 190 theory cases and the facts. A failing case names its spell; fix the engine (or, if the generator mistranslated, the translator and regenerate), never the book file by hand.

- [ ] **Step 4: Run every suite**

Run: `docker compose up -d && dotnet test`
Expected: PASS. `PropertiesStoreTests` now seeds the composed set with its books; `DriverAgreementTests` seeds it once per run and the hash is the same for both drivers.

- [ ] **Step 5: Commit**

```bash
git add server/game/Content/army-books tests/Tabletop.GameServer.Tests/SpellLibraryTests.cs
git commit -m "Generate the 48 official army books and cast every spell in a test"
```
