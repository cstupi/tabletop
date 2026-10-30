using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: the four actions and moving (p.7), terrain (p.11), transports (p.15).</summary>
internal static partial class CoreGames
{
    // ---- core-four-actions -------------------------------------------------------------------------------------------------------------

    /// <summary>p.7: the four actions, their moves, the activation order and the round-4 end under strict.</summary>
    public static TestGame FourActions()
    {
        const string name = "core-four-actions";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Seeking(NearestEnemy)
            .Build();
        return Game(name, scenario,
        [
            MoveAllowances(),
            Check("p.7 Hold: \"the models in the unit may not move\"", log =>
                log.Frames.Where(f => f.Sequence == "hold").SelectMany(f => f.Steps.SelectMany(s => Moved(s.Step).Select(m => (f, m))))
                    .FirstOrDefault(x => log.UnitOf(x.m.ModelId) == x.f.Actor) is { f: { } held } ? $"a hold frame {held.Id} moved its unit" : null),
            Check("p.7 Rush: \"Moves 12\" but can't shoot\"", log =>
                log.Frames.FirstOrDefault(f => f.Sequence == "shooting" && log.Frame(f.Parent)?.Sequence == "rush") is { } shot ? $"shooting {shot.Id} under a rush" : null),
            Check("p.7: \"Units may only take a Charge action if at least one model is within charge distance\" (12\")", log =>
                Require(log.Frames.Where(f => f.Sequence == "charge").SelectMany(f => f.Steps).Where(s => s.Step.Step == "charge-target" && s.Step.Values?.ContainsKey("charge_range") == true).ToList(),
                    "no charge was declared",
                    s => s.Step.Values!["charge_range"] <= 1200 ? null : $"a charge declared at {s.Step.Values["charge_range"] / 100.0}\"")),
            Check("p.7: \"the player picks a unit that has not been activated yet\"", log =>
                log.Activations.GroupBy(a => (a.Round, a.Unit)).FirstOrDefault(g => g.Count() > 1) is { } twice ? $"unit {twice.Key.Unit} activated {twice.Count()} times in round {twice.Key.Round}" : null),
            Check("p.7: \"On each new round the player that finished activating first on the last round gets to activate first\"", log =>
                Require(log.Rounds.Where(r => r.Round >= 2).ToList(), "no second round", r =>
                {
                    var last = log.Activations.Where(a => a.Round == r.Round - 1).GroupBy(a => a.Team).Select(g => (Team: g.Key, Last: g.Max(a => a.Seq))).OrderBy(x => x.Last).ToList();
                    return last.Count < 2 || r.Order[0] == last[0].Team ? null : $"round {r.Round} opened with {r.Order[0]}, but {last[0].Team} finished first in round {r.Round - 1}";
                })),
            Check("p.7: \"Players must activate one unit that has not been activated yet\" (strict offers no pass)", log =>
            {
                var asks = log.Events.Select(e => e.Payload).OfType<AskOpened>().Select(o => o.Ask).Where(a => a.Question == AskQuestions.Activate).ToList();
                return asks.Count == 0 ? "no activate ask was opened" : asks.FirstOrDefault(a => a.AllowPass) is { } pass ? $"activate ask {pass.Id} allowed a pass" : null;
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }


    // ---- core-rough-ground -----------------------------------------------------------------------------------------------------------

    // 48" by 36": a difficult bog across the table in front of Blue's zone (y 13–17) and a dangerous minefield in front of Red's (y 19–23).
    const double BogSouth = 13, BogNorth = 17, MinesSouth = 19, MinesNorth = 23;

    /// <summary>p.11 difficult and dangerous terrain: each army must cross a band of it to reach the other.</summary>
    public static TestGame RoughGround()
    {
        const string name = "core-rough-ground";
        var scenario = new ScenarioBuilder(name, seed: 1)
            .RoundCap(4).Table(48, 36)
            .Army("Red", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 3, 3, weapons: [Rifle(), HandWeapon()])
            .Terrain("bog", new Point3(24, (BogSouth + BogNorth) / 2, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(48, BogNorth - BogSouth), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Difficult, null)])
            .Terrain("minefield", new Point3(24, (MinesSouth + MinesNorth) / 2, 0), volumes:
                [new Volume(VolumeKind.Area, Footprint.Rect(48, MinesNorth - MinesSouth), new Point2(0, 0), new Yaw(0), 0, 1, TerrainDeclarations.Dangerous, null)])
            .Seeking(NearestEnemy)
            .Build();
        // A move whose start and end lie on opposite sides of a band, or end in it, went in or through it.
        static bool Crossed(Point2 from, Point2 to, double south, double north) =>
            (to.Y >= south && to.Y <= north) || (from.Y < south && to.Y > north) || (from.Y > north && to.Y < south) || (from.Y >= south && from.Y <= north);
        return Game(name, scenario,
        [
            Check("p.11: \"If any model in a unit moves in or through difficult terrain at any point of its move, then all models in the unit may not move more than 6\" for that movement\"", log =>
            {
                var capped = 0;
                foreach (var f in log.Frames.Where(f => f.Sequence is "rush" or "charge"))
                    foreach (var s in f.Steps.Where(s => s.Step.Step == "move"))
                    {
                        var moves = Moved(s.Step).Where(m => m is { From: OnTable, To: OnTable }).Select(m => (From: ((OnTable)m.From).Placement.Position.Horizontal, To: ((OnTable)m.To).Placement.Position.Horizontal)).ToList();
                        if (!moves.Any(m => Crossed(m.From, m.To, BogSouth, BogNorth))) continue;
                        capped++;
                        if (moves.FirstOrDefault(m => m.From.DistanceTo(m.To) > 6 + 1e-6) is { From: var from, To: var to } && from != to)
                            return $"a {f.Sequence} into the bog moved a model {from.DistanceTo(to):0.00}\" (seq {s.Seq})";
                    }
                return capped > 0 ? null : "no Rush or Charge went into the bog";
            }),
            Check("p.11: \"If a model moves in or through dangerous terrain ... it must take a dangerous terrain test\"; \"if the result is 1, then the unit takes one automatic wound\"", log =>
            {
                foreach (var f in log.Frames.Where(f => f.Sequence is "advance" or "rush" or "charge"))
                {
                    foreach (var s in f.Steps.Where(s => s.Step.Step == "move"))
                        if (Moved(s.Step).Any(m => m is { From: OnTable from, To: OnTable to } && Crossed(from.Placement.Position.Horizontal, to.Placement.Position.Horizontal, MinesSouth, MinesNorth))
                            && !DangerousTestCovers(log, f, s))
                            return $"a {f.Sequence} into the minefield at seq {s.Seq} took no test";
                    if (DangerousTestedTwice(log, f) is { } twice) return twice;
                }
                var tests = log.Frames.Where(f => f.Sequence == "dangerous-terrain").ToList();
                foreach (var t in tests)
                {
                    var ones = t.Steps.SelectMany(s => s.Step.Rolls).Where(r => r.Roll == "dangerous-terrain").Sum(r => log.Dice(r).Count(d => d.Raw == 1));
                    var removed = log.CasualtiesUnder(t);
                    if (removed != Math.Min(ones, log.LivingModels(t.Actor!.Value, t.StartSeq))) return $"a dangerous terrain test with {ones} one(s) removed {removed}";
                }
                var rolledOne = tests.Select(t => t.Steps.SelectMany(s => s.Step.Rolls).Any(r => r.Roll == "dangerous-terrain" && log.Dice(r).Any(d => d.Raw == 1))).ToList();
                return rolledOne.Contains(true) && rolledOne.Contains(false) ? null : "the logged dangerous terrain tests did not both roll a 1 and not";
            }),
        ]);
    }

    // ---- core-transport-run ----------------------------------------------------------------------------------------------------------

    /// <summary>Red's infantry heads for its own transport; everything else heads for the nearest enemy.</summary>
    static Point2? ToTheTransport(Guid modelId, PolicyView view)
    {
        var unit = view.UnitOf(view.ModelOf(modelId).UnitId);
        if (view.TeamOf(unit.Id) is not { } team || unit.Definition.Models.Any(IsTransport) || !unit.Definition.Name.StartsWith("Red "))
            return NearestEnemy(modelId, view);
        return view.Projection.Models.Values.Where(m => IsTransport(m.Slot) && view.TeamOf(m.UnitId) == team)
            .Select(m => view.PlacedOf(m.Id)?.Placement.Position.Horizontal).FirstOrDefault(p => p is not null);
    }

    static bool IsTransport(ModelSlot slot) => slot.Properties.Any(p => p.Name == "Transport");

    /// <summary>p.15 Transport(X): Red's two single-model infantry units ride a Tough(3) transport that Blue's squads shoot at.</summary>
    public static TestGame TransportRun()
    {
        const string name = "core-transport-run";
        var scenario = new ScenarioBuilder(name, seed: 31)
            .RoundCap(4).Table(24, 36)
            .Army("Red", 3, 1, modelRules: [new ScenarioRule(1, "Transport", 6), new ScenarioRule(1, "Tough", 3)], weapons: [Rifle(), HandWeapon()],
                unitStats: new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "5+" })
            .Army("Blue", 3, 3, weapons: [Rifle(2), HandWeapon()])
            .Seeking(ToTheTransport)
            // Blue Advances and shoots rather than charging (a charge into base contact, p.9, would end the infantry before it rides).
            // and Red's infantry boards the transport whenever a move reaches it (p.15 "units may enter/exit by using any move action").
            .Scripted(AnswerScript.Build()
                .Match(AskQuestions.Activate, (ask, _) => AskPicks.Option(ask, c => c.Label.StartsWith("Blue ", StringComparison.Ordinal) && c.Label.EndsWith(": Advance", StringComparison.Ordinal)))
                .Match("move", (ask, p) => ask.Legal is MoveRegion { Containment.Enterable: [var transport, ..] } m
                    ? new Destinations([.. m.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(transport)))]) : null)
                .Build())
            .Build();
        return Game(name, scenario,
        [
            Check("p.15: units \"must stay fully within 6\" of it when exiting\"", log =>
                Require(log.Exits().ToList(), "no unit disembarked", exit =>
                    exit.Within ? null : $"a model left the transport at seq {exit.Seq} and stood outside 6\" of it"), "Transport"),
            Check("p.15: \"Units inside/outside transports may not target other units outside/inside transports, but may target the transport itself\"", log =>
            {
                foreach (var attack in log.Frames.Where(f => f.Sequence == "attack-sequence" && f.Actor is not null))
                {
                    var at = log.LocationsBefore(attack.StartSeq);
                    bool Inside(Guid unit) => at.Any(kv => log.UnitOf(kv.Key) == unit && kv.Value is Inside);
                    var target = attack.Targets[0];
                    var isTransport = log.Run.Final.Units[target].Definition.Models.Any(IsTransport);
                    if (Inside(attack.Actor!.Value) != Inside(target) && !isTransport) return $"an attack at seq {attack.StartSeq} crossed a transport's hull";
                }
                return null;
            }, "Transport"),
            Check("p.15: \"units can't both embark/disembark as part of the same activation\"", log =>
            {
                foreach (var turn in log.Turns)
                {
                    var moves = log.Frames.Where(f => f.StartSeq > turn.Start && f.StartSeq < turn.End).SelectMany(f => f.Steps.Where(s => s.Step.Step == "move")).SelectMany(s => Moved(s.Step)).ToList();
                    var embarked = moves.Where(m => m is { From: OnTable, To: Inside }).Select(m => log.UnitOf(m.ModelId)).ToHashSet();
                    if (moves.FirstOrDefault(m => m is { From: Inside, To: OnTable } && embarked.Contains(log.UnitOf(m.ModelId))) is { } both) return $"a unit embarked and disembarked in the turn at seq {turn.Start}";
                }
                // One unit that embarked by a move and, in a later activation, disembarked by a move.
                var moved = log.Frames.Where(f => f.Sequence is "advance" or "rush" or "charge").SelectMany(f => f.Steps.Where(s => s.Step.Step == "move"))
                    .SelectMany(s => Moved(s.Step).Select(m => (s.Seq, Unit: log.UnitOf(m.ModelId), m.From, m.To))).ToList();
                return moved.Any(e => e is { From: OnTable, To: Inside } && moved.Any(x => x.Unit == e.Unit && x.Seq > e.Seq && x is { From: Inside, To: OnTable }))
                    ? null : "no unit embarked and, later, disembarked";
            }, "Transport"),
            Check("p.15: \"When a transport is destroyed, units inside must take a dangerous terrain test, are Shaken, and must be placed fully within 6\" of the transport before it's removed\"", log =>
                Require(log.Frames.Where(f => f.Sequence == "transport-destroyed").ToList(), "no transport was destroyed with anybody inside", wreck =>
                {
                    var unit = wreck.Actor!.Value;
                    if (!log.Children(wreck).Any(c => c.Sequence is "dangerous-terrain" or "dangerous-terrain-36")) return "the occupants took no dangerous terrain test";
                    var place = wreck.Steps.FirstOrDefault(s => s.Step.Step == "place");
                    if (place is null) return "the occupants were never placed";
                    if (log.LivingModels(unit, place.Seq) > 0 && !place.Step.Changes.OfType<ConditionSet>().Any(c => c is { Condition: "shaken", Present: true } && c.Subject.Id == unit)) return "the occupants were not Shaken";
                    if (log.Exits().Any(e => e.Seq == place.Seq && !e.Within)) return "an occupant was placed outside 6\" of the wreck";
                    var removed = log.Moves.FirstOrDefault(m => m.Seq >= place.Seq && IsTransport(log.Run.Final.Models[m.Move.ModelId].Slot) && m.Move.To is OnShelf);
                    return removed.Move is null ? "the wreck was never removed" : null;
                }), "Transport"),
        ], covers: ["Transport"]);
    }
}
