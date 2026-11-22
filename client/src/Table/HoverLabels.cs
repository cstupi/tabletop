using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Theming;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Table;

/// <summary>The hovered unit's name in its team colour, the distance from my selected unit with the line between the two closest bases, and under it in the same label the multi-wound models' wounds left (HoverView.WoundsLine). Shift held shows the pick tag at the cursor. A hovered locked objective says how to unlock it, unless a unit is hovered too. In play a hovered terrain piece shows a panel beside the cursor (TerrainLabel.HoverOf: its name, height and one chip per effect in the
/// effect's colour, TerrainEffects), except the selected one, whose details are the terrain card's.</summary>
public partial class HoverLabels : Node3D
{
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly Label3D _name = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, VerticalAlignment = VerticalAlignment.Bottom, FontSize = 40, PixelSize = Palette.ScreenPixel, FixedSize = true, OutlineSize = 10, NoDepthTest = true, Visible = false };
    readonly MeshInstance3D _line = new() { MaterialOverride = LineMesh.Material() };
    readonly Label3D _lock = new() { Text = "Locked — right-click to unlock", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, PixelSize = Palette.ScreenPixel, FixedSize = true, OutlineSize = 8, NoDepthTest = true, Visible = false };
    readonly PanelContainer _terrainPanel = new() { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
    TerrainHover? _terrainShown;
    /// <summary>The terrain panel's layer, in the window over the Hud: a panel inside the table's viewport would sit under the Hud's parts.</summary>
    readonly CanvasLayer _overHud = new() { Layer = 20 };
    readonly CanvasLayer _layer = new(); readonly Label _shift = new() { Text = "Shift: pick models", Visible = false };
    Guid? _hovered, _marker, _terrain;
    Action<Guid?>? _onHover, _onMarker, _onTerrain, _onModel; Action? _onChanged, _onSelection, _onTheme; Action<RenderCause>? _onFrame;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input;
        AddChild(_name); AddChild(_line); AddChild(_lock); AddChild(_layer); _layer.AddChild(_shift);
        _overHud.AddChild(_terrainPanel); GetTree().Root.CallDeferred(Node.MethodName.AddChild, _overHud);
        _onTheme = () => { _terrainShown = null; DrawTerrain(); }; Ui.Changed += _onTheme;
        _onHover = id => { _hovered = id; Draw(); }; _onMarker = id => { _marker = id; Draw(); }; _onChanged = Draw; _onTerrain = id => { _terrain = id; DrawTerrain(); };
        _onSelection = Draw; _onModel = _ => Draw();
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Facts)) != 0) Draw(); };
        input.HoveredUnitChanged += _onHover; input.HoveredModelChanged += _onModel; input.HoveredMarkerChanged += _onMarker; input.SelectionChanged += _onSelection; input.EligibleChanged += _onChanged;
        input.HoveredTerrainChanged += _onTerrain;
        input.Locks.Changed += _onChanged;
        session.Frame.Render += _onFrame;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.Frame.Render -= _onFrame; Ui.Changed -= _onTheme;
        if (IsInstanceValid(_overHud)) _overHud.QueueFree();
        if (IsInstanceValid(_input))
        {
            _input.HoveredUnitChanged -= _onHover; _input.HoveredModelChanged -= _onModel; _input.HoveredMarkerChanged -= _onMarker; _input.SelectionChanged -= _onSelection; _input.EligibleChanged -= _onChanged;
            _input.HoveredTerrainChanged -= _onTerrain;
            _input.Locks.Changed -= _onChanged;
        }
    }

    public override void _Process(double delta)
    {
        _shift.Visible = Input.IsKeyPressed(Key.Shift);
        if (_shift.Visible) _shift.Position = GetViewport().GetMousePosition() + new Vector2(18, 18);
        var shown = TerrainLabel.HoverShown(_input.HeldModels.Count > 0, _input.Proposing.Count > 0, Input.IsKeyPressed(Key.Alt));
        if (shown != _terrainAllowed) { _terrainAllowed = shown; DrawTerrain(); }
        if (_terrainPanel.Visible) PlaceTerrainPanel();
    }

    /// <summary>TerrainLabel.HoverShown as last read: the panel keeps off held models and a placement's ghosts unless Alt is held.</summary>
    bool _terrainAllowed = true;

    /// <summary>Beside the cursor, inside the view and off the Hud's panels (CursorPanel over Session.Hud's Obstacles).</summary>
    void PlaceTerrainPanel()
    {
        var root = GetTree().Root; var view = root.GetVisibleRect().Size; var mouse = root.GetMousePosition(); var size = _terrainPanel.Size;
        var (x, y) = CursorPanel.Place((mouse.X, mouse.Y), (size.X, size.Y), new HudRect(0, 0, view.X, view.Y), _s.Hud.Obstacles);
        _terrainPanel.Position = new Vector2(x, y);
    }

    /// <summary>Where a unit's name label stands: over its models' centre, 1.2" above the tallest; null with none on the table.</summary>
    Vector3? NameAt(Guid unit)
    {
        var nodes = _view.Pieces.OfType<PieceNode>().Where(n => n.UnitId == unit && !n.IsQueuedForDeletion()).ToList();
        if (nodes.Count == 0) return null;
        var centre = nodes.Aggregate(Vector3.Zero, (a, n) => a + n.GlobalPosition) / nodes.Count;
        return new Vector3(centre.X, nodes.Max(n => n.Bounds.End.Y) + 1.2f, centre.Z);
    }

    /// <summary>A unit's name label of this many lines as Draw stands it, shown or not, in the table viewport's pixels (its bottom and height);
    /// null with no model on the table or behind the camera. A fixed-size label is as tall as at one unit from a perspective camera.</summary>
    public UnitLabel? NameBox(Guid unit, int lines)
    {
        var cam = _view.Camera;
        if (NameAt(unit) is not { } at || cam.IsPositionBehind(at)) return null;
        var font = _name.Font ?? ThemeDB.FallbackFont;
        var perUnit = GetViewport().GetVisibleRect().Size.Y / 2 / MathF.Tan(Mathf.DegToRad(cam.Fov) / 2);
        var line = (font.GetHeight(_name.FontSize) + _name.LineSpacing) * _name.PixelSize * perUnit;
        return new UnitLabel(cam.UnprojectPosition(at).Y, lines * line + _name.OutlineSize * _name.PixelSize * perUnit);
    }

    void Draw()
    {
        _name.Visible = false; _line.Mesh = null;
        DrawLock(); DrawTerrain();
        if (_hovered is not { } unit || _s.Projection is not { } p || _s.Game is not { } game) return;
        if (HoverView.Of(p, game, new Names(game, p), unit, MySelectedUnit(p, game), _s.MaxWounds, _input.HoveredModel) is not { } info) return;
        if (NameAt(unit) is not { } at) return;
        var nodes = _view.Pieces.OfType<PieceNode>().Where(n => n.UnitId == unit && !n.IsQueuedForDeletion()).ToList();
        var eligible = _input.Eligible.Contains(unit) || nodes.Any(n => _input.Eligible.Contains(n.ModelId));
        _name.Text = (eligible ? $"{info.Name} · Click to target" : info.Distance is { } d ? $"{info.Name} · {d:0.0}\"" : info.Name)
            + (HoverView.WoundsLine(info.Wounds) is { } wounds ? $"\n{wounds}" : "")    // one label, so the wounds never overlap the name
            + (info.Model is { } model ? $"\nThis model: {model}" : "");                // the model under the pointer, when it is not like the rest
        _name.Modulate = Palette.Team(info.TeamOrdinal);
        _name.Position = at;
        _name.Visible = true;
        if (info.From is { } from && info.To is { } to)
            _line.Mesh = LineMesh.Of([(WorldFrame.ToGodot(from) + Vector3.Up * 0.15f, WorldFrame.ToGodot(to) + Vector3.Up * 0.15f, Colors.White)]);
    }

    void DrawLock()
    {
        _lock.Visible = false;
        // A hovered unit's label is the one shown: a marker under or beside it says nothing, so the two never overlap.
        if (_hovered is not null || _marker is not { } id || !_input.Locks.IsLocked(_s.Game?.Status, _s.Projection, id) || _view.MarkerAt(id) is not { } node) return;
        _lock.Position = new Vector3(node.GlobalPosition.X, node.Bounds.End.Y + 0.6f, node.GlobalPosition.Z);
        _lock.Visible = true;
    }

    /// <summary>The hovered piece's panel, in play only (setup has the terrain card), and not for the selected piece, which the terrain card
    /// shows. Rebuilt only when what it shows changed.</summary>
    void DrawTerrain()
    {
        TerrainHover? hover = null;
        if (_terrainAllowed && _s.Game?.Status is GameStatus.Playing or GameStatus.Ended && _s.Projection is { } p && _terrain is { } id && id != _input.Selected?.PieceId
            && p.Board.Table.Terrain.FirstOrDefault(t => t.Id == id) is { } piece)
            hover = TerrainLabel.HoverOf(piece, _s.TerrainWords);
        _terrainPanel.Visible = hover is not null;
        if (hover is null || Equals(hover, _terrainShown)) return;
        _terrainShown = hover;
        foreach (var c in _terrainPanel.GetChildren()) { _terrainPanel.RemoveChild(c); c.QueueFree(); }
        _terrainPanel.Theme = Ui.Theme; _terrainPanel.AddThemeStyleboxOverride("panel", Ui.FloatingPanel()); _terrainPanel.Size = Vector2.Zero;
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; box.AddThemeConstantOverride("separation", Space.S); _terrainPanel.AddChild(box);
        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; head.AddThemeConstantOverride("separation", Space.M); box.AddChild(head);
        head.AddChild(new Label { Text = hover.Name, ThemeTypeVariation = Variation.Display });
        if (hover.Height.Length > 0) head.AddChild(new Label { Text = hover.Height, ThemeTypeVariation = Variation.Dim, VerticalAlignment = VerticalAlignment.Center });
        var chips = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(hover.Effects.Count > 1 ? 300 : 0, 0) }; chips.AddThemeConstantOverride("h_separation", 6); chips.AddThemeConstantOverride("v_separation", 6); box.AddChild(chips);
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
