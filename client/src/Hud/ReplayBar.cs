using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Hud;

/// <summary>
/// The replay's controls (client design → Replay; UX redesign part 2 → Replay bar), at the left column's foot: labelled icon buttons back
/// by round, turn, action and step, Play/Pause, then forward by step, action, turn and round (ReplayBarView); a scrubber over the whole log
/// with a tick at each round's start; the speed; the caption, the position's block header. A step or play animates as play does; a jump or
/// the scrubber redraws at the new position. Playing stops at the end of an ended game and waits there for a running one's next events.
/// GameScreen.Layout places it.
/// </summary>
public partial class ReplayBar : PanelContainer
{
    Session _s = null!;
    readonly ReplayClock _clock = new();
    Button _play = null!; HSlider _scrub = null!; OptionButton _speed = null!; Label _caption = null!; RoundTicks _ticks = null!;
    readonly List<(Button Button, string Icon)> _icons = [];
    Action<RenderCause>? _onFrame; Action? _onTheme;

    public void Bind(Session session)
    {
        _s = session;
        MouseFilter = MouseFilterEnum.Stop;
        var rows = new VBoxContainer(); rows.AddThemeConstantOverride("separation", 6); AddChild(rows);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; buttons.AddThemeConstantOverride("separation", 2); rows.AddChild(buttons);
        foreach (var b in ReplayBarView.Back) buttons.AddChild(Step(b));
        buttons.AddChild(_play = Btn("Play", "play", "Play the game forward one step at a time", TogglePlay));
        foreach (var b in ReplayBarView.Forward) buttons.AddChild(Step(b));
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); rows.AddChild(line);
        var track = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30) }; line.AddChild(track);
        track.AddChild(_ticks = new RoundTicks()); _ticks.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        track.AddChild(_scrub = new HSlider { MinValue = 0, Step = 1, FocusMode = FocusModeEnum.None, TooltipText = "Drag to any point in the game" });
        _scrub.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide); _scrub.OffsetBottom = 18;
        _scrub.ValueChanged += v => { if (_s.Replay is { } r && (int)v != r.Position) _s.SeekReplay((int)v); };
        line.AddChild(_speed = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "Playback speed", SizeFlagsVertical = SizeFlags.ShrinkBegin });
        foreach (var x in ReplayClock.Speeds) _speed.AddItem($"{x:0.#}×");
        _speed.Select(ReplayClock.Speeds.ToList().IndexOf(1));
        _speed.ItemSelected += i => _clock.Speed = ReplayClock.Speeds[(int)i];
        rows.AddChild(_caption = new Label { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        _onFrame = c => { if ((c & (RenderCause.Events | RenderCause.Board | RenderCause.Game)) != 0) Render(); };
        session.Frame.Render += _onFrame;
        _onTheme = () => { foreach (var (b, icon) in _icons) { b.Icon = Ui.Icon(icon); Tint(b); } };
        Ui.Changed += _onTheme;
        Render();
    }

    public override void _ExitTree() { if (_s is not null) _s.Frame.Render -= _onFrame; Ui.Changed -= _onTheme; }

    public void Place(HudRect slot) => HudPlacement.Bottom(this, slot);

    /// <summary>A flat button with its icon over a small word.</summary>
    Button Btn(string label, string icon, string tip, Action pressed)
    {
        var b = new Button { Text = label, Icon = Ui.Icon(icon), TooltipText = tip, FocusMode = FocusModeEnum.None, ThemeTypeVariation = Variation.FlatButton,
            IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Top, ExpandIcon = false, CustomMinimumSize = new Vector2(42, 0) };
        b.AddThemeFontSizeOverride("font_size", 12);
        b.AddThemeConstantOverride("icon_max_width", 22);
        Tint(b);
        b.Pressed += pressed;
        _icons.Add((b, icon));
        return b;
    }

    /// <summary>The white icons in the theme's text colour, the accent when pressed or hovered, dim when disabled.</summary>
    static void Tint(Button b)
    {
        foreach (var state in new[] { "icon_normal_color", "icon_focus_color" }) b.AddThemeColorOverride(state, Ui.Text);
        foreach (var state in new[] { "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) b.AddThemeColorOverride(state, Ui.Accent);
        b.AddThemeColorOverride("icon_disabled_color", Ui.DimText);
    }

    Button Step(ReplayStepButton s) => Btn(s.Label, s.Icon, s.Tip, () =>
    {
        if (_s.Replay is not { } r) return;
        _clock.Pause();
        // One step or action forward plays out; a turn, a round or anything backward is a jump.
        _s.SeekReplay(s.Back ? r.Previous(s.Unit) : r.Next(s.Unit), animate: !s.Back && s.Unit is ReplayUnit.Step or ReplayUnit.Action);
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

    int _ticksFor = -1;

    void Render()
    {
        var r = _s.Replay;
        Visible = r is not null;
        if (r is null) return;
        _scrub.MaxValue = Math.Max(1, r.Count);
        _scrub.SetValueNoSignal(r.Position);
        _scrub.TooltipText = $"{r.Position} of {r.Count}";
        if (_ticksFor != r.Count) { _ticksFor = r.Count; _ticks.Set(ReplayBarView.Ticks(r.Source)); }
        _caption.Text = ReplayBarView.Caption(_s.CombatLog?.Entries ?? [], r.Projection.Scheduler.Round);
        _caption.TooltipText = _caption.Text;
        var playing = _clock.Playing;
        _play.Text = playing ? "Pause" : "Play";
        _play.Icon = Ui.Icon(playing ? "pause" : "play");
        _icons[_icons.FindIndex(x => x.Button == _play)] = (_play, playing ? "pause" : "play");
    }

    /// <summary>The round boundaries under the scrubber: a tick and the round's number at each, in the dim text colour.</summary>
    partial class RoundTicks : Control
    {
        IReadOnlyList<ReplayTick> _ticks = [];
        public RoundTicks() { MouseFilter = MouseFilterEnum.Ignore; }
        public void Set(IReadOnlyList<ReplayTick> ticks) { _ticks = ticks; QueueRedraw(); }

        public override void _Draw()
        {
            const float inset = 8;                                  // the slider's grabber keeps its centre this far from each end
            var font = GetThemeDefaultFont(); var w = Size.X - 2 * inset; var lastLabel = float.NegativeInfinity;
            foreach (var t in _ticks)
            {
                var x = inset + (float)t.At * w;
                DrawLine(new Vector2(x, 2), new Vector2(x, 18), Ui.DimText, 2);
                if (x - lastLabel < 18) continue;
                DrawString(font, new Vector2(x - 4, Size.Y), t.Round.ToString(), HorizontalAlignment.Left, -1, 11, Ui.DimText);
                lastLabel = x;
            }
        }
    }
}
