using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>The special group's deployment game (pp.13–15).</summary>
static partial class SpecialGames
{
    /// <summary>
    /// Scout, Ambush, Hero. Red unit 1 scouts and Red unit 2 and Blue unit 2 ambush, all three set aside before deployment; Red unit 3's
    /// first model is a Tough(3) Hero on Defense 2+ in a unit on 4+.
    /// </summary>
    static TestGame Deployment()
    {
        const string name = "special-scouts-and-ambushers";
        // Red unit 3 and Blue unit 1 deploy 14" apart; Blue wins the roll-off (fixed) and Blue unit 1 Holds and shoots it first: the first attack at the
        // Hero's unit hits and its block fails, a wound while the other two models live.
        return Game(name, new ScenarioBuilder(name, 1).RoundCap(3).Table(36, 36).Scripted(ShootTheHerosUnit)
                .Army("Red", 3, 3, unitRules: [new(1, "Scout"), new(2, "Ambush")], unitStats: Stats(4, 4), setAside: [1, 2],
                    models: [new ScenarioModel(3, 1, [new(0, "Hero"), new(0, "Tough", 3)], Stats(4, 2))], anchors: new Dictionary<int, Point2> { [3] = new(18, 25) })
                .Army("Blue", 2, 2, unitRules: [new(2, "Ambush")], unitStats: Stats(4, 4), setAside: [2], anchors: new Dictionary<int, Point2> { [1] = new(18, 11) })
                .Build(),
            ["Scout", "Ambush", "Hero"],
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
                    return Each(Arrivals(run, "Red unit 2").Concat(Arrivals(run, "Blue unit 2")), "an Ambusher placement", a => OverNine(run, a));
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
            }, [new("roll-off", 0, [1, 6]), new("hit", 0, [6], new RollKey(Target: "Red unit 3")), new("defense", 0, [1], new RollKey(Target: "Red unit 3"))]);
    }

    /// <summary>
    /// Ambush, Scout, Fast under strict on a 48x24 table: the zones meet at y 12, Blue unit 2 scouts up to 12" out of Blue's zone and Blue
    /// unit 1 (Fast) closes on Red, so by round two much of Red's own zone is within 9" of Blue and Red unit 2's arrival has to be found
    /// elsewhere on the table. A placement within 9" is refused (ambush_distance), and the refused placement stalls the run, so the game
    /// ending is part of the evidence.
    /// </summary>
    static TestGame AmbushCloseQuarters()
    {
        const string name = "special-ambush-over-nine";
        return Game(name, new ScenarioBuilder(name, 3).RoundCap(3).Table(48, 24).Scripted(ShootAndStrike).Seeking(NearestEnemy)
                .Army("Red", 2, 3, unitRules: [new(2, "Ambush")], unitStats: Stats(4, 4), setAside: [2])
                .Army("Blue", 2, 3, unitRules: [new(2, "Scout")], modelRules: [new(1, "Fast")], unitStats: Stats(4, 4), setAside: [2])
                .Build(),
            ["Ambush", "Scout", "Fast"],
            [
                Exact("p.13 Ambush: \"may be deployed anywhere over 9\" away from enemy units\" (under strict, with the enemy in the Ambusher's zone)", ["Ambush"], run =>
                    Each(Arrivals(run, "Red unit 2"), "an Ambusher placement", a => OverNine(run, a))),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
            {
                ["Scout"] = log => log.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployScouts }),
                ["Ambush"] = log => log.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }),
            });
    }

    /// <summary>
    /// Ambush set aside during deployment: Red's last undeployed unit, Red unit 2 (Ambush), is set aside from the menu when Red's deploy Ask
    /// offers it alone, instead of being deployed. Red has then finished deploying; Blue deploys its remaining units, rounds begin, and Red
    /// unit 2 arrives as an Ambusher from round two.
    /// </summary>
    static TestGame AmbushSetAsideAtLastAsk()
    {
        const string name = "special-ambush-set-aside-at-last-ask";
        return Game(name, new ScenarioBuilder(name, 5).RoundCap(2).Table(36, 36).Scripted(RedUnitOneDeploysFirst)
                .Army("Red", 2, 2, unitRules: [new(2, "Ambush")], unitStats: Stats(4, 4), setAsideAtLastAsk: [2])
                .Army("Blue", 3, 2, unitStats: Stats(4, 4))
                .Build(),
            [],   // covers nothing (special games cover three to five rules each): the check names Ambush
            [
                Exact("p.13 Ambush: \"may be set aside before deployment\" (set aside at the team's last deploy Ask; the rest deploy and rounds begin)", ["Ambush"], run =>
                {
                    var payloads = Payloads(run).ToList();
                    var red2 = run.Final.Units.Values.Single(u => u.Definition.Name == "Red unit 2").Id;
                    var offered = payloads.OfType<AskOpened>().Where(a => a.Ask.Question == AskQuestions.Deploy && a.Ask.Legal is PieceSelection { Eligible: [var only] } && only == red2)
                        .Select(a => a.Ask.Id).ToList();
                    if (offered.Count == 0) return "Red unit 2 was never offered alone at a deploy Ask";
                    if (!payloads.OfType<AskClosed>().Any(c => offered.Contains(c.AskId) && c.Answers.Count == 0)) return "the deploy Ask over the set-aside unit was not withdrawn";
                    var deployed = payloads.OfType<AskClosed>().SelectMany(c => c.Answers.Select(a => a.Answer)).OfType<SelectedPieces>()
                        .SelectMany(s => s.PieceIds).Where(run.Final.Units.ContainsKey).Select(id => run.Final.Units[id].Definition.Name).ToList();
                    if (new[] { "Blue unit 1", "Blue unit 2", "Blue unit 3" }.FirstOrDefault(u => !deployed.Contains(u)) is { } missing) return $"{missing} never deployed";
                    if (!payloads.OfType<RoundStarted>().Any(r => r.Round == 1)) return "deployment never ended";
                    var round = 0;
                    var ambushRounds = new List<int>();
                    foreach (var p in payloads)
                    {
                        if (p is RoundStarted r) round = r.Round;
                        if (p is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }) ambushRounds.Add(round);
                    }
                    if (ambushRounds.Count == 0) return "Red unit 2 was never offered as an Ambusher";
                    return ambushRounds.Any(r => r < 2) ? $"Ambushers offered in rounds {string.Join(",", ambushRounds)}" : null;
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>>
            {
                ["Ambush"] = log => log.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }),
            });
    }

    /// <summary>ShootAndStrike, and Red's first deploy pick is Red unit 1, so Red unit 2 is the last unit Red's deploy Ask offers.</summary>
    static AnswerScript RedUnitOneDeploysFirst
    {
        get
        {
            var script = AnswerScript.Build();
            for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
            return script.Match(AskQuestions.Deploy, (ask, p) => AskPicks.Pieces(ask, p, "Red unit 1")).Build();
        }
    }

    /// <summary>An arriving model's closest base-to-base gap to the other army's models on the table just before it; p.13's "over 9”".</summary>
    static string? OverNine(SuiteGameRun run, (Guid Model, Point2 At, long Seq) a)
    {
        var army = UnitOf(run, a.Model).Split(' ')[0];
        var gaps = Placed(run, a.Seq).Where(kv => !UnitOf(run, kv.Key).StartsWith(army))
            .Select(kv => a.At.DistanceTo(kv.Value.At) - kv.Value.Radius - Radius(run, a.Model)).ToList();
        return gaps.Count == 0 || gaps.Min() > 9 ? null : $"{ModelName(run, a.Model)} placed {gaps.Min():0.00}\" from an enemy";
    }

    /// <summary>
    /// Aircraft: Blue unit 1 is the Aircraft; both sides close in and Red charges whenever it may, picking the Aircraft when offered.
    /// Red unit 3, the first to charge, is Fast and Red unit 2 is Slow, so the move allowances the Aircraft's 30" adds to sit beside theirs.
    /// </summary>
    static TestGame Aircraft()
    {
        const string name = "special-aircraft-overhead";
        return Game(name, new ScenarioBuilder(name, 2).RoundCap(3).Table(36, 36).Scripted(ChargeTheAircraft).Seeking(NearestEnemy)
                .Army("Red", 3, 3, modelRules: [new(3, "Fast"), new(2, "Slow")], unitStats: Stats(4, 4))
                .Army("Blue", 2, 2, unitRules: [new(1, "Aircraft")], unitStats: Stats(4, 4))
                .Build(),
            ["Aircraft", "Fast", "Slow"],
            [
                Exact("p.13 Fast: \"move +2\" when using Advance and +4\" when using Rush/Charge\"", ["Fast"], run =>
                    Each(Moves(run).Where(m => m.Own && m.Unit == "Red unit 3" && m.Action is "Advance" or "Rush" or "Charge"), "a Fast move", m =>
                        m.Region.Allowance == (m.Action == "Advance" ? 8 : 16) ? null : $"{m.Action}: allowance {m.Region.Allowance}")),   // p.7: Advance 6", Rush and Charge 12"
                Exact("p.14 Slow: \"move -2\" when using Advance, and -4\" when using Rush/Charge\"", ["Slow"], run =>
                    Each(Moves(run).Where(m => m.Own && m.Unit == "Red unit 2" && m.Action is "Advance" or "Rush" or "Charge"), "a Slow move", m =>
                        m.Region.Allowance == (m.Action == "Advance" ? 4 : 8) ? null : $"{m.Action}: allowance {m.Region.Allowance}")),
                Exact("p.13 Aircraft: \"May only use Advance actions ... adding 30\" to its total move\"", ["Aircraft"], run =>
                {
                    var offered = ActivateOptions(run).Where(o => o.Unit == "Blue unit 1").ToList();
                    if (offered.Count == 0) return "the Aircraft never activated";
                    if (offered.FirstOrDefault(o => !o.Actions.SequenceEqual(["Advance"])) is { } wrong) return $"the Aircraft was offered {string.Join(",", wrong.Actions)}";
                    return Each(Moves(run).Where(m => m.Unit == "Blue unit 1"), "an Aircraft move", m =>
                        m.Region.Allowance == 36 ? null : $"the Aircraft's Advance allowance is {m.Region.Allowance}, expected 36 (6 + 30)");
                }),
                Exact("p.13 Aircraft: \"can't be charged\" (p.7: \"Charge - Moves 12\"\"; Fast +4\", Slow -4\")", ["Aircraft"], run =>
                {
                    // The evidence is a charge whose charger stood within its charge distance of the Aircraft: everywhere else it was out of reach anyway.
                    var aircraft = run.Final.Units.Values.Single(u => u.Definition.Name == "Blue unit 1").Id;
                    var q = LogQueries.Of(run);
                    var asks = q.Asks.Where(a => a.Ask.Question == "charge_target" && a.Ask.Legal is PieceSelection && a.Frame?.Context.ActorUnitId is not null).ToList();
                    if (asks.Count == 0) return "nobody charged";
                    if (asks.FirstOrDefault(a => ((PieceSelection)a.Ask.Legal).Eligible.Contains(aircraft)) is { } offered)
                        return $"the Aircraft was offered as a charge target to {UnitName(run, offered.Frame!.Context.ActorUnitId!.Value)}";
                    PlacedModel? At(IReadOnlyDictionary<Guid, Location> where, Guid model) => where[model] is OnTable t
                        ? new PlacedModel(new ModelBody(run.Final.Models[model].Slot.Base, run.Final.Models[model].Slot.Height), t.Placement) : null;
                    double Reach(string charger) => charger switch { "Red unit 3" => 16, "Red unit 2" => 8, _ => 12 };
                    var inReach = asks.Count(a =>
                    {
                        var where = q.LocationsBefore(a.Seq);
                        var charger = a.Frame!.Context.ActorUnitId!.Value;
                        var mine = run.Final.Models.Values.Where(m => m.UnitId == charger).Select(m => At(where, m.Id)).OfType<PlacedModel>().ToList();
                        var theirs = run.Final.Models.Values.Where(m => m.UnitId == aircraft).Select(m => At(where, m.Id)).OfType<PlacedModel>().ToList();
                        return mine.Any(m => theirs.Any(t => Distance.EdgeToEdge(m, t) <= Reach(UnitName(run, charger))));
                    });
                    return inReach == 0 ? "no charger ever stood within its charge distance of the Aircraft" : null;
                }),
            ],
            new Dictionary<string, Func<IReadOnlyList<GameEvent>, bool>> { ["Aircraft"] = log => OnlyOffered(log, "Blue unit 1", "Advance") });
    }

    /// <summary>ShootAndStrike, and every weapon group fires at Red unit 3, the Hero's unit, whenever it is a legal target of every group.</summary>
    static AnswerScript ShootTheHerosUnit
    {
        get
        {
            var script = AnswerScript.Build();
            for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
            return script
                .Match(AskQuestions.Activate, (ask, _) => AskPicks.Activate(ask, "Blue unit 1", "Hold"))
                .Match("declare_targets", (ask, p) => ask.Legal is Assignment a && a.Targets.FirstOrDefault(t => AskPicks.UnitNameOf(p, t) == "Red unit 3") is var t && t != Guid.Empty
                    && a.Items.All(i => a.TargetsOf(i.Id).Contains(t)) ? new Assigned(a.Items.ToDictionary(i => i.Id, _ => t)) : null)
                .Build();
        }
    }

    /// <summary>
    /// ShootAndStrike, and Red charges whenever a charge is offered, picking the Aircraft when it is a target: p.13 says it can't be, so the
    /// check needs the offer made.
    /// </summary>
    static AnswerScript ChargeTheAircraft
    {
        get
        {
            var script = AnswerScript.Build();
            for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
            return script
                .Match(AskQuestions.Activate, (ask, _) => new[] { "Red unit 3", "Red unit 1", "Red unit 2" }
                    .Select(unit => AskPicks.Activate(ask, unit, "Charge")).FirstOrDefault(a => a is not null))
                .Match("charge_target", (ask, p) => AskPicks.Pieces(ask, p, "Blue unit 1"))
                .Build();
        }
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
