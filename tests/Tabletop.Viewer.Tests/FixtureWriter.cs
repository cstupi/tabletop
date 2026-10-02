using Tabletop.Protocol;
using Tabletop.Sim;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Recording;
using Tabletop.Sim.Run;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Viewer.Tests;

/// <param name="Log">Raw, undone markers included — the viewer's applier drops undone ranges itself.</param>
public sealed record Fixture(
    string Name, Recording Recording, IReadOnlyList<GameEvent> Log, GameInfo Game, Board Board, Guid RecorderParticipantId);

/// <summary>
/// Writes the four frames a live socket delivers on connect, plus the recording the same run produced.
/// The viewer's agreement test replays the events and must reproduce the recording.
/// </summary>
public static class FixtureWriter
{
    public static readonly string[] FixtureNames = ["skirmish", "terrain"];

    public static async Task<Fixture> RunAsync(Scenario scenario, CancellationToken ct)
    {
        var compiled = ScenarioCompiler.Compile(scenario);
        // The policy is built as sim/Program.cs does.
        var answers = new ComposedAnswers(scenario.Script, scenario.ScriptOnly ? null : new AnswerPolicy(new Chooser(scenario.Seed)));
        await using var driver = await InProcessDriver.StartAsync(compiled, ct);
        var loop = new RunLoop(driver, compiled, answers, HandPlacement.ChooserFor(scenario.Seed), new RunSettings());
        var report = await loop.RunAsync(ct);
        if (report.Outcome is RunOutcome.Stuck or RunOutcome.Failed)
            throw new InvalidOperationException($"{scenario.Name}: the run ended {report.Outcome}; a broken run is not written as a fixture");
        return new Fixture(scenario.Name, loop.BuildRecording(report), loop.Log, driver.Info, driver.Board,
            compiled.RecorderParticipantId);
    }

    public static async Task WriteAsync(Fixture fixture, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var name = fixture.Name;
        await RecordingJson.WriteAsync(fixture.Recording, Path.Combine(directory, $"{name}.recording.json"), ct);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.welcome.json"),
            WireJson.Serialize((SocketMessage)new Welcome(fixture.RecorderParticipantId, fixture.Game)), ct);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.board.json"),
            WireJson.Serialize((SocketMessage)new BoardMessage(fixture.Board)), ct);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.events.json"),
            WireJson.Serialize((SocketMessage)new EventsMessage(fixture.Log)), ct);
    }
}
