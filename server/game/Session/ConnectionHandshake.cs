using System.Net.WebSockets;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Connect → Welcome | Rejected. Then the read loop, until the peer closes, breaks the protocol, is replaced or its session expires.
/// Close reasons sent to the peer are fixed phrases; what went wrong is in the server log.</summary>
public sealed class ConnectionHandshake(GameServerOptions options, ISessionVerifier verifier, IGameRows rows, SessionRegistry registry, AccountSockets accounts,
    TimeProvider time, ILogger<ConnectionHandshake> log)
{
    public async Task RunAsync(WebSocket socket, CancellationToken aborted)
    {
        SocketMessage? first;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted))
        {
            timeout.CancelAfter(options.HandshakeTimeout);
            try { first = (await SocketConnection.ReadFrameAsync(socket, options.ConnectFrameBytes, timeout.Token))?.Message; }
            catch (Exception ex) when (ex is OperationCanceledException or System.Net.ProtocolViolationException or WebSocketException or System.IO.IOException)
            { await CloseAsync(socket, ex is ProtocolBreachException { Status: var s } ? s : WebSocketCloseStatus.ProtocolError, "expected a connect frame"); return; }
        }
        if (first is not Connect connect) { await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "the first frame must be connect"); return; }

        if (connect.ClientVersion < options.MinimumClientVersion) { await RejectAsync(socket, RejectReason.UpgradeRequired); return; }

        // A verifier or database exception (a JWKS fetch failure, a database outage) means the server could not check the
        // session, not that the session is bad — that is Rejected(InvalidSession)'s job. "Could not check" closes 1011.
        VerifiedSession? session;
        try { session = await verifier.VerifyAsync(connect.SessionJwt, aborted); }
        catch (Exception ex)
        { log.LogError(ex, "cannot verify the session for {GameId}", connect.GameId); await CloseAsync(socket, WebSocketCloseStatus.InternalServerError, "cannot verify the session"); return; }
        if (session is null) { await RejectAsync(socket, RejectReason.InvalidSession); return; }

        // A participant connects as itself. An admin who is not one watches (admin watch spec); a game that does not exist is
        // refused to the admin exactly as to anyone else.
        Guid? participantId;
        bool watcher;
        try
        {
            participantId = await rows.FindParticipantAsync(connect.GameId, session.AccountId, aborted);
            watcher = participantId is null && session.IsAdmin && await rows.ReadGameAsync(connect.GameId, aborted) is not null;
        }
        catch (Exception ex)
        { log.LogError(ex, "cannot look up the participant for {GameId}", connect.GameId); await CloseAsync(socket, WebSocketCloseStatus.InternalServerError, "cannot verify the session"); return; }
        if (participantId is null && !watcher) { await RejectAsync(socket, RejectReason.NotAParticipant); return; }

        if (registry.Draining) { await CloseAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "shutting down"); return; }

        GameSession game;
        try { game = await registry.GetOrHydrateAsync(connect.GameId, aborted); }
        // ProjectionDesyncException here means the stored log does not apply to the stored snapshot, which is a server bug and
        // not something a reconnect can clear: every attempt replays the same event.
        catch (Exception ex) when (ex is not OperationCanceledException)
        { log.LogError(ex, "cannot hydrate {GameId}", connect.GameId); await CloseAsync(socket, WebSocketCloseStatus.InternalServerError, "cannot load this game"); return; }

        var connection = new SocketConnection(socket, participantId ?? Guid.Empty, session.AccountId, log, options.Outbound);
        var sending = connection.RunSendLoopAsync(CancellationToken.None);
        await accounts.AddAsync(connection);
        // The session's JWT is the authority for this socket only while it is valid: at its expiry the socket closes 1008 and the client
        // reconnects with the token it has refreshed since.
        await using var expiry = time.CreateTimer(_ => _ = connection.CloseAsync(WebSocketCloseStatus.PolicyViolation, "session expired").AsTask(),
            null, Clamp(session.ExpiresAt - time.GetUtcNow()), Timeout.InfiniteTimeSpan);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(aborted, connection.Closing);
        var limiter = new FrameLimiter(options, time);
        Task Join(GameSession g) => watcher ? g.WatchAsync(connection) : g.ConnectAsync(connection);
        try
        {
            try
            {
                try { await Join(game); }
                // ReleaseIfEmptyAsync can take the game's gate between the hydrate above and this connect — the previous
                // socket's departure emptying the session under this one. Nothing is lost, because the log and the rows hold
                // everything: hydrating again returns the instance that replaced it. Once more only, and not while shutting down.
                catch (SessionDrainedException)
                {
                    if (registry.Draining) { await connection.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "shutting down"); return; }
                    game = await registry.GetOrHydrateAsync(connect.GameId, aborted);
                    await Join(game);
                }
                while (connection.CloseRequested is null && await SocketConnection.ReadFrameAsync(socket, Math.Max(options.FrameBytes, options.DraftFrameBytes), reading.Token, CapOf) is { } inbound)
                {
                    if (connection.CloseRequested is not null) break;
                    var frame = inbound.Message;
                    if (!await limiter.AdmitAsync(frame, reading.Token)) continue;
                    if (!inbound.CleanText) { await RefuseTextAsync(connection, frame); continue; }
                    await game.ReceiveAsync(connection, frame);
                }
            }
            catch (ProtocolBreachException ex) { log.LogInformation("Connection {Connection}: {Reason}", connection.ConnectionId, ex.Message); await connection.CloseAsync(ex.Status, ex.Message); }
            // The transport reports a crashed client (no close handshake) as IOException, not WebSocketException; either way
            // the peer is gone, which is not a server failure. A cancelled read is this connection closing (replaced, expired).
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or System.IO.IOException) { }
            // Last resort: whatever a handler threw, this connection closes with a reason instead of the frame silently ending the
            // loop. Failures the session cannot survive have already called FaultAsync and closed every socket themselves.
            catch (Exception ex) { log.LogError(ex, "handling a frame for {GameId} failed", connect.GameId); await connection.CloseAsync(WebSocketCloseStatus.InternalServerError, "the server failed to handle a frame"); }
            finally
            {
                accounts.Remove(connection);
                await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye");
                try { await game.DetachAsync(connection); }
                finally { await sending; }
            }
        }
        finally { await registry.ReleaseIfEmptyAsync(game); }
    }

    /// <summary>A command or edit holding a control or bidi-override character is refused by its id; any other such frame is dropped.</summary>
    private static async Task RefuseTextAsync(SocketConnection connection, SocketMessage frame)
    {
        const string Detail = "text holds a control or bidi-override character";
        var id = frame switch { CommandMessage c => c.CommandId, EditMessage e => e.EditId, _ => (Guid?)null };
        if (id is { } refused) await connection.SendAsync(new Refused(refused, RefusalReason.Unknown, Detail));
    }

    /// <summary>A layout loaded whole may be larger than any other frame; presence is held smaller.</summary>
    private int CapOf(SocketMessage frame) => frame switch
    {
        EditMessage { Edit: ReplaceDraft } => options.DraftFrameBytes,
        PresenceMessage => options.PresenceFrameBytes,
        _ => options.FrameBytes,
    };

    private static TimeSpan Clamp(TimeSpan delay) => delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromDays(30) ? TimeSpan.FromDays(30) : delay;

    private static async Task RejectAsync(WebSocket socket, RejectReason reason)
    {
        try { await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(WireJson.Serialize(new Rejected(reason))), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception ex) when (ex is WebSocketException or System.IO.IOException) { return; }
        await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, reason.ToString());
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try { if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(status, SocketConnection.Truncate(reason), CancellationToken.None); }
        catch (Exception ex) when (ex is WebSocketException or System.IO.IOException) { }
    }
}

/// <summary>One connection's frame allowance (security review H4): commands, edits and sight queries wait for a token, so a flooding
/// peer is slowed rather than refused; a ping or presence frame over its own rate is dropped, as a later one supersedes it.</summary>
public sealed class FrameLimiter(GameServerOptions options, TimeProvider time)
{
    private readonly TokenBucket _actions = new(options.ActionRate, time), _pings = new(options.PingRate, time), _presence = new(options.PresenceRate, time);

    public async Task<bool> AdmitAsync(SocketMessage frame, CancellationToken ct)
    {
        switch (frame)
        {
            case PingMessage: return _pings.TryTake();
            case PresenceMessage: return _presence.TryTake();
            default: await _actions.TakeAsync(ct); return true;
        }
    }
}
