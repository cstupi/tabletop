using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Scheduler;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>A rule granted "once" and read only by name is spent where it acts: the grant does not outlive the use (final review I2).</summary>
public class GrantConsumptionTests
{
    static EffectDocument Row(string entry) =>
        new(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, entry, null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], []);
    static EntryDocument Spell(string name, string grants) =>
        new(name, CatalogKind.Spell, false, null, 1, null, $"{name}.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 18), [Row(grants)]);

    static readonly EntryDocument Spikes = new("Spikes", CatalogKind.Spell, false, null, 1, null, "Counts as dangerous terrain.", new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 18),
        [Row("x") with { Kind = EffectKind.CountAsTerrain, TargetEntry = null, TargetTerrain = TargetTerrain.Dangerous }]);

    static readonly PropertySet Props = TestProperties.WithSpells(Spell("Fog", "Counter-Attack"), Spell("Slow Hands", "Unwieldy"), Spell("Wings", "Flying"), Spell("Spotter", "Indirect"), Spell("Blessed Ammo", "Shred"), Spikes);

    static void Grant(Sim sim, Guid unit, string spell) =>
        sim.Append([new GrantAdded(unit, new Tabletop.Protocol.Grant(Guid.CreateVersion7(), Props.Entry(spell)!.Effects[0].Id, spell, unit, 1))]);

    static ScriptedDice Twos => new(Enumerable.Repeat(2, 60).ToArray());

    /// <summary>The scheduler's end of the activation that began at seq 0: its turn-end hook.</summary>
    static void EndActivation(Sim sim) =>
        sim.Append(GrimdarkFutureRules.Instance.OnTurnEnded(sim.State, Props, new TurnContext(Samples.Team1, null, 0, sim.State.Projection.LastSeq)));

    static SequenceContext MeleeOf(Tabletop.Content.Unit charger, Tabletop.Content.Unit target) =>
        new(charger.Id, [target.Id], charger.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Charge);

    [Fact]
    public void A_granted_Counter_Attack_strikes_first_once_and_is_spent_by_the_melee()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Sword", 1)); var defender = Unit("D", "4+", "4+", [], 1, [], Melee("Axe", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, defender.Id, "Fog");
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        var started = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal(defender.Id, started[0].Context.ActorUnitId);                         // the defender struck first
        Assert.Empty(sim.Unit(defender.Id).Grants);
    }

    [Fact]
    public void An_innate_Counter_Attack_strikes_first_and_is_not_offered_a_second_strike()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Sword", 1)); var defender = Unit("D", "4+", "4+", [P("Counter-Attack")], 1, [], Melee("Axe", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Props, Twos);
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        var started = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).ToList();
        Assert.Equal([defender.Id, charger.Id], started.Select(s => s.Context.ActorUnitId!.Value).ToArray());
        Assert.Null(sim.AskForOrNull(Samples.Team2));                                       // no strike-back to offer: the axe struck first
    }

    [Fact]
    public void A_granted_Counter_Attack_leaves_the_defender_no_second_strike_back()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Sword", 1)); var defender = Unit("D", "4+", "4+", [], 1, [], Melee("Axe", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, defender.Id, "Fog");
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        Assert.Equal(2, sim.Emitted.OfType<SequenceStarted>().Count(s => s.Sequence == GfNames.AttackSequence));
        Assert.Null(sim.AskForOrNull(Samples.Team2));
    }

    [Fact]
    public void A_granted_Unwieldy_makes_the_charger_strike_last_once_and_is_spent_by_the_melee()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Sword", 1)); var defender = Unit("D", "4+", "4+", [], 1, [], Melee("Axe", 1));
        var sim = new Sim(State([charger], [defender], x1: 0, x2: 1.3), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, charger.Id, "Slow Hands");
        sim.Start(GfNames.Melee, MeleeOf(charger, defender));
        sim.Answer(Samples.Team2, new Chosen("strike"));                                    // the defender strikes back before the charger's last strike
        var started = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).Select(s => s.Context.ActorUnitId!.Value).ToList();
        Assert.Equal([defender.Id, charger.Id], started);
        Assert.Empty(sim.Unit(charger.Id).Grants);
    }

    static TerrainPlacement Lava(double x, double y) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("lava"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null))]);

    [Fact]
    public void A_granted_Flying_skips_the_activation_test_in_dangerous_terrain_and_is_spent_when_the_activation_ends()
    {
        var u = Unit("U", "4+", "4+", [], 1, []);
        var sim = new Sim(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [Lava(-12, -18.5)]), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, u.Id, "Wings");
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.Empty(sim.Steps("test"));
        Assert.Single(sim.Unit(u.Id).Grants);
        EndActivation(sim);
        Assert.Empty(sim.Unit(u.Id).Grants);
    }

    [Fact]
    public void A_granted_Flying_is_kept_when_nothing_in_the_activation_needed_it()
    {
        var u = Unit("U", "4+", "4+", [], 1, []);
        var sim = new Sim(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, u.Id, "Wings");
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        EndActivation(sim);
        Assert.Single(sim.Unit(u.Id).Grants);
    }

    [Fact]
    public void A_granted_Flying_crosses_dangerous_terrain_untested_and_is_spent_when_the_activation_ends()
    {
        var u = Unit("U", "4+", "4+", [], 1, []);
        var sim = new Sim(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: [Lava(-8, -18.5)]), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, u.Id, "Wings");
        sim.Start(GfNames.RushSeq, new SequenceContext(u.Id, [], [], GfNames.Rush));
        var m = u.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 8 } }))]));
        Assert.Empty(sim.Steps("test"));
        Assert.Single(sim.Unit(u.Id).Grants);
        EndActivation(sim);
        Assert.Empty(sim.Unit(u.Id).Grants);
    }

    [Fact]
    public void A_granted_Indirect_is_spent_by_the_shooting_attack_that_uses_it()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, a.Id, "Spotter");
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [b.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void A_granted_Shred_is_spent_by_the_next_attack_though_no_defense_die_shows_a_one()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "4+", [], 3, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Props, new ScriptedDice(6, 5));
        Grant(sim, a.Id, "Blessed Ammo");
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [b.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Hold));
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    static TerrainPlacement Rough(double x, double y) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("rubble"), new Point3(x, y, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult, null))]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_granted_Flying_used_by_an_applied_move_whatever_it_crosses_is_spent_when_the_activation_ends(bool difficult)
    {
        var u = Unit("U", "4+", "4+", [], 1, []);
        var sim = new Sim(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12, terrain: difficult ? [Rough(-8, -18.5)] : null), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, u.Id, "Wings");
        sim.Start(GfNames.RushSeq, new SequenceContext(u.Id, [], [], GfNames.Rush));
        var m = u.Models[0].Id; var at = ((OnTable)sim.Model(m).Location).Placement;
        sim.Answer(Samples.Team1, new Destinations([new ModelRelocation(m, sim.Model(m).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + 8 } }))]));
        Assert.Single(sim.Unit(u.Id).Grants);
        EndActivation(sim);
        Assert.Empty(sim.Unit(u.Id).Grants);
    }

    [Fact]
    public void A_granted_Indirect_still_gives_its_minus_one_to_hit_after_an_Advance_and_is_then_spent()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1)); var b = Unit("B", "4+", "6+", [], 1, []);
        var sim = new Sim(State([a], [b], x1: -6, x2: 6), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, a.Id, "Spotter");
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [b.Id], a.Models.SelectMany(m => m.Weapons.Select(w => w.Id)).ToList(), GfNames.Advance));
        Assert.Contains(sim.Steps("roll-to-hit").Single().Rolls.Single().Modifiers, m => m.Source == "Indirect" && m.Amount == -1);
        Assert.Empty(sim.Unit(a.Id).Grants);
    }

    [Fact]
    public void An_innate_Flying_skipping_the_activation_test_spends_a_count_as_dangerous_grant()
    {
        var u = Unit("U", "4+", "4+", [], 1, [P("Flying")]);
        var sim = new Sim(State([u], [Unit("T", "4+", "4+", [], 1, [])], x1: -12, x2: 12), GrimdarkFutureRules.Instance, Props, Twos);
        Grant(sim, u.Id, "Spikes");
        sim.Start(GfNames.HoldSeq, new SequenceContext(u.Id, [], [], GfNames.Hold));
        Assert.Empty(sim.Steps("test"));
        Assert.Empty(sim.Unit(u.Id).Grants);
    }

    // ---- Owner ruling: "Granted flying applies to the whole move - its per activation" -------------------------------------------------------

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    /// <summary>Answers the casting checkpoint with a Fog that fails (every die is a 2), until the move Asks again.</summary>
    static void CastAndFail(Sim sim)
    {
        for (var i = 0; i < 10 && sim.AskFor(Samples.Team1).Question != "move"; i++)
        {
            var ask = sim.AskFor(Samples.Team1);
            sim.Answer(Samples.Team1, ask.Legal switch
            {
                Options o when ask.Question == "cast" => new Chosen(o.Choices.First(c => c.Id.StartsWith("Fog|")).Id),
                Options o => new Chosen(o.Choices[0].Id),
                _ => new Passed(),
            });
        }
        Assert.Equal("move", sim.AskFor(Samples.Team1).Question);
    }

    static int DangerousTests(Sim sim) => sim.Emitted.OfType<SequenceStarted>().Count(s => s.Sequence == GfNames.DangerousTerrain);

    [Fact]
    public void A_granted_Flying_lasts_the_whole_activation_through_the_activation_test_and_every_leg_of_a_split_move_and_is_gone_by_the_next()
    {
        // Activated in lava; the first leg crosses a second lava, a cast splits the move, and the second leg crosses a third, past 6" of movement.
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = new Sim(State([wizard], [Unit("T", "4+", "4+", [], 1, [])], x1: -20, x2: 20, terrain: [Lava(-20, -18.5), Lava(-16.5, -18.5), Lava(-11, -18.5), Rough(-11, -18.5)]),
            GrimdarkFutureRules.Instance, Props, Twos);
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        Grant(sim, wizard.Id, "Wings");
        var m = wizard.Models[0].Id;
        sim.Start(GfNames.RushSeq, new SequenceContext(wizard.Id, [], [], GfNames.Rush));
        Assert.Equal(0, DangerousTests(sim));                                                               // the activation-start test skipped
        Assert.Single(sim.Unit(wizard.Id).Grants);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, m, 6)]));
        Assert.Single(sim.Unit(wizard.Id).Grants);
        CastAndFail(sim);
        var second = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Null(second.DifficultCap);                                                                   // still flying on the second leg
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, m, 5)]));
        Assert.Equal(0, DangerousTests(sim));                                                               // neither leg's lava tested
        Assert.Single(sim.Unit(wizard.Id).Grants);
        while (sim.AskForOrNull(Samples.Team1) is not null) sim.Answer(Samples.Team1, new Passed());       // no second cast: the rush ends
        Assert.Empty(sim.State.Projection.Stack);
        EndActivation(sim);
        Assert.Empty(sim.Unit(wizard.Id).Grants);
        sim.Append([new ModelsMoved([new ModelRelocation(m, sim.Model(m).Location, new OnTable(new Placement(new Point3(-20, -18.5, 0), new Yaw(0))))])]);
        sim.Start(GfNames.HoldSeq, new SequenceContext(wizard.Id, [], [], GfNames.Hold));
        Assert.Equal(1, DangerousTests(sim));                                                               // the next activation in lava tests
    }
}
