using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Scenarios;

/// <summary>The fluent form. Every setter is optional except two armies — a game needs two teams.</summary>
public sealed class ScenarioBuilder(string name, ulong seed)
{
    private SchedulerShape _shape = SchedulerShape.AlternatingActivation;
    private Enforcement _enforcement = Enforcement.Strict;
    private int _roundCap = 8;
    private TableSize _table = new(72, 48);
    private readonly List<ScenarioArmy> _armies = [];
    private readonly List<ScenarioTerrain> _terrain = [];
    private readonly List<ScenarioMarker> _markers = [];
    private string? _zoneStyle;
    private AnswerScript _script = AnswerScript.Empty;
    private bool _scriptOnly;
    private readonly List<Expectation> _expectations = [];
    private MoveGoal? _goal;
    private IReadOnlyDictionary<string, string> _optionalRules = new Dictionary<string, string>();
    private bool _selfPlay;
    private string _rules = RuleSets.Gf351;

    public ScenarioBuilder Shape(SchedulerShape shape) { _shape = shape; return this; }
    public ScenarioBuilder Enforcing(Enforcement e) { _enforcement = e; return this; }
    /// <summary>The game's optional rules, as the ruleset's catalog names them.</summary>
    public ScenarioBuilder Options(IReadOnlyDictionary<string, string> optionalRules) { _optionalRules = optionalRules; return this; }
    public ScenarioBuilder RoundCap(int rounds) { _roundCap = rounds; return this; }
    /// <summary>One account plays every side (Scenario.SelfPlay).</summary>
    public ScenarioBuilder SelfPlay() { _selfPlay = true; return this; }
    /// <summary>The game-rules id the scenario plays under; 3.5.1 unless named.</summary>
    public ScenarioBuilder Rules(string rulesId) { _rules = rulesId; return this; }
    public ScenarioBuilder Table(double width, double length) { _table = new TableSize(width, length); return this; }
    /// <summary>A zone style id from Tabletop.Geometry.ZoneStyles; its shapes expand in team-ordinal order.</summary>
    public ScenarioBuilder Zones(string style) { _zoneStyle = style; return this; }

    public ScenarioBuilder Army(string armyName, int units, int modelsPerUnit,
        IReadOnlyList<ScenarioRule>? unitRules = null, IReadOnlyList<ScenarioRule>? modelRules = null,
        IReadOnlyList<ScenarioWeapon>? weapons = null, IReadOnlyDictionary<string, string>? unitStats = null, string? armyBook = null,
        IReadOnlyList<int>? setAside = null, IReadOnlyList<ScenarioModel>? models = null, IReadOnlyDictionary<int, int>? passengers = null,
        IReadOnlyDictionary<int, int>? sizes = null, IReadOnlyDictionary<int, Point2>? anchors = null, IReadOnlyList<(int Unit, string Condition)>? conditions = null,
        IReadOnlyList<ScenarioReserve>? reserves = null, IReadOnlyList<int>? setAsideAtLastAsk = null, int? team = null)
    {
        _armies.Add(new ScenarioArmy(armyName, units, modelsPerUnit, unitRules ?? [], modelRules ?? [],
            weapons ?? [new ScenarioWeapon("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, [])],
            unitStats ?? new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" }, armyBook)
        { SetAside = setAside ?? [], Models = models ?? [], Passengers = passengers ?? new Dictionary<int, int>(),
          Sizes = sizes ?? new Dictionary<int, int>(), Anchors = anchors ?? new Dictionary<int, Point2>(), Conditions = conditions ?? [], Reserves = reserves ?? [],
          SetAsideAtLastAsk = setAsideAtLastAsk ?? [], Team = team });
        return this;
    }

    public ScenarioBuilder Terrain(string mesh, Point3 at, double yaw = 0, double scale = 1, params Volume[] volumes)
    { _terrain.Add(new ScenarioTerrain(mesh, at, yaw, scale, volumes)); return this; }

    public ScenarioBuilder Marker(MarkerKind kind, Point3 at, double diameter = 2, string? label = null)
    { _markers.Add(new ScenarioMarker(kind, at, diameter, label)); return this; }

    public ScenarioBuilder Scripted(AnswerScript script, bool only = false) { _script = script; _scriptOnly = only; return this; }
    /// <summary>Opts the policy into ranking its legal move destinations by this goal (AnswerPolicy, MoveGoal).</summary>
    public ScenarioBuilder Seeking(MoveGoal goal) { _goal = goal; return this; }
    public ScenarioBuilder Expect(params Expectation[] expectations) { _expectations.AddRange(expectations); return this; }

    public Scenario Build()
    {
        var teams = _armies.Select((a, i) => a.Team ?? i + 1).Distinct().Count();
        if (teams < 2) throw new InvalidOperationException($"scenario '{name}' needs at least two teams; it has {teams}");
        if (_selfPlay && teams != _armies.Count) throw new InvalidOperationException($"scenario '{name}': self-play holds one seat per team");
        foreach (var a in _armies)
        {
            if (a.SetAside.Concat(a.SetAsideAtLastAsk).Where(u => u < 1 || u > a.Units).Select(u => (int?)u).FirstOrDefault() is { } bad)
                throw new InvalidOperationException($"scenario '{name}': army '{a.Name}' sets aside unit {bad}; its units are 1..{a.Units}");
            if (a.Passengers.SelectMany(kv => new[] { kv.Key, kv.Value }).Where(u => u < 1 || u > a.Units).Select(u => (int?)u).FirstOrDefault() is { } stray)
                throw new InvalidOperationException($"scenario '{name}': army '{a.Name}' names passenger unit {stray}; its units are 1..{a.Units}");
            if (a.Sizes.Keys.Concat(a.Anchors.Keys).Where(u => u < 1 || u > a.Units).Select(u => (int?)u).FirstOrDefault() is { } unsized)
                throw new InvalidOperationException($"scenario '{name}': army '{a.Name}' sizes or anchors unit {unsized}; its units are 1..{a.Units}");
            if (a.Models.FirstOrDefault(m => m.Unit < 1 || m.Unit > a.Units || m.Model < 1 || m.Model > a.SizeOf(m.Unit)) is { } model)
                throw new InvalidOperationException($"scenario '{name}': army '{a.Name}' has no model {model.Unit}.{model.Model}");
        }
        var style = _zoneStyle ?? (teams > 2 ? ZoneStyles.FreeForAll : ZoneStyles.Frontline);
        var shapes = ZoneStyles.ShapesOf(style).Take(teams).ToList();
        return new Scenario(name, seed, _shape, _enforcement, _roundCap, _table, _armies, _terrain, _markers, shapes, _script, _scriptOnly, _expectations) { Goal = _goal, OptionalRules = _optionalRules, SelfPlay = _selfPlay, Rules = _rules };
    }
}
