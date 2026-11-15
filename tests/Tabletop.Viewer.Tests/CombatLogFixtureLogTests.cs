using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Viewer.Tests;

/// <summary>
/// The games the combat log's agreement renders besides the recorded skirmishes (viewer/tests/fixtures/log/): played in process from
/// rules-suite games under their edition (3.6, and a 3.5.1 Deadly game), with no dice fixed, and written as the frames a socket delivers. Their ids differ between runs, so
/// they are regenerated deliberately; viewer/tests/fixtures/combatlog.json is regenerated after them
/// (TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Client.Tests --filter CombatLogViewerFixture).
/// </summary>
public class CombatLogFixtureLogTests
{
    static Scenario Suite(string name)
    {
        var game = SuiteCatalog.All.Single(g => g.Name == name);
        return game.Scenario with { Rules = game.Edition };
    }

    public static readonly (string Name, Func<Scenario> Scenario)[] Logs =
    [
        ("spears", () => Suite("special-charge-into-spears@3.6")),
        ("gunline", () => Suite("special-long-range-gunline@3.6")),
        ("indirect", () => Suite("special-indirect-on-hold@3.6")),
        ("spells", () => Suite("spells-daemons-of-change@3.6") with { RoundCap = 1 }),
        ("deadly", () => Suite("special-blast-deadly-takedown")),
    ];

    public static TheoryData<string> Names() => new(Logs.Select(c => c.Name));

    [Theory, MemberData(nameof(Names))]
    public async Task The_log_exists_and_holds_what_the_combat_log_cases_need(string name)
    {
        var dir = Path.Combine(RepoPaths.Fixtures, "log");
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1")
        {
            var (_, scenario) = Logs.Single(c => c.Name == name);
            await FixtureWriter.WriteFramesAsync(await FixtureWriter.RunAsync(scenario(), CancellationToken.None), name, dir, GfOptions.Catalog, CancellationToken.None);
        }
        var path = Path.Combine(dir, $"{name}.events.json");
        Assert.True(File.Exists(path), $"{path} is missing; run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests --filter CombatLogFixtureLog");
        var events = Assert.IsType<EventsMessage>(WireJson.Deserialize<SocketMessage>(File.ReadAllText(path))).Events;
        var rolls = events.Select(e => e.Payload).OfType<StepCompleted>().SelectMany(s => s.Rolls).ToList();
        Assert.True(name switch
        {
            "spears" => rolls.Any(r => r.Roll == "Impact") && rolls.Any(r => r.Roll == "morale"),
            "gunline" => rolls.Any(r => r.Dice.Any(d => d.Triggered is { Count: > 0 })) && rolls.Any(r => r.Cover is not null),
            "indirect" => rolls.Any(r => r.Cover is { InCover: true }),
            "spells" => rolls.Any(r => r.Roll == "casting") && rolls.Any(r => r.Roll == "defense"),
            "deadly" => events.Any(e => e.Payload is StepCompleted { Values: { } v } && v.GetValueOrDefault("wound_multiplier") > 1),
            _ => false,
        }, $"log/{name} lacks what its combat log case needs");
    }
}
