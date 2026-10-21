using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Tabletop.Protocol;

namespace Tabletop.Sim.Drivers;

/// <summary>The read side of a client socket, shared by SocketDriver and the lifecycle suite's ClientSocket.</summary>
public static class SocketFrames
{
    /// <summary>
    /// Whole frames off the socket into the inbox, until the socket closes or `stopping` fires; the inbox completes either way. The first
    /// thing that broke the loop goes to `fault`: a server close as an IOException with its status and reason (a server fault closes
    /// InternalServerError with one), or the transport, framing or JSON exception.
    /// </summary>
    public static async Task PumpAsync(WebSocket socket, ChannelWriter<SocketMessage> inbox, Action<Exception> fault, CancellationToken stopping)
    {
        var buffer = new byte[4 * 1024 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var total = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (total == buffer.Length) throw new InvalidOperationException($"a frame larger than {buffer.Length} bytes");
                    result = await socket.ReceiveAsync(new Memory<byte>(buffer, total, buffer.Length - total), stopping);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        fault(new IOException($"the server closed the socket: {socket.CloseStatus} {socket.CloseStatusDescription}"));
                        inbox.TryComplete();
                        return;
                    }
                    total += result.Count;
                } while (!result.EndOfMessage);
                inbox.TryWrite(WireJson.Deserialize(Encoding.UTF8.GetString(buffer, 0, total)));
            }
        }
        catch (OperationCanceledException) { inbox.TryComplete(); }
        catch (Exception ex)
        {
            fault(ex);
            inbox.TryComplete(ex);
        }
        finally { inbox.TryComplete(); }
    }
}
