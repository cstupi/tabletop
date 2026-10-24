using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: four teams of two players, an army each (four-team games spec; rulebook p.50 Free-for-all and Team Games).</summary>
internal static partial class CoreGames
{
    // ---- core-four-teams -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Eight players, two a team on four teams, an army each, on p.50's free-for-all corners. The checks are FreeForAllChecks': the roll-off ranks
    /// every team, deployment goes round them "in descending order" one unit a turn, every unit stands in its team's corner, round one follows the
    /// deployment order and later rounds who "finished activating first", every team takes its turns, and the winners hold the most markers.
    /// </summary>
    public static TestGame FourTeams()
    {
        const string name = "core-four-teams";
        var builder = new ScenarioBuilder(name, seed: 11).RoundCap(5).Zones(ZoneStyles.FreeForAll)
            .Marker(MarkerKind.Objective, new Point3(30, 24, 0), label: "West").Marker(MarkerKind.Objective, new Point3(42, 24, 0), label: "East");
        string[] names = ["Red", "Blue", "Green", "Gold", "Crimson", "Azure", "Olive", "Amber"];
        for (var i = 0; i < names.Length; i++) builder = builder.Army(names[i], 2, 2, weapons: [Rifle(), HandWeapon()], team: i % 4 + 1);
        return Game(name, builder.Build(),
        [
            Queried("p.50: all players roll-off to determine the order in which they will deploy", log => FreeForAllChecks.RollOff(log, 4)),
            Queried("p.50: players alternate deploying one unit each, in descending order", log => FreeForAllChecks.DeploymentRotation(log)),
            Queried("p.50: each team deploys within its own 2'x1' corner, teammates in the same zone", log => FreeForAllChecks.DeployedInZones(log)),
            Queried("p.50: round one from the team that deployed first; later rounds from the team that finished activating first", log => FreeForAllChecks.TurnOrder(log)),
            Queried("p.50: for each team's turn only one of the players may activate a single unit", log => FreeForAllChecks.TurnRotation(log)),
            Queried("p.50: every team takes its turns in every round", log => FreeForAllChecks.EveryTeamActivated(log)),
            Queried("p.6: the player that controls most markers wins", log => FreeForAllChecks.Winners(log)),
            FinalRound(Enforcement.Strict),
        ]);
    }

    /// <summary>A check over the run's log read by LogQueries.</summary>
    static GameCheck Queried(string name, Func<LogQueries, string?> failure) => new(name, run => failure(LogQueries.Of(run)));
}
