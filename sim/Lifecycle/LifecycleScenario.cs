using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Drivers;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Lifecycle;

/// <summary>
/// The CompiledScenario RunLoop and SocketDriver play, read back from a game the API and the sockets made rather than compiled from a
/// Scenario: the GameInfo and Board the server sent once play began, the shipped set, and a Scenario that carries only what RunLoop reads
/// of it (round cap, self-play, each army's set-aside units, the expectations).
/// </summary>
public static class LifecycleScenario
{
    /// <summary>
    /// The board's armies in its order are the seats (RunLoop and HandPlacement index a scenario's armies by ParticipantIds); the recorder,
    /// a participant with no team, goes last among the participants, where SocketDriver looks for it. A self-play account's connection is
    /// its lowest participant row (the server binds an account's socket to it), so that row's army must come first.
    /// </summary>
    /// <param name="setAside">Per seat, the unit ordinals the run sets aside before the first answer.</param>
    public static CompiledScenario From(string name, ulong seed, GameInfo info, Board board, Guid recorder, bool selfPlay,
        IReadOnlyDictionary<Guid, IReadOnlyList<int>> setAside, int roundCap)
    {
        var properties = ScenarioCompiler.CoreProperties;
        if (info.PropertiesHash != properties.ContentHash)
            throw new InvalidOperationException($"the game runs set {info.PropertiesHash}, not the game server's embedded set {properties.ContentHash}");
        if (info.Participants.All(p => p.Id != recorder) || info.Participants.First(p => p.Id == recorder).TeamId is not null)
            throw new InvalidOperationException($"the recorder {recorder} is not a participant with no team");

        var seats = board.Armies.Select(a => info.Participants.Single(p => p.Id == a.ParticipantId)).ToList();
        if (seats.Any(s => s.TeamId is null)) throw new InvalidOperationException("an army on the board belongs to a participant with no team");
        if (selfPlay && seats[0].Id != seats.Where(s => s.AccountId == seats[0].AccountId).Min(s => s.Id))
            throw new InvalidOperationException("under self-play the board's first army is not the account's lowest participant row");

        var others = info.Participants.Where(p => p.Id != recorder && seats.All(s => s.Id != p.Id));
        var ordered = info with { Participants = [.. seats, .. others, info.Participants.Single(p => p.Id == recorder)] };
        var armies = board.Armies.Select(a => new ScenarioArmy(a.Snapshot.Name, a.Snapshot.Units.Count, 1, [], [], [], new Dictionary<string, string>())
        {
            SetAside = setAside.GetValueOrDefault(a.ParticipantId, []),
            Team = info.Teams.Single(t => t.Id == seats.Single(s => s.Id == a.ParticipantId).TeamId).Ordinal,
        }).ToList();
        var scenario = new Scenario(name, seed, info.SchedulerShape, info.Enforcement, roundCap, board.Table.Size, armies, [], [], [],
            AnswerScript.Empty, ScriptOnly: false, [new Expectation.Outcome("ended"), new Expectation.ReachedRound(4)])
        {
            SelfPlay = selfPlay,
            OptionalRules = info.OptionalRules,
        };
        return new CompiledScenario(scenario, ordered, board, properties,
            info.Teams.OrderBy(t => t.Ordinal).Select(t => t.Id).ToList(), seats.Select(s => s.Id).ToList(),
            seats.ToDictionary(s => s.Id, s => s.TeamId!.Value), board.Table, recorder);
    }

    /// <summary>One session per participant of the compiled game, in its order (SocketDriver's credential index), each its account's.</summary>
    public static SocketCredentials Credentials(CompiledScenario compiled, IReadOnlyDictionary<Guid, MintedSession> byAccount,
        Func<Guid, CancellationToken, Task<MintedSession>> refresh) =>
        new(compiled.Info.Participants.Select(p => byAccount[p.AccountId]).ToList())
        {
            Refresh = (i, ct) => refresh(compiled.Info.Participants[i].AccountId, ct),
        };
}
