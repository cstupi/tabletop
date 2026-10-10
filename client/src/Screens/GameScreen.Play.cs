using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Hud;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

/// <summary>
/// Play (spec → Layout): the Hud parts float over the table, the overlays draw on it, and the events between them meet here.
/// Everything is built once when play starts and freed when the screen leaves play.
/// </summary>
public partial class GameScreen
{
    HeaderBar? _header; StatSheet? _sheet; DecisionBox? _decision; DiceTray? _dice; LogDrawer? _logDrawer; EndMessage? _end; KeyOverlay? _keys;
    HoverLabels? _hover; SightLines? _sight; TargetLines? _targets; Lasers? _lasers; CanvasLayer? _tableLayer;
    Animator? _animator; PieceMenu? _menu;
    // Input outlives the play parts: its subscriptions are fields and come off when they go.
    Action<Guid, Vector2>? _onPieceRight; Action<Vector3>? _onTableRight; Action<MenuItem>? _onItem;
    readonly MenuItem _leave = new("Leave the game", MenuInput.None, _ => null);

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { FreePlayPanels(); return; }
        if (_header is not null) return;

        // Hud parts, bottom of the input order first: the end message and the key overlay are added last and hear keys first.
        _hud.AddChild(_header = new HeaderBar()); _header.Bind(_session, Table, Input);
        _hud.AddChild(_sheet = new StatSheet()); _sheet.Bind(_session, Table, Input);
        _hud.AddChild(_decision = new DecisionBox()); _decision.Bind(_session, Table, Input);
        _hud.AddChild(_dice = new DiceTray()); _dice.Bind(_session, Table, Input); _dice.Follow(_decision);
        _hud.AddChild(_logDrawer = new LogDrawer()); _logDrawer.Bind(_session, Table, Input);
        _hud.AddChild(_end = new EndMessage()); _end.Bind(_session, Table, Input);
        _hud.AddChild(_keys = new KeyOverlay()); _keys.Bind(_session, Table, Input);

        // Table overlays: Node3D children of the table, and the undo button in a canvas layer of the table's own viewport.
        Table.AddChild(_hover = new HoverLabels()); _hover.Bind(_session, Table, Input);
        Table.AddChild(_sight = new SightLines()); _sight.Bind(_session, Table, Input);
        Table.AddChild(_targets = new TargetLines()); _targets.Bind(_session, Table, Input);
        Table.AddChild(_lasers = new Lasers()); _lasers.Bind(_session, Table, Input);
        Table.AddChild(_tableLayer = new CanvasLayer());
        var undo = new UndoMoveButton(); _tableLayer.AddChild(undo); undo.Bind(_session, Table, Input);

        _animator = new Animator(_session, Table, Input);
        _menu = new PieceMenu(_session, Toast); AddChild(_menu);
        _menu.PickPieceRequested += Input.PickNextPiece; _menu.PickPointRequested += Input.PickNextPoint;
        _onItem = item =>
        {
            if (ReferenceEquals(item, _leave)) Guarded.Run(_session.LeaveAsync, Toast);   // Leave is a management call, not a command
            else Input.Locks.Chosen(item);                                                // Unlock and Lock are local
        };
        _menu.ItemChosen += _onItem;
        _onPieceRight = (id, at) => _menu?.Open(ItemsFor(id, Godot.Input.IsKeyPressed(Key.Shift)), at);
        _onTableRight = at => _menu?.Open([Token(at), _leave], GetViewport().GetMousePosition());
        Input.PieceRightClicked += _onPieceRight; Input.TableRightClicked += _onTableRight;

        // The parts die with the screen, so their own events need no unsubscribing.
        _header.TeamMenuRequested += OpenTeamMenu;
        _header.GameOverPressed += _end.Open;
        _sheet.ModelPicked += SelectModel;
        _decision.TargetsChanged += ShowTargets;
        _logDrawer.Reported += Toast;
    }

    void FreePlayPanels()
    {
        if (Input is not null && IsInstanceValid(Input)) { Input.PieceRightClicked -= _onPieceRight; Input.TableRightClicked -= _onTableRight; }
        _animator?.Dispose(); _animator = null;
        foreach (var node in new Node?[] { _header, _sheet, _decision, _dice, _logDrawer, _end, _keys, _hover, _sight, _targets, _lasers, _tableLayer, _menu })
            if (node is not null && IsInstanceValid(node)) node.QueueFree();
        _header = null; _sheet = null; _decision = null; _dice = null; _logDrawer = null; _end = null; _keys = null;
        _hover = null; _sight = null; _targets = null; _lasers = null; _tableLayer = null; _menu = null;
        _onPieceRight = null; _onTableRight = null; _onItem = null;
    }

    /// <summary>The team menu, from the header's team names (spec → Layout): what the team panel's right-click opened.</summary>
    void OpenTeamMenu(Guid teamId, Vector2 at)
    {
        if (_session.Game is { } game && _session.Projection is { } p)
            _menu?.Open(MenuModel.ForTeam(game, p, teamId).Concat(MenuModel.OffTable(p, teamId, new Names(game, p))).ToList(), at);
    }

    /// <summary>A model clicked in the stat sheet's strip selects that one model on the table (spec → Stat sheet).</summary>
    void SelectModel(Guid modelId)
    {
        if (_session.Projection?.Models.GetValueOrDefault(modelId) is { } model) Input.Select(model.UnitId, modelId);
    }

    void ShowTargets()
    {
        if (_targets is null || !IsInstanceValid(_targets)) return;
        if (_decision?.Targets is { } t) _targets.Show(t.Groups, t.Assigned); else _targets.Clear();
        if (_sight is not null && IsInstanceValid(_sight))
            _sight.ShowAssigned(_decision?.Targets?.Groups ?? [], _decision?.Targets is { } a ? new Dictionary<string, Guid>(a.Assigned) : new Dictionary<string, Guid>());
    }

    IReadOnlyList<MenuItem> ItemsFor(Guid id, bool shift)
    {
        var p = _session.Projection; if (p is null) return [];
        if (p.Models.TryGetValue(id, out var model))
        {
            var known = MenuModel.KnownActionNames(p, _session.Log);
            return shift ? MenuModel.ForUnit(p, model.UnitId, known) : MenuModel.ForModel(p, id, known).Concat(MenuModel.ForFrame(p)).ToList();
        }
        return _session.Game is { } game ? MenuModel.ForMarker(game, p, id, Input.Locks) : [];
    }

    static MenuItem Token(Vector3 at) => new("Drop a token here", MenuInput.Text,
        label => new PlaceMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, WorldFrame.FromGodot(at), new Yaw(0), 1, label, VisualRef.OfBuiltin("chip"))));
}
