namespace Tabletop.Sounds;

/// <summary>The building blocks of a cue: seeded noise, filters (one-pole, biquad, a swept state-variable filter), sine, sweep and FM
/// tones, envelopes, saturation, grains, echo, a room, and the master stage that sets loudness under a peak ceiling.</summary>
public static class Synth
{
    const double Rate = Wav.Rate;

    public static int Samples(double seconds) => (int)(seconds * Rate);
    public static float[] Silence(double seconds) => new float[Samples(seconds)];

    public static float[] Noise(int seed, int length)
    {
        var rng = new Random(seed); var x = new float[length];
        for (var i = 0; i < length; i++) x[i] = (float)(rng.NextDouble() * 2 - 1);
        return x;
    }

    /// <summary>Brown noise (leaky-integrated white) at an RMS of 0.5: rumble, weight.</summary>
    public static float[] Brown(int seed, int length)
    {
        var w = Noise(seed, length); var y = new float[length]; double s = 0;
        for (var i = 0; i < length; i++) { s = 0.995 * s + w[i] * 0.1; y[i] = (float)s; }
        return Rms(y, 0.5);
    }

    /// <summary>Two one-pole low-pass stages: 12 dB per octave, soft.</summary>
    public static float[] LowPass(float[] x, double hz) => Pole(Pole(x, hz, false), hz, false);

    public static float[] HighPass(float[] x, double hz) => Pole(Pole(x, hz, true), hz, true);

    static float[] Pole(float[] x, double hz, bool high)
    {
        var a = (float)(1 - Math.Exp(-2 * Math.PI * hz / Rate)); var y = new float[x.Length]; float lp = 0;
        for (var i = 0; i < x.Length; i++) { lp += a * (x[i] - lp); y[i] = high ? x[i] - lp : lp; }
        return y;
    }

    public enum Mode { Low, High, Band }

    /// <summary>A resonant 12 dB/octave filter (RBJ biquad) at a fixed frequency; band pass at unit peak gain.</summary>
    public static float[] Filter(float[] x, Mode mode, double hz, double q = 0.707) => Swept(x, mode, _ => hz, q);

    /// <summary>A state-variable filter (topology-preserving) whose frequency follows <paramref name="hzAt"/> (seconds → Hz): a whoosh, a
    /// discharge, a sizzle that falls.</summary>
    public static float[] Swept(float[] x, Mode mode, Func<double, double> hzAt, double q = 0.707)
    {
        var y = new float[x.Length]; double ic1 = 0, ic2 = 0; var k = 1 / q;
        for (var i = 0; i < x.Length; i++)
        {
            var g = Math.Tan(Math.PI * Math.Min(hzAt(i / Rate), Rate * 0.45) / Rate);
            var a1 = 1 / (1 + g * (g + k)); var a2 = g * a1; var a3 = g * a2;
            var v3 = x[i] - ic2; var v1 = a1 * ic1 + a2 * v3; var v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2 * v1 - ic1; ic2 = 2 * v2 - ic2;
            y[i] = (float)(mode switch { Mode.Low => v2, Mode.High => x[i] - k * v1 - v2, _ => k * v1 });
        }
        return y;
    }

    /// <summary>An exponential glide from <paramref name="from"/> to <paramref name="to"/> over <paramref name="seconds"/>, then held.</summary>
    public static Func<double, double> Glide(double from, double to, double seconds) => t => from * Math.Pow(to / from, Math.Min(1, t / seconds));

    /// <summary>A sine whose pitch falls exponentially from <paramref name="from"/> to <paramref name="to"/> Hz over its length.</summary>
    public static float[] Sweep(int length, double from, double to) => Tone(length, t => from * Math.Pow(to / from, t * Rate / length));

    /// <summary>A sine following <paramref name="hzAt"/> (seconds → Hz).</summary>
    public static float[] Tone(int length, Func<double, double> hzAt)
    {
        var y = new float[length]; double phase = 0;
        for (var i = 0; i < length; i++) { y[i] = (float)Math.Sin(phase); phase += 2 * Math.PI * hzAt(i / Rate) / Rate; }
        return y;
    }

    public static float[] Sine(int length, double hz) => Tone(length, _ => hz);

    /// <summary>Two-operator FM: a carrier following <paramref name="hzAt"/>, its modulator at <paramref name="ratio"/> times it, the
    /// modulation index following <paramref name="indexAt"/>: bright and buzzing at a high index, a plain tone at zero.</summary>
    public static float[] Fm(int length, Func<double, double> hzAt, double ratio, Func<double, double> indexAt)
    {
        var y = new float[length]; double pc = 0, pm = 0;
        for (var i = 0; i < length; i++)
        {
            var t = i / Rate; var hz = hzAt(t);
            y[i] = (float)Math.Sin(pc + indexAt(t) * Math.Sin(pm));
            pc += 2 * Math.PI * hz / Rate; pm += 2 * Math.PI * hz * ratio / Rate;
        }
        return y;
    }

    /// <summary>Linear rise over <paramref name="attack"/> seconds, then exponential decay with time constant <paramref name="tau"/>.</summary>
    public static float[] Envelope(float[] x, double attack, double tau)
    {
        var y = new float[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var t = i / Rate;
            y[i] = x[i] * (float)(Math.Min(1, t / attack) * Math.Exp(-t / tau));
        }
        return y;
    }

    /// <summary>Soft clipping (tanh), level kept at full scale: harmonics that carry a low thump onto small speakers, and grit.</summary>
    public static float[] Saturate(float[] x, double drive)
    {
        var norm = Math.Tanh(drive);
        return x.Select(v => (float)(Math.Tanh(drive * v) / norm)).ToArray();
    }

    public static float[] Scale(float[] x, double gain) => x.Select(v => v * (float)gain).ToArray();

    /// <summary>Scales to a peak of 1.</summary>
    public static float[] Normal(float[] x) { var p = x.Max(Math.Abs); return p > 0 ? Scale(x, 1 / p) : x; }

    /// <summary>Scales to an RMS of <paramref name="rms"/>.</summary>
    public static float[] Rms(float[] x, double rms) { var r = Math.Sqrt(x.Average(v => (double)v * v)); return r > 0 ? Scale(x, rms / r) : x; }

    /// <summary>Adds <paramref name="part"/> into <paramref name="into"/> from <paramref name="at"/> seconds.</summary>
    public static void Mix(float[] into, float[] part, double at = 0)
    {
        var start = (int)(at * Rate);
        for (var i = 0; i < part.Length && start + i < into.Length; i++) into[start + i] += part[i];
    }

    /// <summary>Amplitude wobble at <paramref name="hz"/>: depth 0 leaves the signal, 1 swings it to silence.</summary>
    public static float[] Tremolo(float[] x, double hz, double depth)
    {
        var y = new float[x.Length];
        for (var i = 0; i < x.Length; i++) y[i] = x[i] * (float)(1 - depth * 0.5 * (1 - Math.Cos(2 * Math.PI * hz * i / Rate)));
        return y;
    }

    /// <summary><paramref name="count"/> short clicks of band-passed noise at random times within [from, to) seconds, each louder or
    /// softer at random and decaying with <paramref name="tau"/>: crackle, grit, debris.</summary>
    public static float[] Grains(int seed, int length, int count, double from, double to, double tau, double lowHz, double highHz)
    {
        var y = new float[length]; var rng = new Random(seed); var grain = Samples(tau * 5);
        for (var g = 0; g < count; g++)
        {
            var at = from + rng.NextDouble() * (to - from); var gain = 0.3 + 0.7 * rng.NextDouble();
            var click = Envelope(Filter(Filter(Noise(seed * 1000 + g, grain), Mode.High, lowHz), Mode.Low, highHz), 0.0003, tau);
            Mix(y, Scale(click, gain * (1 - 0.6 * (at - from) / Math.Max(1e-9, to - from))), at);
        }
        return y;
    }

    /// <summary>A delayed, darkened copy added back: a slap off the terrain.</summary>
    public static float[] Echo(float[] x, double delay, double gain, double lowHz)
    {
        var y = (float[])x.Clone(); Mix(y, Scale(LowPass(x, lowHz), gain), delay);
        return y;
    }

    static readonly int[] Combs = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617], AllPasses = [556, 441, 341, 225];

    /// <summary>A room (Freeverb: eight damped combs into four all-passes), mono. <paramref name="size"/> scales the delays (1 is a
    /// hall's, 0.3 a small room's), <paramref name="feedback"/> sets the tail's length, <paramref name="damp"/> darkens it, and the wet
    /// part's RMS is <paramref name="wet"/> times the dry part's.</summary>
    public static float[] Room(float[] x, double size, double feedback, double damp, double wet)
    {
        var w = new double[x.Length];
        foreach (var d0 in Combs)
        {
            var d = Math.Max(1, (int)(d0 * size)); var buf = new double[d]; var at = 0; double store = 0;
            for (var i = 0; i < x.Length; i++)
            {
                var o = buf[at]; store = o * (1 - damp) + store * damp; buf[at] = x[i] + store * feedback; at = (at + 1) % d; w[i] += o;
            }
        }
        foreach (var d0 in AllPasses)
        {
            var d = Math.Max(1, (int)(d0 * size)); var buf = new double[d]; var at = 0;
            for (var i = 0; i < x.Length; i++) { var b = buf[at]; var o = -w[i] + b; buf[at] = w[i] + b * 0.5; at = (at + 1) % d; w[i] = o; }
        }
        double Rms(IEnumerable<double> v) => Math.Sqrt(v.Average(s => s * s));
        var g = wet * Rms(x.Select(v => (double)v)) / Math.Max(1e-12, Rms(w));
        return x.Select((v, i) => (float)(v + g * w[i])).ToArray();
    }

    /// <summary>Fades in and out linearly so the file starts and ends at zero: no click.</summary>
    public static void Fade(float[] x, double inSeconds, double outSeconds)
    {
        int a = (int)(inSeconds * Rate), b = (int)(outSeconds * Rate);
        for (var i = 0; i < a && i < x.Length; i++) x[i] *= (float)i / a;
        for (var i = 0; i < b && i < x.Length; i++) x[x.Length - 1 - i] *= (float)i / b;
    }

    /// <summary>The ceiling every cue's samples stay under, in dBFS.</summary>
    public const double CeilingDb = -1.2;

    /// <summary>
    /// The master stage: DC and rumble below 25 Hz out, then the gain that brings the loudest 400 ms window
    /// (<see cref="Measure.MomentaryMax"/>) to <paramref name="lufs"/>, through a soft limiter whose knee starts 4.4 dB under
    /// <see cref="CeilingDb"/>, so a transient is rounded instead of clipped; then the 2 ms fade in and 15 ms fade out.
    /// </summary>
    public static float[] Master(float[] x, double lufs)
    {
        var clean = Filter(x, Mode.High, 25, 0.6);
        double lo = -40, hi = 40; float[] y = clean;
        for (var step = 0; step < 36; step++)
        {
            var mid = (lo + hi) / 2; y = Limit(clean, Math.Pow(10, mid / 20));
            if (Measure.MomentaryMax(y) < lufs) lo = mid; else hi = mid;
        }
        Fade(y, 0.002, 0.015);
        return y;
    }

    static float[] Limit(float[] x, double gain)
    {
        var c = Math.Pow(10, CeilingDb / 20); var k = 0.6 * c;
        return x.Select(v =>
        {
            var a = Math.Abs(v * gain);
            var o = a <= k ? a : k + (c - k) * Math.Tanh((a - k) / (c - k));
            return (float)(Math.Sign(v) * o);
        }).ToArray();
    }
}
