using Tabletop.Content;
using Tabletop.GameServer.Properties;
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
    private readonly List<EntryDocument> _spells = [];
    private AnswerScript _script = AnswerScript.Empty;
    private bool _scriptOnly;
    private readonly List<Expectation> _expectations = [];

    public ScenarioBuilder Shape(SchedulerShape shape) { _shape = shape; return this; }
    public ScenarioBuilder Enforcing(Enforcement e) { _enforcement = e; return this; }
    public ScenarioBuilder RoundCap(int rounds) { _roundCap = rounds; return this; }
    public ScenarioBuilder Table(double width, double length) { _table = new TableSize(width, length); return this; }
    /// <summary>A zone style id from Tabletop.Geometry.ZoneStyles; its shapes expand in team-ordinal order.</summary>
    public ScenarioBuilder Zones(string style) { _zoneStyle = style; return this; }

    /// <summary>Spells for this run only, folded into the pinned PropertySet the way TestProperties.WithSpells does.
    /// The shipped content document is every special rule on pages 13-15 and holds no spells, because a spell list belongs
    /// to an army book this project has not designed. Nothing invented here reaches the game's content.</summary>
    public ScenarioBuilder Spells(params EntryDocument[] spells) { _spells.AddRange(spells); return this; }

    public ScenarioBuilder Army(string armyName, int units, int modelsPerUnit,
        IReadOnlyList<ScenarioRule>? unitRules = null, IReadOnlyList<ScenarioRule>? modelRules = null,
        IReadOnlyList<ScenarioWeapon>? weapons = null, IReadOnlyDictionary<string, string>? unitStats = null)
    {
        _armies.Add(new ScenarioArmy(armyName, units, modelsPerUnit, unitRules ?? [], modelRules ?? [],
            weapons ?? [new ScenarioWeapon("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, [])],
            unitStats ?? new Dictionary<string, string> { ["Quality"] = "4+", ["Defense"] = "4+" }));
        return this;
    }

    public ScenarioBuilder Terrain(string mesh, Point3 at, double yaw = 0, double scale = 1, params Volume[] volumes)
    { _terrain.Add(new ScenarioTerrain(mesh, at, yaw, scale, volumes)); return this; }

    public ScenarioBuilder Marker(MarkerKind kind, Point3 at, double diameter = 2, string? label = null)
    { _markers.Add(new ScenarioMarker(kind, at, diameter, label)); return this; }

    public ScenarioBuilder Scripted(AnswerScript script, bool only = false) { _script = script; _scriptOnly = only; return this; }
    public ScenarioBuilder Expect(params Expectation[] expectations) { _expectations.AddRange(expectations); return this; }

    public Scenario Build()
    {
        if (_armies.Count < 2) throw new InvalidOperationException($"scenario '{name}' needs at least two armies; it has {_armies.Count}");
        var style = _zoneStyle ?? (_armies.Count > 2 ? ZoneStyles.FreeForAll : ZoneStyles.Frontline);
        var shapes = ZoneStyles.ShapesOf(style).Take(_armies.Count).ToList();
        return new Scenario(name, seed, _shape, _enforcement, _roundCap, _table, _armies, _terrain, _markers, shapes, _spells, _script, _scriptOnly, _expectations);
    }
}
