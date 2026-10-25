using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;
using Tabletop.Sim.Suite;

namespace Tabletop.Viewer.Tests;

/// <summary>
/// The seven games the calls agreement cuts its states from (same-calls part 3): played in process and written as the frames a socket
/// delivers, with no recording. Their ids differ between runs, so they are regenerated deliberately and never diffed against a previous
/// version; viewer/tests/fixtures/calls.json is regenerated after them.
/// </summary>
public class CallLogFixtureTests
{
    static Scenario Suite(string name) => SuiteCatalog.All.Single(g => g.Name == name).Scenario;

    public static readonly (string Name, Func<Scenario> Scenario)[] CallLogs =
    [
        ("ambush", () => Suite("special-scouts-and-ambushers")),
        ("transport", () => Suite("core-transport-run")),
        ("passengers", () => Suite("core-transport-deploy")),
        ("selfplay", () => Suite("core-self-play") with { RoundCap = 2 }),
        ("gameover", () => ScenarioCatalog.Get("final-round-warn")),
        ("casting", () => ScenarioCatalog.Get("casting")),
        ("objectives", () => ScenarioCatalog.Get("objectives") with { RoundCap = 2 }),
    ];

    public static TheoryData<string> Names() => new(CallLogs.Select(c => c.Name));

    [Theory, MemberData(nameof(Names))]
    public async Task The_call_log_exists_and_holds_what_its_cases_need(string name)
    {
        if (Environment.GetEnvironmentVariable("TABLETOP_WRITE_FIXTURES") == "1")
        {
            var (_, scenario) = CallLogs.Single(c => c.Name == name);
            await FixtureWriter.WriteFramesAsync(await FixtureWriter.RunAsync(scenario(), CancellationToken.None), name,
                Path.Combine(RepoPaths.Fixtures, "calls"), GfOptions.Catalog, CancellationToken.None);
        }
        var (welcome, board, events) = Read(name);
        Assert.Equal(WireJson.Serialize(GfOptions.Catalog), WireJson.Serialize(welcome.Options));
        Assert.True(Holds(name, welcome, board, events), $"calls/{name} lacks what its cases need");
    }

    static (Welcome Welcome, Board Board, IReadOnlyList<GameEvent> Events) Read(string name)
    {
        T Of<T>(string suffix) where T : SocketMessage
        {
            var path = Path.Combine(RepoPaths.Fixtures, "calls", $"{name}.{suffix}.json");
            Assert.True(File.Exists(path), $"{path} is missing; run TABLETOP_WRITE_FIXTURES=1 dotnet test tests/Tabletop.Viewer.Tests --filter CallLogFixtureTests");
            return Assert.IsType<T>(WireJson.Deserialize<SocketMessage>(File.ReadAllText(path)));
        }
        return (Of<Welcome>("welcome"), Of<BoardMessage>("board").Board, Of<EventsMessage>("events").Events);
    }

    static IEnumerable<StateChange> Changes(IReadOnlyList<GameEvent> events) =>
        events.SelectMany<GameEvent, StateChange>(e => e.Payload is StepCompleted s ? s.Changes : e.Payload is StateChange c ? [c] : []);

    static bool Holds(string name, Welcome welcome, Board board, IReadOnlyList<GameEvent> events) => name switch
    {
        // MenuModel.SetAside is "set-aside"; the viewer tests do not reference the client.
        "ambush" => Changes(events).Any(c => c is ConditionSet { Condition: "set-aside", Subject.Kind: SubjectKind.Unit, Present: true })
            && events.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.DeployAmbusher }),
        "transport" => events.Any(e => e.Payload is AskClosed c && c.Answers.Any(a => a.Answer is Destinations d && d.Moves.Any(m => m.To is Inside)))
            && events.Any(e => e.Payload is AskClosed c && c.Answers.Any(a => a.Answer is Destinations d && d.Moves.Any(m => m.From is Inside))),
        "passengers" => Changes(events).Any(c => c is ModelsMoved m && m.Moves.Any(r => r.From is OnShelf && r.To is Inside)),
        "selfplay" => welcome.Game.Participants.Where(p => p.TeamId is not null).GroupBy(p => p.AccountId).Any(g => g.Select(p => p.TeamId).Distinct().Count() == 2),
        "gameover" => events.Any(e => e.Payload is AskOpened { Ask.Question: AskQuestions.GameOver }),
        "casting" => events.Any(e => e.Payload is AskOpened { Ask.Legal: TokenSpend }),
        "objectives" => board.Table.Markers.Any(m => m.Kind == MarkerKind.Objective),
        _ => false,
    };
}
