using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;

namespace Tabletop.Sim.Scenarios;

public sealed record ScenarioRule(int Unit, string Name, int? Parameter = null);
public sealed record ScenarioWeapon(string Name, IReadOnlyDictionary<string, string> Stats, IReadOnlyList<ScenarioRule> Rules);
public sealed record ScenarioTerrain(string Mesh, Point3 Position, double Yaw, double Scale, IReadOnlyList<Volume> Volumes);
public sealed record ScenarioMarker(MarkerKind Kind, Point3 Position, double Diameter, string? Label);

/// <summary>One army per team, in team-ordinal order. Rules go on the side their catalog entry is declared for.</summary>
public sealed record ScenarioArmy(
    string Name,
    int Units,
    int ModelsPerUnit,
    IReadOnlyList<ScenarioRule> UnitRules,
    IReadOnlyList<ScenarioRule> ModelRules,
    IReadOnlyList<ScenarioWeapon> Weapons,
    IReadOnlyDictionary<string, string> UnitStats,
    string? ArmyBook = null);

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

    /// <summary>The policy for a run of this scenario, seeded from it, or null for a script-only run.</summary>
    public AnswerPolicy? Policy() => ScriptOnly ? null : new AnswerPolicy(new Chooser(Seed), goal: Goal);
}
