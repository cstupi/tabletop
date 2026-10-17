using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: a Transport deployed with units inside (p.15; passengers spec).</summary>
internal static partial class CoreGames
{
    /// <summary>
    /// p.15 Transport(X): Red unit 1 is a Transport(6); Red units 2 and 3 go inside it before deployment and deploy with it. Under strict, so every
    /// hand move the run makes is one the server allows.
    /// </summary>
    public static TestGame TransportDeploy()
    {
        const string name = "core-transport-deploy";
        var scenario = new ScenarioBuilder(name, seed: 3)
            .RoundCap(4).Table(24, 36)
            .Army("Red", 4, 1, modelRules: [new ScenarioRule(1, "Transport", 6), new ScenarioRule(1, "Tough", 3)], weapons: [Rifle(), HandWeapon()],
                passengers: new Dictionary<int, int> { [2] = 1, [3] = 1 })
            .Army("Blue", 3, 3, weapons: [Rifle(2), HandWeapon()])
            .Seeking(NearestEnemy)
            .Build();
        return Game(name, scenario,
        [
            Check("p.15: \"Transports may deploy with units inside\": the passengers went in from the shelf before their Transport was placed", log =>
            {
                var (transport, passengers) = Riders(log);
                var placed = log.Moves.FirstOrDefault(m => log.UnitOf(m.Move.ModelId) == transport && m.Move is { From: OnShelf, To: OnTable });
                if (placed.Move is null) return "the Transport never deployed";
                foreach (var unit in passengers)
                {
                    var first = log.Moves.Where(m => log.UnitOf(m.Move.ModelId) == unit).Select(m => (m.Seq, m.Move)).FirstOrDefault();
                    if (first.Move is not { From: OnShelf, To: Inside }) return $"{Name(log, unit)} did not go from the shelf into the Transport first";
                    if (first.Seq > placed.Seq) return $"{Name(log, unit)} went in after the Transport was placed";
                }
                return null;
            }, "Transport"),
            Check("p.15: \"units inside Transports are deployed at the same time as the Transport\": never a deploy pick, inside it on the table at round one", log =>
            {
                var (transport, passengers) = Riders(log);
                if (log.Deployments.FirstOrDefault(d => passengers.Contains(d.Unit)) is { Question: not null } d) return $"{Name(log, d.Unit)} was picked to deploy";
                if (log.Rounds.Count == 0) return "round one never started";
                var roundOne = log.Events.First(e => e.Payload is RoundStarted { Round: 1 }).Seq;
                var at = log.LocationsBefore(roundOne);
                var hull = log.Run.Final.Units[transport].Definition.Models[0].Id;
                if (at[hull] is not OnTable) return "the Transport was not on the table at round one";
                return passengers.SelectMany(u => log.Run.Final.Units[u].Definition.Models).FirstOrDefault(m => at[m.Id] != new Inside(hull)) is { } outside
                    ? $"{Name(log, log.UnitOf(outside.Id))} was not inside the Transport at round one" : null;
            }, "Transport"),
            Check("p.15: the passengers are in play once their Transport is: each activates", log =>
            {
                var (_, passengers) = Riders(log);
                return passengers.FirstOrDefault(u => log.Activations.All(a => a.Unit != u)) is var idle && idle != Guid.Empty ? $"{Name(log, idle)} never activated" : null;
            }, "Transport"),
        ], covers: ["Transport"]);
    }

    static (Guid Transport, IReadOnlyList<Guid> Passengers) Riders(CoreLog log)
    {
        Guid Unit(string name) => log.Run.Final.Units.Values.Single(u => u.Definition.Name == name).Id;
        return (Unit("Red unit 1"), [Unit("Red unit 2"), Unit("Red unit 3")]);
    }

    static string Name(CoreLog log, Guid unit) => log.Run.Final.Units[unit].Definition.Name;
}
