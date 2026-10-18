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
    /// only one of the players may activate a single unit". Every seat deploys and activates its own units; every answer to an Ask about a unit
    /// is that unit's player's.
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
            Check("p.50: each player deploys and activates their own units, and answers for their own units", log =>
            {
                var owner = log.Run.Final.Units.Values.ToDictionary(u => u.Id, u => u.ParticipantId);
                var asks = new Dictionary<Guid, Ask>();
                var deployed = new HashSet<Guid>(); var activated = new HashSet<Guid>();
                foreach (var e in log.Events)
                {
                    if (e.Payload is AskOpened o) asks[o.Ask.Id] = o.Ask;
                    if (e.Payload is not AskAnswered a || !asks.TryGetValue(a.AskId, out var ask) || e.Actor is not { } actor) continue;
                    if (ask.Seat is { } seat && seat != actor) return $"seq {e.Seq}: '{ask.Question}' about another player's unit was answered by {actor}";
                    if (!ask.OwnUnitsOnly) continue;
                    IEnumerable<Guid> picked = a.Answer switch
                    {
                        Chosen c when ask.Legal is Options opts => opts.Choices.Where(x => x.Id == c.OptionId).SelectMany(x => x.Pieces),
                        SelectedPieces s => s.PieceIds,
                        _ => [],
                    };
                    foreach (var unit in picked.Where(owner.ContainsKey))
                    {
                        if (owner[unit] != actor) return $"seq {e.Seq}: '{ask.Question}' picked another player's unit";
                        (ask.Question == AskQuestions.Deploy ? deployed : activated).Add(actor);
                    }
                }
                var seats = owner.Values.Distinct().Count();
                return seats != 4 ? $"{seats} player(s) brought a unit" : deployed.Count != 4 ? $"{deployed.Count} player(s) deployed" : activated.Count != 4 ? $"{activated.Count} player(s) activated" : null;
            }),
            FinalRound(Enforcement.Strict),
        ]);
    }
}
