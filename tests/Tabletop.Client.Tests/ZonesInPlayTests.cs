using System.Net;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>H3: the client's projection carries the zones of the board it was sent at playing.</summary>
public class ZonesInPlayTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("tabletop-zones-tests").FullName;
    readonly FakeManagement _http = new();
    readonly FakeSocketFactory _sockets = new();
    readonly Session _s;

    public ZonesInPlayTests() => _s = new Session(new ManagementClient(new HttpClient(_http), new Uri("http://localhost:5100")), _sockets,
        new SessionStore(Path.Combine(_dir, "session.bin")), ClientSettings.Default, _ => { }, new ImmediateDispatcher(), _ => TimeSpan.Zero);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task The_board_in_play_keeps_the_zones()
    {
        var game = Samples.Game; var board = Samples.Board;            // each read mints fresh ids: read once
        _http.Answer(HttpStatusCode.OK, new SessionResponse("a.jwt.z", new AccountRow(game.Participants[0].AccountId, "Clay")));
        await _s.LoginWithPasswordAsync("c@example.com", "pw");
        await _s.ConnectAsync(game.Id);
        _sockets.Last.Deliver(new Welcome(Samples.P1, game));
        _sockets.Last.Deliver(new BoardMessage(board));

        Assert.Null(_s.Draft);
        Assert.Equal(2, board.Table.Zones.Count);
        Assert.Equal(board.Table.Zones.Select(z => (z.Id, z.Shape, z.TeamOrdinal)), _s.Projection!.Board.Table.Zones.Select(z => (z.Id, z.Shape, z.TeamOrdinal)));
    }
}
