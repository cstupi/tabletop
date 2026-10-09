using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's deployment game (pp.13–15).</summary>
static partial class SpecialGames
{
    /// <summary>
    /// Scout, Ambush, Aircraft, Hero. Scout and Ambush need a unit set aside before deployment, and Hero a hero joined to a unit without the
    /// rule; a sim scenario can express neither yet, so their checks stand failing until it can. Aircraft's move and "can't be charged" are
    /// engine defects. Red unit 1 scouts, Red unit 2 and Blue unit 2 ambush, Red unit 3 holds the Hero, Blue unit 1 is the Aircraft.
    /// </summary>
    static TestGame Deployment()
    {
        const string name = "special-scouts-and-ambushers";
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(3).Table(36, 36).Scripted(ShootAndStrike)
                .Army("Red", 3, 3, unitRules: [new(1, "Scout"), new(2, "Ambush")], modelRules: [new(3, "Hero")], unitStats: Stats(4, 4))
                .Army("Blue", 2, 2, unitRules: [new(1, "Aircraft"), new(2, "Ambush")], unitStats: Stats(4, 4))
                .Build(),
            ["Scout", "Ambush", "Aircraft", "Hero"],
            [
                Exact("p.14 Scout: \"After all other units are deployed, may be deployed anywhere fully within 12\" of their deployment zone\"", ["Scout"], run =>
                {
                    var asks = Payloads(run).OfType<AskOpened>().Select(a => a.Ask.Question).ToList();
                    if (!asks.Contains(AskQuestions.DeployScouts)) return "no Scout was deployed";
                    if (asks.IndexOf(AskQuestions.DeployScouts) < asks.LastIndexOf(AskQuestions.Deploy)) return "a Scout deployed before the other units";
                    var zone = ZoneBounds(run, teamOrdinal: 1);
                    return Each(Arrivals(run, "Red unit 1"), "a Scout placement", a =>
                    {
                        var reach = OutsideBy(zone, a.At) + Radius(run, a.Model);
                        return reach <= 12.01 ? null : $"{ModelName(run, a.Model)} placed {reach:0.00}\" from its zone";
                    });
                }),
                Exact("p.13 Ambush: \"At the start of any round after the first, may be deployed anywhere over 9\" away from enemy units\"", ["Ambush"], run =>
                {
                    var round = 0;
                    var rounds = new List<int>();
                    foreach (var p in Payloads(run))
                    {
                        if (p is RoundStarted r) round = r.Round;
                        if (p is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }) rounds.Add(round);
                    }
                    if (rounds.Count == 0) return "no Ambusher was offered";
                    if (rounds.Any(r => r < 2)) return $"Ambushers offered in rounds {string.Join(",", rounds)}";
                    return Each(Arrivals(run, "Red unit 2").Concat(Arrivals(run, "Blue unit 2")), "an Ambusher placement", a =>
                    {
                        var army = UnitOf(run, a.Model).Split(' ')[0];
                        var gaps = Placed(run, a.Seq).Where(kv => !UnitOf(run, kv.Key).StartsWith(army))
                            .Select(kv => a.At.DistanceTo(kv.Value.At) - kv.Value.Radius - Radius(run, a.Model)).ToList();
                        return gaps.Count == 0 || gaps.Min() > 9 ? null : $"{ModelName(run, a.Model)} placed {gaps.Min():0.00}\" from an enemy";
                    });
                }),
                Exact("p.13 Aircraft: \"May only use Advance actions ... adding 30\" to its total move\"", ["Aircraft"], run =>
                {
                    var offered = ActivateOptions(run).Where(o => o.Unit == "Blue unit 1").ToList();
                    if (offered.Count == 0) return "the Aircraft never activated";
                    if (offered.FirstOrDefault(o => !o.Actions.SequenceEqual(["Advance"])) is { } wrong) return $"the Aircraft was offered {string.Join(",", wrong.Actions)}";
                    return Each(Moves(run).Where(m => m.Unit == "Blue unit 1"), "an Aircraft move", m =>
                        m.Region.Allowance == 36 ? null : $"the Aircraft's Advance allowance is {m.Region.Allowance}, expected 36 (6 + 30)");
                }),
                Exact("p.13 Aircraft: \"can't be charged\"", ["Aircraft"], run =>
                {
                    var aircraft = run.Final.Units.Values.Single(u => u.Definition.Name == "Blue unit 1").Id;
                    var targets = Payloads(run).OfType<AskOpened>().Where(a => a.Ask.Question == "charge_target").Select(a => a.Ask.Legal).OfType<PieceSelection>().ToList();
                    return targets.Count == 0 ? "nobody charged" : targets.Any(t => t.Eligible.Contains(aircraft)) ? "the Aircraft was offered as a charge target" : null;
                }),
                Exact("p.15 Tough: \"heroes must be assigned wounds last\"", ["Hero"], run =>
                {
                    var (heroes, others) = HeroSplit(run);
                    var wounds = Attacks(run).Where(a => a.Target == "Red unit 3").SelectMany(a => a.Assigns.Select(s => (a.At, s))).ToList();
                    return Each(wounds.Where(w => Placed(run, w.At).Keys.Any(others.Contains) && w.s.Changes.OfType<WoundsSet>().Any()), "a wound on the Hero's unit while another model lived", w =>
                        w.s.Changes.OfType<WoundsSet>().FirstOrDefault(x => heroes.Contains(x.ModelId) && x.Wounds > 0) is { } hit
                            ? $"{ModelName(run, hit.ModelId)} took a wound while another model of its unit lived" : null);
                }),
                Exact("p.14 Hero: \"must use the unit's Defense until all other models have been killed\" (the unit's 4+)", ["Hero"], run =>
                {
                    var (_, others) = HeroSplit(run);
                    var blocks = Attacks(run).Where(a => a.Target == "Red unit 3" && a.Block is not null && Placed(run, a.At).Keys.Any(others.Contains)).ToList();
                    return Each(blocks.SelectMany(a => a.Block!.Rolls.Where(r => r.Roll == "defense")), "a defense roll by the Hero's unit while another model lived", r =>
                        r.Threshold == 4 ? null : $"defense {Show(r)} is not at the unit's 4+");
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
            {
                // Scheduler and legality rules act where no step completes (TestGame.Evidence): the Ask the rule shapes is the evidence.
                ["Scout"] = log => log.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployScouts }),
                ["Ambush"] = log => log.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }),
                ["Aircraft"] = log => OnlyOffered(log, "Blue unit 1", "Advance"),
            });
    }

    /// <summary>Red unit 3's models with the Hero rule, and the rest of the unit.</summary>
    static (HashSet<Guid> Heroes, HashSet<Guid> Others) HeroSplit(SuiteGameRun run)
    {
        var models = run.Final.Models.Values.Where(m => UnitName(run, m.UnitId) == "Red unit 3").ToList();
        return (models.Where(m => m.Slot.Properties.Any(p => p.Name == "Hero")).Select(m => m.Id).ToHashSet(),
            models.Where(m => m.Slot.Properties.All(p => p.Name != "Hero")).Select(m => m.Id).ToHashSet());
    }

    /// <summary>Each model of the unit as it first arrives on the table from the shelf: where, and at which log position.</summary>
    static IEnumerable<(Guid Model, Point2 At, long Seq)> Arrivals(SuiteGameRun run, string unit) =>
        Events(run).SelectMany(e => ChangesOf(e.Payload).OfType<ModelsMoved>().SelectMany(m => m.Moves)
                .Where(m => m.From is OnShelf && m.To is OnTable && UnitOf(run, m.ModelId) == unit)
                .Select(m => (m.ModelId, ((OnTable)m.To).Placement.Position.Horizontal, e.Seq)))
            .GroupBy(x => x.ModelId).Select(g => g.First());

    /// <summary>The bounding box of the team's deployment zone, as the board's zone shape resolves on this table.</summary>
    static (double MinX, double MinY, double MaxX, double MaxY) ZoneBounds(SuiteGameRun run, int teamOrdinal)
    {
        var table = run.Final.Board.Table;
        var vertices = ZoneStyles.Resolve(table.Zones.Single(z => z.TeamOrdinal == teamOrdinal).Shape, table.Size).SelectMany(p => p.Vertices).ToList();
        return (vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Max(v => v.X), vertices.Max(v => v.Y));
    }

    /// <summary>How far the point lies outside the box; zero inside.</summary>
    static double OutsideBy((double MinX, double MinY, double MaxX, double MaxY) box, Point2 at)
    {
        var dx = Math.Max(0, Math.Max(box.MinX - at.X, at.X - box.MaxX));
        var dy = Math.Max(0, Math.Max(box.MinY - at.Y, at.Y - box.MaxY));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
