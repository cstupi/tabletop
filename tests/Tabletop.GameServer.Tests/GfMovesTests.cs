using Tabletop.Content;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Dispatch;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Properties;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;
using static Tabletop.GameServer.Tests.Infrastructure.Armies;
using Refused = Tabletop.GameServer.Dispatch.Refused;

namespace Tabletop.GameServer.Tests;

public class GfMovesTests
{
    // Armies.State puts team1's first unit at x1, its models at y -18.5 and -17. The patch lies across the first model's path only.
    static TerrainPlacement Patch(TerrainDeclarations kind, double x = -17, double y = -19, double width = 2, double length = 1) =>
        new(Guid.CreateVersion7(), VisualRef.OfBuiltin("patch"), new Point3(x, y, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Area, Footprint.Rect(width, length), new Point2(0, 0), new Yaw(0), 0, 1, kind, null))]);

    static Sim Rush(Enforcement enforcement, PropertyInstance[] unitRules, out Unit unit)
    {
        unit = Unit("A", "4+", "4+", unitRules, 2, []);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, enforcement: enforcement, terrain: [Patch(TerrainDeclarations.Difficult)]), 4);
        sim.Start(GfNames.RushSeq, new SequenceContext(unit.Id, [], [], GfNames.Rush));
        return sim;
    }

    static ModelRelocation Shifted(Sim sim, Guid model, double dx)
    {
        var at = ((OnTable)sim.Model(model).Location).Placement;
        return new ModelRelocation(model, sim.Model(model).Location, new OnTable(at with { Position = at.Position with { X = at.Position.X + dx } }));
    }

    static DispatchOutcome Move(Sim sim, params ModelRelocation[] moves) =>
        sim.Dispatch(new AnswerAsk(sim.AskFor(Samples.Team1).Id, new Destinations(moves)), Samples.P1, Samples.Team1);

    [Fact]
    public void Under_strict_a_rush_whose_one_model_entered_difficult_ground_holds_every_model_to_six_inches()
    {
        // p.11: "If any model in a unit moves in or through difficult terrain ... then all models in the unit may not move more than 6"".
        var sim = Rush(Enforcement.Strict, [], out var unit);
        var outcome = Move(sim, Shifted(sim, unit.Models[0].Id, 5), Shifted(sim, unit.Models[1].Id, -9));
        var refused = Assert.IsType<Refused>(outcome);
        Assert.Equal(RefusalReason.Illegal, refused.Reason);
        Assert.Contains("6\"", refused.Detail);
        var ask = sim.AskFor(Samples.Team1);
        Assert.Equal(GfNames.DifficultCapRule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, ask,
            new Destinations([Shifted(sim, unit.Models[0].Id, 5), Shifted(sim, unit.Models[1].Id, -9)]))!.Rule);
        Assert.IsType<Applied>(Move(sim, Shifted(sim, unit.Models[0].Id, 5), Shifted(sim, unit.Models[1].Id, -6)));
    }

    [Fact]
    public void Under_warn_the_held_rush_is_applied_with_a_warning()
    {
        var sim = Rush(Enforcement.Warn, [], out var unit);
        var applied = Assert.IsType<Applied>(Move(sim, Shifted(sim, unit.Models[0].Id, 5), Shifted(sim, unit.Models[1].Id, -9)));
        Assert.NotNull(applied.Warning);
        var answered = applied.Events.OfType<AskAnswered>().Single();
        Assert.False(answered.OutsideLegalSet);   // inside the legal set: it broke a named rule
        Assert.Equal(GfNames.DifficultCapRule, answered.Breach);
    }

    [Fact]
    public void A_rush_on_open_ground_and_a_strider_rush_through_difficult_ground_are_not_held()
    {
        var open = Rush(Enforcement.Strict, [], out var unit);
        Assert.IsType<Applied>(Move(open, Shifted(open, unit.Models[0].Id, -9), Shifted(open, unit.Models[1].Id, -9)));
        var strider = Rush(Enforcement.Strict, [P("Strider")], out var striders);
        Assert.IsType<Applied>(Move(strider, Shifted(strider, striders.Models[0].Id, 9), Shifted(strider, striders.Models[1].Id, -9)));
    }

    // ---- A move a cast splits (p.11 "for that movement") ------------------------------------------------------------------------------

    static readonly EntryDocument Ward = new("Ward", CatalogKind.Spell, false, null, 1, "Test Book", "Target up to two friendly units within 24\" get Regeneration once.",
        new SpellTargeting(TargetSide.Friendly, TargetGrain.Unit, 2, 24),
        [new EffectDocument(EffectSubject.Self, 10, EffectKind.GrantProperty, null, null, "Regeneration", null, null, null, null, null, false, null, null, null, EffectDuration.Once, [], [])]);

    /// <summary>A lone caster rushing under strict, with a bog at the given x; its casting die is a 1, so each cast fails and the move resumes.</summary>
    static Sim CasterRush(double bogX, out Unit wizard)
    {
        wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = new Sim(State([wizard], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, enforcement: Enforcement.Strict,
            terrain: [Patch(TerrainDeclarations.Difficult, x: bogX, y: -18.5, width: 2, length: 2)]), GrimdarkFutureRules.Instance, TestProperties.WithSpells(Ward), new ScriptedDice(1));
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3)]);
        sim.Start(GfNames.RushSeq, new SequenceContext(wizard.Id, [], [], GfNames.Rush));
        return sim;
    }

    /// <summary>Answers the casting checkpoint with a Ward that fails, until the move Asks again.</summary>
    static void CastAndFail(Sim sim)
    {
        for (var i = 0; i < 10 && sim.AskFor(Samples.Team1).Question != "move"; i++)
        {
            var ask = sim.AskFor(Samples.Team1);
            sim.Answer(Samples.Team1, ask.Legal switch
            {
                Options o when ask.Question == "cast" => new Chosen(o.Choices.First(c => c.Id.StartsWith("Ward|")).Id),
                Options o => new Chosen(o.Choices[0].Id),
                _ => new Passed(),
            });
        }
        Assert.Equal("move", sim.AskFor(Samples.Team1).Question);
    }

    [Fact]
    public void After_a_cast_a_move_that_went_into_difficult_ground_has_only_what_is_left_of_six_inches()
    {
        // 5" into the bog, a cast, then at most 1" more: "all models in the unit may not move more than 6" for that movement".
        var sim = CasterRush(-14, out var wizard);
        Assert.IsType<Applied>(Move(sim, Shifted(sim, wizard.Models[0].Id, 5)));
        sim.Run();
        CastAndFail(sim);
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((1.0, 1.0), (region.Allowance, region.DifficultCap!.Value));
        Assert.IsType<Refused>(Move(sim, Shifted(sim, wizard.Models[0].Id, -3)));                     // back out over open ground: still the same move
        Assert.IsType<Applied>(Move(sim, Shifted(sim, wizard.Models[0].Id, -1)));
    }

    [Fact]
    public void After_a_cast_a_move_past_six_inches_on_open_ground_may_not_enter_difficult_ground()
    {
        // 9" on open ground, a cast, then 2" into the bog behind it: the move went in difficult terrain at 11".
        var sim = CasterRush(-31.5, out var wizard);
        Assert.IsType<Applied>(Move(sim, Shifted(sim, wizard.Models[0].Id, -9)));
        sim.Run();
        CastAndFail(sim);
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal((3.0, 0.0), (region.Allowance, region.DifficultCap!.Value));
        Assert.IsType<Refused>(Move(sim, Shifted(sim, wizard.Models[0].Id, -2)));
        Assert.IsType<Applied>(Move(sim, Shifted(sim, wizard.Models[0].Id, 2)));
    }

    // ---- Aircraft (p.13) ---------------------------------------------------------------------------------------------------------------

    static Sim Fly(TerrainPlacement? terrain, out Unit aircraft, params int[] dice)
    {
        aircraft = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var sim = Sim.Gf(State([aircraft], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 30, terrain: terrain is null ? null : [terrain]), dice.Length == 0 ? [4] : dice);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(aircraft.Id, [], [], GfNames.Advance));
        return sim;
    }

    [Fact]
    public void An_aircraft_advance_adds_thirty_inches_and_ignores_terrain()
    {
        // p.13: "adding 30" to its total move"; "Aircraft ignore all units and terrain when moving and stopping".
        var sim = Fly(Patch(TerrainDeclarations.Difficult | TerrainDeclarations.Impassable, width: 2, length: 4), out var jet);
        var region = Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal);
        Assert.Equal(36, region.Allowance);
        Assert.Empty(region.ImpassableVolumeIds);
        Assert.Empty(region.DifficultVolumeIds);
        Assert.Null(region.DifficultCap);
        Assert.Equal(30, region.MinDistance);
        Assert.False(sim.AskFor(Samples.Team1).AllowPass);                                           // "their mandatory move"
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, jet.Models[0].Id, 30)]));
        Assert.Contains(GfNames.Aircraft, sim.Steps("move").SelectMany(s => s.Applied ?? []));
    }

    [Fact]
    public void An_aircraft_takes_no_dangerous_terrain_test_moving_over_it_or_activated_in_it()
    {
        var sim = Fly(Patch(TerrainDeclarations.Dangerous, x: -20, y: -18.5, width: 4, length: 4), out var jet, 1);
        sim.Answer(Samples.Team1, new Destinations([Shifted(sim, jet.Models[0].Id, 30)]));
        Assert.Empty(sim.Steps("test"));
    }

    [Fact]
    public void Under_strict_an_aircraft_that_moves_less_than_thirty_inches_is_refused()
    {
        // p.13: "Aircraft can't move into the table edges to move less than 30"".
        var jet = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var sim = Sim.Gf(State([jet], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict), 4);
        var id = jet.Models[0].Id;
        sim.Append([new ModelsMoved([new ModelRelocation(id, sim.Model(id).Location, new OnTable(new Placement(new Point3(10, 24, 0), new Yaw(0))))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(jet.Id, [], [], GfNames.Advance));
        var refused = Assert.IsType<Refused>(Move(sim, Shifted(sim, id, 20)));
        Assert.Equal(RefusalReason.Illegal, refused.Reason);
        Assert.Equal(GfNames.AircraftMinimumRule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations([Shifted(sim, id, 20)]))!.Rule);
        Assert.IsType<Refused>(Move(sim));                                                            // left out: it did not move
        Assert.IsType<Applied>(Move(sim, Shifted(sim, id, 32)));
    }

    [Fact]
    public void An_aircraft_the_table_has_no_room_for_must_move_as_far_as_the_table_lets_it()
    {
        // In the middle of a 30" square table the farthest inset corner is under 21" away: that is the minimum, not 30".
        var jet = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var sim = Sim.Gf(State([jet], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict, size: new TableSize(30, 30)), 4);
        var id = jet.Models[0].Id;
        sim.Append([new ModelsMoved([new ModelRelocation(id, sim.Model(id).Location, new OnTable(new Placement(new Point3(15, 15, 0), new Yaw(0))))])]);
        var (reach, corner) = Movement.FarthestOnTable(Battlefield.Placed(sim.State, id)!.Value, sim.State.Board.Table.Size);
        Assert.InRange(reach, 19, 21);
        var region = new MoveRegion([id], 36, [], [], null, null, 30);
        ModelRelocation To(Point2 p) => new(id, sim.Model(id).Location, new OnTable(new Placement(new Point3(p.X, p.Y, 0), new Yaw(0))));
        Assert.Null(GfMoves.MinimumBreach(sim.State, region, new Destinations([To(corner)])));
        Assert.NotNull(GfMoves.MinimumBreach(sim.State, region, new Destinations([To(new Point2(20, 15))])));
    }

    [Fact]
    public void An_aircraft_is_never_a_charge_target()
    {
        // p.13: Aircraft "can't be charged".
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Claws", 1));
        var jet = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var walker = Unit("Walker", "4+", "4+", [], 1, []);
        var state = State([charger], [jet, walker], x1: -2, x2: 2);
        Assert.Equal([walker.Id], GfMelee.ChargeTargets(state, TestProperties.Core, charger.Id));
    }

    // ---- One reading of a move answer: what the breach checks judge is what the step applies ------------------------------------------

    static Sim Jet(Enforcement enforcement, out Unit jet, params Unit[] others)
    {
        jet = Unit("Jet", "4+", "4+", [P(GfNames.Aircraft)], 1, []);
        var sim = Sim.Gf(State([jet, .. others], [Unit("B", "4+", "4+", [], 1, [])], enforcement: enforcement), 4);
        var id = jet.Models[0].Id;
        sim.Append([new ModelsMoved([new ModelRelocation(id, sim.Model(id).Location, new OnTable(new Placement(new Point3(10, 24, 0), new Yaw(0))))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(jet.Id, [], [], GfNames.Advance));
        return sim;
    }

    static ModelRelocation At(Sim sim, Guid model, double x, double y) =>
        new(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))));

    [Fact]
    public void Under_strict_a_duplicated_model_a_stranger_and_a_non_finite_placement_are_outside_the_legal_set()
    {
        var sim = Jet(Enforcement.Strict, out var jet, Unit("Other", "4+", "4+", [], 1, []));
        var id = jet.Models[0].Id;
        var stranger = sim.State.Projection.Models.Values.First(m => m.UnitId != jet.Id).Id;
        Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(Move(sim, Shifted(sim, id, 2), Shifted(sim, id, 32))).Reason);
        Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(Move(sim, Shifted(sim, id, 32), At(sim, stranger, 40, 24))).Reason);
        Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(Move(sim, At(sim, id, double.NaN, 24))).Reason);
        Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(Move(sim, At(sim, id, double.PositiveInfinity, 24))).Reason);
        var yawless = Shifted(sim, id, 32) is { To: OnTable on } r ? r with { To = new OnTable(on.Placement with { Yaw = new Yaw(double.NaN) }) } : null!;
        Assert.Equal(RefusalReason.OutsideLegalSet, Assert.IsType<Refused>(Move(sim, yawless)).Reason);
        Assert.IsType<Applied>(Move(sim, Shifted(sim, id, 32)));
    }

    [Fact]
    public void Under_warn_a_duplicated_model_is_judged_and_applied_by_its_first_relocation()
    {
        // The check and the step read one relocation per model, the first: a short first and a long second is judged short, and moves short.
        var sim = Jet(Enforcement.Warn, out var jet);
        var id = jet.Models[0].Id;
        var x = ((OnTable)sim.Model(id).Location).Placement.Position.X;
        var answer = new Destinations([Shifted(sim, id, 2), Shifted(sim, id, 32)]);
        Assert.Equal(GfNames.AircraftMinimumRule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), answer)!.Rule);
        Assert.NotNull(Assert.IsType<Applied>(Move(sim, [.. answer.Moves])).Warning);
        sim.Run();
        Assert.Equal(x + 2, ((OnTable)sim.Model(id).Location).Placement.Position.X, 6);
    }

    [Fact]
    public void A_placement_the_reading_drops_is_reported_and_is_not_applied()
    {
        // Under warn a non-finite or impossibly far placement is accepted with a warning (move_not_offered); the step does not apply it.
        foreach (var bad in new[] { double.NaN, 1e9 })
        {
            var sim = Jet(Enforcement.Warn, out var jet);
            var id = jet.Models[0].Id;
            var before = sim.Model(id).Location;
            var answer = new Destinations([At(sim, id, bad, 24)]);
            Assert.Equal(GfNames.MoveNotOfferedRule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), answer)!.Rule);
            Move(sim, [.. answer.Moves]);
            sim.Run();
            Assert.Equal(before, sim.Model(id).Location);
        }
        var strict = Jet(Enforcement.Strict, out var far);
        Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(strict, At(strict, far.Models[0].Id, 1e9, 24))).Reason);
    }

    [Fact]
    public void A_unit_that_embarked_cannot_step_out_on_a_later_segment_that_offers_no_exit()
    {
        // p.15: "units can't both embark/disembark as part of the same activation". After a cast the second segment offers no exit;
        // a relocation out of the transport anyway is not applied.
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var wizard = Unit("Wizard", "4+", "4+", [], 1, [P("Caster", 3)]);
        var sim = new Sim(State([bus, wizard], [Unit("B", "4+", "4+", [], 1, [])], x1: -20, x2: 20, enforcement: Enforcement.Warn),
            GrimdarkFutureRules.Instance, TestProperties.WithSpells(Ward), new ScriptedDice(1));
        var container = bus.Models[0].Id;
        sim.Append([new SpellTokensSet(wizard.Models[0].Id, 3), new ModelsMoved([At(sim, container, 36, 24), At(sim, wizard.Models[0].Id, 36, 28)])]);
        sim.Start(GfNames.RushSeq, new SequenceContext(wizard.Id, [], [], GfNames.Rush));
        Assert.Contains(container, Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Containment!.Enterable);
        Assert.IsType<Applied>(Move(sim, new ModelRelocation(wizard.Models[0].Id, sim.Model(wizard.Models[0].Id).Location, new Inside(container))));
        sim.Run();
        CastAndFail(sim);
        Assert.Null(Assert.IsType<MoveRegion>(sim.AskFor(Samples.Team1).Legal).Containment);
        var seat = ((OnTable)sim.Model(container).Location).Placement.Position;
        Move(sim, new ModelRelocation(wizard.Models[0].Id, new Inside(container), new OnTable(new Placement(seat with { X = seat.X + 3 }, new Yaw(0)))));
        sim.Run();
        Assert.Equal(new Inside(container), sim.Model(wizard.Models[0].Id).Location);
    }

    // ---- A move answer's geometry under strict (p.7, p.11, p.15) -----------------------------------------------------------------------

    static void Place(Sim sim, Guid model, double x, double y) =>
        sim.Append([new ModelsMoved([new ModelRelocation(model, sim.Model(model).Location, new OnTable(new Placement(new Point3(x, y, 0), new Yaw(0))))])]);

    static void RefusedFor(Sim sim, string rule, params ModelRelocation[] moves)
    {
        Assert.Equal(rule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, sim.AskFor(Samples.Team1), new Destinations(moves))?.Rule);
        Assert.Equal(RefusalReason.Illegal, Assert.IsType<Refused>(Move(sim, moves)).Reason);
    }

    [Fact]
    public void Under_strict_an_advance_over_its_allowance_through_impassable_terrain_or_off_the_table_is_refused()
    {
        var unit = Unit("A", "4+", "4+", [], 2, []);
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(24, 24, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 10), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null))]);
        var sim = Sim.Gf(State([unit], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict, terrain: [wall]), 4);
        var (a, b) = (unit.Models[0].Id, unit.Models[1].Id);
        Place(sim, a, 20, 24); Place(sim, b, 2, 10);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(unit.Id, [], [], GfNames.Advance));
        RefusedFor(sim, GfNames.MoveAllowanceRule, At(sim, a, 12, 24));                               // p.7: 8" on a 6" Advance
        RefusedFor(sim, GfNames.MoveImpassableRule, At(sim, a, 25.5, 24));                             // p.11: through the wall
        RefusedFor(sim, GfNames.MoveOffTableRule, At(sim, b, -1, 10));                                 // p.7: off the table edge
        Assert.IsType<Applied>(Move(sim, At(sim, a, 15, 24), At(sim, b, 6, 10)));
    }

    [Fact]
    public void Under_strict_a_charge_and_the_defenders_close_in_are_held_to_their_allowances()
    {
        var charger = Unit("C", "4+", "4+", [], 1, [], Melee("Fist", 1));
        var target = Unit("T", "4+", "4+", [], 2, [], Melee("Fist", 1));
        var sim = Sim.Gf(State([charger], [target], enforcement: Enforcement.Strict), 1);
        Place(sim, charger.Models[0].Id, 20, 24); Place(sim, target.Models[0].Id, 28, 24); Place(sim, target.Models[1].Id, 28, 28);
        sim.Start(GfNames.ChargeSeq, new SequenceContext(charger.Id, [], [], GfNames.Charge));
        sim.Answer(Samples.Team1, new SelectedPieces([target.Id]));
        RefusedFor(sim, GfNames.MoveAllowanceRule, At(sim, charger.Models[0].Id, 6, 24));             // 14" on a 12" charge
        Assert.IsType<Applied>(Move(sim, At(sim, charger.Models[0].Id, 26.74, 24)));
        sim.Run();
        // The defender's free model may close in 3" (p.9): 5" is refused, 2" is applied.
        var free = target.Models[1].Id;
        var close = sim.AskFor(Samples.Team2);
        Assert.Equal(GfNames.MoveAllowanceRule, GrimdarkFutureRules.Instance.AnswerBreach(sim.State, close, new Destinations([At(sim, free, 28, 23)]))?.Rule);
        Assert.IsType<Refused>(sim.Dispatch(new AnswerAsk(close.Id, new Destinations([At(sim, free, 28, 23)])), P2, Samples.Team2));
        Assert.IsType<Applied>(sim.Dispatch(new AnswerAsk(close.Id, new Destinations([At(sim, free, 28, 26)])), P2, Samples.Team2));
    }

    [Fact]
    public void Under_strict_a_disembark_outside_six_inches_and_a_forced_move_through_a_wall_are_refused()
    {
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var riders = Unit("Riders", "4+", "4+", [], 1, []);
        var sim = Sim.Gf(State([bus, riders], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict), 4);
        var container = bus.Models[0].Id;
        Place(sim, container, 36, 24);
        sim.Append([new ModelsMoved([new ModelRelocation(riders.Models[0].Id, sim.Model(riders.Models[0].Id).Location, new Inside(container))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(riders.Id, [], [], GfNames.Advance));
        ModelRelocation Out(double x) => new(riders.Models[0].Id, new Inside(container), new OnTable(new Placement(new Point3(x, 24, 0), new Yaw(0))));
        RefusedFor(sim, GfNames.MoveExitRule, Out(48));                                                  // p.15: 12" out
        Assert.IsType<Applied>(Move(sim, Out(40)));

        // A spell's forced move carries the same parameters (GfCasting): the same judge reads it.
        var wall = new TerrainPlacement(Guid.CreateVersion7(), VisualRef.OfBuiltin("wall"), new Point3(24, 10, 0), new Yaw(0), 1,
            [new TerrainVolume(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(1, 10), new Point2(0, 0), new Yaw(0), 0, 4, TerrainDeclarations.Impassable, null))]);
        var pushed = Unit("P", "4+", "4+", [], 1, []);
        var other = Sim.Gf(State([pushed], [Unit("B", "4+", "4+", [], 1, [])], enforcement: Enforcement.Strict, terrain: [wall]), 4);
        Place(other, pushed.Models[0].Id, 22, 10);
        var region = new MoveRegion([pushed.Models[0].Id], 6, [wall.Volumes[0].Id], [], GfNames.DifficultCap);
        Assert.Equal(GfNames.MoveImpassableRule, GfMoves.PathBreach(other.State, region, new Destinations([At(other, pushed.Models[0].Id, 26, 10)]))?.Rule);
        Assert.Null(GfMoves.PathBreach(other.State, region, new Destinations([At(other, pushed.Models[0].Id, 18, 10)])));
    }

    // ---- Leaving a transport (p.7, p.15) -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_disembark_placement_with_part_of_the_base_off_the_table_is_refused()
    {
        // p.7: "models may also never move outside of the battlefield (no part of them, at any point)".
        var bus = Unit("Bus", "4+", "4+", [], 1, [P("Transport", 11)]);
        var riders = Unit("Riders", "4+", "4+", [], 2, []);
        var sim = Sim.Gf(State([bus, riders], [Unit("B", "4+", "4+", [], 1, [])]), 4);
        var container = bus.Models[0].Id;
        var seat = new Placement(new Point3(3, 24, 0), new Yaw(0));
        sim.Append([new ModelsMoved([new ModelRelocation(container, sim.Model(container).Location, new OnTable(seat)),
            .. riders.Models.Select(m => new ModelRelocation(m.Id, sim.Model(m.Id).Location, new Inside(container)))])]);
        sim.Start(GfNames.AdvanceSeq, new SequenceContext(riders.Id, [], [], GfNames.Advance));
        sim.Answer(Samples.Team1, new Destinations([
            new ModelRelocation(riders.Models[0].Id, new Inside(container), new OnTable(seat with { Position = seat.Position with { X = 0.5 } })),   // within 6", half off the edge
            new ModelRelocation(riders.Models[1].Id, new Inside(container), new OnTable(seat with { Position = seat.Position with { X = 6 } })),
        ]));
        Assert.IsType<Inside>(sim.Model(riders.Models[0].Id).Location);
        Assert.IsType<OnTable>(sim.Model(riders.Models[1].Id).Location);
    }
}
