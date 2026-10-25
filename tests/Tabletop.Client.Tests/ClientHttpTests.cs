using System.Net;
using Tabletop.Client.Core;

namespace Tabletop.Client.Tests;

public class ClientHttpTests
{
    [Fact]
    public void The_handler_asks_for_and_decodes_every_compression_management_sends()
    {
        using var handler = ClientHttp.Handler();
        Assert.Equal(DecompressionMethods.All, handler.AutomaticDecompression);
    }
}
