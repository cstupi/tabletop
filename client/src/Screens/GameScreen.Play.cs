using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Content;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Screens;

public partial class GameScreen
{
    AskPanel? _askPanel; LogPanel? _logPanel; Animator? _animator; CheckBox? _animate; PieceMenu? _menu;
    // Input outlives the play panels: its subscriptions are fields and come off when they go.
    Action<Guid, Vector2>? _onPieceRight; Action<Vector3>? _onTableRight; Action<MenuItem>? _onItem;
    readonly MenuItem _leave = new("Leave the game", MenuInput.None, _ => null);

    partial void ShowPlayPanels(bool playing)
    {
        if (!playing) { FreePlayPanels(); return; }
        if (_askPanel is null)
        {
            _askPanel = new AskPanel(); _side.AddChild(_askPanel); _askPanel.Bind(_session, Table, Input);
            _logPanel = new LogPanel(); _side.AddChild(_logPanel); _logPanel.Bind(_session, Table, Toast);
            _animator = new Animator(_session, Table, Input);
            _animate = new CheckBox { Text = "Animate", ButtonPressed = _session.Settings.Animate };
            _animate.Toggled += on => _session.UpdateSettings(x => x with { Animate = on });
            _side.AddChild(_animate);
            _menu = new PieceMenu(_session, Toast); AddChild(_menu);
            _menu.PickPieceRequested += Input.PickNextPiece; _menu.PickPointRequested += Input.PickNextPoint;
            _onItem = item => { if (ReferenceEquals(item, _leave)) Guarded.Run(_session.LeaveAsync, Toast); };   // Leave is a management call, not a command
            _menu.ItemChosen += _onItem;
            _onPieceRight = (id, at) => { if (_menu is not null) _menu.Open(ItemsFor(id, Godot.Input.IsKeyPressed(Key.Shift)), at); };
            _onTableRight = at => _menu?.Open([Token(at), _leave], GetViewport().GetMousePosition());
            Input.PieceRightClicked += _onPieceRight; Input.TableRightClicked += _onTableRight;
        }
        // The team panels are new on every Rebuild, so they are wired on every call.
        foreach (var team in _teams)
        {
            var id = team.Team.Id;
            team.RightClicked += at =>
            {
                if (_session.Game is { } game && _session.Projection is { } p) _menu?.Open(MenuModel.ForTeam(game, p, id).Concat(MenuModel.OffTable(p, id, new Names(game, p))).ToList(), at);
            };
        }
    }

    void FreePlayPanels()
    {
        if (Input is not null && IsInstanceValid(Input)) { Input.PieceRightClicked -= _onPieceRight; Input.TableRightClicked -= _onTableRight; }
        _animator?.Dispose(); _animator = null;
        _askPanel?.QueueFree(); _askPanel = null;
        _logPanel?.QueueFree(); _logPanel = null;
        _animate?.QueueFree(); _animate = null;
        _menu?.QueueFree(); _menu = null;
        _onPieceRight = null; _onTableRight = null; _onItem = null;
    }

    IReadOnlyList<MenuItem> ItemsFor(Guid id, bool shift)
    {
        var p = _session.Projection; if (p is null) return [];
        if (p.Models.TryGetValue(id, out var model))
        {
            var known = MenuModel.KnownActionNames(p, _session.Log);
            return shift ? MenuModel.ForUnit(p, model.UnitId, known) : MenuModel.ForModel(p, id, known).Concat(MenuModel.ForFrame(p)).ToList();
        }
        return _session.Game is { } game ? MenuModel.ForMarker(game, p, id) : [];
    }

    static MenuItem Token(Vector3 at) => new("Drop a token here", MenuInput.Text,
        label => new PlaceMarker(new Marker(Guid.CreateVersion7(), MarkerKind.Token, WorldFrame.FromGodot(at), new Yaw(0), 1, label, VisualRef.OfBuiltin("chip"))));
}
