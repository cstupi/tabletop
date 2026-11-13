using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0, written from the rulebook text.
///
/// AP(X) (p.13): "Targets get -X to Defense rolls when blocking hits from weapons with this special rule."
///   1. -X on the Defense roll (X the parameter).  2. only on hits from the weapon carrying it (another weapon's hits block unmodified).
/// Blast(X) (p.13): "Ignores cover, and after resolving other special rules, each hit is multiplied by X, where X is up to as many hits as models in the
/// target unit." Example: 2 Attacks, Blast(3), a unit of 2 models: each hit ×2, 4 hits.
///   1. ignores cover.  2. each hit ×X.  3. X capped at the target unit's models (the Example).  4. not capped below X when the unit has more models.
///   5. "after resolving other special rules": an extra hit another rule deals is multiplied too.
/// Deadly(X) (p.14): "Assign each wound to one model, and multiply it by X. Hits from Deadly must be resolved first, and these wounds don't carry over to
/// other models if the original target is killed."
///   1. each wound assigned to one model.  2. ×X on that model.  3. Deadly hits resolved before the other weapons' (shooting and melee).
///   4. the excess over the model's wounds does not carry over.
/// </summary>
public class ClausesAttackApBlastDeadlyTests
{
    static Sim Shoot(Unit a, Unit t, IReadOnlyDictionary<string, Guid> assigned, double x1, double x2, params int[] dice)
    {
        var sim = Sim.Gf36(State([a], [t], x1: x1, x2: x2), dice);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(assigned.ToDictionary(kv => kv.Key, kv => kv.Value)));
        return sim;
    }

    static Sim Fire(Unit a, Unit t, double x1, double x2, params int[] dice)
    {
        var sim = Sim.Gf36(State([a], [t], x1: x1, x2: x2), dice);
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [t.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        return sim;
    }

    static List<RollRecord> Blocks(Sim sim) => [.. sim.Steps("roll-to-block").SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.DefenseRoll)];

    // ---- AP -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Ap_takes_x_off_the_defense_rolls_of_its_own_weapons_hits_and_not_another_weapons()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("A Gun", "24\"", 1, P("AP", 2)), Gun("B Gun", "24\"", 1));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 6)]);
        var sim = Shoot(a, t, new Dictionary<string, Guid> { ["A Gun"] = t.Id, ["B Gun"] = t.Id }, -6, 6, 6, 5, 6, 5);   // each hits; each block rolls 5
        var blocks = Blocks(sim);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(-2, blocks[0].Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.False(blocks[0].Dice.Single().Success);                                                   // 5 - 2 = 3 against 4+
        Assert.DoesNotContain(blocks[1].Modifiers, m => m.Source == "AP");
        Assert.True(blocks[1].Dice.Single().Success);
    }

    [Fact]
    public void Ap_acts_on_melee_hits_of_its_weapon()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Axe", 1, P("AP", 1)));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 6)], Melee("Fist", 1));
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), 6, 4, 1, 1, 1, 1);
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        var block = Blocks(sim).First();
        Assert.Equal(-1, block.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.False(block.Dice.Single().Success);                                                       // 4 - 1 = 3 against 4+
    }

    // ---- Blast ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Blast_example_two_hits_against_two_models_with_blast_three_are_four_hits()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 2, P("Blast", 3)));
        var t = Unit("T", "4+", "6+", [], 2, []);
        var sim = Fire(a, t, -6, 6, 4, 4, 1, 1, 1, 1);
        Assert.Equal(2, sim.Steps("roll-to-hit").Single().Values!["hit_multiplier"]);
        Assert.Equal(4, Blocks(sim).Sum(r => r.Dice.Count));
    }

    [Fact]
    public void Blast_multiplies_by_the_full_x_against_a_unit_with_more_models()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 1, P("Blast", 3)));
        var t = Unit("T", "4+", "2+", [], 5, []);
        var sim = Fire(a, t, -6, 6, 4, 6, 6, 6);
        Assert.Equal(3, sim.Steps("roll-to-hit").Single().Values!["hit_multiplier"]);
        Assert.Equal(3, Blocks(sim).Sum(r => r.Dice.Count));
    }

    [Fact]
    public void Blast_multiplies_after_other_special_rules_so_a_surge_extra_hit_is_multiplied_too()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Mortar", "30\"", 1, P("Blast", 3), P("Surge")));
        var t = Unit("T", "4+", "2+", [], 3, []);
        var sim = Fire(a, t, -6, 6, [6, .. Enumerable.Repeat(6, 6)]);                                     // one 6 to hit: the hit plus Surge's extra hit
        Assert.Equal(1, sim.Steps("score-hits").Single().Values!["extra_hits"]);
        Assert.Equal(6, Blocks(sim).Sum(r => r.Dice.Count));                                             // (1 + 1) × 3
    }

    static TerrainPlacement Wood(Point3 at) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), at, new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);

    /// <summary>A one-model target inside a 3"×3" cover wood at (6, -18.5), its own row; plain guns block at +1, Blast ignores it.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Blast_ignores_cover_that_a_plain_weapon_gets(bool blast)
    {
        var a = Unit("A", "4+", "4+", [], 1, [], blast ? Gun("Mortar", "30\"", 1, P("Blast", 3)) : Gun("Rifle", "30\"", 1));
        var t = Unit("T", "4+", "6+", [], 1, []);
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6, terrain: [Wood(new Point3(6, -18.5, 0))]), 6, 5);
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [t.Id], a.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var block = Blocks(sim).Single();
        Assert.Equal(!blast, block.Modifiers.Any(m => m.Source == "cover" && m.Amount == 1));
        Assert.Equal(blast ? "Blast" : null, block.Cover?.IgnoredBy);
    }

    // ---- Deadly ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Deadly_puts_each_wound_on_one_model_multiplied_and_the_excess_does_not_carry_over()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Lance", "24\"", 1, P("Deadly", 3)));
        var t = Unit("T", "4+", "4+", [], 2, [P("Tough", 2)]);
        var sim = Fire(a, t, -6, 6, 6, 1);                                                               // a hit, a failed block: one wound
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 1 }));
        Assert.IsType<OnShelf>(sim.Model(t.Models[0].Id).Location);                                       // ×3 on a Tough(2) model kills it
        Assert.Equal(0, sim.Model(t.Models[1].Id).Wounds);                                                // the third wound is lost
        Assert.Empty(sim.State.Projection.Stack);
    }

    [Fact]
    public void Two_deadly_wounds_go_on_two_models()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Lance", "24\"", 2, P("Deadly", 3)));
        var t = Unit("T", "4+", "4+", [], 3, [P("Tough", 3)]);
        var sim = Fire(a, t, -6, 6, 6, 6, 1, 1);
        sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[0].Id] = 1 }));
        if (sim.AskForOrNull(Samples.Team2) is { Legal: Allocation }) sim.Answer(Samples.Team2, new Allocated(new Dictionary<Guid, int> { [t.Models[1].Id] = 1 }));
        Assert.Equal(1, sim.Alive(t.Id));
        Assert.Equal(0, sim.Model(t.Models[2].Id).Wounds);
    }

    [Fact]
    public void Deadly_hits_are_resolved_before_the_other_weapons_when_shooting()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Alpha Rifle", "24\"", 1), Gun("Zulu Lance", "24\"", 1, P("Deadly", 2)));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Shoot(a, t, new Dictionary<string, Guid> { ["Alpha Rifle"] = t.Id, ["Zulu Lance"] = t.Id }, -6, 6, 1, 1);
        var first = sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.AttackSequence);
        Assert.Equal("Zulu Lance", a.Models[0].Weapons.Single(w => w.Id == first.Context.WeaponIds.Single()).Name);
    }

    [Fact]
    public void Deadly_hits_are_resolved_before_the_other_weapons_in_melee()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Melee("Alpha Sword", 1), Melee("Zulu Axe", 1, P("Deadly", 2)));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), Enumerable.Repeat(1, 10).ToArray());
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        var first = sim.Emitted.OfType<SequenceStarted>().First(s => s.Sequence == GfNames.AttackSequence);
        Assert.Equal("Zulu Axe", a.Models[0].Weapons.Single(w => w.Id == first.Context.WeaponIds.Single()).Name);
    }
}
