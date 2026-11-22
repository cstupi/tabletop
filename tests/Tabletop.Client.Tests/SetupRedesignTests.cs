using Tabletop.Client.Core;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>Spec 2026-11-07-ux-redesign-1-client-shell.md → Setup, Waiting for players, Loading.</summary>
public class SetupRedesignTests
{
    static readonly IReadOnlyCollection<BuiltinTerrain> Catalog = BuiltinContent.Packed.Terrain.Values.ToList();

    [Fact]
    public void Tiles_are_every_piece_once_by_group_then_name_with_the_groups_icon_key()
    {
        var tiles = TerrainTiles.Of(Catalog);
        Assert.Equal(Catalog.Count, tiles.Count);
        Assert.Equal(["blocks-sight", "impassable", "dangerous", "cover", "difficult", "open-ground"], tiles.Select(t => t.Kind.Key).Distinct());
        string KeyOf(string name) => tiles.Single(t => t.Entry.Name == name).Kind.Key;
        Assert.Equal("blocks-sight", KeyOf("large-forest"));
        Assert.Equal("impassable", KeyOf("large-lake"));
        Assert.Equal("dangerous", KeyOf("quicksand"));
        Assert.Equal("cover", KeyOf("hill"));
        Assert.Equal("difficult", KeyOf("shallow-river"));
        Assert.Equal("open-ground", KeyOf("hill-small"));
        // The groups agree with the terrain list's.
        foreach (var group in TerrainGroups.Of(Catalog))
            Assert.Single(group.Entries.Select(e => tiles.Single(t => t.Entry == e).Kind).Distinct());
        Assert.Equal("Cover, Difficult, Blocks line of sight, See in and out", tiles.Single(t => t.Entry.Name == "large-forest").Summary);
    }

    [Fact]
    public void The_legend_draws_each_group_in_its_lead_effects_icon_and_colour()
    {
        var lead = new[] { TerrainDeclarations.BlocksLineOfSight, TerrainDeclarations.Impassable, TerrainDeclarations.Dangerous, TerrainDeclarations.Cover, TerrainDeclarations.Difficult };
        Assert.Equal(lead.Select(TerrainEffects.Of).Append(TerrainEffects.Open).Select(e => (e.Icon, e.Colour)), TerrainTiles.Legend.Select(k => (k.Key, k.Colour)));
        foreach (var t in TerrainTiles.Of(Catalog))
            Assert.Contains(TerrainEffects.Rows(t.Entry.Volume.Declarations, null, null), r => r.Icon == t.Kind.Key && r.Colour == t.Kind.Colour
                || t.Kind == TerrainTiles.Dangerous && r.Flag == TerrainDeclarations.DangerousOnRushCharge || t.Kind == TerrainTiles.Difficult && r.Flag == TerrainDeclarations.Steep);
    }

    [Fact]
    public void A_legend_dot_hides_its_group_and_the_search_filters_by_name()
    {
        Assert.Equal(["Blocks sight", "Impassable", "Dangerous", "Cover", "Difficult", "Open"], TerrainTiles.Legend.Select(k => k.Legend));
        var hidden = TerrainTiles.Toggle(new HashSet<string>(), "cover");
        Assert.DoesNotContain(TerrainTiles.Of(Catalog, "", hidden), t => t.Kind == TerrainTiles.Cover);
        Assert.Contains(TerrainTiles.Of(Catalog, "", TerrainTiles.Toggle(hidden, "cover")), t => t.Kind == TerrainTiles.Cover);
        Assert.Equal(["Forest", "Large Forest"], TerrainTiles.Of(Catalog, "forest").Select(t => t.Entry.Label));
        Assert.Empty(TerrainTiles.Of(Catalog, "forest", new HashSet<string> { "blocks-sight" }));
    }

    [Fact]
    public void A_tile_dropped_on_the_table_lands_where_it_was_dropped_or_clear_of_impassable_terrain()
    {
        var open = SetupEdits.DropBuiltin("hill", [], new Point2(30, 20));
        Assert.Equal((30.0, 20.0), (open.Placement.Position.X, open.Placement.Position.Y));
        var blocker = BuiltinContent.Place("large-sight-blocker", new Point3(30, 20, 0), new Yaw(0));
        var dropped = SetupEdits.DropBuiltin("large-sight-blocker", [blocker], new Point2(30, 20));
        Assert.NotEqual((30.0, 20.0), (dropped.Placement.Position.X, dropped.Placement.Position.Y));
        Assert.NotEqual(blocker.Id, dropped.Placement.Id);
    }

    [Fact]
    public void The_zone_diagram_fits_the_table_to_the_width_and_draws_each_teams_zone_with_y_up()
    {
        var size = new TableSize(72, 48);
        var zones = ZoneStyles.ShapesOf(ZoneStyles.Frontline).Select((s, i) => new DeploymentZone(Guid.NewGuid(), s, i + 1)).ToList();
        var view = ZoneDiagram.Of(size, zones, ZoneLegs.Default, 288, 400);
        Assert.Equal((288.0, 192.0), (view.Width, view.Height));
        Assert.Equal([1, 2], view.Zones.Select(z => z.Ordinal));
        Assert.Equal("Frontline", view.Style);
        foreach (var z in view.Zones) Assert.All(z.Polygons.SelectMany(p => p), p => Assert.InRange(p.X, 0, 288.0001));
        // The table's Y runs up the drawing.
        var first = ZoneStyles.Resolve(zones[0].Shape, size)[0].Vertices;
        Assert.Equal(first.Select(v => (v.X * 4, 192 - v.Y * 4)), view.Zones[0].Polygons[0].Select(p => (p.X, p.Y)));
        Assert.Equal(100, ZoneDiagram.Of(size, zones, ZoneLegs.Default, 288, 100).Height, 6);
        Assert.Equal("No zones", ZoneDiagram.Of(size, [], ZoneLegs.Default, 288, 400).Style);
    }

    [Fact]
    public void Objectives_are_rows_of_label_and_position()
    {
        var draft = Tabletop.Content.Tests.TableStateTests.Sample();
        var objective = new Marker(Guid.NewGuid(), MarkerKind.Objective, new Point3(12, 30.25, 0), new Yaw(0), 3, null, VisualRef.OfBuiltin("objective-ring"));
        var named = objective with { Id = Guid.NewGuid(), Label = "Relay", Position = new Point3(60, 8, 0) };
        var rows = ObjectiveRows.Of(draft with { Markers = [objective, named] });
        Assert.Equal([("Objective 1", "(12, 30.3)"), ("Relay", "(60, 8)")], rows.Select(r => (r.Label, r.Position)));
        Assert.Empty(ObjectiveRows.Of(null));
    }

    [Fact]
    public void A_team_folds_to_one_line_of_names_and_armies()
    {
        var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid(); var t3 = Guid.NewGuid();
        var game = Samples.Game with
        {
            Status = GameStatus.Setup, GameRules = RuleSets.Gf360, Teams = [new Team(t1, 1), new Team(t2, 2), new Team(t3, 3)],
            Participants = [new Participant(Guid.NewGuid(), Guid.NewGuid(), "Clay", t1, Guid.NewGuid(), "Orcs"), new Participant(Guid.NewGuid(), Guid.NewGuid(), "Ben", t1, null),
                new Participant(Guid.NewGuid(), Guid.NewGuid(), "Sam", t2, null)],
        };
        var lines = TeamColumns.Of(game, null, false, compact: true).Select(TeamColumns.Line).ToList();
        Assert.Equal(["Clay · Orcs · 3.6, Ben", "Sam · no army yet", "No players yet"], lines);
    }

    [Fact]
    public void Waiting_columns_carry_the_waits_state_and_the_join_or_picker_inside_the_column()
    {
        var game = Samples.Game;                                                              // Clay on team 1, no one on team 2
        var waiting = WaitingForPlayers.Teams(game, Samples.Board);
        var columns = WaitingView.Columns(game, waiting, Samples.P1);
        Assert.Equal([(1, ColumnState.Ready), (2, ColumnState.NeedsPlayer)], columns.Select(c => (c.Ordinal, c.State)));
        Assert.Equal(["Ready", "Needs a player"], columns.Select(c => c.StateText));
        Assert.Null(columns[0].Join);
        Assert.Equal(new PickTeam(Samples.Team2), columns[1].Join!.Edit);
        Assert.Null(WaitingView.Columns(game, waiting, Guid.Empty)[1].Join);
        Assert.False(WaitingView.Changed(columns, WaitingView.Columns(game, waiting, Samples.P1)));
        Assert.True(WaitingView.Changed(columns, WaitingView.Columns(game, [], Samples.P1)));
        Assert.True(WaitingView.Changed(null, columns));
    }

    [Fact]
    public void The_curtain_names_the_game_its_status_and_who_plays()
    {
        Assert.Null(CurtainHead.Of(null));
        var head = CurtainHead.Of(Samples.Game with { Status = GameStatus.Playing })!;
        Assert.Equal("Playing", head.Status);
        Assert.Equal([1, 2], head.Teams.Select(t => t.Ordinal));
        Assert.StartsWith("Team 1 · ", head.Teams[0].Text);
        Assert.Equal("Team 2", head.Teams[1].Text);
    }
}
