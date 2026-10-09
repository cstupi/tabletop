using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Tabletop.GameServer.Tests.Infrastructure;

namespace Tabletop.GameServer.Tests;

public class SpellLibraryTests
{
    static EntryDocument Spell(string name, int hits) => new(name, CatalogKind.Spell, false, null, 1, null, $"{name} text.", new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 12),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, null, null, null, hits, false, null, null, null, EffectDuration.Interaction, [], [])]);
    static PropertySetDocument Part(params EntryDocument[] entries) => new("part", "grimdark-future", [], [], [], entries);

    [Fact]
    public void The_embedded_set_is_one_composed_document_named_for_the_ruleset()
    {
        var doc = Assert.Single(PropertiesSeeder.EmbeddedDocuments(), d => d.GameRules == "grimdark-future");
        Assert.Equal("Grimdark Future v3.5.1", doc.Name);
        Assert.Empty(PropertiesJson.Validate(doc));
        Assert.All(TestProperties.RulebookDocument.Entries, e => Assert.Contains(doc.Entries, x => x.Name == e.Name));
    }

    [Fact]
    public void A_reprinted_spell_is_written_once_and_listed_by_both_books()
    {
        var zap = Spell("Zap", 2);
        var doc = PropertiesSeeder.Compose(TestProperties.RulebookDocument, [],
            [new BookDocument("b2", "Book Two", "1", "grimdark-future", [zap]), new BookDocument("b1", "Book One", "1", "grimdark-future", [zap])]);
        Assert.Single(doc.Entries, e => e.Name == "Zap");
        Assert.Equal(["b1", "b2"], doc.Books!.Select(b => b.Uid));
        Assert.All(doc.Books!, b => Assert.Equal(["Zap"], b.Spells));
        Assert.Empty(PropertiesJson.Validate(doc));
    }

    [Fact]
    public void Two_different_entries_under_one_name_are_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => PropertiesSeeder.Compose(TestProperties.RulebookDocument, [],
            [new BookDocument("b1", "One", "1", "grimdark-future", [Spell("Zap", 2)]), new BookDocument("b2", "Two", "1", "grimdark-future", [Spell("Zap", 3)])]));
        Assert.Contains("'Zap'", ex.Message);
    }

    [Fact]
    public void A_part_adds_its_entries_and_another_rulesets_parts_and_books_are_ignored()
    {
        var rule = new EntryDocument("Evasive", CatalogKind.Model, false, Aggregation.All, null, null, "Enemies get -1 to hit rolls when attacking units where all models have this rule.", null, []);
        var doc = PropertiesSeeder.Compose(TestProperties.RulebookDocument, [Part(rule), Part(rule with { Name = "Elsewhere" }) with { GameRules = "other" }],
            [new BookDocument("x", "X", "1", "other", [Spell("Zap", 1)])]);
        Assert.Contains(doc.Entries, e => e.Name == "Evasive");
        Assert.DoesNotContain(doc.Entries, e => e.Name is "Elsewhere" or "Zap");
        Assert.Empty(doc.Books!);
    }

    [Fact]
    public void Composition_is_stable_and_a_book_change_changes_the_hash()
    {
        var books = new[] { new BookDocument("b1", "One", "1", "grimdark-future", [Spell("Zap", 2)]) };
        var a = PropertiesJson.Hash(PropertiesSeeder.Compose(TestProperties.RulebookDocument, [], books));
        var b = PropertiesJson.Hash(PropertiesSeeder.Compose(TestProperties.RulebookDocument, [], books));
        var c = PropertiesJson.Hash(PropertiesSeeder.Compose(TestProperties.RulebookDocument, [], [books[0] with { Version = "2" }]));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

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

    static readonly HashSet<string> HookRules = ["Hit & Run Fighter", "Hit & Run Shooter", "Harassing", "Harassing Boost", "Quick Shot", "Grounded Reinforcement", "Teleport",
        "Rapid Blink", "Rapid Blink Boost", "Unpredictable Fighter", "Unpredictable Shooter", "Primal", "Primal Boost", "Clan Warrior", "Clan Warrior Boost"];

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
                    // The spell's weapon rules belong to every hit: one block die per hit (times the multiplier), and each rule is a live attacker rule on the hits.
                    var hits = (hit.Values!["hits"] + hit.Rolls.Sum(Dice.RollOutcome.Successes) + sim.Steps("score-hits").Single().Values!["extra_hits"]) * hit.Values["hit_multiplier"];
                    var blocks = sim.Steps("roll-to-block").Single().Rolls;
                    Assert.Equal(hits, blocks.Sum(r => r.Dice.Count));
                    var rules = row.Children.Where(c => c.Kind == EffectKind.GrantProperty).ToList();
                    var settled = LiveProperties.Settle(new Interaction(sim.State, props, Party.Whole(sim.State, wizard.Id), Party.Whole(sim.State, enemy.Id), AttackKind.Spell, null, null, [],
                        SpellRules: rules.Select(c => (c.TargetEntry!, c.Amount)).ToList()));
                    foreach (var rule in rules) Assert.True(settled.Has(Side.Attacker, rule.TargetEntry!), $"{name}: {rule.TargetEntry} is not on the hits' live set");
                    if (rules.Any(c => c.TargetEntry == "AP")) Assert.Contains(blocks, r => r.Modifiers.Any(m => m.Source == "AP"));
                    break;
                case EffectKind.TakeTest:
                    Assert.Contains(sim.Emitted.OfType<SequenceStarted>(), s => s.Sequence == GfNames.MoraleTest && s.Context.ActorUnitId == targetUnit);
                    break;
                default:
                    Assert.Equal(EffectDuration.Once, row.Duration);
                    Assert.Contains(sim.Unit(targetUnit).Grants, g => g.EffectId == row.Id && g.EntryName == name);
                    // The fifteen army-rule hooks (army-rule hooks spec): a granted rule is live on the side its row names.
                    if (row.Kind == EffectKind.GrantProperty && HookRules.Contains(row.TargetEntry!))
                    {
                        var other = targetUnit == wizard.Id ? enemy.Id : wizard.Id;
                        var (attacker, defender) = row.Subject == EffectSubject.Self ? (targetUnit, other) : (other, targetUnit);
                        var live = LiveProperties.Settle(new Interaction(sim.State, props, Party.Whole(sim.State, attacker), Party.Whole(sim.State, defender),
                            row.Conditions.Any(c => c.Kind == ConditionKind.AttackKind && c.Values.Contains("melee")) ? AttackKind.Melee : AttackKind.Shooting, GfNames.Advance, 12, []));
                        Assert.True(live.Has(Side.Attacker, row.TargetEntry!), $"{name}: {row.TargetEntry} is not live on the side its grant names");
                    }
                    break;
            }
        }
    }
}
