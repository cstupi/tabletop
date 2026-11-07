using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Theming;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Table;

/// <summary>The hovered unit's name in its team colour, the distance from my selected unit with the line between the two closest bases, and each multi-wound model's wounds left (HoverView). Shift held shows the pick tag at the cursor. A hovered locked objective says how to unlock it. In play a hovered terrain piece shows a panel beside the cursor (TerrainLabel.HoverOf: its name, height and one chip per effect in the
/// effect's colour, TerrainEffects), except the selected one, whose details are the terrain card's.</summary>
public partial class HoverLabels : Node3D
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly Label3D _name = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = Palette.ScreenPixel, FixedSize = true, OutlineSize = 10, NoDepthTest = true, Visible = false };
    readonly MeshInstance3D _line = new() { MaterialOverride = LineMesh.Material() };
    readonly Node3D _wounds = new();
    readonly Label3D _lock = new() { Text = "Locked — right-click to unlock", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = 0.01f, OutlineSize = 8, NoDepthTest = true, Visible = false };
    readonly PanelContainer _terrainPanel = new() { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
    TerrainHover? _terrainShown;
    readonly CanvasLayer _layer = new(); readonly Label _shift = new() { Text = "Shift: pick models", Visible = false };
    Guid? _hovered, _marker, _terrain;
    Action<Guid?>? _onHover, _onMarker, _onTerrain; Action? _onChanged, _onSelection, _onTheme; Action<RenderCause>? _onFrame;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input;
        AddChild(_name); AddChild(_line); AddChild(_wounds); AddChild(_lock); AddChild(_layer); _layer.AddChild(_shift); _layer.AddChild(_terrainPanel);
        _onTheme = () => { _terrainShown = null; DrawTerrain(); }; Ui.Changed += _onTheme;
        _onHover = id => { _hovered = id; Draw(); }; _onMarker = id => { _marker = id; Draw(); }; _onChanged = Draw; _onTerrain = id => { _terrain = id; DrawTerrain(); };
        _onSelection = Draw;
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Facts)) != 0) Draw(); };
        input.HoveredUnitChanged += _onHover; input.HoveredMarkerChanged += _onMarker; input.SelectionChanged += _onSelection; input.EligibleChanged += _onChanged;
        input.HoveredTerrainChanged += _onTerrain;
        input.Locks.Changed += _onChanged;
        session.Frame.Render += _onFrame;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.Frame.Render -= _onFrame; Ui.Changed -= _onTheme;
        if (IsInstanceValid(_input))
        {
            _input.HoveredUnitChanged -= _onHover; _input.HoveredMarkerChanged -= _onMarker; _input.SelectionChanged -= _onSelection; _input.EligibleChanged -= _onChanged;
            _input.HoveredTerrainChanged -= _onTerrain;
            _input.Locks.Changed -= _onChanged;
        }
    }

    public override void _Process(double delta)
    {
        _shift.Visible = Input.IsKeyPressed(Key.Shift);
        if (_shift.Visible) _shift.Position = GetViewport().GetMousePosition() + new Vector2(18, 18);
        if (_terrainPanel.Visible) PlaceTerrainPanel();
    }

    /// <summary>Below and right of the cursor, kept inside the view: left of it near the right edge, above it near the foot.</summary>
    void PlaceTerrainPanel()
    {
        var view = GetViewport().GetVisibleRect().Size; var mouse = GetViewport().GetMousePosition(); var size = _terrainPanel.Size;
        var x = mouse.X + 22 + size.X > view.X ? mouse.X - 16 - size.X : mouse.X + 22;
        var y = mouse.Y + 22 + size.Y > view.Y ? mouse.Y - 16 - size.Y : mouse.Y + 22;
        _terrainPanel.Position = new Vector2(Math.Max(4, x), Math.Max(4, y));
    }

    void Draw()
    {
        _name.Visible = false; _line.Mesh = null;
        foreach (var c in _wounds.GetChildren()) c.QueueFree();
        DrawLock(); DrawTerrain();
        if (_hovered is not { } unit || _s.Projection is not { } p || _s.Game is not { } game) return;
        if (HoverView.Of(p, game, new Names(game, p), unit, MySelectedUnit(p, game), _s.MaxWounds) is not { } info) return;
        var nodes = _view.Pieces.OfType<PieceNode>().Where(n => n.UnitId == unit && !n.IsQueuedForDeletion()).ToList();
        if (nodes.Count == 0) return;
        var top = nodes.Max(n => n.Bounds.End.Y);
        var centre = nodes.Aggregate(Vector3.Zero, (a, n) => a + n.GlobalPosition) / nodes.Count;
        var eligible = _input.Eligible.Contains(unit) || nodes.Any(n => _input.Eligible.Contains(n.ModelId));
        _name.Text = eligible ? $"{info.Name} · Click to target" : info.Distance is { } d ? $"{info.Name} · {d:0.0}\"" : info.Name;
        _name.Modulate = Palette.Team(info.TeamOrdinal);
        _name.Position = new Vector3(centre.X, top + 1.2f, centre.Z);
        _name.Visible = true;
        if (info.From is { } from && info.To is { } to)
            _line.Mesh = LineMesh.Of([(WorldFrame.ToGodot(from) + Vector3.Up * 0.15f, WorldFrame.ToGodot(to) + Vector3.Up * 0.15f, Colors.White)]);
        foreach (var (model, left, max) in info.Wounds)
        {
            if (max <= 1 || _view.PieceAt(model) is not { } node) continue;
            _wounds.AddChild(new Label3D { Text = $"{left}/{max}", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = 0.01f, OutlineSize = 8, NoDepthTest = true,
                Position = new Vector3(node.GlobalPosition.X, node.Bounds.End.Y + 0.4f, node.GlobalPosition.Z) });
        }
    }

    void DrawLock()
    {
        _lock.Visible = false;
        if (_marker is not { } id || !_input.Locks.IsLocked(_s.Game?.Status, _s.Projection, id) || _view.MarkerAt(id) is not { } node) return;
        _lock.Position = new Vector3(node.GlobalPosition.X, node.Bounds.End.Y + 0.6f, node.GlobalPosition.Z);
        _lock.Visible = true;
    }

    /// <summary>The hovered piece's panel, in play only (setup has the terrain card), and not for the selected piece, which the terrain card
    /// shows. Rebuilt only when what it shows changed.</summary>
    void DrawTerrain()
    {
        TerrainHover? hover = null;
        if (_s.Game?.Status is GameStatus.Playing or GameStatus.Ended && _s.Projection is { } p && _terrain is { } id && id != _input.Selected?.PieceId
            && p.Board.Table.Terrain.FirstOrDefault(t => t.Id == id) is { } piece)
            hover = TerrainLabel.HoverOf(piece, _s.TerrainWords);
        _terrainPanel.Visible = hover is not null;
        if (hover is null || Equals(hover, _terrainShown)) return;
        _terrainShown = hover;
        foreach (var c in _terrainPanel.GetChildren()) { _terrainPanel.RemoveChild(c); c.QueueFree(); }
        _terrainPanel.Theme = Ui.Theme; _terrainPanel.AddThemeStyleboxOverride("panel", Ui.FloatingPanel()); _terrainPanel.Size = Vector2.Zero;
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; box.AddThemeConstantOverride("separation", 8); _terrainPanel.AddChild(box);
        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; head.AddThemeConstantOverride("separation", 12); box.AddChild(head);
        head.AddChild(new Label { Text = hover.Name, ThemeTypeVariation = Variation.Display });
        if (hover.Height.Length > 0) head.AddChild(new Label { Text = hover.Height, ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        var chips = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, 0) }; chips.AddThemeConstantOverride("h_separation", 6); chips.AddThemeConstantOverride("v_separation", 6); box.AddChild(chips);
        foreach (var e in hover.Effects) chips.AddChild(HudBits.EffectChip(e));
        PlaceTerrainPanel();
    }

    /// <summary>The selected unit when it is one of mine: "With one of my units selected, the distance follows the name".</summary>
    Guid? MySelectedUnit(Projection p, GameInfo game)
    {
        if (_input.Selected?.PieceId is not { } id || p.Units.GetValueOrDefault(id) is not { } u) return null;
        var team = game.Participants.FirstOrDefault(x => x.Id == u.ParticipantId)?.TeamId;
        return team is not null && team == _s.MyTeamId ? id : null;
    }
}

/// <summary>Unshaded segments, each in its own colour, drawn over everything: the table's measuring lines.</summary>
public static class LineMesh
{
    public static ImmediateMesh Of(IEnumerable<(Vector3 From, Vector3 To, Color Colour)> segments)
    {
        var mesh = new ImmediateMesh(); var open = false;
        foreach (var (a, b, c) in segments)
        {
            if (!open) { mesh.SurfaceBegin(Mesh.PrimitiveType.Lines); open = true; }
            mesh.SurfaceSetColor(c); mesh.SurfaceAddVertex(a); mesh.SurfaceSetColor(c); mesh.SurfaceAddVertex(b);
        }
        if (open) mesh.SurfaceEnd();
        return mesh;
    }

    public static StandardMaterial3D Material() =>
        new() { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
}
