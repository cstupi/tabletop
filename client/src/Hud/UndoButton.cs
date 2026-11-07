using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Panels;
using Tabletop.Client.Table;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>Lower right, left of the log button (client UX spec → Undo), drawn `↶ Undo ⌃Z`: undoes the last action in the game, whoever took it (UndoPlan).
/// Disabled only when nothing is undoable; the tooltip names the action. When the undo would take dice rolls with it, a dialog lists
/// them first. Ctrl+Z presses it. GameScreen.Layout places it.</summary>
public partial class UndoButton : Button
{
    Session _s = null!; TableInput _input = null!;
    UndoPlan? _plan; ConfirmationDialog? _asking;
    Action<RenderCause>? _onFrame; Action<Intent>? _onIntent; Action? _onTheme;

    /// <summary>A failed undo, for the screen's toast.</summary>
    public event Action<string>? Reported;

    public void Bind(Session session, TableInput input)
    {
        _s = session; _input = input;
        HudBits.Fill(this, "undo", "Undo", "⌃Z"); FocusMode = FocusModeEnum.None;
        Pressed += Press;
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board | RenderCause.Game)) != 0) Render(); };
        _onIntent = intent => { if (intent == Intent.Undo && Visible && !Disabled) Press(); };
        session.Frame.Render += _onFrame; input.IntentPressed += _onIntent;
        _onTheme = () => HudBits.Fill(this, "undo", "Undo", "⌃Z"); Ui.Changed += _onTheme;
        Render();
    }

    public override void _ExitTree()
    {
        Ui.Changed -= _onTheme;
        if (_s is null) return;
        _s.Frame.Render -= _onFrame;
        if (IsInstanceValid(_input)) _input.IntentPressed -= _onIntent;
    }

    /// <summary>While an undo travels the log still holds what it undoes: the shared combat log plans as if its marker had landed, so each press steps back one more.</summary>
    void Render()
    {
        Visible = !_s.IsWatching;                       // a watcher's undo is refused: the button is not offered
        _plan = _s.CombatLog?.Undo;
        Disabled = _plan is null;
        Modulate = Disabled ? Ui.Faded : HudBits.Plain;   // the drawn row does not follow the button's disabled colours
        TooltipText = _plan?.Tooltip ?? "Nothing to undo";
    }

    void Press()
    {
        if (_plan is not { } plan || _asking is not null && IsInstanceValid(_asking)) return;
        if (plan.LostRolls.Count == 0) { Send(plan.ToSeq); return; }
        var d = _asking = new ConfirmationDialog
        {
            Title = "Undo dice rolls?", DialogText = string.Join("\n", plan.LostRolls), OkButtonText = "Undo", CancelButtonText = "Cancel", Theme = Ui.Theme,
        };
        d.GetCancelButton().Flat = true; d.GetCancelButton().ThemeTypeVariation = Variation.FlatButton;   // Undo is the one lit button
        d.Confirmed += () => Send(plan.ToSeq);
        d.VisibilityChanged += () => { if (!d.Visible) { d.QueueFree(); _asking = null; } };
        AddChild(d); d.PopupCentered();
    }

    void Send(long to) => Guarded.Run(() => _s.UndoToAsync(to), message => Reported?.Invoke($"Undo failed: {message}"));
}
