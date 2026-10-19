namespace Tabletop.Client.Core;

/// <summary>How a model's hitbox (the translucent base volume) draws.</summary>
public enum PieceProxy
{
    /// <summary>The coloured volume at full strength: the model has no mesh.</summary>
    Solid,
    /// <summary>A faint volume beneath the mesh.</summary>
    Faint,
    /// <summary>No volume: the mesh alone, with a thin ring on the base's outline in the team colour (the highlight colour when highlighted).</summary>
    RingOnly,
}

/// <summary>How a terrain piece's volume boxes draw.</summary>
public enum TerrainProxy { Solid, Faint, Hidden }

/// <summary>
/// What draws for a hitbox under the Show hitboxes settings: models read <see cref="ClientSettings.ShowHitboxes"/>, terrain <see cref="ClientSettings.ShowTerrainHitboxes"/>. A piece without a mesh always
/// draws its volume, or it would vanish; a placement ghost keeps its own look (a faint volume under its mesh) whatever the setting.
/// </summary>
public static class HitboxLook
{
    public static PieceProxy Piece(bool showHitboxes, bool hasMesh, bool ghost) =>
        !hasMesh ? PieceProxy.Solid : showHitboxes || ghost ? PieceProxy.Faint : PieceProxy.RingOnly;

    /// <summary>Terrain with a mesh shows its volumes faintly, or not at all with terrain hitboxes off; a piece being edited in setup (the
    /// selected one, whose card edits its volumes, or one in hand) shows them at full strength either way.</summary>
    public static TerrainProxy Terrain(bool showTerrainHitboxes, bool hasMesh, bool editing) =>
        !hasMesh || editing ? TerrainProxy.Solid : showTerrainHitboxes ? TerrainProxy.Faint : TerrainProxy.Hidden;

    /// <summary>A terrain piece is being edited when setup is open (no game in play) and it is the selected piece.</summary>
    public static bool Editing(bool inSetup, Guid pieceId, Guid? selected) => inSetup && selected == pieceId;
}
