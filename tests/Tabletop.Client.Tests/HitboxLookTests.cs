using Tabletop.Client.Core;
namespace Tabletop.Client.Tests;

public class HitboxLookTests
{
    [Theory]
    [InlineData(true, false, false, PieceProxy.Solid)]
    [InlineData(false, false, false, PieceProxy.Solid)]      // no mesh: the hitbox is all there is
    [InlineData(true, true, false, PieceProxy.Faint)]
    [InlineData(false, true, false, PieceProxy.RingOnly)]
    [InlineData(false, true, true, PieceProxy.Faint)]        // a ghost keeps its look
    [InlineData(false, false, true, PieceProxy.Solid)]
    public void Piece(bool show, bool mesh, bool ghost, PieceProxy want) => Assert.Equal(want, HitboxLook.Piece(show, mesh, ghost));

    [Theory]
    [InlineData(true, false, false, TerrainProxy.Solid)]
    [InlineData(false, false, false, TerrainProxy.Solid)]
    [InlineData(true, true, false, TerrainProxy.Faint)]
    [InlineData(false, true, false, TerrainProxy.Hidden)]
    [InlineData(false, true, true, TerrainProxy.Solid)]      // editing shows the volumes
    public void Terrain(bool show, bool mesh, bool editing, TerrainProxy want) => Assert.Equal(want, HitboxLook.Terrain(show, mesh, editing));

    [Fact]
    public void Editing_is_the_selected_piece_in_setup()
    {
        var id = Guid.NewGuid();
        Assert.True(HitboxLook.Editing(true, id, id));
        Assert.False(HitboxLook.Editing(false, id, id)); Assert.False(HitboxLook.Editing(true, id, Guid.NewGuid())); Assert.False(HitboxLook.Editing(true, id, null));
    }

    [Fact]
    public void Show_hitboxes_defaults_on_persists_and_reads_missing_as_on()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            Assert.True(ClientSettings.Default.ShowHitboxes);
            (ClientSettings.Default with { ShowHitboxes = false }).Save(path);
            Assert.False(ClientSettings.Load(path).ShowHitboxes);
            File.WriteAllText(path, """{ "animate": false }""");
            Assert.True(ClientSettings.Load(path).ShowHitboxes);
            Assert.NotEqual(ClientSettings.Default, ClientSettings.Default with { ShowHitboxes = false });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Show_terrain_hitboxes_defaults_on_persists_independently_and_reads_missing_as_on()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            Assert.True(ClientSettings.Default.ShowTerrainHitboxes);
            (ClientSettings.Default with { ShowTerrainHitboxes = false }).Save(path);
            var loaded = ClientSettings.Load(path);
            Assert.False(loaded.ShowTerrainHitboxes); Assert.True(loaded.ShowHitboxes);
            File.WriteAllText(path, """{ "animate": false }""");
            Assert.True(ClientSettings.Load(path).ShowTerrainHitboxes);
            Assert.NotEqual(ClientSettings.Default, ClientSettings.Default with { ShowTerrainHitboxes = false });
        }
        finally { File.Delete(path); }
    }

    [Fact] public void B_toggles_hitboxes() => Assert.Equal(Intent.ToggleHitboxes, Hotkeys.Of(HotKey.B, false, false));
    [Fact] public void Shift_B_toggles_terrain_hitboxes() => Assert.Equal(Intent.ToggleTerrainHitboxes, Hotkeys.Of(HotKey.B, true, false));
}
