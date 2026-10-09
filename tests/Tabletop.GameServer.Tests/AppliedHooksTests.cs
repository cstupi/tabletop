using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.AppliedKindsTests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>StepCompleted.Applied for the rules Rules/GrimdarkFuture/ implements by name, one family at a time: the hook records its rule when it changed the step's result.
/// Deployment's hooks (Scout, Ambush) and action legality (Aircraft, Immobile) run in the scheduler, where no step completes, so they have no StepCompleted to list them on.</summary>
public class AppliedHooksTests
{
    static TerrainPlacement Patch(TerrainDeclarations kind) => new(Guid.CreateVersion7(), VisualRef.OfBuiltin("patch"), new Point3(-17, -18.5, 0), new Yaw(0), 1,
        [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(2, 4), new Point2(0, 0), new Yaw(0), 0, 1, kind, null))]);

    static Sim Advance(PropertyInstance[] props, TerrainPlacement? terrain, double dx) => Moved(GfNames.AdvanceSeq, GfNames.Advance, props, terrain, dx);
    static Sim Rush(PropertyInstance[] props, TerrainPlacement? terrain, double dx, PropertySet? set = null, string? grant = null) => Moved(GfNames.RushSeq, GfNames.Rush, props, terrain, dx, set, grant);

    static Sim Moved(string sequence, string action, PropertyInstance[] props, TerrainPlacement? terrain, double dx, PropertySet? set = null, string? grant = null)
    {
        var a = Unit("A", "4+", "4+", [], 1, props);
        set ??= TestProperties.Core;
        var sim = new Sim(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, terrain: terrain is null ? null : [terrain]), GrimdarkFutureRules.Instance, set, new ScriptedDice(4));
        if (grant is not null) sim.Append([new GrantAdded(a.Id, new Grant(Guid.CreateVersion7(), set.Entry(grant)!.Effects[0].Id, grant, a.Id, 1))]);
        sim.Start(sequence, new SequenceContext(a.Id, [], [], action));
        Shift(sim, a.Models[0].Id, dx);
        return sim;
    }

    static readonly PropertySet Stride = TestProperties.WithSpells(new EntryDocument("Stride", CatalogKind.Spell, false, null, 1, null, "Stride.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 1, 18),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, "Strider", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]));

    [Fact]
    public void Movement_lists_strider_across_difficult_terrain_and_nothing_on_open_ground()
    {
        Assert.Equal(["Strider"], Applied(Rush([P("Strider")], Patch(TerrainDeclarations.Difficult), 8), "move"));
        NoneApplied(Rush([P("Strider")], null, 8));
        NoneApplied(Rush([], Patch(TerrainDeclarations.Difficult), 5));
    }

    [Fact]
    public void Movement_lists_no_strider_on_an_advance_whose_allowance_the_difficult_cap_never_bound()
    {
        NoneApplied(Advance([P("Strider")], Patch(TerrainDeclarations.Difficult), 5));
        NoneApplied(Advance([P("Flying")], Patch(TerrainDeclarations.Difficult), 5));
    }

    [Fact]
    public void A_granted_strider_is_listed_with_its_spell_and_not_spent_by_the_move()
    {
        var sim = Rush([], Patch(TerrainDeclarations.Difficult), 8, Stride, "Stride");
        Assert.Equal(["Strider", "Stride"], Applied(sim, "move"));
        Assert.Single(sim.Unit(sim.State.Board.Armies[0].Snapshot.Units[0].Id).Grants);
    }

    [Fact]
    public void Movement_lists_flying_over_dangerous_terrain_it_skipped_and_nothing_on_open_ground()
    {
        var over = Advance([P("Flying")], Patch(TerrainDeclarations.Dangerous), 5);
        Assert.Equal(["Flying"], Applied(over, "move"));
        Assert.Empty(over.Steps("test"));
        NoneApplied(Advance([P("Flying")], null, 5));
    }

    static Sim Lob(bool wall, params int[] dice)
    {
        var mortar = Unit("Mortar", "4+", "4+", [], 1, [], Gun("Mortar", "36\"", 1, P("Indirect"))); var t = Unit("T", "4+", "4+", [], 1, []);
        var block = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(0, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 6), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null))]);
        var sim = Sim.Gf(State([mortar], [t], x1: -6, x2: 6, terrain: wall ? [block] : null), dice);
        sim.Start(GfNames.AttackSequence, new SequenceContext(mortar.Id, [t.Id], mortar.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        return sim;
    }

    [Fact]
    public void Attack_lists_indirect_where_it_let_a_hidden_target_be_shot_and_nothing_at_a_target_in_the_open()
    {
        var hidden = Lob(wall: true, 5, 6);
        Assert.Equal(1, hidden.Steps("determine-attacks").Single().Values!["attacks"]);
        Assert.Contains("Indirect", Applied(hidden, "determine-attacks"));
        Assert.Empty(Applied(hidden, "roll-to-hit"));
        NoneApplied(Lob(wall: false, 5, 6));
    }

    [Fact]
    public void Attack_lists_takedown_on_the_wound_it_aimed()
    {
        var takedown = Unit("Takedown", "5+", "4+", [], 1, [], Gun("Long Rifle", "30\"", 1, P("Takedown")));
        var t = Unit("T", "4+", "6+", [], 2, []);
        var sim = Sim.Gf(State([takedown], [t], x1: -6, x2: 6), 5, 1);
        sim.Start(GfNames.AttackSequence, new SequenceContext(takedown.Id, [t.Id], takedown.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        sim.Answer(Samples.Team1, new SelectedPieces([t.Models[1].Id]));
        Assert.Equal(["Takedown"], Applied(sim, "assign"));
    }

    [Fact]
    public void Shooting_lists_limited_when_it_kept_a_fired_weapon_out()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Missile", "24\"", 1, P("Limited")), Gun("Rifle", "24\"", 1));
        var b = Unit("B", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([a], [b], x1: -6, x2: 6), 4, 6);
        var fired = Guid.CreateVersion7();
        sim.Append([new SequenceStarted(fired, GfNames.AttackSequence, new SequenceContext(a.Id, [], [a.Models[0].Weapons[0].Id], GfNames.Hold)), new SequenceFinished(fired)]);
        sim.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        Assert.Equal(["Limited"], Applied(sim, "declare-targets"));

        var fresh = Sim.Gf(State([a], [Unit("B", "4+", "4+", [], 1, [])], x1: -6, x2: 6), 4);
        fresh.Start(GfNames.Shooting, new SequenceContext(a.Id, [], [], GfNames.Hold));
        Assert.Equal("declare_targets", fresh.AskFor(Samples.Team1).Question);
        Assert.Empty(Applied(fresh, "declare-targets"));
    }

    [Fact]
    public void Attack_lists_hero_when_it_deferred_the_wound_and_tough_when_a_model_soaked_it()
    {
        var rifle = Unit("R", "4+", "4+", [], 1, [], Gun("Rifle", "24\"", 1));
        var t = Unit("T", "4+", "6+", [], 2, []);
        var joined = t with { Models = [t.Models[0] with { Properties = [P("Hero")] }, t.Models[1]] };
        var sim = Sim.Gf(State([rifle], [joined], x1: -6, x2: 6), 5, 1);
        sim.Start(GfNames.AttackSequence, new SequenceContext(rifle.Id, [joined.Id], rifle.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        Assert.Equal(["Hero"], Applied(sim, "assign"));

        var ogre = Unit("O", "4+", "6+", [], 1, [P("Tough", 3)]);
        var tough = Sim.Gf(State([rifle], [ogre], x1: -6, x2: 6), 5, 1);
        tough.Start(GfNames.AttackSequence, new SequenceContext(rifle.Id, [ogre.Id], rifle.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        Assert.Equal(["Tough"], Applied(tough, "assign"));
        Assert.Equal(1, tough.Model(ogre.Models[0].Id).Wounds);
    }

    [Fact]
    public void Morale_lists_the_hero_whose_quality_the_unit_tested_on()
    {
        var squad = Unit("Squad", "5+", "5+", [], 2, []);
        var joined = squad with { Models = [squad.Models[0] with { Stats = new Dictionary<string, string> { ["Quality"] = "3+", ["Defense"] = "2+" }, Properties = [P("Hero")] }, squad.Models[1]] };
        var sim = Sim.Gf(State([joined], [Unit("E", "4+", "4+", [], 1, [])]), 4);
        sim.Start(GfNames.MoraleTest, new SequenceContext(joined.Id, [], [], "wounds"));
        Assert.Equal(1, sim.Steps("test").Single().Values!["passed"]);
        Assert.Equal(["Hero"], Applied(sim, "test"));

        var plain = Sim.Gf(State([Unit("P", "4+", "4+", [], 2, [])], [Unit("E", "4+", "4+", [], 1, [])]), 4);
        plain.Start(GfNames.MoraleTest, new SequenceContext(plain.State.Board.Armies[0].Snapshot.Units[0].Id, [], [], "wounds"));
        NoneApplied(plain);
    }

    [Fact]
    public void Morale_lists_fearless_when_its_roll_was_taken()
    {
        var u = Unit("U", "4+", "4+", [], 3, [P("Fearless")]);
        var sim = Sim.Gf(State([u], [Unit("E", "4+", "4+", [], 1, [])]), 1, 4);
        sim.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        Assert.Equal(["Fearless"], Applied(sim, "test"));
        var passed = Sim.Gf(State([u], [Unit("E", "4+", "4+", [], 1, [])]), 5);
        passed.Start(GfNames.MoraleTest, new SequenceContext(u.Id, [], [], "wounds"));
        NoneApplied(passed);
    }

    [Fact]
    public void Casting_lists_caster_at_the_pick_and_the_spell_where_it_takes_effect()
    {
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 6)]); var t = Unit("T", "4+", "4+", [], 2, []);
        var props = TestProperties.WithSpells(new EntryDocument("Zap", CatalogKind.Spell, false, null, 1, null, "Zap.",
            new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 24),
            [new EffectDocument(EffectSubject.Self, 10, EffectKind.AddHit, null, null, null, null, null, null, null, 1, false, null, null, null, EffectDuration.Interaction, [], [])]));
        var sim = new Sim(State([wizard], [t], x1: -4, x2: 4), new WithSequences(GrimdarkFutureRules.Instance, WithSequences.Parent("test-parent", "w")), props, new ScriptedDice(6, 6, 6));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 6)]);
        sim.Start(GfNames.CastingSeq, new SequenceContext(wizard.Id, [], [], null));
        sim.Answer(Samples.Team1, new Chosen($"Zap|{wizard.Models[0].Id}"));
        sim.Answer(Samples.Team1, new Passed());
        sim.Answer(Samples.Team1, new SelectedPieces([t.Id]));
        Assert.Equal(["Caster"], Applied(sim, "pick-spell"));
        Assert.Equal(["Zap"], Applied(sim, "hit"));
        Assert.Equal(["Zap"], Applied(sim, "effects"));
    }
}
