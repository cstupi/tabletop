using System.Net.WebSockets;

namespace Tabletop.Protocol;

/// <summary>
/// permessage-deflate on `/ws` (RFC 7692), as a .NET client offers it: context takeover both ways at the full 32 KB window.
/// A frame carrying the session JWT is sent uncompressed, so the token never enters the client's deflate window and no later
/// frame's compressed length depends on it.
/// </summary>
public static class SocketCompression
{
    public static WebSocketDeflateOptions Offer() => new() { ClientContextTakeover = true, ServerContextTakeover = true, ClientMaxWindowBits = 15, ServerMaxWindowBits = 15 };

    /// <summary>A whole message, uncompressed: the flags for a frame carrying the session JWT.</summary>
    public const WebSocketMessageFlags Uncompressed = WebSocketMessageFlags.EndOfMessage | WebSocketMessageFlags.DisableCompression;

    public static WebSocketMessageFlags FlagsFor(SocketMessage message) => message is Connect or Reauth ? Uncompressed : WebSocketMessageFlags.EndOfMessage;
}
