using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;

namespace Tabletop.GameServer.Tests;

/// <summary>
/// Clause tests, Grimdark Future 3.6.0, written from the rulebook and army-book text.
///
/// Hazardous (army rule): "Gets AP(4), but on unmodified results of 1 to hit this model takes one wound, or X wounds if this weapon has the
/// Blast(X)/Deadly(X) rule."
///   1. AP(4) on its hits.  2. only an unmodified 1 (a 2 modified to 1 is no wound).  3. "this model" takes it (GfHazardous36Tests).
///   4. X wounds with Blast(X)/Deadly(X) (GfHazardous36Tests).  5. in melee too: the rule names no attack kind.
/// Indirect (p.14): "When using Hold actions and shooting, may target enemies that are not in line of sight as if in line of sight, and ignores cover
/// from Sight Blockers."
///   1. on a Hold, an unseen target (GfIndirect36Tests).  2. not after another action (GfIndirect36Tests).  3. sight-blocker cover ignored
///   (GfIndirect36Tests).  4. cover terrain is not a Sight Blocker: kept.
/// Quick Throw (army rule): "This model may always target enemies that are not in line of sight when shooting with Indirect weapons (instead of only
/// when using Hold actions)."
///   1. on an Advance it targets unseen (GfQuickThrow36Tests, hand entry; here on the shipped entry).  2. "This model": a squad-mate without it does not.
/// Quick Shot (army rule): "This model may shoot after using Rush actions."
///   1. a Rush offers shooting with it, not without.  2. "This model": only the holding model's weapons fire.
/// Assault (p.13): "Counts as both a ranged weapon and a melee weapon."
///   1. shoots and strikes (GfAssault36Tests).  2. a charged unit strikes back with it.
/// </summary>
public class ClausesAttackHazardousIndirectTests
{
    static List<RollRecord> Rolls(Sim sim, string roll) => [.. sim.Emitted.OfType<StepCompleted>().SelectMany(s => s.Rolls).Where(r => r.Roll == roll)];

    // ---- Hazardous ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Hazardous_gets_ap_four()
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Tough", 3)], Gun("Drill", "24\"", 1, P("Hazardous")));
        var t = Unit("T", "4+", "2+", [], 1, [P("Tough", 6)]);
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6), 5, 5);
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [t.Id], a.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var block = Rolls(sim, GfNames.DefenseRoll).Single();
        Assert.Equal(-4, block.Modifiers.Where(m => m.Source == "AP").Sum(m => m.Amount));
        Assert.False(block.Dice.Single().Success);                                                      // 5 - 4 against 2+
    }

    /// <summary>The target has Stealth and stands 12" away: every hit roll is at -1. A raw 2 (modified to 1) wounds nobody; a raw 1 wounds the shooter.</summary>
    [Theory]
    [InlineData(2, 0)]
    [InlineData(1, 1)]
    public void Hazardous_wounds_only_on_an_unmodified_one(int face, int wounds)
    {
        var a = Unit("A", "4+", "4+", [], 1, [P("Tough", 3)], Gun("Drill", "24\"", 1, P("Hazardous")));
        var t = Unit("T", "4+", "4+", [], 1, [P("Stealth"), P("Tough", 6)]);
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6), face, 4, 4);
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [t.Id], a.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var hit = Rolls(sim, GfNames.Hit).Single();
        Assert.Contains(hit.Modifiers, m => m.Source == "Stealth" && m.Amount == -1);
        Assert.Equal(wounds, sim.Steps("score-hits").Single().Values!["self_wounds"]);
        Assert.Equal(wounds, sim.Model(a.Models[0].Id).Wounds);
    }

    [Fact]
    public void Hazardous_wounds_its_model_on_a_one_in_melee()
    {
        var a = Unit("A", "4+", "4+", [], 2, [P("Tough", 3)], Melee("Chainfist", 1, P("Hazardous")));
        var t = Unit("T", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Fist", 1));
        var sim = Sim.Gf36(State([a], [t], x1: -1.3, x2: 0), [5, 1, .. Enumerable.Repeat(4, 20)]);
        sim.Start(GfNames.Melee, new SequenceContext(a.Id, [t.Id], GfMelee.MeleeWeapons(sim.State, a.Id), GfNames.Charge));
        Assert.Equal((0, 1), (sim.Model(a.Models[0].Id).Wounds, sim.Model(a.Models[1].Id).Wounds));
    }

    // ---- Indirect -------------------------------------------------------------------------------------------------------------------------

    /// <summary>The target stands in a cover wood (cover terrain, not a sight blocker): an Indirect weapon on a Hold still blocks at +1.</summary>
    [Fact]
    public void Indirect_on_a_hold_keeps_cover_terrain()
    {
        var a = Unit("A", "4+", "4+", [], 1, [], Gun("Mortar", "36\"", 1, P("Indirect")));
        var t = Unit("T", "4+", "6+", [], 1, []);
        var wood = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wood"), new Point3(6, -18.5, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(3, 3), new Point2(0, 0), new Yaw(0), 0, 3, TerrainDeclarations.Cover, null))]);
        var sim = Sim.Gf36(State([a], [t], x1: -6, x2: 6, terrain: [wood]), 6, 5);
        sim.Start(GfNames.AttackSequence, new SequenceContext(a.Id, [t.Id], a.Models[0].Weapons.Select(w => w.Id).ToList(), GfNames.Hold));
        var block = Rolls(sim, GfNames.DefenseRoll).Single();
        Assert.Contains(block.Modifiers, m => m.Source == "cover" && m.Amount == 1);
        Assert.True(block.Dice.Single().Success);
    }

    // ---- Quick Throw ----------------------------------------------------------------------------------------------------------------------

    /// <summary>A 4"-tall wall at x = 0 hides the target at x = 6 from both mortars at x = -6. On an Advance only the model holding Quick Throw may fire.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Quick_throw_lets_only_its_holders_fire_indirect_at_an_unseen_target_after_an_advance(int holders, int attacks)
    {
        var mortars = Unit("Mortars", "4+", "4+", [], 2, [], Gun("Mortar", "36\"", 1, P("Indirect")));
        mortars = mortars with { Models = [.. mortars.Models.Select((m, i) => i < holders ? m with { Properties = [P(GfNames.QuickThrow)] } : m)] };
        var t = Unit("T", "4+", "6+", [], 1, []);
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(0, -18, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 8), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.BlocksLineOfSight, null))]);
        var sim = Sim.Gf36(State([mortars], [t], x1: -6, x2: 6, terrain: [wall]), Enumerable.Repeat(1, 10).ToArray());
        sim.Start(GfNames.Shooting, new SequenceContext(mortars.Id, [], [], GfNames.Advance));
        if (holders == 0) { Assert.Null(sim.AskForOrNull(Samples.Team1)); return; }
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Mortar"] = t.Id }));
        Assert.Equal(attacks, sim.Steps("determine-attacks").Sum(s => s.Values!["attacks"]));
    }

    // ---- Quick Shot -----------------------------------------------------------------------------------------------------------------------

    static Sim Rush(Unit a, Unit b)
    {
        var sim = Sim.Gf36(State([a], [b], x1: -6, x2: 6), Enumerable.Repeat(1, 20).ToArray());
        sim.Start(GfNames.RushSeq, new SequenceContext(a.Id, [], [], GfNames.Rush));
        while (sim.AskForOrNull(Samples.Team1) is { Question: "move" or AskQuestions.React or AskQuestions.Confirm }) sim.Answer(Samples.Team1, new Passed());
        return sim;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rush_offers_shooting_only_with_quick_shot(bool quick)
    {
        var sim = Rush(Unit("A", "4+", "4+", [], 1, quick ? [P(GfNames.QuickShot)] : [], Gun("Rifle", "24\"", 1)), Unit("B", "4+", "4+", [], 1, []));
        Assert.Equal(quick ? "shoot_or_not" : null, sim.AskForOrNull(Samples.Team1)?.Question);
    }

    [Fact]
    public void After_a_rush_only_the_quick_shot_models_weapons_fire()
    {
        var a = Unit("A", "4+", "4+", [], 2, [], Gun("Rifle", "24\"", 1));
        a = a with { Models = [a.Models[0] with { Properties = [P(GfNames.QuickShot)] }, a.Models[1]] };
        var b = Unit("B", "4+", "4+", [], 1, [P("Tough", 9)]);
        var sim = Rush(a, b);
        sim.Answer(Samples.Team1, new Chosen("shoot"));
        sim.Answer(Samples.Team1, new Assigned(new Dictionary<string, Guid> { ["Rifle"] = b.Id }));
        var fired = sim.Emitted.OfType<SequenceStarted>().Where(s => s.Sequence == GfNames.AttackSequence).SelectMany(s => s.Context.WeaponIds).ToList();
        Assert.Contains(a.Models[0].Weapons[0].Id, fired);
        Assert.Equal(1, sim.Steps("determine-attacks").Sum(s => s.Values!["attacks"]));
    }

    // ---- Assault --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_charged_unit_strikes_back_with_an_assault_weapon()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [P("Tough", 9)], Melee("Claw", 1));
        var raiders = Unit("R", "4+", "4+", [], 1, [], Gun("Pistol", "12\"", 1, P(GfNames.Assault)));
        var sim = Sim.Gf36(State([charger], [raiders], x1: -1.3, x2: 0), Enumerable.Repeat(1, 10).ToArray());
        sim.Start(GfNames.Melee, new SequenceContext(charger.Id, [raiders.Id], GfMelee.MeleeWeapons(sim.State, charger.Id), GfNames.Charge));
        Assert.Equal("strike_back", sim.AskFor(Samples.Team2).Question);
        sim.Answer(Samples.Team2, new Chosen("strike"));
        var back = sim.Emitted.OfType<SequenceStarted>().Last(s => s.Sequence == GfNames.AttackSequence);
        Assert.Equal((raiders.Id, raiders.Models[0].Weapons[0].Id), (back.Context.ActorUnitId!.Value, back.Context.WeaponIds.Single()));
    }
}
