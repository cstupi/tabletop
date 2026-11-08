using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The game screen's corner beside the home button (UX redesign part 2 → Frame): the game's name, which the corner clips; the status
/// chip with the game's real phase (Chip, null before the game arrives); the reconnect, a tail never clipped.</summary>
public sealed record CornerStatus(string Name, string? Chip, string Tail);

public static class CornerLine
{
    /// <summary>Before the game arrives the name is what the socket is doing. The chip: Replay, Watching, else Lobby, Setup, Deployment
    /// (playing before round 1), Round N, Ended.</summary>
    public static CornerStatus Of(SessionPhase phase, int attempt, GameInfo? game, int round, bool watching, bool replaying = false)
    {
        var reconnecting = phase == SessionPhase.Reconnecting
            ? attempt > 1 ? $"reconnecting, attempt {attempt}; Home stops it…" : "reconnecting…"
            : null;
        if (game is null) return new(reconnecting is null ? "Connecting…" : char.ToUpperInvariant(reconnecting[0]) + reconnecting[1..], null, "");
        var chip = replaying ? "Replay" : watching ? "Watching" : game.Status switch
        {
            GameStatus.Lobby => "Lobby",
            GameStatus.Setup => "Setup",
            GameStatus.Playing => round > 0 ? $"Round {round}" : "Deployment",
            _ => "Ended",
        };
        return new(game.Name, chip, reconnecting ?? "");
    }
}
