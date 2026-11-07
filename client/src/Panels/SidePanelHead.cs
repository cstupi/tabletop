using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;

namespace Tabletop.Client.Panels;

/// <summary>The fixed head of lobby and setup (SetupHead, spec 2026-11-07-lobby-and-setup-redesign-design.md): the phase, the creator's Start
/// setup or End setup, Still to do, and in setup the compact team columns. It never scrolls.</summary>
public partial class SidePanelHead : VBoxContainer
{
    Session _session = null!; Action? _onBoard;
    Button? _action;

    public override void _ExitTree() { if (_session is not null && _onBoard is not null) _session.BoardChanged -= _onBoard; }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session;
        if (s.Game is not { } game || SetupHead.Of(game, s.IsCreator, s.Draft) is not { } head) return;
        var lobby = game.Status == Tabletop.Protocol.GameStatus.Lobby;
        AddThemeConstantOverride("separation", lobby ? 12 : 8);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 24); AddChild(top);
        top.AddChild(new Label { Text = head.Phase, ThemeTypeVariation = Variation.Display, SizeFlagsVertical = SizeFlags.ShrinkCenter });
        if (head.Edit is { } edit)
        {
            _action = new Button { Text = head.Action, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = lobby ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(lobby ? 220 : 0, 0) };
            _action.Pressed += () => Guarded.Send(s, edit);
            if (lobby) top.AddChild(_action); else AddChild(_action);
            // End setup counts the pieces off the table, so it follows the draft.
            _onBoard = () => { if (IsInstanceValid(_action) && s.Game is { } g && SetupHead.Of(g, s.IsCreator, s.Draft) is { Action: { } text }) _action.Text = text; };
            s.BoardChanged += _onBoard;
        }
        var todo = lobby ? (Container)new HFlowContainer() : new VBoxContainer();
        todo.AddThemeConstantOverride(lobby ? "h_separation" : "separation", lobby ? 28 : 2);
        AddChild(todo);
        foreach (var line in head.ToDo)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); todo.AddChild(row);
            row.AddChild(new TextureRect { Texture = line.Missing ? Ui.Icon("warning") : null, Modulate = Ui.Warning,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            row.AddChild(new Label { Text = line.Text, ThemeTypeVariation = line.Missing ? Variation.Warning : Variation.Accent,
                AutowrapMode = lobby ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = lobby ? SizeFlags.Fill : SizeFlags.ExpandFill });
        }
        if (!lobby) AddChild(new TeamColumnsPanel(compact: true));
    }
}
