using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Screens;

namespace Tabletop.Client.Panels;

/// <summary>Setup's piece edits from the table, whichever tool is open: a right-click on a terrain piece or setup marker opens its menu
/// (MenuModel.ForSetupPiece); Delete or Backspace removes the selected one unless a text field has the key.</summary>
public partial class SetupPieceKeys : Node
{
    readonly GameScreen _screen = null!;
    Session _s = null!; PieceMenu _menu = null!;
    Action<Guid, Vector2>? _onRight; Action<Intent>? _onIntent;

    public SetupPieceKeys() { }
    public SetupPieceKeys(GameScreen screen) => _screen = screen;

    public override void _Ready()
    {
        var s = _s = Main.Instance.Session;
        AddChild(_menu = new PieceMenu(s, _screen.Toast));
        _onRight = (id, at) => { if (s.Draft is { } d) _menu.Open(MenuModel.ForSetupPiece(d, id), at); };
        _onIntent = i =>
        {
            if (i == Intent.Remove && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit) && s.Draft is { } d && _screen.Input.Selected is { } sel
                && MenuModel.RemoveFromDraft(d, sel.PieceId) is { } remove) Guarded.Send(s, remove);
        };
        _screen.Input.PieceRightClicked += _onRight; _screen.Input.IntentPressed += _onIntent;
    }

    public override void _ExitTree()
    {
        if (_s is null || !IsInstanceValid(_screen.Input)) return;
        _screen.Input.PieceRightClicked -= _onRight; _screen.Input.IntentPressed -= _onIntent;
    }
}
