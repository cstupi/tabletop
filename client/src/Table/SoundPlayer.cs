using Godot; using Tabletop.Client.Core; using Tabletop.Protocol;
namespace Tabletop.Client.Table;

/// <summary>Plays the cues <see cref="SoundCues"/> reads off each live batch, each at its delay, pitch and level. A volley's die sounds
/// play on their own pool of voices and every other cue on another, so a long volley never steals an impact's or a wound's voice and
/// one sound never cuts another off; when a pool is busy its oldest voice is reused. A replay plays its forward steps (Play at 1× or slower, Step and
/// Action forward); silent while the session catches up, on a replay's jump or step back, and when Settings.Sound is off; an undo or a new board drops what is still waiting.</summary>
public partial class SoundPlayer : Node
{
    const int Voices = 8, VolleyVoices = 16;
    Session _s = null!; readonly SoundCues _cues = new();
    readonly Dictionary<string, AudioStream> _streams = new();
    readonly List<AudioStreamPlayer> _pool = [], _volley = [];
    readonly List<(double At, CueAt Cue, AudioStream Stream)> _waiting = [];
    readonly Dictionary<AudioStreamPlayer, double> _started = new();
    double _now;
    Action<IReadOnlyList<GameEvent>>? _onEvents; Action? _onBoard;

    public void Bind(Session session)
    {
        _s = session;
        foreach (var cue in Enum.GetValues<Cue>())
            for (var v = 0; v < SoundCues.Variants(cue); v++)
            {
                var file = SoundCues.File(cue, v);
                if (GD.Load<AudioStream>($"res://content/sounds/{file}.wav") is { } stream) _streams[file] = stream;
            }
        for (var i = 0; i < Voices; i++) { var p = new AudioStreamPlayer { Name = $"Voice{i}" }; AddChild(p); _pool.Add(p); }
        for (var i = 0; i < VolleyVoices; i++) { var p = new AudioStreamPlayer { Name = $"Volley{i}" }; AddChild(p); _volley.Add(p); }
        if (session.Projection is { } p0) _cues.Seed(p0);
        _onEvents = batch =>
        {
            if (_s.Projection is not { } q || _s.Game is not { } game) return;
            if (batch.Any(e => e.Payload is Undone)) _waiting.Clear();
            foreach (var cue in _cues.OnBatch(batch, q, _s.CatchingUp, new Names(game, q)))
                if (_streams.TryGetValue(SoundCues.File(cue.Cue, cue.Variant), out var stream)) _waiting.Add((_now + cue.Delay, cue, stream));
        };
        _onBoard = () => { _cues.OnBoard(); _waiting.Clear(); };
        session.EventsApplied += _onEvents; session.BoardChanged += _onBoard;
    }

    public override void _Process(double delta)
    {
        _now += delta;
        for (var i = 0; i < _waiting.Count;)
        {
            if (_waiting[i].At > _now) { i++; continue; }
            Play(_waiting[i].Cue, _waiting[i].Stream); _waiting.RemoveAt(i);
        }
    }

    void Play(CueAt cue, AudioStream stream)
    {
        if (!_s.Settings.Sound) return;
        var pool = cue.Volley ? _volley : _pool;
        var voice = pool.FirstOrDefault(p => !p.Playing) ?? pool.MinBy(p => _started.GetValueOrDefault(p))!;
        _started[voice] = _now;
        voice.Stream = stream;
        voice.PitchScale = (float)cue.Pitch;
        voice.VolumeDb = (_s.Settings.Volume <= 0 ? -80f : Mathf.LinearToDb(_s.Settings.Volume / 100f)) + (float)cue.VolumeDb;
        voice.Play();
    }

    public override void _ExitTree() { if (_s is null) return; _s.EventsApplied -= _onEvents; _s.BoardChanged -= _onBoard; }
}
