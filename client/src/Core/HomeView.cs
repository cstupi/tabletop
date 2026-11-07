using System.Globalization;
using System.Text.RegularExpressions;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

public enum HomeAction { Play, Continue, Open, Replay, Join, End, Delete, Leave }

/// <summary>One game on Home (UX redesign part 1 → Home): its edition, the first three players and how many more, one state line (StateWarns:
/// in warning colour), the primary action on the card and the rest behind its ▸ menu.</summary>
public sealed record HomeCard(GameSummary Game, string Edition, IReadOnlyList<string> Players, int MorePlayers, string State, bool StateWarns,
    HomeAction Primary, IReadOnlyList<HomeAction> Menu);

/// <summary>Home's sections: my games not ended and those ended in the last RecentDays (cards), the others not ended (rows with Join), my
/// other ended games (rows with Replay; empty unless asked for). HasAny: I hold a game at all, ended or not, whatever the search.</summary>
public sealed record HomeLists(IReadOnlyList<HomeCard> Yours, IReadOnlyList<HomeCard> Open, IReadOnlyList<HomeCard> Ended, bool HasAny);

/// <summary>What Home shows for the games management lists. The viewer's games page reads the same (viewer/src/lib/games/homeView.ts).</summary>
public static partial class HomeView
{
    public const int PlayersShown = 3;
    /// <summary>How long an ended game of mine stays among Your games.</summary>
    public const int RecentDays = 7;

    public static HomeLists Of(IEnumerable<GameSummary> games, Guid? me, string search, bool endedToo, DateTimeOffset now)
    {
        var shown = games.Where(g => search.Length == 0 || g.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.CreatedAt).ThenByDescending(g => g.Id).ToList();
        List<HomeCard> Cards(Func<GameSummary, bool> keep) => shown.Where(keep).Select(g => CardOf(g, me, now)).ToList();
        bool Recent(GameSummary g) => g.EndedAt is { } at && now - at <= TimeSpan.FromDays(RecentDays);
        return new(
            Cards(g => g.Joined && (g.Status != GameStatus.Ended || Recent(g))),
            Cards(g => !g.Joined && g.Status != GameStatus.Ended),
            endedToo ? Cards(g => g.Joined && g.Status == GameStatus.Ended && !Recent(g)) : [],
            games.Any(g => g.Joined));
    }

    public static HomeCard CardOf(GameSummary g, Guid? me, DateTimeOffset now)
    {
        var names = g.ParticipantNames ?? [];
        var (state, warns) = StateOf(g, me, now);
        return new(g, EditionOf(g.RuleSet), names.Take(PlayersShown).ToList(), Math.Max(0, names.Count - PlayersShown), state, warns, PrimaryOf(g), MenuOf(g, me));
    }

    static HomeAction PrimaryOf(GameSummary g) => !g.Joined ? HomeAction.Join : g.Status switch
    {
        GameStatus.Lobby => HomeAction.Open,
        GameStatus.Setup => HomeAction.Continue,
        GameStatus.Playing => HomeAction.Play,
        _ => HomeAction.Replay,
    };

    /// <summary>Management takes End and Delete from the creator alone; End only before the end. Leave: any game of mine not ended.</summary>
    static List<HomeAction> MenuOf(GameSummary g, Guid? me)
    {
        if (!g.Joined) return [];
        var creator = me is { } id && g.CreatedBy == id;
        var menu = new List<HomeAction>();
        if (g.Status == GameStatus.Playing) menu.Add(HomeAction.Replay);
        if (creator && g.Status != GameStatus.Ended) menu.Add(HomeAction.End);
        if (creator) menu.Add(HomeAction.Delete);
        if (g.Status != GameStatus.Ended) menu.Add(HomeAction.Leave);
        return menu;
    }

    static (string, bool) StateOf(GameSummary g, Guid? me, DateTimeOffset now)
    {
        var p = g.Progress;
        switch (g.Status)
        {
            case GameStatus.Lobby when g.Teams > 0: return ($"{g.TeamsReady} of {g.Teams} teams ready", false);
            case GameStatus.Setup: return ("Laying the table", false);
            case GameStatus.Playing when p is not null:
                if (me is { } id && p.WaitingOn.Contains(id)) return ("Your move", true);
                var round = p.Round > 0 ? $"Round {p.Round}" : "Deployment";
                return (p.ActingTeam is { } team ? $"{round} · Team {team}'s turn" : round, false);
            case GameStatus.Ended when p?.Outcome is { } winners:
                return (winners.Count switch { 0 => "No result", 1 => $"Team {winners[0]} wins", _ => "Draw" }, false);
            default: return (Ago(g.CreatedAt, now), false);
        }
    }

    /// <summary>The version in a rule set's name ("Grimdark Future v3.6.0" → "3.6.0"); the name when it carries none.</summary>
    public static string EditionOf(string? ruleSet) => ruleSet is null ? "" : Version().Match(ruleSet) is { Success: true } m ? m.Value : ruleSet;

    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex Version();

    public static string Label(HomeAction a) => a switch
    {
        HomeAction.End => "End game",
        HomeAction.Delete => "Delete game",
        _ => a.ToString(),
    };

    /// <summary>"just now", "5 minutes ago", "3 hours ago", "yesterday", "4 days ago", then the date.</summary>
    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var minutes = (int)Math.Round((now - then).TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 1) return "just now";
        if (minutes < 60) return $"{minutes} minute{(minutes == 1 ? "" : "s")} ago";
        var hours = (int)Math.Round(minutes / 60.0, MidpointRounding.AwayFromZero);
        if (hours < 24) return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
        var days = (int)Math.Round(hours / 24.0, MidpointRounding.AwayFromZero);
        if (days == 1) return "yesterday";
        if (days < 14) return $"{days} days ago";
        return then.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }
}
