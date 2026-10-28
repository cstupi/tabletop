using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The folding sections of the lobby and setup side panel, and the terrain card's two in setup.</summary>
public enum SetupFold { Terrain, Markers, Table, Zones, Layouts, Game, Players, PieceMesh, PieceVolumes }

/// <summary>
/// Spec 2026-10-27-godot-setup-screen-design.md → Side panel: which sections show in which order, their headings, and which are open.
/// Setup opens Terrain alone; the lobby opens Players and Game settings; the terrain card's Mesh and Volumes start closed. A section the
/// player opens or closes stays so, per status, for as long as this instance lives (the game screen's).
/// </summary>
public sealed class SetupFolds
{
    readonly Dictionary<(SetupFold, GameStatus), bool> _chosen = new();

    public static IReadOnlyList<SetupFold> Order(GameStatus status) => status switch
    {
        GameStatus.Lobby => [SetupFold.Players, SetupFold.Game],
        GameStatus.Setup => [SetupFold.Terrain, SetupFold.Markers, SetupFold.Table, SetupFold.Zones, SetupFold.Layouts, SetupFold.Game, SetupFold.Players],
        _ => [],
    };

    public bool IsOpen(SetupFold fold, GameStatus status) => _chosen.TryGetValue((fold, status), out var open) ? open : Default(fold, status);

    public void Set(SetupFold fold, bool open, GameStatus status) => _chosen[(fold, status)] = open;

    static bool Default(SetupFold fold, GameStatus status) => status switch
    {
        GameStatus.Lobby => fold is SetupFold.Players or SetupFold.Game,
        GameStatus.Setup => fold is SetupFold.Terrain,
        _ => false,
    };

    /// <summary>The heading, with its count where it has one.</summary>
    public static string Title(SetupFold fold, TableState? draft, GameInfo? game) => fold switch
    {
        SetupFold.Terrain => draft is null ? "Terrain" : $"Terrain {draft.Terrain.Count}/{SetupLimits.Terrain}",
        SetupFold.Markers => draft is null ? "Markers" : $"Markers {draft.Markers.Count}/{SetupLimits.Markers}",
        SetupFold.Table => draft is null ? "Table"
            : $"Table {draft.Size.Width} × {draft.Size.Length}\"{(draft.Texture.Builtin is { } t ? $" · {SetupWords.Texture(t)}" : "")}",
        SetupFold.Zones => "Deployment zones",
        SetupFold.Layouts => "Layouts",
        SetupFold.Game => "Game settings",
        SetupFold.Players => game is null ? "Players" : $"Players {game.Participants.Count}",
        SetupFold.PieceMesh => "Mesh",
        SetupFold.PieceVolumes => "Volumes",
        _ => fold.ToString(),
    };

    /// <summary>The terrain card's Volumes heading: "Volume" for one, "Volumes 3" for more.</summary>
    public static string PieceVolumesTitle(int count) => count == 1 ? "Volume" : $"Volumes {count}";
}

/// <summary>The side panel's fixed head: the phase, the one main action and its edit (the creator's), or what everyone else waits for.</summary>
public sealed record SetupHeadView(string Phase, string? Action, LobbyEdit? Edit, string? Note);

public static class SetupHead
{
    /// <summary>Null outside lobby and setup.</summary>
    public static SetupHeadView? Of(GameInfo game, bool isCreator, TableState? draft) => game.Status switch
    {
        GameStatus.Lobby => isCreator ? new("Lobby", "Start setup", LobbyEdits.StartSetup(), null) : new("Lobby", null, null, LobbyView.WaitingForCreator(game, false)),
        GameStatus.Setup => isCreator ? new("Setup", LobbyView.EndSetupText(draft), LobbyEdits.EndSetup(), null) : new("Setup", null, null, LobbyView.WaitingForCreator(game, false)),
        _ => null,
    };
}

/// <summary>Spec → Side panel: the terrain list's height, what the window leaves after the head, the Terrain section's other controls and
/// the closed headings under it, between 160 and 640 px.</summary>
public static class SetupLayout
{
    public const double Reserved = 540, MinList = 160, MaxList = 640;

    public static double TerrainListHeight(double windowHeight) => Math.Clamp(windowHeight - Reserved, MinList, MaxList);
}
