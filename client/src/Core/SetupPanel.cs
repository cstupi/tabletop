using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The folding sections of the setup side panel, and the terrain card's two.</summary>
public enum SetupFold { Terrain, Objectives, Table, Zones, Layouts, Game, PieceMesh, PieceVolumes }

/// <summary>
/// Spec 2026-10-27-godot-setup-screen-design.md → Side panel: which sections show in which order, their headings, and which are open.
/// Setup opens Terrain alone; the terrain card's Mesh and Volumes start closed; the lobby has no folds (its teams are columns, spec
/// 2026-11-07-lobby-and-setup-redesign-design.md). A section the player opens or closes stays so, per status, for as long as this instance
/// lives (the game screen's).
/// </summary>
public sealed class SetupFolds
{
    readonly Dictionary<(SetupFold, GameStatus), bool> _chosen = new();

    public static IReadOnlyList<SetupFold> Order(GameStatus status) => status switch
    {
        GameStatus.Setup => [SetupFold.Terrain, SetupFold.Objectives, SetupFold.Table, SetupFold.Zones, SetupFold.Layouts, SetupFold.Game],
        _ => [],
    };

    public bool IsOpen(SetupFold fold, GameStatus status) => _chosen.TryGetValue((fold, status), out var open) ? open : Default(fold, status);

    public void Set(SetupFold fold, bool open, GameStatus status) => _chosen[(fold, status)] = open;

    static bool Default(SetupFold fold, GameStatus status) => status switch
    {
        GameStatus.Setup => fold is SetupFold.Terrain,
        _ => false,
    };

    /// <summary>The heading, with its count where it has one.</summary>
    public static string Title(SetupFold fold, TableState? draft, GameInfo? game) => fold switch
    {
        SetupFold.Terrain => draft is null ? "Terrain" : $"Terrain {draft.Terrain.Count}/{SetupLimits.Terrain}",
        SetupFold.Objectives => draft is null ? "Objectives" : $"Objectives {draft.Markers.Count}/{SetupLimits.Markers}",
        SetupFold.Table => draft is null ? "Table"
            : $"Table {draft.Size.Width} × {draft.Size.Length}\"{(draft.Texture.Builtin is { } t ? $" · {SetupWords.Texture(t)}" : "")}",
        SetupFold.Zones => "Deployment zones",
        SetupFold.Layouts => "Layouts",
        SetupFold.Game => "Game settings",
        SetupFold.PieceMesh => "Mesh",
        SetupFold.PieceVolumes => "Volumes",
        _ => fold.ToString(),
    };

    /// <summary>The terrain card's Volumes heading: "Volume" for one, "Volumes 3" for more.</summary>
    public static string PieceVolumesTitle(int count) => count == 1 ? "Volume" : $"Volumes {count}";
}

/// <summary>The fixed head of lobby and setup: the phase, the creator's one main action and its edit, and the Still to do lines everyone reads
/// (TeamColumns.StillToDo).</summary>
public sealed record SetupHeadView(string Phase, string? Action, LobbyEdit? Edit, IReadOnlyList<ToDoLine> ToDo);

public static class SetupHead
{
    /// <summary>Null outside lobby and setup.</summary>
    public static SetupHeadView? Of(GameInfo game, bool isCreator, TableState? draft) => game.Status switch
    {
        GameStatus.Lobby => new("Lobby", isCreator ? "Start setup" : null, isCreator ? LobbyEdits.StartSetup() : null, TeamColumns.StillToDo(game, isCreator)),
        GameStatus.Setup => new("Setup", isCreator ? LobbyView.EndSetupText(draft) : null, isCreator ? LobbyEdits.EndSetup() : null, TeamColumns.StillToDo(game, isCreator)),
        _ => null,
    };
}

/// <summary>Spec → Side panel: the terrain list's height, what the window leaves after the head (its compact team columns included), the
/// Terrain section's other controls and the closed headings under it, between 160 and 640 px.</summary>
public static class SetupLayout
{
    public const double Reserved = 450, MinList = 160, MaxList = 640;

    public static double TerrainListHeight(double windowHeight, double headHeight) => Math.Clamp(windowHeight - headHeight - Reserved, MinList, MaxList);
}
