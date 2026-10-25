using System.Net;

namespace Tabletop.Client.Core;

/// <summary>The client's one HttpClient: it offers br, gzip and deflate and decodes what management compresses.</summary>
public static class ClientHttp
{
    public static SocketsHttpHandler Handler() => new() { AutomaticDecompression = DecompressionMethods.All };
    public static HttpClient Create() => new(Handler());
}
