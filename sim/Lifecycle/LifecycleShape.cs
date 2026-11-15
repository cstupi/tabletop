using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Sim.Lifecycle;

/// <summary>
/// An account of the shape (an index into LifecycleRunner.Players) on a team ordinal, bringing an army of its own: a whole list when List names
/// one (LifecycleArmy.Lists), else the three-unit cut of a book (an index into LifecycleArmy.Books).
/// </summary>
public sealed record Seat(int Account, int Team, int Book = 0, string? List = null);

/// <summary>
/// One game the lifecycle suite plays from creation to deletion. Account 0 creates it. Seats are taken in order, each by
/// the seat's own account, on an empty team or one other accounts hold (PickTeam). Late is
/// a seat left empty when setup ends and taken while deployment waits for players. Ambush: account 0's army carries Ambush on its
/// riders (LifecycleArmy.AmbushUnit), set aside before deployment. LobbyOnly: no play; the leave and delete of a lobby game. Rules: the game-rules id the game is created on, 3.6.0 unless
/// the shape names 3.5.1.
/// </summary>
public sealed record LifecycleShape(string Name, string Summary, Enforcement Enforcement, IReadOnlyList<Seat> Seats,
    Seat? Late = null, bool Ambush = false, bool LobbyOnly = false, string Rules = RuleSets.Gf360)
{
    /// <summary>The army edition the shape's rule set takes; its armies are imported stamped with it.</summary>
    public string Edition => RuleSets.EditionOf(Rules);
    public int Teams => Seats.Append(Late).OfType<Seat>().Select(s => s.Team).DefaultIfEmpty(2).Max();
    /// <summary>A seat brings a whole list: the game is checked as a whole game (LifecycleGame.Whole.cs), and every seat sets aside before
    /// deployment every unit the server's ModelFacts says it may (Scout, Ambush), as a player who uses those rules does.</summary>
    public bool Whole => Seats.Append(Late).OfType<Seat>().Any(s => s.List is not null);
    public IReadOnlyList<int> Accounts => Seats.Append(Late).OfType<Seat>().Select(s => s.Account).Append(0).Distinct().Order().ToList();
}

public static class LifecycleShapes
{
    public static IReadOnlyList<LifecycleShape> All { get; } =
    [
        new("two-players", "two accounts, one per team", Enforcement.Strict, [new(0, 1), new(1, 2)]),
        new("two-players-3.5", "two accounts, one per team, on the 3.5.1 rule set with 3.5 armies", Enforcement.Strict, [new(0, 1), new(1, 2)], Rules: RuleSets.Gf351),
        new("self-play", "one account holds both teams, an army each", Enforcement.Warn, [new(0, 1), new(0, 2)]),
        new("team-game", "two accounts a side; the creator places the second of each", Enforcement.Strict, [new(0, 1), new(2, 2), new(1, 1), new(3, 2)]),
        new("late-joiner", "setup ends with team 2 empty; a second account joins, takes it and brings an army", Enforcement.Strict, [new(0, 1)], Late: new(1, 2)),
        new("ambusher", "two accounts; the creator's riders ambush after round one", Enforcement.Strict, [new(0, 1), new(1, 2)], Ambush: true),
        new("four-teams", "eight accounts, two a team on four teams, armies from four books; the creator places the second of each", Enforcement.Strict,
            [new(0, 1, 0), new(2, 2, 1), new(4, 3, 2), new(6, 4, 3), new(1, 1, 1), new(3, 2, 2), new(5, 3, 3), new(7, 4, 0)]),
        new("goblins-vs-disciples", "two accounts, the whole Goblin Reclaimers and War Disciples lists", Enforcement.Strict,
            [new(0, 1, List: "goblin-reclaimers"), new(1, 2, List: "war-disciples")]),
        new("hdf-vs-ratmen", "two accounts, the whole HDF Artillery Blob and Ratmen Clans lists", Enforcement.Strict,
            [new(0, 1, List: "hdf-artillery-blob"), new(1, 2, List: "ratmen-clans")]),
        new("self-play-ratmen-hdf", "one account holds both teams, the whole Ratmen Clans and HDF Artillery Blob lists", Enforcement.Warn,
            [new(0, 1, List: "ratmen-clans"), new(0, 2, List: "hdf-artillery-blob")]),
        new("leave-and-delete", "a lobby game: a player joins, takes a team and leaves; the creator deletes it", Enforcement.Strict, [new(0, 1), new(1, 2)], LobbyOnly: true),
    ];

    /// <summary>The shapes named, in catalog order; none named is every shape. An unknown name is a usage error.</summary>
    public static IReadOnlyList<LifecycleShape> Select(IReadOnlyList<string> only)
    {
        var unknown = only.Where(n => All.All(s => s.Name != n)).ToList();
        if (unknown.Count > 0) throw new UsageException($"no lifecycle shape {string.Join(", ", unknown)}; known: {string.Join(", ", All.Select(s => s.Name))}");
        return only.Count == 0 ? All : All.Where(s => only.Contains(s.Name)).ToList();
    }
}
