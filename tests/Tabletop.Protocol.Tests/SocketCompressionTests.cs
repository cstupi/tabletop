using System.Net.WebSockets;
using Tabletop.Protocol;

namespace Tabletop.Protocol.Tests;

public class SocketCompressionTests
{
    [Fact]
    public void The_offer_keeps_context_both_ways_at_full_window()
    {
        var offer = SocketCompression.Offer();
        Assert.True(offer.ClientContextTakeover);
        Assert.True(offer.ServerContextTakeover);
        Assert.Equal(15, offer.ClientMaxWindowBits);
        Assert.Equal(15, offer.ServerMaxWindowBits);
    }

    [Fact]
    public void A_frame_carrying_the_session_jwt_is_sent_uncompressed_and_every_other_frame_compressed()
    {
        const WebSocketMessageFlags Plain = WebSocketMessageFlags.EndOfMessage | WebSocketMessageFlags.DisableCompression;
        Assert.Equal(Plain, SocketCompression.FlagsFor(new Connect("jwt", Guid.NewGuid(), 1)));
        Assert.Equal(Plain, SocketCompression.FlagsFor(new Reauth(Guid.NewGuid(), "jwt")));
        Assert.Equal(WebSocketMessageFlags.EndOfMessage, SocketCompression.FlagsFor(new SightQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())));
    }
}
