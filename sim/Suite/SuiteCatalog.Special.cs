using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

public static partial class SuiteCatalog
{
    private static partial IEnumerable<TestGame> Special() => SpecialGames.All();
}

/// <summary>
/// The special group: the 33 core special rules (grimdark-future-core.properties.json, rulebook pp.13–15), three to five a game, chosen to
/// collide, and the relic wargear built (grimdark-future-relic-wargear.part.json, p.36), a game of its own. Each exact check quotes its rule and states the rule's number as a literal; it reads the effective log and the final projection
/// and never computes a rules outcome. Every check also requires the case it judges to have happened, so a seed that never brings it into
/// play fails rather than passing empty.
/// </summary>
static partial class SpecialGames
{
    public static IEnumerable<TestGame> All() =>
    [
        Regrowth(), BlastDeadly(), Gunline(), Artillery(), Charge(), Swift(), EternalFlame(), Command(), Deployment(),
        Aircraft(), CastMidMove(), AmbushCloseQuarters(), TakedownPicksOne(),
        StealthAtNineAndAHalf(), AmbushSetAsideAtLastAsk(),
    ];

    // ---- Building blocks ---------------------------------------------------------------------------------------------------------------

    static TestGame Game(string name, Scenario scenario, IReadOnlyList<string> covers, IReadOnlyList<GameCheck> checks,
        IReadOnlyDictionary<string, Func<IReadOnlyList<GameEvent>, bool>>? evidence = null, IReadOnlyList<DiceFix>? dice = null) =>
        new(name, SuiteCatalog.SpecialGroup, scenario, covers, dice ?? [], checks, evidence);

    static GameCheck Exact(string name, IReadOnlyList<string> entries, Func<SuiteGameRun, string?> failure) => new(name, failure, entries);

    static ScenarioWeapon Gun(string name, int range, int attacks, params ScenarioRule[] rules) =>
        new(name, new Dictionary<string, string> { ["Range"] = $"{range}\"", ["Attacks"] = $"{attacks}" }, rules);

    static ScenarioWeapon Blade(string name, int attacks, params ScenarioRule[] rules) =>
        new(name, new Dictionary<string, string> { ["Attacks"] = $"{attacks}" }, rules);

    static ScenarioRule Rule(string name, int? parameter = null) => new(0, name, parameter);

    static IReadOnlyDictionary<string, string> Stats(int quality, int defense) =>
        new Dictionary<string, string> { ["Quality"] = $"{quality}+", ["Defense"] = $"{defense}+" };

    /// <summary>Every shoot_or_not and strike_back Ask answered yes: the option ids are the server's fixed ones, so the dice decide the rest.</summary>
    static AnswerScript ShootAndStrike
    {
        get
        {
            var script = AnswerScript.Build();
            for (var i = 0; i < 80; i++) script.At("shoot_or_not", i, new Chosen("shoot")).At("strike_back", i, new Chosen("strike"));
            return script.Build();
        }
    }

    /// <summary>The nearest enemy model on the table: moves close the gap, so charges come into range.</summary>
    static Point2? NearestEnemy(Guid modelId, PolicyView view)
    {
        if (view.PlacedOf(modelId) is not { } placed) return null;
        var team = view.TeamOf(view.ModelOf(modelId).UnitId);
        var at = placed.Placement.Position.Horizontal;
        return view.Projection.Models.Values
            .Where(m => m.Location is OnTable && view.TeamOf(m.UnitId) != team)
            .Select(m => (Point2?)((OnTable)m.Location).Placement.Position.Horizontal)
            .MinBy(p => p!.Value.DistanceTo(at));
    }

    /// <summary>Red unit 1 heads for Red unit 2's first model, so it reaches the Transport and may embark; every other unit for the nearest enemy.</summary>
    static Point2? ToTransportElseEnemy(Guid modelId, PolicyView view)
    {
        if (view.UnitOf(view.ModelOf(modelId).UnitId).Definition.Name != "Red unit 1") return NearestEnemy(modelId, view);
        var transport = view.Projection.Units.Values.FirstOrDefault(u => u.Definition.Name == "Red unit 2")?.Definition.Models[0].Id;
        return transport is { } id && view.PlacedOf(id) is { } placed ? placed.Placement.Position.Horizontal : null;
    }
}
