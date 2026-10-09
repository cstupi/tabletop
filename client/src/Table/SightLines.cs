using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;
using Projection = Tabletop.Protocol.Projection;

namespace Tabletop.Client.Table;

/// <summary>The server's sight answer drawn: one line per model pair (green clear, amber screened, red blocked), the blockers lit, a summary over the target, and under a target pick the models that cannot shoot dimmed.</summary>
public partial class SightLines : Node3D
{
    static readonly Color ClearColour = new(0.2f, 0.9f, 0.3f), ScreenedColour = new(1f, 0.75f, 0.1f), BlockedColour = new(0.95f, 0.15f, 0.15f);
    Session _s = null!; TableView _view = null!; TableInput _input = null!;
    readonly MeshInstance3D _lines = new() { MaterialOverride = LineMesh.Material() };
    readonly Node3D _marks = new();
    readonly Label3D _summary = new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 44, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true, Visible = false };
    Guid? _hovered, _query; bool _asking;
    readonly Dictionary<Guid, SightAnswer> _early = new();   // answers that beat their QuerySightAsync continuation
    Action<Guid?>? _onHover; Action? _onChanged; Action<IReadOnlyList<GameEvent>>? _onEvents; Action<SightAnswer>? _onAnswer;

    public void Bind(Session session, TableView view, TableInput input)
    {
        _s = session; _view = view; _input = input;
        AddChild(_lines); AddChild(_marks); AddChild(_summary);
        _onHover = id => { _hovered = id; Ask(); }; _onChanged = Ask; _onEvents = _ => Ask(); _onAnswer = OnAnswer;
        input.HoveredUnitChanged += _onHover; input.SightHeldChanged += _onChanged; input.SelectionChanged += _onChanged;
        session.EventsApplied += _onEvents; session.SightAnswered += _onAnswer;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        _s.EventsApplied -= _onEvents; _s.SightAnswered -= _onAnswer;
        if (IsInstanceValid(_input)) { _input.HoveredUnitChanged -= _onHover; _input.SightHeldChanged -= _onChanged; _input.SelectionChanged -= _onChanged; }
    }

    /// <summary>My open Ask, when it is a target assignment: the shooting unit is its frame's actor.</summary>
    Guid? Shooter(Projection p, GameInfo game) =>
        AskViewModel.Mine(p, game, _s.MyTeamId, _s.Settings.MutedWindowKinds) is { Mode: AskMode.Assign } mine && mine.Ask.FrameId is { } f
            ? p.Stack.FirstOrDefault(x => x.Id == f)?.Context.ActorUnitId : null;

    async void Ask()
    {
        if (_s.Projection is not { } p || _s.Game is not { } game) { Clear(); return; }
        var observer = Shooter(p, game) ?? (_input.SightHeld ? _input.Selected?.PieceId : null);
        if (observer is not { } from || _hovered is not { } to || from == to || !p.Units.ContainsKey(from)) { _query = null; Clear(); return; }
        _asking = true;
        try
        {
            var id = await _s.QuerySightAsync(from, to);
            _query = id;
            if (_early.Remove(id, out var answer)) Draw(answer);
        }
        catch (Exception ex) { GD.PushWarning($"Sight query failed: {ex.Message}"); }
        finally { _asking = false; _early.Clear(); }
    }

    void OnAnswer(SightAnswer answer)
    {
        if (answer.QueryId == _query) Draw(answer);
        else if (_asking) _early[answer.QueryId] = answer;
    }

    void Clear()
    {
        _lines.Mesh = null; _summary.Visible = false;
        foreach (var c in _marks.GetChildren()) c.QueueFree();
    }

    void Draw(SightAnswer answer)
    {
        Clear();
        if (_s.Projection is not { } p || _s.Game is not { } game || _hovered is not { } target) return;
        Vector3? Eye(Guid model) => p.Models.GetValueOrDefault(model) is { Location: OnTable on } m
            ? WorldFrame.ToGodot(on.Placement.Position) + Vector3.Up * (float)(m.Slot.Height / 2) : null;
        var segments = new List<(Vector3, Vector3, Color)>();
        foreach (var line in answer.Lines)
            if (Eye(line.FromModelId) is { } a && Eye(line.ToModelId) is { } b)
                segments.Add((a, b, !line.Clear ? BlockedColour : line.Screened ? ScreenedColour : ClearColour));
        _lines.Mesh = LineMesh.Of(segments);
        foreach (var id in answer.Lines.Where(l => !l.Clear).SelectMany(l => l.BlockedBy).Distinct())
            if (BoundsOf(p, id) is { } box) Mark(box, BlockedColour with { A = 0.35f });

        var observers = answer.Lines.Select(l => l.FromModelId).Distinct().ToList();
        var seeing = answer.Lines.Where(l => l.Clear).Select(l => l.FromModelId).Distinct().Count();
        var inCover = seeing > 0 && answer.Lines.Where(l => l.Clear).All(l => l.Screened);
        var text = $"{seeing} of {observers.Count} models can see {new Names(game, p).Unit(target)}" + (inCover ? " · in cover" : "");
        if (Shooter(p, game) is not null)
        {
            text += $" · {answer.CanShoot.Count} can shoot";
            foreach (var id in observers.Where(o => !answer.CanShoot.Contains(o)))
                if (_view.PieceAt(id) is { } node) Mark(node.Bounds, new Color(0, 0, 0, 0.55f));
        }
        var nodes = _view.Pieces.OfType<PieceNode>().Where(n => n.UnitId == target).ToList();
        if (nodes.Count == 0) return;
        var centre = nodes.Aggregate(Vector3.Zero, (a, n) => a + n.GlobalPosition) / nodes.Count;
        _summary.Text = text;
        _summary.Position = new Vector3(centre.X, nodes.Max(n => n.Bounds.End.Y) + 2.0f, centre.Z);   // above the hover label
        _summary.Visible = true;
    }

    /// <summary>A blocker's box: a model's node, or the terrain piece that owns the volume.</summary>
    Aabb? BoundsOf(Projection p, Guid id)
    {
        if (_view.PieceAt(id) is { } piece) return piece.Bounds;
        var placement = p.Board.Table.Terrain.FirstOrDefault(t => t.Volumes.Any(v => v.Id == id));
        return placement is not null && _view.TerrainAt(placement.Id) is { } terrain ? terrain.Bounds : null;
    }

    void Mark(Aabb box, Color colour) =>
        _marks.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = box.Size + Vector3.One * 0.1f }, Position = box.GetCenter(), MaterialOverride = Palette.Translucent(colour) });
}
