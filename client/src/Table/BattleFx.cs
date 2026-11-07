using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Table;

/// <summary>
/// The table's battle effects (client AGENTS → Animation): for each shot (Session.Fired) a faint aim beam in the shooter's team colour,
/// held for its volley; and, while Settings.Animate is on, what <see cref="BattleEffects"/> plans from the batch's cues
/// (<see cref="SoundPlayer.Cued"/>) on the sound's clock: muzzle flashes with recoil, tracers, strikes with sparks, dust where a miss
/// lands, melee jabs, deflections, a wounded model's shake and a removed model's fall (the node <see cref="TableView.Removing"/> hands
/// over, kept standing until its Wound cue). Meshes and materials are shared; a flash fades by its instance's transparency. No physics:
/// a spark's arc is computed.
/// </summary>
public partial class BattleFx : Node3D
{
    const float Gravity = 30;   // inches a second², for sparks and debris
    Session _s = null!; TableView _view = null!;
    double _now;
    readonly List<(double At, FxAt Fx)> _waiting = [];
    readonly List<Live> _live = [];
    readonly Dictionary<Guid, PieceNode> _dying = new();
    readonly HashSet<Guid> _falling = [];
    IReadOnlyList<CueAt> _cues = [];
    Action<IReadOnlyList<CueAt>>? _onCued; Action<IReadOnlyList<Shot>>? _onFired; Action? _onBoard; SoundPlayer? _sounds;

    sealed class Live(double start, double seconds, Action<float> update, Action end)
    {
        public double Start = start, Seconds = seconds; public Action<float> Update = update; public Action End = end;
    }

    public void Bind(Session session, TableView view, SoundPlayer sounds)
    {
        _s = session; _view = view; _sounds = sounds;
        view.Removing = Take;
        _onCued = cues =>
        {
            _cues = cues;
            if (!_s.Settings.Animate) return;
            foreach (var fx in BattleEffects.Plan(cues, []))
            {
                if (fx.Kind == Fx.Fall && fx.Model is { } m) _falling.Add(m);
                _waiting.Add((_now + fx.Delay, fx));
            }
        };
        _onFired = shots =>
        {
            foreach (var group in shots.GroupBy(s => s.FrameId))
            {
                var last = _cues.Where(c => c.Cue == Cue.Shoot && c.Frame == group.Key).Select(c => c.Delay).DefaultIfEmpty(-1).Max();
                foreach (var shot in group) Beam(shot, last < 0 ? 1.0 : last + 0.6);
            }
            if (!_s.Settings.Animate) return;
            foreach (var fx in BattleEffects.Plan(_cues, shots).Where(f => f.From is not null)) _waiting.Add((_now + fx.Delay, fx));
        };
        _onBoard = Clear;
        sounds.Cued += _onCued; session.Fired += _onFired; session.BoardChanged += _onBoard;
    }

    public override void _ExitTree()
    {
        if (_s is null) return;
        if (_sounds is not null) _sounds.Cued -= _onCued;
        _s.Fired -= _onFired; _s.BoardChanged -= _onBoard;
        if (IsInstanceValid(_view)) _view.Removing = null;
    }

    /// <summary>A removed model's node, while animating: kept standing here until its fall plays; one the batch's cues do not fell is
    /// freed on the next frame.</summary>
    bool Take(PieceNode node)
    {
        if (!_s.Settings.Animate || _s.CatchingUp) return false;
        if (_dying.Remove(node.ModelId, out var old) && IsInstanceValid(old)) old.QueueFree();
        node.Reparent(this); _dying[node.ModelId] = node;
        return true;
    }

    /// <summary>A new board: nothing waiting plays, everything drawn goes.</summary>
    void Clear()
    {
        _waiting.Clear(); _falling.Clear(); _cues = [];
        foreach (var l in _live) l.End();
        _live.Clear();
        foreach (var n in _dying.Values) if (IsInstanceValid(n)) n.QueueFree();
        _dying.Clear();
        foreach (var c in GetChildren()) c.QueueFree();
    }

    public override void _Process(double delta)
    {
        _now += delta;
        // A removed model no Wound cue fells (an undo, a silent batch) goes at once.
        foreach (var id in _dying.Keys.Where(id => !_falling.Contains(id)).ToList()) { if (IsInstanceValid(_dying[id])) _dying[id].QueueFree(); _dying.Remove(id); }
        for (var i = 0; i < _waiting.Count;)
        {
            if (_waiting[i].At > _now) { i++; continue; }
            var fx = _waiting[i].Fx; _waiting.RemoveAt(i);
            Start(fx);
        }
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var l = _live[i]; var u = (float)Math.Clamp((_now - l.Start) / l.Seconds, 0, 1);
            l.Update(u);
            if (u >= 1) { l.End(); _live.RemoveAt(i); }
        }
    }

    void Run(double seconds, Action<float> update, Action end) { update(0); _live.Add(new Live(_now, Math.Max(seconds, 1e-3), update, end)); }

    void Start(FxAt fx)
    {
        switch (fx.Kind)
        {
            case Fx.Muzzle: Muzzle(fx); break;
            case Fx.Tracer: Tracer(fx); break;
            case Fx.Strike: Strike(fx); break;
            case Fx.Puff: Puff(WorldFrame.ToGodot(fx.To!.Value), fx.Index); break;
            case Fx.Jab: Jab(fx); break;
            case Fx.Deflect: Deflect(fx); break;
            case Fx.Shake: Shake(fx); break;
            case Fx.Fall: Fall(fx); break;
        }
    }

    // ---- shooting ----

    static readonly Color Ballistic = new(1f, 0.72f, 0.3f), Plasma = new(0.45f, 0.75f, 1f), Las = new(1f, 0.25f, 0.2f), Flame = new(1f, 0.5f, 0.12f),
        Dust = new(0.45f, 0.4f, 0.34f), Blood = new(0.85f, 0.18f, 0.08f), Spark = new(1f, 0.85f, 0.55f), Steel = new(0.75f, 0.85f, 1f);

    static Color ColourOf(int variant) => variant switch
    {
        SoundCues.Plasma => Plasma, SoundCues.Las => Las, SoundCues.Flame => Flame, _ => Ballistic,
    };

    static float SizeOf(int variant) => variant switch { SoundCues.Heavy => 2.6f, SoundCues.Plasma => 1.7f, SoundCues.Flame => 1.7f, SoundCues.Las => 1.0f, _ => 1.3f };

    void Muzzle(FxAt fx)
    {
        var from = WorldFrame.ToGodot(fx.From!.Value); var dir = (WorldFrame.ToGodot(fx.To!.Value) - from).Normalized();
        var colour = ColourOf(fx.Variant); var size = SizeOf(fx.Variant);
        var seconds = BattleEffects.MuzzleSeconds * (fx.Variant == SoundCues.Heavy ? 2.5 : 1.5);
        // Out in front of the body, so the hitbox does not veil it.
        Flash(from + dir * 0.9f, colour, size, seconds);
        Flash(from + dir * 0.8f, Colors.White, size * 0.4f, seconds * 0.7);
        if (fx.Model is { } id && _view.PieceAt(id) is { } shooter)
        {
            var kick = -dir with { Y = 0 } * (fx.Variant == SoundCues.Heavy ? 0.25f : 0.12f);
            Run(0.16, u => { if (IsInstanceValid(shooter)) shooter.SetJolt(kick * (u < 0.15f ? u / 0.15f : 1 - Ease((u - 0.15f) / 0.85f))); },
                () => { if (IsInstanceValid(shooter)) shooter.SetJolt(Vector3.Zero); });
        }
    }

    void Tracer(FxAt fx)
    {
        var from = WorldFrame.ToGodot(fx.From!.Value); var to = WorldFrame.ToGodot(fx.To!.Value); var length = from.DistanceTo(to);
        if (length < 0.01f) return;
        var dir = (to - from) / length; var colour = ColourOf(fx.Variant);
        if (fx.Variant == SoundCues.Flame)
        {
            // A gout of fire: puffs of flame rolling out along the line, growing as they go.
            for (var k = 0; k < 4; k++)
            {
                var lag = k * 0.04;
                var puff = Billboard(colour, 0.5f); AddChild(puff);
                Run(fx.Seconds + 0.25, u =>
                {
                    var t = (float)Math.Clamp((u * (fx.Seconds + 0.25) - lag) / fx.Seconds, 0, 1);
                    puff.Position = from + (to - from) * t + Vector3.Up * 0.3f * t; puff.Scale = Vector3.One * (0.4f + 1.4f * t);
                    puff.Transparency = t <= 0 ? 1 : 0.15f + 0.85f * Ease(t);
                }, puff.QueueFree);
            }
            return;
        }
        var bolt = new MeshInstance3D { Mesh = BoltMesh, MaterialOverride = Glow(colour.Lightened(0.35f)), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(bolt);
        bolt.Basis = new Basis(new Quaternion(Vector3.Up, dir));
        var streak = fx.Variant == SoundCues.Las ? Math.Min(length, 6f) : Math.Min(length * 0.35f, 1.6f);
        var thick = fx.Variant switch { SoundCues.Heavy => 3.2f, SoundCues.Plasma => 3.4f, SoundCues.Las => 1.3f, _ => 1.7f };
        // The streak runs behind its head and never back past the muzzle.
        Run(Math.Max(fx.Seconds, 0.06), u =>
        {
            var travelled = length * u; var shown = Math.Max(0.3f, Math.Min(streak, travelled));
            bolt.Position = from + dir * (Math.Max(travelled, shown) - shown / 2);
            bolt.Scale = new Vector3(thick, shown, thick); bolt.Transparency = fx.Variant == SoundCues.Las ? u : 0;
        }, bolt.QueueFree);
    }

    void Strike(FxAt fx)
    {
        Vector3 at; Vector3 from;
        if (fx.To is { } to) { at = WorldFrame.ToGodot(to); from = WorldFrame.ToGodot(fx.From!.Value); }
        else if (Pair(fx) is { Target: { } target } pair) { at = Chest(target); from = pair.Attacker is { } attacker ? Chest(attacker) : at + Vector3.Up; }
        else return;
        var colour = fx.To is null ? Spark : ColourOf(fx.Variant); var size = fx.To is null ? 0.8f : SizeOf(fx.Variant) * 1.2f;
        Flash(at, colour, size, 0.16);
        Flash(at, Colors.White, size * 0.4f, 0.1);
        Flash(at, Blood, size * 0.8f, 0.26);
        Sparks(at, (from - at).Normalized(), Spark, fx.Variant == SoundCues.Heavy ? 10 : 6, fx.Index);
        if (fx.Variant == SoundCues.Heavy && fx.To is not null) Puff(at with { Y = Math.Max(0, at.Y - 1) }, fx.Index, 1.8f);
    }

    void Puff(Vector3 at, int seed, float size = 1)
    {
        var dust = Billboard(Dust, 0.9f); AddChild(dust);
        Run(BattleEffects.PuffSeconds, u =>
        {
            dust.Position = at + Vector3.Up * (0.15f + 0.6f * u) * size; dust.Scale = Vector3.One * (0.35f + 1.1f * Ease(u)) * size;
            dust.Transparency = 0.25f + 0.75f * u;
        }, dust.QueueFree);
        Sparks(at + Vector3.Up * 0.1f, Vector3.Up, Dust.Darkened(0.4f), 4, seed);
    }

    // ---- melee, armour, wounds ----

    void Jab(FxAt fx)
    {
        if (Pair(fx) is not { Attacker: { } attacker, Target: { } target }) return;
        var dir = (target.GlobalPosition - attacker.GlobalPosition) with { Y = 0 };
        if (dir.LengthSquared() < 1e-6) return;
        var lunge = dir.Normalized() * 0.4f;
        Run(BattleEffects.JabSeconds, u => { if (IsInstanceValid(attacker)) attacker.SetJolt(lunge * Mathf.Sin(Mathf.Pi * Mathf.Pow(u, 0.7f))); },
            () => { if (IsInstanceValid(attacker)) attacker.SetJolt(Vector3.Zero); });
    }

    void Deflect(FxAt fx)
    {
        var models = UnitNodes(fx.TargetUnit).ToList();
        if (models.Count == 0) return;
        var target = models[(int)((uint)fx.Delay.GetHashCode() % (uint)models.Count)];
        var at = Chest(target);
        Flash(at, Steel, 0.6f, 0.12);
        Sparks(at, Vector3.Up, Steel, 7, models.Count);
    }

    void Shake(FxAt fx)
    {
        if (fx.Model is not { } id || _view.PieceAt(id) is not { } node) return;
        Flash(Chest(node), Blood, 0.7f, 0.18);
        var seed = id.GetHashCode() & 0xFF;
        Run(fx.Seconds, u =>
        {
            if (!IsInstanceValid(node)) return;
            var a = 0.09f * (1 - u) * (1 - u); var t = u * 40 + seed;
            node.SetJolt(new Vector3(Mathf.Sin(t * 1.7f) * a, 0, Mathf.Cos(t * 2.3f) * a));
        }, () => { if (IsInstanceValid(node)) node.SetJolt(Vector3.Zero); });
    }

    /// <summary>A removed model topples away from a side of its own (a hash of its id), sinks and fades, and is gone.</summary>
    void Fall(FxAt fx)
    {
        if (fx.Model is not { } id || !_dying.Remove(id, out var node) || !IsInstanceValid(node)) { if (fx.Model is { } m) _falling.Remove(m); return; }
        _falling.Remove(id);
        Flash(Chest(node), Blood, 0.9f, 0.25);
        var angle = (id.GetHashCode() & 0xFFFF) / 65535f * Mathf.Tau; var axis = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
        var rest = node.Transform; var geometry = Geometry(node).ToList();
        Run(fx.Seconds, u =>
        {
            var fall = Mathf.Min(1, u / 0.42f); var tilt = Mathf.DegToRad(85) * fall * fall;
            var settle = u < 0.42f ? 0 : (u - 0.42f) / 0.58f;
            node.Transform = new Transform3D(new Basis(axis, tilt) * rest.Basis, rest.Origin + Vector3.Down * 0.35f * settle);
            foreach (var g in geometry) if (IsInstanceValid(g)) g.Transparency = Math.Max(g.Transparency, settle);
        }, node.QueueFree);
    }

    // ---- aim beams ----

    void Beam(Shot shot, double seconds)
    {
        var a = WorldFrame.ToGodot(shot.From); var b = WorldFrame.ToGodot(shot.To); var length = a.DistanceTo(b);
        if (length < 0.01f) return;
        var pivot = new Node3D { Position = a, Basis = new Basis(new Quaternion(Vector3.Up, (b - a) / length)) }; AddChild(pivot);
        var beam = new MeshInstance3D { Mesh = BeamMesh, MaterialOverride = Glow(TeamColour(shot.ShooterUnitId)), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        pivot.AddChild(beam);
        const double Reach = 0.15, Fade = 0.3;
        Run(seconds, u =>
        {
            var t = u * seconds;
            beam.Scale = new Vector3(1, length * (float)Math.Min(1, t / Reach), 1); beam.Position = Vector3.Up * beam.Scale.Y / 2;
            beam.Transparency = 0.6f + 0.4f * (float)Math.Clamp((t - (seconds - Fade)) / Fade, 0, 1);
        }, pivot.QueueFree);
    }

    Color TeamColour(Guid unitId)
    {
        var participant = _s.Projection?.Units.GetValueOrDefault(unitId)?.ParticipantId;
        var teamId = _s.Game?.Participants.FirstOrDefault(p => p.Id == participant)?.TeamId;
        return Palette.Team(_s.Game?.Teams.FirstOrDefault(t => t.Id == teamId)?.Ordinal ?? 0);
    }

    // ---- pieces ----

    /// <summary>A melee die's attacker (round robin over its unit) and the target model nearest it, standing or falling.</summary>
    (PieceNode? Attacker, PieceNode? Target)? Pair(FxAt fx)
    {
        var attackers = UnitNodes(fx.Unit).ToList(); var targets = UnitNodes(fx.TargetUnit).ToList();
        if (targets.Count == 0) return null;
        var attacker = attackers.Count == 0 ? null : attackers[fx.Index % attackers.Count];
        var target = attacker is null ? targets[fx.Index % targets.Count] : targets.MinBy(t => t.GlobalPosition.DistanceSquaredTo(attacker.GlobalPosition));
        return (attacker, target);
    }

    IEnumerable<PieceNode> UnitNodes(Guid? unit)
    {
        if (unit is not { } u || _s.Projection is not { } p) yield break;
        foreach (var m in p.Models.Values.Where(m => m.UnitId == u).OrderBy(m => m.Id))
            if (_view.PieceAt(m.Id) is { } n) yield return n;
            else if (_dying.TryGetValue(m.Id, out var d) && IsInstanceValid(d)) yield return d;
    }

    static Vector3 Chest(PieceNode n) => n.Bounds is var b && b.Size != Vector3.Zero ? b.GetCenter() : n.GlobalPosition + Vector3.Up * 0.6f;

    static IEnumerable<GeometryInstance3D> Geometry(Node n)
    {
        if (n is GeometryInstance3D g) yield return g;
        foreach (var c in n.GetChildren()) foreach (var x in Geometry(c)) yield return x;
    }

    // ---- primitives ----

    void Flash(Vector3 at, Color colour, float size, double seconds)
    {
        var flash = Billboard(colour, size); flash.Position = at; AddChild(flash);
        Run(seconds, u => { flash.Scale = Vector3.One * size * (0.6f + 0.8f * Mathf.Sqrt(u)); flash.Transparency = Ease(u); }, flash.QueueFree);
    }

    /// <summary>Sparks thrown out about <paramref name="away"/> on computed arcs, fading as they fall.</summary>
    void Sparks(Vector3 at, Vector3 away, Color colour, int count, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + count + (int)(at.X * 31 + at.Z * 17)) };
        var material = Glow(colour);
        for (var k = 0; k < count; k++)
        {
            var v = (away.Normalized() * 0.6f + new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(0.2f, 1.2f), rng.RandfRange(-1, 1))).Normalized() * rng.RandfRange(4, 10);
            var spark = new MeshInstance3D { Mesh = SparkMesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(spark);
            var life = rng.RandfRange(0.22f, 0.4f);
            Run(life, u =>
            {
                var t = u * life; var vel = v + Vector3.Down * Gravity * t;
                spark.Position = at + v * t + Vector3.Down * 0.5f * Gravity * t * t;
                if (vel.LengthSquared() > 1e-6) spark.Basis = new Basis(new Quaternion(Vector3.Up, vel.Normalized()));
                spark.Transparency = u;
            }, spark.QueueFree);
        }
    }

    MeshInstance3D Billboard(Color colour, float size) => new()
    {
        Mesh = FlashMesh, MaterialOverride = FlashMaterial(colour), Scale = Vector3.One * size, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    static float Ease(float u) => 1 - (1 - u) * (1 - u);

    // ---- shared resources: one mesh per shape, one material per colour ----

    static readonly QuadMesh FlashMesh = new() { Size = Vector2.One };
    static readonly CapsuleMesh BoltMesh = new() { Radius = 0.05f, Height = 1, RadialSegments = 6, Rings = 1 };
    static readonly BoxMesh SparkMesh = new() { Size = new Vector3(0.05f, 0.32f, 0.05f) };
    static readonly CylinderMesh BeamMesh = new() { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 1, RadialSegments = 6, Rings = 1 };
    static readonly Dictionary<Color, StandardMaterial3D> Glows = new(), Flashes = new();
    static GradientTexture2D? _soft;

    /// <summary>A soft round spot, white in the middle to clear at the rim: every flash, puff and flame.</summary>
    static GradientTexture2D Soft => _soft ??= new GradientTexture2D
    {
        Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient { Offsets = [0f, 0.35f, 1f], Colors = [Colors.White, new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0)] },
    };

    /// <summary>An unshaded, glowing, see-through material in <paramref name="c"/>: bolts, sparks, beams.</summary>
    internal static StandardMaterial3D Glow(Color c)
    {
        if (Glows.TryGetValue(c, out var m)) return m;
        return Glows[c] = new StandardMaterial3D
        {
            AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    /// <summary>A camera-facing soft spot in <paramref name="c"/>, blended additively: muzzle flashes, strikes; dust is blended normally.</summary>
    internal static StandardMaterial3D FlashMaterial(Color c)
    {
        if (Flashes.TryGetValue(c, out var m)) return m;
        return Flashes[c] = new StandardMaterial3D
        {
            AlbedoColor = c, AlbedoTexture = Soft, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, BlendMode = c == Dust ? BaseMaterial3D.BlendModeEnum.Mix : BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false,
        };
    }

    /// <summary>For the shader warm-up: one of each mesh and material kind the effects draw.</summary>
    internal static IEnumerable<Node3D> WarmupNodes()
    {
        yield return new MeshInstance3D { Mesh = FlashMesh, MaterialOverride = FlashMaterial(Ballistic), Transparency = 0.3f };
        yield return new MeshInstance3D { Mesh = FlashMesh, MaterialOverride = FlashMaterial(Dust), Transparency = 0.3f, Position = new Vector3(1, 0, 0) };
        yield return new MeshInstance3D { Mesh = BoltMesh, MaterialOverride = Glow(Plasma), Position = new Vector3(2, 0, 0), Transparency = 0.2f };
        yield return new MeshInstance3D { Mesh = SparkMesh, MaterialOverride = Glow(Spark), Position = new Vector3(3, 0, 0), Transparency = 0.2f };
        yield return new MeshInstance3D { Mesh = BeamMesh, MaterialOverride = Glow(Palette.Team(0)), Position = new Vector3(4, 0, 0), Transparency = 0.6f };
        // A falling model fades its body: the proxy drawn see-through by its instance's transparency.
        yield return new MeshInstance3D { Mesh = Palette.Body(Tabletop.Geometry.Base.Circle(1), 1), MaterialOverride = Palette.Translucent(Palette.Proxy), Position = new Vector3(5, 0, 0), Transparency = 0.5f };
    }
}
