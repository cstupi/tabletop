using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: one account plays both sides (game-flow spec → One account, several teams).</summary>
internal static partial class CoreGames
{
    // ---- core-self-play --------------------------------------------------------------------------------------------------------------

    /// <summary>One account holds both teams, a participant row each with its own army, and plays both through one connection: the roll-off,
    /// deployment and four rounds of activations, every answer acting as the seat of the team it answers for.</summary>
    public static TestGame SelfPlay()
    {
        const string name = "core-self-play";
        var scenario = new ScenarioBuilder(name, seed: 3)
            .RoundCap(5).Table(48, 36).SelfPlay()
            .Army("Red", 2, 2, weapons: [Rifle(), HandWeapon()])
            .Army("Blue", 2, 2, weapons: [Rifle(), HandWeapon()])
            .Build();
        return Game(name, scenario,
        [
            Check("self-play: both teams deploy and activate, each answer acting as its own team's seat", log =>
            {
                var teams = log.Deployments.Select(d => d.Team).Distinct().Count();
                if (teams != 2) return $"{teams} team(s) deployed";
                var activated = log.Activations.Select(a => a.Team).Distinct().Count();
                if (activated != 2) return $"{activated} team(s) activated";
                var seats = new Dictionary<Guid, Guid>();                                                  // actor seat -> the team its army is on
                foreach (var unit in log.Run.Final.Units.Values)
                    if (log.TeamOf(unit.Id) is { } team) seats[unit.ParticipantId] = team;
                if (seats.Count != 2) return $"{seats.Count} seat(s) brought a deployed army";
                foreach (var e in log.Events)
                    if (e.Payload is AskAnswered a && e.Actor is { } actor && seats.TryGetValue(actor, out var team) && team != a.TeamId)
                        return $"seq {e.Seq}: team {a.TeamId} answered as the other team's seat";
                return log.Events.Any(e => e.Payload is AskAnswered && e.Actor is { } x && !seats.ContainsKey(x)) ? "an answer acted as no seat" : null;
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }
}
