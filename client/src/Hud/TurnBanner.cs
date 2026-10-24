using Godot; using Tabletop.Client.Core; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Hud;

/// <summary>Across the upper third (HudPart.Banner), one banner after another: the roll-off's result (RollOffBanners) ahead of the batch's
/// round and turn banners, its rolls on a smaller second line. Off with Animate off or the banner setting off; the roll-off's result also
/// not in a replay. Clicks pass through.</summary>
public partial class TurnBanner : PanelContainer
{
    Session _s = null!; readonly TurnBanners _banners = new(); readonly Queue<Banner> _queue = new(); double _left;
    readonly Label _text = new() { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = Variation.Banner, MouseFilter = MouseFilterEnum.Ignore };
    readonly Label _detail = new() { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = Variation.BannerDetail, MouseFilter = MouseFilterEnum.Ignore };
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session session)
    {
        _s = session; MouseFilter = MouseFilterEnum.Ignore; Visible = false;
        var lines = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        lines.AddChild(_text); lines.AddChild(_detail); AddChild(lines);
        _banners.Seed(session.Projection?.Scheduler.ActiveTeamId);
        _onEvents = batch =>
        {
            if (_s.Game is not { } g) return;
            var names = new Names(g, _s.Projection); var enabled = _s.Settings.Animate && _s.Settings.TurnBanner;
            if (RollOffBanners.Of(batch, names, enabled && !_s.Replaying, _s.CatchingUp) is { } rollOff) _queue.Enqueue(rollOff);
            foreach (var b in _banners.OnBatch(batch, names, enabled, _s.CatchingUp)) _queue.Enqueue(b);
        };
        _onBoard = _banners.OnBoard;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
    }
    public override void _ExitTree() { if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }

    public override void _Process(double delta)
    {
        if ((_left -= delta) > 0) return;
        Visible = _queue.TryDequeue(out var b);
        if (b is null) return;
        _text.Text = b.Text; _detail.Text = b.Detail ?? ""; _detail.Visible = b.Detail is not null; _left = b.Seconds;
    }
}
