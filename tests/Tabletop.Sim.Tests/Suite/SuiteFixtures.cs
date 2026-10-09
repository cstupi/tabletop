using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Tests.Suite;

/// <summary>
/// The sample test game: not in the catalog, so the framework's tests do not move when the catalog's games do. One model a side
/// with an AP(1) rifle and a hand weapon, so every hit roll is one die. AP is a modify_roll on the defense roll, so it fires through
/// RollModifier.Source and the fired check holds whether or not the server fills StepCompleted.Applied.
/// </summary>
public static class SuiteFixtures
{
    public const string Name = "sample-ap-volley";
    public const string Group = "fixture";

    static readonly ScenarioWeapon Rifle = new("Rifle", new Dictionary<string, string> { ["Range"] = "24\"", ["Attacks"] = "1" }, [new ScenarioRule(0, "AP", 1)]);
    static readonly ScenarioWeapon HandWeapon = new("Hand Weapon", new Dictionary<string, string> { ["Attacks"] = "1" }, []);

    public static Scenario Scenario(ulong seed = 3) => new ScenarioBuilder(Name, seed)
        .RoundCap(4).Table(24, 24)
        .Army("Red", 1, 1, weapons: [Rifle, HandWeapon])
        .Army("Blue", 1, 1, weapons: [Rifle, HandWeapon])
        .Build();

    public static TestGame Game(IReadOnlyList<DiceFix>? dice = null, IReadOnlyList<GameCheck>? checks = null, string name = Name, string group = Group,
        IReadOnlyList<string>? covers = null) =>
        new(name, group, Scenario() with { Name = name }, covers ?? ["AP"], dice ?? [],
            checks ?? [Checks.Ended(), Checks.Invariants(), Checks.FiredAll()]);

    /// <summary>The block step under the first edited hit roll's frame: its defense rolls, or none when no hit landed.</summary>
    public static IReadOnlyList<RollRecord>? DefenseAfterFirstEdit(IReadOnlyList<GameEvent> log)
    {
        var payloads = log.Select(e => e.Payload).ToList();
        var edit = payloads.OfType<DiceEdited>().FirstOrDefault();
        if (edit is null) return null;
        var resolve = payloads.SkipWhile(p => p != edit).OfType<SequenceStarted>().First(s => s.Sequence == "resolve-hits");
        var block = payloads.OfType<StepCompleted>().FirstOrDefault(s => s.FrameId == resolve.FrameId && s.Step == "roll-to-block");
        return block?.Rolls.Where(r => r.Roll == "defense").ToList();
    }

    public static CancellationToken Ct => CancellationToken.None;

    public static string TempDir() => Path.Combine(Path.GetTempPath(), $"suite-{Guid.CreateVersion7():N}");
}
