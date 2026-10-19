using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>
/// The replay's controls (client design → Replay), at the left column's foot: back and forward by round, turn, action and step, play
/// and pause at a speed, a scrubber over the whole log, and where the table stands. A step or play animates as play does; a jump or
/// the scrubber redraws at the new position. Playing stops at the end of an ended game and waits there for a running one's next events.
/// GameScreen.Layout places it.
/// </summary>
public partial class ReplayBar : PanelContainer
{
    Session _s = null!;
    readonly ReplayClock _clock = new();
    Button _play = null!; HSlider _scrub = null!; OptionButton _speed = null!; Label _caption = null!;
    Action<RenderCause>? _onFrame;

    public void Bind(Session session)
    {
        _s = session;
        MouseFilter = MouseFilterEnum.Stop;
        var rows = new VBoxContainer(); AddChild(rows);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; rows.AddChild(buttons);
        buttons.AddChild(Step("«R", "Back to the start of this round, or the round before", ReplayUnit.Round, back: true));
        buttons.AddChild(Step("«T", "Back to the start of this turn, or the turn before", ReplayUnit.Turn, back: true));
        buttons.AddChild(Step("‹A", "Back one action", ReplayUnit.Action, back: true));
        buttons.AddChild(Step("‹", "Back one step: a player's input and what the rules did with it", ReplayUnit.Step, back: true));
        buttons.AddChild(_play = Btn("Play", "Play the game forward one step at a time", TogglePlay));
        buttons.AddChild(Step("›", "Forward one step: a player's input and what the rules did with it", ReplayUnit.Step, back: false));
        buttons.AddChild(Step("A›", "Forward one action", ReplayUnit.Action, back: false));
        buttons.AddChild(Step("T»", "Forward to the next turn", ReplayUnit.Turn, back: false));
        buttons.AddChild(Step("R»", "Forward to the next round", ReplayUnit.Round, back: false));
        var line = new HBoxContainer(); rows.AddChild(line);
        line.AddChild(_scrub = new HSlider { MinValue = 0, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None,
            TooltipText = "Drag to any point in the game" });
        _scrub.ValueChanged += v => { if (_s.Replay is { } r && (int)v != r.Position) _s.SeekReplay((int)v); };
        line.AddChild(_speed = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "Playback speed" });
        foreach (var x in ReplayClock.Speeds) _speed.AddItem($"{x:0.#}×");
        _speed.Select(ReplayClock.Speeds.ToList().IndexOf(1));
        _speed.ItemSelected += i => _clock.Speed = ReplayClock.Speeds[(int)i];
        rows.AddChild(_caption = new Label { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true });
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board | RenderCause.Game)) != 0) Render(); };
        session.Frame.Render += _onFrame;
        Render();
    }

    public override void _ExitTree() { if (_s is not null) _s.Frame.Render -= _onFrame; }

    public void Place(HudRect slot) => HudPlacement.Bottom(this, slot);

    static Button Btn(string text, string tip, Action pressed)
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None };
        b.Pressed += pressed;
        return b;
    }

    Button Step(string text, string tip, ReplayUnit unit, bool back) => Btn(text, tip, () =>
    {
        if (_s.Replay is not { } r) return;
        _clock.Pause();
        // One step or action forward plays out; a turn, a round or anything backward is a jump.
        _s.SeekReplay(back ? r.Previous(unit) : r.Next(unit), animate: !back && unit is ReplayUnit.Step or ReplayUnit.Action);
        Render();
    });

    void TogglePlay()
    {
        if (_clock.Playing) _clock.Pause();
        else
        {
            if (_s.Replay is { AtEnd: true } && _s.Game?.Status == GameStatus.Ended) _s.SeekReplay(0);   // play again from the start
            _clock.Play();
        }
        Render();
    }

    public override void _Process(double delta)
    {
        if (!_clock.Tick(delta) || _s.Replay is not { } r) return;
        if (r.AtEnd) { if (_s.Game?.Status == GameStatus.Ended) { _clock.Pause(); Render(); } return; }
        _s.SeekReplay(r.Next(ReplayUnit.Step), animate: _clock.Speed <= 1);
    }

    void Render()
    {
        var r = _s.Replay;
        Visible = r is not null;
        if (r is null) return;
        _scrub.MaxValue = Math.Max(1, r.Count);
        _scrub.SetValueNoSignal(r.Position);
        _caption.Text = r.Caption;
        _play.Text = _clock.Playing ? "Pause" : "Play";
    }
}
