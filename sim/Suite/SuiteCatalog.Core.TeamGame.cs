using Tabletop.Protocol;
using Tabletop.Sim.Scenarios;

namespace Tabletop.Sim.Suite;

/// <summary>Core games: two players a side, each with their own army (rulebook p.50 Team Games; game-flow spec → Participants).</summary>
internal static partial class CoreGames
{
    // ---- core-team-game --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Four players, two a side, an army each. p.50: "players from the same team must deploy their units in the same deployment zone, placing a
    /// single unit when it's their team's turn to deploy"; "the turn order is on a team basis and not on a player basis, so for each team's turn
    /// only one of the players may activate a single unit". Owner ruling: "Either player from the team can take the activation across any unit in
    /// their team." The run's seats take turns answering their team's Asks, so players deploy and activate their teammates' units.
    /// </summary>
    public static TestGame TeamGame()
    {
        const string name = "core-team-game";
        var scenario = new ScenarioBuilder(name, seed: 5)
            .RoundCap(5).Table(48, 36)
            .Army("Red", 2, 2, weapons: [Rifle(), HandWeapon()], team: 1)
            .Army("Blue", 2, 2, weapons: [Rifle(), HandWeapon()], team: 2)
            .Army("Crimson", 2, 2, weapons: [Rifle(), HandWeapon()], team: 1)
            .Army("Azure", 2, 2, weapons: [Rifle(), HandWeapon()], team: 2)
            .Build();
        return Game(name, scenario,
        [
            Check("p.50: teams take turns to deploy, one unit a turn, whichever of their players places it", log =>
            {
                var order = log.Deployments.Where(d => d.Question == AskQuestions.Deploy).Select(d => d.Team).ToList();
                if (order.Count != 8) return $"{order.Count} unit(s) deployed; 8 expected";
                for (var i = 1; i < order.Count; i++)
                    if (order[i] == order[i - 1]) return $"deployment {i + 1} was the same team's as the one before it";
                return null;
            }),
            Check("owner ruling: either player of a team takes the activation, for any unit of the team", log =>
            {
                var owner = log.Run.Final.Units.Values.ToDictionary(u => u.Id, u => u.ParticipantId);
                var teamOf = new Dictionary<Guid, Guid>();                                          // seat → team, from the team each answered for
                var asks = new Dictionary<Guid, Ask>();
                var deployed = new HashSet<Guid>(); var activated = new HashSet<Guid>();
                var activations = new List<(Guid Actor, Guid Owner)>();
                foreach (var e in log.Events)
                {
                    if (e.Payload is AskOpened o) asks[o.Ask.Id] = o.Ask;
                    if (e.Payload is not AskAnswered a || !asks.TryGetValue(a.AskId, out var ask) || e.Actor is not { } actor) continue;
                    teamOf[actor] = a.TeamId;
                    if (ask.Question is not (AskQuestions.Deploy or AskQuestions.Activate)) continue;
                    IEnumerable<Guid> picked = a.Answer switch
                    {
                        Chosen c when ask.Legal is Options opts => opts.Choices.Where(x => x.Id == c.OptionId).SelectMany(x => x.Pieces),
                        SelectedPieces s => s.PieceIds,
                        _ => [],
                    };
                    foreach (var unit in picked.Where(owner.ContainsKey))
                    {
                        if (ask.Question == AskQuestions.Activate) activations.Add((actor, owner[unit]));
                        (ask.Question == AskQuestions.Deploy ? deployed : activated).Add(actor);
                    }
                }
                if (activations.FirstOrDefault(x => teamOf.GetValueOrDefault(x.Owner) != teamOf[x.Actor]) is { Actor: var stranger } && stranger != Guid.Empty)
                    return $"{stranger} activated another team's unit";
                var teammates = activations.Count(x => x.Owner != x.Actor);
                var seats = owner.Values.Distinct().Count();
                return seats != 4 ? $"{seats} player(s) brought a unit" : deployed.Count != 4 ? $"{deployed.Count} player(s) deployed"
                    : activated.Count != 4 ? $"{activated.Count} player(s) activated" : teammates == 0 ? "no player activated a teammate's unit" : null;
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }
}
