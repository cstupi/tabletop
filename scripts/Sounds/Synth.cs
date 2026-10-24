namespace Tabletop.Sounds;

/// <summary>The building blocks of a cue: seeded noise, one-pole filters, sine partials, envelopes, fades and level.</summary>
public static class Synth
{
    const double Rate = Wav.Rate;

    public static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    public static float[] Noise(int seed, int length)
    {
        var rng = new Random(seed); var x = new float[length];
        for (var i = 0; i < length; i++) x[i] = (float)(rng.NextDouble() * 2 - 1);
        return x;
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

    /// <summary>A sine whose pitch falls exponentially from <paramref name="from"/> to <paramref name="to"/> Hz over its length.</summary>
    public static float[] Sweep(int length, double from, double to)
    {
        var y = new float[length]; double phase = 0;
        for (var i = 0; i < length; i++)
        {
            y[i] = (float)Math.Sin(phase);
            phase += 2 * Math.PI * from * Math.Pow(to / from, (double)i / length) / Rate;
        }
        return y;
    }

    public static float[] Sine(int length, double hz)
    {
        var y = new float[length];
        for (var i = 0; i < length; i++) y[i] = (float)Math.Sin(2 * Math.PI * hz * i / Rate);
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

    public static float[] Scale(float[] x, double gain) => x.Select(v => v * (float)gain).ToArray();

    /// <summary>Adds <paramref name="part"/> into <paramref name="into"/> from <paramref name="at"/> seconds.</summary>
    public static void Mix(float[] into, float[] part, double at = 0)
    {
        var start = (int)(at * Rate);
        for (var i = 0; i < part.Length && start + i < into.Length; i++) into[start + i] += part[i];
    }

    /// <summary>Fades in and out linearly so the file starts and ends at zero: no click.</summary>
    public static void Fade(float[] x, double inSeconds, double outSeconds)
    {
        int a = (int)(inSeconds * Rate), b = (int)(outSeconds * Rate);
        for (var i = 0; i < a && i < x.Length; i++) x[i] *= (float)i / a;
        for (var i = 0; i < b && i < x.Length; i++) x[x.Length - 1 - i] *= (float)i / b;
    }

    /// <summary>Scales the loudest sample to <paramref name="peakDb"/> dBFS.</summary>
    public static void Normalize(float[] x, double peakDb)
    {
        var peak = x.Max(Math.Abs);
        var g = (float)(Math.Pow(10, peakDb / 20) / peak);
        for (var i = 0; i < x.Length; i++) x[i] *= g;
    }
}
