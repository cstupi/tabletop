using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Scenarios;

public sealed record ScenarioRule(int Unit, string Name, int? Parameter = null);
public sealed record ScenarioWeapon(string Name, IReadOnlyDictionary<string, string> Stats, IReadOnlyList<ScenarioRule> Rules);
public sealed record ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IReadOnlyList<Volume> Volumes);
public sealed record ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label);

/// <summary>
/// One model of a unit with its own rules, stats and weapons (a Hero among non-heroes). Unit and Model are 1-based ordinals. Rules are
/// added to the unit's model rules; Stats are the slot's own (a stat the slot names overrides the unit's); Weapons, when given, replace the
/// army's weapons for this model. A rule's Unit field is not read here: the model's Unit names the unit.
/// </summary>
public sealed record ScenarioModel(int Unit, int Model, IReadOnlyList<ScenarioRule>? Rules = null,
    IReadOnlyDictionary<string, string>? Stats = null, IReadOnlyList<ScenarioWeapon>? Weapons = null);

/// <summary>One army per participant, in order; each on its own team in that order unless Team names its team ordinal (a team game, rulebook
/// p.50: several players a side, each with their own army). Rules go on the side their catalog entry is declared for.</summary>
public sealed record ScenarioArmy(
    string Name,
    int Units,
    int ModelsPerUnit,
    IReadOnlyList<ScenarioRule> UnitRules,
    IReadOnlyList<ScenarioRule> ModelRules,
    IReadOnlyList<ScenarioWeapon> Weapons,
    IReadOnlyDictionary<string, string> UnitStats,
    string? ArmyBook = null)
{
    /// <summary>Unit ordinals the run sets aside before deployment (the menu's set-aside condition): a Scout or Ambush unit's way in.</summary>
    public IReadOnlyList<int> SetAside { get; init; } = [];

    /// <summary>Unit ordinals the run sets aside from the menu when its team's deploy Ask offers that unit alone, in place of answering the Ask
    /// (a player setting aside an Ambusher, their last unit, during their own deploy turn).</summary>
    public IReadOnlyList<int> SetAsideAtLastAsk { get; init; } = [];
    /// <summary>The team ordinal (1-based) this army's player sits on; null is its own team, the army's position in the list.</summary>
    public int? Team { get; init; }

    /// <summary>Unit ordinals and a condition the run sets on each before the first answer, as the unit menu does (a unit Shaken from the start).</summary>
    public IReadOnlyList<(int Unit, string Condition)> Conditions { get; init; } = [];

    /// <summary>Unit ordinal to the Transport unit ordinal it deploys inside (p.15: "Transports may deploy with units inside"; passengers spec): the run
    /// puts each in its Transport's first model before the first answer, after the set-aside conditions, as the roster menu does.</summary>
    public IReadOnlyDictionary<int, int> Passengers { get; init; } = new Dictionary<int, int>();

    /// <summary>Models with their own rules, stats or weapons.</summary>
    public IReadOnlyList<ScenarioModel> Models { get; init; } = [];

    /// <summary>Unit ordinal to its model count, for a unit not ModelsPerUnit strong.</summary>
    public IReadOnlyDictionary<int, int> Sizes { get; init; } = new Dictionary<int, int>();

    /// <summary>Unit ordinal to where its first model deploys: HandPlacement puts the unit's row there in place of a drawn anchor.</summary>
    public IReadOnlyDictionary<int, Point2> Anchors { get; init; } = new Dictionary<int, Point2>();

    /// <summary>Unit ordinals the army holds off the table as an import's reserve units (Unit.Reserve): the property placing each, and the unit (or, with
    /// a model ordinal, the model) holding it.</summary>
    public IReadOnlyList<ScenarioReserve> Reserves { get; init; } = [];

    public int SizeOf(int unit) => Sizes.GetValueOrDefault(unit, ModelsPerUnit);
}

/// <summary>Unit is a reserve unit placed by Property, held by unit OfUnit or, with OfModel, by that model of it (1-based).</summary>
public sealed record ScenarioReserve(int Unit, string Property, int OfUnit, int? OfModel = null);

/// <summary>
/// What a run is: a board, armies, a scheduler shape, enforcement, a round cap, a seed, a script and
/// the expectations. Declared in C# because that keeps the assertions typed (spec → Deliberately not decided).
/// </summary>
/// <param name="RoundCap">
/// The maximum number of *whole* rounds. A cap of n ends the run on round n's boundary Ask; round n + 1 never
/// starts. Task 6 → Termination, exactly.
/// </param>
public sealed record Scenario(
    string Name,
    ulong Seed,
    SchedulerShape Shape,
    Enforcement Enforcement,
    int RoundCap,
    TableSize Table,
    IReadOnlyList<ScenarioArmy> Armies,
    IReadOnlyList<ScenarioTerrain> Terrain,
    IReadOnlyList<ScenarioMarker> Markers,
    IReadOnlyList<string> ZoneShapes,
    AnswerScript Script,
    bool ScriptOnly,
    IReadOnlyList<Expectation> Expectations)
{
    public Scenario WithSeed(ulong seed) => this with { Seed = seed };

    /// <summary>The move goal the scenario's policy ranks legal destinations by; null for the uniform policy.</summary>
    public MoveGoal? Goal { get; init; }

    /// <summary>
    /// The game's step gate (GameInfo.WaitsBetweenSteps): every step boundary of a frame with an actor waits on a Confirm to the
    /// actor's team, so a completed step's rolls stay on the stack where SetDice can reach them. Off unless a run fixes dice.
    /// </summary>
    public bool WaitsBetweenSteps { get; init; }

    /// <summary>Self-play: one account holds every team, a participant row per team (game-flow spec → One account, several teams), and the
    /// driver plays every side through that account's one connection.</summary>
    public bool SelfPlay { get; init; }

    /// <summary>The game's optional rules (GameInfo.OptionalRules): the ruleset's keys and values, empty for the core game.</summary>
    public IReadOnlyDictionary<string, string> OptionalRules { get; init; } = new Dictionary<string, string>();

    /// <summary>The policy for a run of this scenario, seeded from it, or null for a script-only run.</summary>
    public AnswerPolicy? Policy() => ScriptOnly ? null : new AnswerPolicy(new Chooser(Seed), goal: Goal);
}
