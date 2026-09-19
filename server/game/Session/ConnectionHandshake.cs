using System.Net;
using System.Net.WebSockets;
using Tabletop.GameServer.Auth;
using Tabletop.GameServer.Configuration;
using Tabletop.GameServer.Persistence;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Session;

/// <summary>Connect → Welcome | Rejected. Then the read loop, until the peer closes or breaks the protocol.</summary>
public sealed class ConnectionHandshake(GameServerOptions options, ISessionVerifier verifier, IGameRows rows, SessionRegistry registry, ILogger<ConnectionHandshake> log)
{
    public async Task RunAsync(WebSocket socket, CancellationToken aborted)
    {
        SocketMessage? first;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted))
        {
            timeout.CancelAfter(options.HandshakeTimeout);
            try { first = await SocketConnection.ReadFrameAsync(socket, timeout.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or ProtocolViolationException or WebSocketException)
            { await CloseAsync(socket, WebSocketCloseStatus.ProtocolError, "expected a connect frame"); return; }
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

        Guid? participantId;
        try { participantId = await rows.FindParticipantAsync(connect.GameId, session.AccountId, aborted); }
        catch (Exception ex)
        { log.LogError(ex, "cannot look up the participant for {GameId}", connect.GameId); await CloseAsync(socket, WebSocketCloseStatus.InternalServerError, "cannot verify the session"); return; }
        if (participantId is null) { await RejectAsync(socket, RejectReason.NotAParticipant); return; }

        if (registry.Draining) { await CloseAsync(socket, WebSocketCloseStatus.EndpointUnavailable, "shutting down"); return; }

        GameSession game;
        try { game = await registry.GetOrHydrateAsync(connect.GameId, aborted); }
        // ProjectionDesyncException here means the stored log does not apply to the stored snapshot, which is a server bug and
        // not something a reconnect can clear: every attempt replays the same event.
        catch (Exception ex) when (ex is GameNotFoundException or GameRowsInconsistentException or Rules.UnknownGameRulesException or ProjectionDesyncException)
        { log.LogError(ex, "cannot hydrate {GameId}", connect.GameId); await CloseAsync(socket, WebSocketCloseStatus.InternalServerError, "cannot hydrate this game"); return; }

        var connection = new SocketConnection(socket, participantId.Value, log);
        var sending = connection.RunSendLoopAsync(CancellationToken.None);
        await game.ConnectAsync(connection);
        try
        {
            while (await SocketConnection.ReadFrameAsync(socket, aborted) is { } frame) await game.ReceiveAsync(connection, frame);
        }
        catch (ProtocolViolationException ex) { await connection.CloseAsync(WebSocketCloseStatus.ProtocolError, ex.Message); }
        // The transport reports a crashed client (no close handshake) as IOException, not WebSocketException; either way
        // the peer is gone, which is not a server failure.
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or System.IO.IOException) { }
        // Last resort: whatever a handler threw, this connection closes with a reason instead of the frame silently ending the
        // loop. Failures the session cannot survive have already called FaultAsync and closed every socket themselves.
        catch (Exception ex) { log.LogError(ex, "handling a frame for {GameId} failed", connect.GameId); await connection.CloseAsync(WebSocketCloseStatus.InternalServerError, "the server failed to handle a frame"); }
        finally
        {
            await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye");
            await game.DetachAsync(connection);
            await sending;
            await registry.ReleaseIfEmptyAsync(game);
        }
    }

    private static async Task RejectAsync(WebSocket socket, RejectReason reason)
    {
        await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(WireJson.Serialize(new Rejected(reason))), WebSocketMessageType.Text, true, CancellationToken.None);
        await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, reason.ToString());
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        try { if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseOutputAsync(status, reason, CancellationToken.None); }
        catch (WebSocketException) { }
    }
}
