using Godot; using Tabletop.Client.Core; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>Plays the cues <see cref="SoundCues"/> reads off each live batch, one AudioStreamPlayer per cue (a cue already playing restarts).
/// Silent while the session catches up or replays, and when Settings.Sound is off.</summary>
public partial class SoundPlayer : Node
{
    Session _s = null!; readonly SoundCues _cues = new(); readonly Dictionary<Cue, AudioStreamPlayer> _players = new();
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session session)
    {
        _s = session;
        foreach (var cue in Enum.GetValues<Cue>())
        {
            var player = new AudioStreamPlayer { Name = cue.ToString(), Stream = GD.Load<AudioStream>($"res://content/sounds/{cue.ToString().ToLowerInvariant()}.wav") };
            AddChild(player); _players[cue] = player;
        }
        if (session.Projection is { } p) _cues.Seed(p);
        _onEvents = batch =>
        {
            if (_s.Projection is not { } q) return;
            foreach (var cue in _cues.OnBatch(batch, q, _s.CatchingUp || _s.Replaying)) Play(cue);
        };
        _onBoard = () => _cues.OnBoard();
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
    }

    void Play(Cue cue)
    {
        if (!_s.Settings.Sound || !_players.TryGetValue(cue, out var player) || player.Stream is null) return;
        player.VolumeDb = _s.Settings.Volume <= 0 ? -80f : Mathf.LinearToDb(_s.Settings.Volume / 100f);
        player.Play();
    }

    public override void _ExitTree() { if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }
}
