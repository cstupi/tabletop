namespace Tabletop.Protocol;

/// <summary>Where a game in play stands, as the game list shows it (UX redesign part 1 → Home): the round (0 before the first), the
/// acting team's ordinal, the accounts an open Ask waits on, and once ended the winners' ordinals (one wins, several draw, none is no
/// result). The game server writes it on the game row after each run of the log; management lists it.</summary>
public sealed record GameProgress(int Round, int? ActingTeam, IReadOnlyList<Guid> WaitingOn, IReadOnlyList<int>? Outcome)
{
    public static GameProgress Of(Projection p, GameInfo game)
    {
        int? Ordinal(Guid team) => game.Teams.FirstOrDefault(t => t.Id == team)?.Ordinal;
        List<Guid> waiting = p.Ended is not null ? [] : p.OpenAsks.Values
            .SelectMany(open => open.Ask.Teams.Where(t => !open.Answers.ContainsKey(t)))
            .SelectMany(team => game.Participants.Where(x => x.TeamId == team).Select(x => x.AccountId))
            .Distinct().Order().ToList();
        var outcome = p.Ended?.Winners.Select(Ordinal).OfType<int>().Order().ToList();
        return new(p.Scheduler.Round, p.Scheduler.ActiveTeamId is { } active ? Ordinal(active) : null, waiting, outcome);
    }

    /// <summary>Record equality compares the lists by reference; this compares their items.</summary>
    public bool SameAs(GameProgress? other) => other is not null && Round == other.Round && ActingTeam == other.ActingTeam
        && WaitingOn.SequenceEqual(other.WaitingOn) && (Outcome is null ? other.Outcome is null : other.Outcome is not null && Outcome.SequenceEqual(other.Outcome));
}
