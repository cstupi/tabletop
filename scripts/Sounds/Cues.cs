namespace Tabletop.Sounds;

/// <summary>The four table sounds, by the file name the client loads (spec 2026-10-24-sound-effects → Sounds). Each is seeded, so a
/// build is the same bytes every time; each peaks at -6 dBFS after a 2 ms fade in and a 12 ms fade out.</summary>
public static class Cues
{
    public const double PeakDb = -6;

    public static IReadOnlyList<(string Name, Func<float[]> Build)> All { get; } =
    [
        ("shoot", Shoot), ("melee", Melee), ("move", Move), ("wound", Wound),
    ];

    static float[] Finish(float[] x) { Synth.Fade(x, 0.002, 0.012); Synth.Normalize(x, PeakDb); return x; }

    /// <summary>A sharp filtered crack over a short low thump.</summary>
    static float[] Shoot()
    {
        var x = Synth.Silence(0.28); var n = x.Length;
        var crack = Synth.LowPass(Synth.HighPass(Synth.Noise(11, n), 1400), 6500);
        Synth.Mix(x, Synth.Envelope(crack, 0.002, 0.03));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sweep(n, 170, 60), 0.003, 0.07), 0.6));
        return Finish(x);
    }

    /// <summary>A short metallic clash: inharmonic partials with quick decays over a noise tick.</summary>
    static float[] Melee()
    {
        var x = Synth.Silence(0.4); var n = x.Length;
        (double Hz, double Amp, double Tau)[] partials = [(520, 1.0, 0.11), (1370, 0.7, 0.08), (2210, 0.5, 0.055), (3100, 0.3, 0.035)];
        foreach (var (hz, amp, tau) in partials) Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sine(n, hz), 0.002, tau), amp));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(23, n), 1800), 7000), 0.001, 0.012), 0.8));
        return Finish(Synth.LowPass(x, 8000));
    }

    /// <summary>Two soft scuffs of footsteps.</summary>
    static float[] Move()
    {
        var x = Synth.Silence(0.3); var n = x.Length;
        Synth.Mix(x, Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(37, n), 250), 1500), 0.012, 0.035));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(38, n), 250), 1300), 0.012, 0.04), 0.8), 0.12);
        return Finish(x);
    }

    /// <summary>A dull thud: a falling low tone with a muffled noise body.</summary>
    static float[] Wound()
    {
        var x = Synth.Silence(0.3); var n = x.Length;
        Synth.Mix(x, Synth.Envelope(Synth.Sweep(n, 125, 48), 0.004, 0.08));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.Noise(53, n), 600), 0.003, 0.04), 0.7));
        return Finish(x);
    }
}
