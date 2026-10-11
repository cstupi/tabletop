using Godot; using Tabletop.Client.Core; using Tabletop.Client.Theming; using Tabletop.Protocol;
namespace Tabletop.Client.Hud;

/// <summary>Across the upper third (HudPart.Banner), one banner after another; off with Animate off or the banner setting off. Clicks pass through.</summary>
public partial class TurnBanner : PanelContainer
{
    Session _s = null!; readonly TurnBanners _banners = new(); readonly Queue<Banner> _queue = new(); double _left;
    readonly Label _text = new() { HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = Variation.Banner, MouseFilter = MouseFilterEnum.Ignore };
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session session)
    {
        _s = session; AddChild(_text); MouseFilter = MouseFilterEnum.Ignore; Visible = false;
        _banners.Seed(session.Projection?.Scheduler.ActiveTeamId);
        _onEvents = batch => { if (_s.Game is { } g) foreach (var b in _banners.OnBatch(batch, new Names(g, _s.Projection), _s.Settings.Animate && _s.Settings.TurnBanner, _s.CatchingUp)) _queue.Enqueue(b); };
        _onBoard = _banners.OnBoard;
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
    }
    public override void _ExitTree() { if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }

    public override void _Process(double delta)
    {
        if ((_left -= delta) > 0) return;
        Visible = _queue.TryDequeue(out var b);
        if (b is not null) { _text.Text = b.Text; _left = b.Seconds; }
    }
}
