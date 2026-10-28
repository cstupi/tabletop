using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests.Infrastructure;

public static class TestGames
{
    public sealed record Built(GameState State, GameInfo Info, IReadOnlyList<Guid> Teams, IReadOnlyList<Guid> Participants);

    /// <summary>One participant per entry, each on its own team unless teamOf gives entry i's team index (a team game: rulebook p.50, several
    /// players a side); a null army is a seat that brought nothing. Spectators are participants with no team. Markers join the sample table's objective.</summary>
    public static Built Build(IReadOnlyList<Army?> armyPerTeam, SchedulerShape shape = SchedulerShape.AlternatingActivation,
        Enforcement enforcement = Enforcement.Warn, bool waits = false, int spectators = 0, IGameRules? rules = null, long rngSeed = 7, IReadOnlyList<int>? teamOf = null,
        IReadOnlyList<Marker>? markers = null)
    {
        var teams = Enumerable.Range(0, teamOf is null ? armyPerTeam.Count : teamOf.Max() + 1).Select(i => new Team(Guid.CreateVersion7(), i + 1)).ToList();
        var participants = armyPerTeam.Select((a, i) => new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Player {i + 1}", teams[teamOf?[i] ?? i].Id, a?.Id)).ToList();
        participants.AddRange(Enumerable.Range(0, spectators).Select(i => new Participant(Guid.CreateVersion7(), Guid.CreateVersion7(), $"Watcher {i}", null, null)));
        var info = new GameInfo(Guid.CreateVersion7(), "Test", GameStatus.Playing, participants[0].AccountId, GrimdarkFutureRules.RulesId, "sha256:test",
            shape, enforcement, waits, rngSeed, teams, participants);                        // RngSeed is positional (contracts plan): a build that forgot it would not compile
        var armies = participants.Zip(armyPerTeam, (p, a) => (p, a)).Where(x => x.a is not null)
            .Select(x => new ArmyInPlay(x.p.Id, ArmySnapshot.Of(x.a!, Guid.CreateVersion7()))).ToList();
        var table = TableStateTests.Sample();
        var board = new Board(table with { Markers = [.. table.Markers, .. markers ?? []] }, armies);
        var state = new GameState(info, board, new EventApplier(board, info), rules ?? GrimdarkFutureRules.Instance);
        return new Built(state, info, teams.Select(t => t.Id).ToList(), participants.Select(p => p.Id).ToList());
    }

    public static void Apply(Built game, params EventPayload[] payloads)
    {
        foreach (var p in payloads) game.State.Applier.Apply(new GameEvent(game.State.Projection.LastSeq + 1, null, p, null));
    }

    /// <summary>Every model of the unit onto the table, in a row. Place is the event (for Apply/Append); Move is the identical command (for Dispatch).</summary>
    public static IReadOnlyList<ModelRelocation> Relocations(GameState state, Guid unitId) => state.Projection.Units[unitId].Definition.Models
        .Select((m, i) => new ModelRelocation(m.Id, state.Projection.Models[m.Id].Location, new OnTable(new Placement(new Point3(6 + i * 2, 6, 0), new Yaw(0))))).ToList();

    public static Subject UnitSubject(Guid unitId) => new(SubjectKind.Unit, unitId);

    /// <summary>The activate Ask's option id for a unit's action.</summary>
    public static string Activate(Guid unitId, string action = "Hold") => Tabletop.GameServer.Scheduler.ActivationOption.Id(unitId, action);

    public static ModelsMoved Place(GameState state, Guid unitId) => new(Relocations(state, unitId));
    public static MoveModels Move(GameState state, Guid unitId) => new(Relocations(state, unitId));
}
