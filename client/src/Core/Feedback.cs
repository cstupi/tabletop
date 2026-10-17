using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// What the player reads when the server says no or the connection changes: a refused command or edit, a socket close by code, a
/// rejected connect, a failed management request. Every message keeps the server's own detail.
/// </summary>
public static class Feedback
{
    /// <summary>The toast for a refusal. Null for a watcher's refused presence or ping (message id empty): the client stops sending
    /// those once it knows it watches, and a stray one is not news.</summary>
    public static string? Refusal(Refused r)
    {
        if (r.Reason == RefusalReason.Watcher) return r.MessageId == Guid.Empty ? null : "You are watching this game: a watcher cannot change it.";
        var kind = r.Reason switch
        {
            RefusalReason.OutsideLegalSet => "Not one of the choices offered",
            RefusalReason.Illegal => "Against the rules",
            RefusalReason.WrongStatus => "Not at this point of the game",
            RefusalReason.NotPermitted => "Not allowed",
            _ => "Refused",
        };
        return With(kind, r.Detail);
    }

    /// <summary>The toast for a closed socket or a resync, by close code (the game server's codes: 1000 a replaced socket, 1001 shutting
    /// down, 1008 the JWT's expiry, 1009 a frame too large, 1011 a server failure, 1013 a client too far behind).</summary>
    public static string Closed(SocketClosed c, bool willReconnect)
    {
        if (c.Error is ProjectionDesyncException)
            return willReconnect ? "The table fell out of step with the server; rebuilding it from the log…" : $"This game's log cannot be replayed: {c.Reason}";
        var reason = string.IsNullOrWhiteSpace(c.Reason) ? null : c.Reason;
        var what = c.Code switch
        {
            1000 when reason?.Contains("too many", StringComparison.OrdinalIgnoreCase) == true => "Your account has too many game windows open; the oldest closed",
            1000 when reason?.Contains("replaced", StringComparison.OrdinalIgnoreCase) == true => "This game was opened in another window, which took over",
            1001 => "The game server is restarting",
            1008 => willReconnect ? "Your sign-in on the game server expired" : "Your sign-in on the game server expired; sign in again",
            1009 => "A message was larger than the server accepts",
            1011 => reason is null ? "The game server failed" : $"The game server failed ({reason})",
            1013 => "The connection fell too far behind the game",
            null when c.Error is not null => reason is null ? "Cannot reach the game server" : $"Cannot reach the game server ({reason})",
            null => "The connection dropped",
            _ => reason is null ? $"Disconnected (code {c.Code})" : $"Disconnected: {reason}",
        };
        if (!willReconnect) return what + ".";
        return c.Code == 1008 ? what + "; reconnecting with a fresh one…" : what + "; reconnecting…";
    }

    /// <summary>Home's line for a connect the server rejected.</summary>
    public static string Rejected(RejectReason reason) => reason switch
    {
        RejectReason.UpgradeRequired => "This client is older than the game server accepts.",
        RejectReason.InvalidSession => "The game server did not accept your sign-in. Sign out and in again if it happens again.",
        RejectReason.NotAParticipant => "You are not in that game: join it from the list.",
        _ => reason.ToString(),
    };

    /// <summary>A failed management request, an unreachable server or anything else a screen's action threw.</summary>
    public static string Error(Exception e, string managementUrl) => e switch
    {
        UpdateRequiredException => "This client is older than the server accepts.",
        ManagementException { Status: 401 } => "Your sign-in has expired: sign in again.",
        ManagementException { Status: 403 } m => With("Not allowed", m.Detail),
        ManagementException { Status: 404 } m => With("Not found", m.Detail),
        ManagementException { Status: 413 } m => m.Detail is { Length: > 0 } d ? With("Too large for the server", d) : "Too large for the server.",
        ManagementException { Status: 422 } m => m.Detail is { Length: > 0 } d ? d : m.Title,
        ManagementException { Status: 429 } m => m.Detail is { Length: > 0 } d ? d : "Too many requests. Wait a minute and try again.",
        ManagementException { Status: >= 500 } m => $"The server failed ({m.Title}). Try again shortly.",
        ManagementException m => m.Detail is null ? m.Title : With(m.Title, m.Detail),
        HttpRequestException h => $"Cannot reach {managementUrl}: {h.Message}",
        TaskCanceledException => $"{managementUrl} did not answer in time.",
        _ => e.Message,
    };

    /// <summary>"Kind: detail." keeping the detail's own stop; "Kind." without one.</summary>
    static string With(string kind, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return kind + ".";
        detail = detail.Trim();
        return detail[^1] is '.' or '!' or '?' ? $"{kind}: {detail}" : $"{kind}: {detail}.";
    }
}
