using Tabletop.Protocol;
namespace Tabletop.Client.Core;

public sealed record Banner(string Text, double Seconds);

/// <summary>Spec → Turn banner: "Round n" on RoundStarted from round 1, "Team k's turn" on a TurnStarted whose team differs from the previous TurnStarted's.
/// A batch while Session is catching up, a batch with an undo, and a disabled banner show nothing but still track the team.</summary>
public sealed class TurnBanners
{
    public const double RoundSeconds = 1.5, TurnSeconds = 1.0;
    Guid? _lastTeam;
    /// <summary>Bound mid-game: the active team is the last TurnStarted's.</summary>
    public void Seed(Guid? activeTeam) => _lastTeam = activeTeam;
    public void OnBoard() => _lastTeam = null;

    public IReadOnlyList<Banner> OnBatch(IReadOnlyList<GameEvent> batch, Names n, bool enabled, bool catchingUp)
    {
        var shown = new List<Banner>();
        foreach (var e in batch)
            switch (e.Payload)
            {
                case RoundStarted { Round: > 0 } r: shown.Add(new($"Round {r.Round}", RoundSeconds)); break;
                case TurnStarted t when t.TeamId != _lastTeam: _lastTeam = t.TeamId; shown.Add(new($"{n.Team(t.TeamId)}'s turn", TurnSeconds)); break;
            }
        var silent = catchingUp || !enabled || batch.Any(e => e.Payload is Undone);
        return silent ? [] : shown;
    }
}
