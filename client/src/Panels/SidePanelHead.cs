using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>The side panel's fixed head in lobby and setup (SetupHead): the phase, then the creator's Start setup or End setup, or what
/// everyone else waits for. It never scrolls.</summary>
public partial class SidePanelHead : VBoxContainer
{
    Session _session = null!; Action? _onBoard;
    Button? _action;

    public override void _ExitTree() { if (_session is not null && _onBoard is not null) _session.BoardChanged -= _onBoard; }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session;
        if (s.Game is not { } game || SetupHead.Of(game, s.IsCreator, s.Draft) is not { } head) return;
        AddChild(new Label { Text = head.Phase, ThemeTypeVariation = Variation.Display });
        if (head.Edit is { } edit)
        {
            AddChild(_action = new Button { Text = head.Action, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            _action.Pressed += () => Guarded.Send(s, edit);
            // End setup counts the pieces off the table, so it follows the draft.
            _onBoard = () => { if (IsInstanceValid(_action) && s.Game is { } g && SetupHead.Of(g, s.IsCreator, s.Draft) is { Action: { } text }) _action.Text = text; };
            s.BoardChanged += _onBoard;
        }
        if (head.Note is { } note) AddChild(new Label { Text = note, ThemeTypeVariation = Variation.Accent, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }
}
