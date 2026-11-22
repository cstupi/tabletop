using Tabletop.Content;
using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The setup tools on the rail, top to bottom.</summary>
public enum SetupTool { Terrain, Objectives, Table, Zones, Layouts, Teams, Game }

/// <summary>A key hint at the foot of setup: the key caps and the word after them.</summary>
public sealed record KeyHint(string Keys, string Word);

/// <summary>
/// Spec 2026-11-21-setup-screen-layout-design.md → Layout: the tool rail (order, icons, names, badges), which one tool's panel is open, each
/// panel's heading and the key hint bar. One tool is open at a time; pressing the open one closes it and gives the table the width.
/// </summary>
public sealed class SetupTools
{
    public static readonly IReadOnlyList<SetupTool> Order = Enum.GetValues<SetupTool>();

    public static readonly IReadOnlyList<KeyHint> Hints =
    [
        new("Drag", "place"), new("Q E", "turn"), new("PgUp PgDn", "level"), new("Del", "remove"), new("Ctrl", "measure"),
        new("Right-click", "more"), new("Esc", "menu"),
    ];

    /// <summary>The open tool; null with every panel closed.</summary>
    public SetupTool? Open { get; private set; } = SetupTool.Terrain;

    /// <summary>A rail button: opens its tool, or closes it when it is the open one.</summary>
    public void Press(SetupTool tool) => Open = Open == tool ? null : tool;

    /// <summary>Opens the tool whatever is open (a team chip opens Teams).</summary>
    public void Show(SetupTool tool) => Open = tool;

    /// <summary>The theme icon key.</summary>
    public static string Icon(SetupTool tool) => tool switch
    {
        SetupTool.Terrain => "terrain", SetupTool.Objectives => "objective", SetupTool.Table => "table", SetupTool.Zones => "zone",
        SetupTool.Layouts => "layout", SetupTool.Teams => "team", _ => "settings",
    };

    /// <summary>The rail button's tooltip.</summary>
    public static string Name(SetupTool tool) => tool.ToString();

    /// <summary>The panel's heading, with its count where it has one.</summary>
    public static string Title(SetupTool tool, TableState? draft, GameInfo? game) => tool switch
    {
        SetupTool.Terrain => draft is null ? "Terrain" : $"Terrain {draft.Terrain.Count}/{SetupLimits.Terrain}",
        SetupTool.Objectives => draft is null ? "Objectives" : $"Objectives {draft.Markers.Count}/{SetupLimits.Markers}",
        SetupTool.Table => draft is null ? "Table"
            : $"Table {draft.Size.Width} × {draft.Size.Length}\"{(draft.Texture.Builtin is { } t ? $" · {SetupWords.Texture(t)}" : "")}",
        SetupTool.Zones => draft is null || ZoneDiagram.StyleOf(draft.Zones) is not { } style ? "Zones" : $"Zones · {SetupWords.ZoneStyle(style)}",
        SetupTool.Layouts => "Layouts",
        SetupTool.Teams => game is null ? "Teams" : $"Teams {game.Teams.Count}",
        _ => "Game settings",
    };

    /// <summary>The rail icon's count: the terrain pieces and objectives placed; null at zero and for the other tools.</summary>
    public static int? Badge(SetupTool tool, TableState? draft)
    {
        var n = tool switch
        {
            SetupTool.Terrain => draft?.Terrain.Count ?? 0,
            SetupTool.Objectives => draft?.Markers.Count(m => m.Kind == MarkerKind.Objective) ?? 0,
            _ => 0,
        };
        return n > 0 ? n : null;
    }
}
