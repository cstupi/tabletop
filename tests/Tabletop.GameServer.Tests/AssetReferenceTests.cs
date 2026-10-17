using Tabletop.Content;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Content-path review: a setup edit references only assets in a participant's library.</summary>
public class AssetReferenceTests
{
    [Fact]
    public async Task A_texture_from_a_strangers_library_or_none_is_refused_and_a_participants_is_taken()
    {
        var db = new InMemorySeeder();
        using var factory = new GameServerFactory(db);
        var seeded = await db.SeedGameAsync(GameStatus.Setup);
        await db.SeedTableStateAsync(seeded.GameId);
        var stranger = await db.SeedAccountAsync("Stranger");
        var (mine, theirs, missing) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.GameRows.InsertAsset(mine, seeded.Participants[1].AccountId);
        db.GameRows.InsertAsset(theirs, stranger);
        await using var client = await TestClient.OpenAsync(factory);
        await client.ConnectAsync(seeded.CreatorAccountId, seeded.GameId);
        await client.ExpectAsync<AtRest>();

        db.GameRows.InsertAsset(Guid.Empty, stranger);   // the empty id is no sentinel: it is refused like any other foreign id
        foreach (var asset in new[] { theirs, missing, Guid.Empty })
        {
            await client.SendAsync(new EditMessage(Guid.NewGuid(), new SetTexture(VisualRef.OfAsset(asset))));
            Assert.Equal(RefusalReason.NotPermitted, (await client.ExpectAsync<Refused>()).Reason);
        }
        await client.SendAsync(new EditMessage(Guid.NewGuid(), new SetTexture(VisualRef.OfAsset(mine))));
        Assert.Equal(mine, (await client.ExpectAsync<BoardMessage>()).Board.Table.Texture.AssetId);
    }
}
