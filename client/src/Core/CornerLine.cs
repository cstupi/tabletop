using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The game screen's corner line, beside Home: the game's name, which the corner clips, and after it the tail it never clips (the
/// status, "watching", the reconnect). Before the game arrives the name is what the socket is doing and the tail is empty.</summary>
public static class CornerLine
{
    public static (string Name, string Tail) Of(SessionPhase phase, int attempt, GameInfo? game, bool watching)
    {
        var reconnecting = phase == SessionPhase.Reconnecting
            ? attempt > 1 ? $"reconnecting, attempt {attempt}; Home stops it…" : "reconnecting…"
            : null;
        if (game is null) return (reconnecting is null ? "Connecting…" : char.ToUpperInvariant(reconnecting[0]) + reconnecting[1..], "");
        var tail = $"— {game.Status.ToString().ToLowerInvariant()}" + (watching ? " — watching" : "");
        return (game.Name, reconnecting is null ? tail : $"{tail} ({reconnecting})");
    }
}
