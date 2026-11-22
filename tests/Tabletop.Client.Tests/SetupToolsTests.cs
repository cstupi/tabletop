using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Spec 2026-11-21-setup-screen-layout-design.md → Layout: the tool rail, one panel open at a time, the badges, the key hints.</summary>
public class SetupToolsTests
{
    [Fact]
    public void The_rail_lists_the_tools_top_to_bottom_with_their_icons()
    {
        Assert.Equal([SetupTool.Terrain, SetupTool.Objectives, SetupTool.Table, SetupTool.Zones, SetupTool.Layouts, SetupTool.Teams, SetupTool.Game], SetupTools.Order);
        Assert.Equal(["terrain", "objective", "table", "zone", "layout", "team", "settings"], SetupTools.Order.Select(SetupTools.Icon));
        Assert.Equal(["Terrain", "Objectives", "Table", "Zones", "Layouts", "Teams", "Game"], SetupTools.Order.Select(SetupTools.Name));
    }

    [Fact]
    public void Terrain_opens_first_and_pressing_the_open_tool_closes_it()
    {
        var tools = new SetupTools();
        Assert.Equal(SetupTool.Terrain, tools.Open);
        tools.Press(SetupTool.Zones);
        Assert.Equal(SetupTool.Zones, tools.Open);
        tools.Press(SetupTool.Zones);
        Assert.Null(tools.Open);
        tools.Press(SetupTool.Teams);
        Assert.Equal(SetupTool.Teams, tools.Open);
        tools.Show(SetupTool.Teams);
        Assert.Equal(SetupTool.Teams, tools.Open);
    }

    [Fact]
    public void Headings_carry_their_counts()
    {
        var table = Tabletop.Content.Tests.TableStateTests.Sample() with { Texture = VisualRef.OfBuiltin("grass") };
        Assert.Equal($"Terrain {table.Terrain.Count}/64", SetupTools.Title(SetupTool.Terrain, table, Samples.Game));
        Assert.Equal($"Objectives {table.Markers.Count}/32", SetupTools.Title(SetupTool.Objectives, table, Samples.Game));
        Assert.Equal($"Table {table.Size.Width} × {table.Size.Length}\" · Grass", SetupTools.Title(SetupTool.Table, table, Samples.Game));
        var zones = table with { Zones = [new DeploymentZone(Guid.NewGuid(), "ground-war/1", 1), new DeploymentZone(Guid.NewGuid(), "ground-war/2", 2)] };
        Assert.Equal("Zones · Ground War", SetupTools.Title(SetupTool.Zones, zones, Samples.Game));
        Assert.Equal("Zones", SetupTools.Title(SetupTool.Zones, zones with { Zones = [] }, Samples.Game));
        Assert.Equal("Layouts", SetupTools.Title(SetupTool.Layouts, table, Samples.Game));
        Assert.Equal("Teams 2", SetupTools.Title(SetupTool.Teams, table, Samples.Game));
        Assert.Equal("Game settings", SetupTools.Title(SetupTool.Game, table, Samples.Game));
        Assert.Equal("Terrain", SetupTools.Title(SetupTool.Terrain, null, null));
        Assert.Equal("Teams", SetupTools.Title(SetupTool.Teams, null, null));
    }

    [Fact]
    public void A_badge_counts_the_pieces_and_objectives_placed_and_none_at_zero()
    {
        var table = Tabletop.Content.Tests.TableStateTests.Sample();
        var objective = Samples.Objective;
        var draft = table with { Markers = [objective, objective with { Id = Guid.NewGuid() }] };
        Assert.Equal(2, SetupTools.Badge(SetupTool.Objectives, draft));
        Assert.Null(SetupTools.Badge(SetupTool.Objectives, draft with { Markers = [] }));
        Assert.Equal(table.Terrain.Count == 0 ? null : table.Terrain.Count, SetupTools.Badge(SetupTool.Terrain, table));
        Assert.Null(SetupTools.Badge(SetupTool.Terrain, table with { Terrain = [] }));
        Assert.Null(SetupTools.Badge(SetupTool.Zones, draft));
        Assert.Null(SetupTools.Badge(SetupTool.Objectives, null));
    }

    [Fact]
    public void The_hint_bar_names_the_setup_keys()
    {
        Assert.Equal(["Drag place", "Q E turn", "PgUp PgDn level", "Del remove", "Ctrl measure", "Right-click more", "Esc menu"],
            SetupTools.Hints.Select(h => $"{h.Keys} {h.Word}"));
    }
}
