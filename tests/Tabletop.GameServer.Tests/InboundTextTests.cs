using System.Text;
using Tabletop.Content;
using Tabletop.GameServer.Session;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Content-path review: control and bidi-override characters in any string from the socket; security review M3: presence bounds.</summary>
public class InboundTextTests
{
    static bool Clean(SocketMessage m) => InboundText.IsClean(Encoding.UTF8.GetBytes(WireJson.Serialize(m)));
    static EditMessage Label(string label) => new(Guid.NewGuid(), new AddSetupMarker(new Marker(Guid.NewGuid(), MarkerKind.Objective, new Point3(1, 1, 0), new Yaw(0), 3, label, VisualRef.OfBuiltin("objective-ring"))));

    [Theory]
    [InlineData("a\u0000b")] [InlineData("line\nbreak")] [InlineData("‮evil")] [InlineData("x⁦y")] [InlineData("del\u007F")]
    public void Control_and_bidi_characters_are_caught(string text) => Assert.False(Clean(Label(text)));

    [Theory]
    [InlineData("Objective A")] [InlineData("Ünïcödé — ok")] [InlineData("日本語")]
    public void Ordinary_text_passes(string text) => Assert.True(Clean(Label(text)));

    [Fact]
    public void Json_too_deep_to_walk_is_not_clean() =>
        Assert.False(InboundText.IsClean(Encoding.UTF8.GetBytes(new string('[', 100) + new string(']', 100))));

    [Fact]
    public void A_property_name_is_checked_too() =>
        Assert.False(InboundText.IsClean(Encoding.UTF8.GetBytes("{\"kind\":\"ping\",\"a\\u0000\":1}")));

    [Fact]
    public async Task An_edit_with_a_bidi_label_is_refused_by_its_id()
    {
        var db = new InMemorySeeder();
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Setup);
        await db.SeedTableStateAsync(seeded.GameId);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.ExpectAsync<AtRest>();
        var edit = Label("‮evil");
        await client.SendAsync(edit);
        var refused = await client.ExpectAsync<Refused>();
        Assert.Equal(edit.EditId, refused.MessageId);
    }

    [Fact]
    public void Presence_over_its_bounds_or_not_finite_is_dropped()
    {
        Presence P(int placements = 0, int models = 0, double x = 1) => new(Guid.Empty, new Cursor(new(x, 1, 0), null), new Selection(Guid.NewGuid(), Enumerable.Range(0, models).Select(_ => Guid.NewGuid()).ToList()),
            Enumerable.Range(0, placements).Select(_ => new PresencePlacement(Guid.NewGuid(), new(1, 1, 0), new Yaw(0), PlacementKind.Held)).ToList(), null);
        Assert.True(PresenceLimits.Holds(P(64, 64)));
        Assert.False(PresenceLimits.Holds(P(placements: 65)));
        Assert.False(PresenceLimits.Holds(P(models: 65)));
        Assert.False(PresenceLimits.Holds(P(x: double.NaN)));
        Assert.True(PresenceLimits.Holds(P() with { Measurement = new Measurement(new(0, 0, 0), new(6, 0, 3), MeasureMode.Straight) }));
        Assert.False(PresenceLimits.Holds(P() with { Measurement = new Measurement(new(0, 0, 0), new(6, 0, 3), (MeasureMode)7) }));
        Assert.False(PresenceLimits.Holds(new Ping(Guid.Empty, new(double.PositiveInfinity, 0, 0), null)));
    }
}
