using Tabletop.Geometry;

namespace Tabletop.Client.Core;

/// <summary>What the table draws for a cue: a muzzle flash and recoil on a shooter, a tracer in flight, a strike (flash and sparks) on a
/// hit, a puff of dust where a miss lands, a melee jab, a deflection off armour, a wounded model's shake, a removed model's fall.</summary>
public enum Fx { Muzzle, Tracer, Strike, Puff, Jab, Deflect, Shake, Fall }

/// <summary>One effect, Delay seconds after its batch arrives, lasting Seconds. Model is the model it plays on (the shooter, the
/// struck, wounded or removed model); Unit and TargetUnit the attacking and attacked units of a melee jab or strike, or a deflect's
/// unit, where the table picks the models (Index, the die's place in its volley, round-robins them); From and To a shot's points;
/// Variant the shot's weapon group (SoundCues.Plasma …).</summary>
public sealed record FxAt(Fx Kind, double Delay, double Seconds)
{
    public Guid? Model { get; init; }
    public Guid? Unit { get; init; }
    public Guid? TargetUnit { get; init; }
    public Point3? From { get; init; }
    public Point3? To { get; init; }
    public int Variant { get; init; }
    public int Index { get; init; }
}

/// <summary>
/// The table's battle effects for a batch (client AGENTS → Animation), on the sound's clock: they take the cues <see cref="SoundCues"/>
/// read off the batch and the shots <see cref="ShotLines"/> read off it, so a muzzle flashes when its report sounds and a strike lands
/// when the round would. A volley's die k fires the frame's shot k round robin (a group of three models with two attacks each flashes
/// each model twice); its tracer flies at its weapon group's speed, at most <see cref="MaxFlight"/>, to the target point on a hit, or to
/// a point scattered beyond the target on a miss. A melee die is a jab by the attacking unit, with a strike on a hit. A Wound cue shakes
/// every model it wounded and fells every model it removed; a Block deflects off the target unit.
/// </summary>
public static class BattleEffects
{
    public const double MaxFlight = 0.3, MuzzleSeconds = 0.08, StrikeSeconds = 0.45, PuffSeconds = 0.7, JabSeconds = 0.24,
        DeflectSeconds = 0.3, ShakeSeconds = 0.32, FallSeconds = 1.1;
    /// <summary>A melee strike lands this far into its jab: when the jab reaches the target.</summary>
    public const double JabContact = 0.4;
    /// <summary>A miss lands between these distances beyond the target point, in inches.</summary>
    public const double ScatterNear = 0.7, ScatterFar = 1.8;

    /// <summary>A weapon group's tracer speed, inches a second: a las bolt is all but instant, a flamer's gout slow.</summary>
    public static double Speed(int variant) => variant switch
    {
        SoundCues.Las => 600, SoundCues.Plasma => 140, SoundCues.Heavy => 110, SoundCues.Flame => 45, _ => 260,
    };

    public static double Flight(Shot s, int variant) => Math.Min(MaxFlight, Distance(s.From, s.To) / Speed(variant));

    public static IReadOnlyList<FxAt> Plan(IReadOnlyList<CueAt> cues, IReadOnlyList<Shot> shots)
    {
        var fx = new List<FxAt>(); var fired = new Dictionary<Guid, int>();
        foreach (var c in cues)
        {
            switch (c.Cue)
            {
                case Cue.Shoot when c.Frame is { } frame:
                {
                    var own = shots.Where(s => s.FrameId == frame).ToList();
                    var mine = own.Count > 0 ? own : shots;
                    if (mine.Count == 0) break;
                    var k = fired.GetValueOrDefault(frame); fired[frame] = k + 1;
                    var shot = mine[k % mine.Count]; var flight = Flight(shot, c.Variant);
                    var lands = c.Hit ? shot.To : Scatter(shot, frame, k);
                    fx.Add(new FxAt(Fx.Muzzle, c.Delay, MuzzleSeconds) { Model = shot.ShooterModelId, Unit = shot.ShooterUnitId, From = shot.From, To = shot.To, Variant = c.Variant, Index = k });
                    fx.Add(new FxAt(Fx.Tracer, c.Delay, flight) { Model = shot.ShooterModelId, Unit = shot.ShooterUnitId, From = shot.From, To = lands, Variant = c.Variant, Index = k });
                    fx.Add(c.Hit
                        ? new FxAt(Fx.Strike, c.Delay + flight, StrikeSeconds) { Model = shot.TargetModelId == Guid.Empty ? null : shot.TargetModelId, From = shot.From, To = shot.To, Variant = c.Variant, Index = k }
                        : new FxAt(Fx.Puff, c.Delay + flight, PuffSeconds) { From = shot.From, To = lands, Variant = c.Variant, Index = k });
                    break;
                }
                case Cue.Melee or Cue.Whiff when c.Volley && c.Actor is { } actor && c.Target is { } target:
                {
                    var key = c.Frame ?? Guid.Empty; var k = fired.GetValueOrDefault(key); fired[key] = k + 1;
                    fx.Add(new FxAt(Fx.Jab, c.Delay, JabSeconds) { Unit = actor, TargetUnit = target, Index = k });
                    if (c.Cue == Cue.Melee) fx.Add(new FxAt(Fx.Strike, c.Delay + JabContact * JabSeconds, StrikeSeconds) { Unit = actor, TargetUnit = target, Index = k });
                    break;
                }
                case Cue.Block when c.Target is { } unit:
                    fx.Add(new FxAt(Fx.Deflect, c.Delay, DeflectSeconds) { TargetUnit = unit, Unit = c.Actor });
                    break;
                case Cue.Wound:
                    foreach (var m in c.Wounded.Where(m => !c.Removed.Contains(m))) fx.Add(new FxAt(Fx.Shake, c.Delay, ShakeSeconds) { Model = m });
                    foreach (var m in c.Removed) fx.Add(new FxAt(Fx.Fall, c.Delay, FallSeconds) { Model = m });
                    break;
            }
        }
        return fx;
    }

    /// <summary>Where a miss lands: beyond the target point along the line of fire, swung off it by up to 35°, at the target's foot.</summary>
    public static Point3 Scatter(Shot s, Guid frame, int k)
    {
        double U(int salt) => (Fnv(frame, k * 7 + salt) & 0xFFFFFF) / (double)0xFFFFFF;
        var dx = s.To.X - s.From.X; var dy = s.To.Y - s.From.Y; var len = Math.Sqrt(dx * dx + dy * dy);
        var heading = len < 1e-9 ? 0 : Math.Atan2(dy, dx);
        var angle = heading + (2 * U(1) - 1) * 35 * Math.PI / 180; var r = ScatterNear + (ScatterFar - ScatterNear) * U(2);
        return new Point3(s.To.X + r * Math.Cos(angle), s.To.Y + r * Math.Sin(angle), s.Foot);
    }

    static uint Fnv(Guid g, int salt)
    {
        var h = 2166136261u;
        foreach (var b in BitConverter.GetBytes(salt).Concat(g.ToByteArray())) { h ^= b; h *= 16777619; }
        return h;
    }

    static double Distance(Point3 a, Point3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
}
