using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's 3.6.0 aircraft and transport game (3.6.0 pp.13–15).</summary>
static partial class SpecialGames
{
    // ---- special-aircraft-overhead@3.6 -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Aircraft, Transport. Blue unit 1 is one Aircraft with Transport(6) at (3, 3), Blue unit 3 one ground Transport(6) at (14, 4), and Blue unit
    /// 2 (two models from (7, 7), within 6" of both) Advances first and embarks in the ground Transport; the Aircraft Advances last, 35" to (33, 21). Red unit 1 (three models, 36"
    /// rifles) Holds and shoots the Aircraft, about 18" away; its hit roll is fixed to [5,4].
    /// </summary>
    static TestGame Aircraft36()
    {
        const string name = "special-aircraft-overhead";
        var script = Plays((ask, _) => AskPicks.Activate(ask, "Blue unit 2", "Advance") ?? AskPicks.Activate(ask, "Blue unit 3", "Hold") ?? AskPicks.Activate(ask, "Blue unit 1", "Advance")
                ?? AskPicks.Activate(ask, "Red unit 1", "Hold"),
            ("move", (ask, p) => ask.Legal is MoveRegion { Containment.Enterable: { Count: > 0 } into } region && Scripts36.UnitOf(p, region) == "Blue unit 2"
                && into.FirstOrDefault(id => AskPicks.UnitNameOf(p, id) == "Blue unit 3") is var ground && ground != Guid.Empty
                ? new Destinations([.. region.ModelIds.Select(id => new ModelRelocation(id, p.Models[id].Location, new Inside(ground)))])
                : ask.Legal is MoveRegion aircraft && Scripts36.UnitOf(p, aircraft) == "Blue unit 1" ? new Destinations([Scripts36.MoveTo(p, aircraft.ModelIds[0], new(33, 21))]) : null),
            ("declare_targets", (ask, p) => ShooterOf(ask, p) == "Red unit 1" ? AllAt(ask, p, "Blue unit 1") : null));
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(1).Table(36, 24).Scripted(script)
                .Army("Red", 1, 3, weapons: [Gun("Rifle", 36, 1)], unitStats: Stats(4, 4), anchors: new Dictionary<int, Point2> { [1] = new(12, 20) })
                .Army("Blue", 3, 1, unitRules: [new(1, "Aircraft")], modelRules: [new(1, "Transport", 6), new(1, "Tough", 6), new(3, "Transport", 6), new(3, "Tough", 6)],
                    weapons: [Blade("Bayonet", 1)], unitStats: Stats(4, 4), sizes: new Dictionary<int, int> { [2] = 2 },
                    anchors: new Dictionary<int, Point2> { [1] = new(3, 3), [2] = new(7, 7), [3] = new(14, 4) })
                .Build(),
            ["Aircraft", "Transport"],
            [
                Exact("3.6.0 p.13 Aircraft: \"units targeting them get -12” range and -1 to hit rolls\" (at 4+ a raw 5 hits, a raw 4 misses)", ["Aircraft"], run =>
                {
                    var rolls = Attacks(run).Where(a => a.Target == "Blue unit 1").Select(HitRoll).OfType<RollRecord>().ToList();
                    return rolls.FirstOrDefault(r => r.Modifiers.Count(m => m.Source == "Aircraft" && m.Amount == -1) != 1) is { } wrong ? $"a shot at the Aircraft {Show(wrong)}"
                        : Judged(rolls, "a shot at the Aircraft", 0, -1);
                }),
                Exact("3.6.0 p.15 Transport(X): \"If the Transport is unreachable (for example because it's flying), then units can't embark in it\" (the ground Transport offered, the Aircraft not)", ["Transport"], run =>
                {
                    var (aircraft, ground) = (run.Final.Models.Values.Single(m => UnitOf(run, m.Id) == "Blue unit 1").Id, run.Final.Models.Values.Single(m => UnitOf(run, m.Id) == "Blue unit 3").Id);
                    return Each(Moves(run).Where(m => m.Own && m.Unit == "Blue unit 2"), "a move by the infantry beside both Transports", m =>
                        m.Region.Containment?.Enterable is not { } into ? "no Transport was offered to enter"
                        : into.Contains(aircraft) ? "the Aircraft Transport was offered to enter" : into.Contains(ground) ? null : "the ground Transport was not offered")
                        ?? (Steps(run).Any(s => s.Step.Changes.OfType<ModelsMoved>().Any(mm => mm.Moves.Any(x => x.To is Inside i && i.ContainerId == ground))) ? null : "nobody embarked in the ground Transport");
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Aircraft"] = log => OnlyOffered(log, "Blue unit 1", "Advance") },
            dice: [new("hit", 0, [5, 4], new RollKey(Actor: "Red unit 1", Target: "Blue unit 1"))]);
    }
}
