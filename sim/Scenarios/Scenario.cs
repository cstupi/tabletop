using Tabletop.Content;
using Tabletop.GameServer.Properties;
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
    IReadOnlyDictionary<string, string> UnitStats);

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
    IReadOnlyList<EntryDocument> Spells,
    AnswerScript Script,
    bool ScriptOnly,
    IReadOnlyList<Expectation> Expectations)
{
    public Scenario WithSeed(ulong seed) => this with { Seed = seed };
}
