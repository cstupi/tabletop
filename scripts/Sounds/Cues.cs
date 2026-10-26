namespace Tabletop.Sounds;

/// <summary>The table sounds, by the file name the client loads (spec 2026-10-24-sound-effects → Sounds). Each is seeded, so a build is
/// the same bytes every time; each peaks at -6 dBFS after a 2 ms fade in and a 12 ms fade out.</summary>
public static class Cues
{
    public const double PeakDb = -6;

    public static IReadOnlyList<(string Name, Func<float[]> Build)> All { get; } =
    [
        ("shoot-1", Plasma), ("shoot-2", Rifle), ("shoot-3", Las), ("impact", Impact), ("miss", Miss), ("melee", Melee), ("whiff", Whiff),
        ("block", Block), ("wound", Wound), ("cast-friendly", CastFriendly), ("cast-enemy", CastEnemy), ("fizzle", Fizzle), ("move", Move),
    ];

    static float[] Finish(float[] x) { Synth.Fade(x, 0.002, 0.012); Synth.Normalize(x, PeakDb); return x; }

    /// <summary>A sharp band of noise: the crack at the front of every shot.</summary>
    static float[] Crack(int seed, int n, double tau) => Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(seed, n), 1400), 6500), 0.002, tau);

    /// <summary>A plasma bolt: the crack, a whine falling from 1.4 kHz with its octave, a light thump (0.36 s).</summary>
    static float[] Plasma()
    {
        var x = Synth.Silence(0.36); var n = x.Length;
        Synth.Mix(x, Crack(11, n, 0.025));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sweep(n, 1400, 180), 0.003, 0.07), 0.8));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sweep(n, 2800, 360), 0.003, 0.04), 0.22));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sweep(n, 150, 50), 0.003, 0.05), 0.35));
        return Finish(Synth.LowPass(x, 7000));
    }

    /// <summary>A bolter round: the crack over a deep thump and a short low rumble (0.4 s).</summary>
    static float[] Rifle()
    {
        var x = Synth.Silence(0.4); var n = x.Length;
        Synth.Mix(x, Crack(12, n, 0.03));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sweep(n, 120, 40), 0.003, 0.08), 0.9));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.Noise(61, n), 400), 0.004, 0.08), 0.7));
        return Finish(Synth.LowPass(x, 7000));
    }

    /// <summary>A las shot: a short crack and a bright zap falling from 2.6 kHz to 500 Hz over a sizzle (0.3 s).</summary>
    static float[] Las()
    {
        var x = Synth.Silence(0.3); var n = x.Length;
        Synth.Mix(x, Synth.Scale(Crack(13, n, 0.012), 0.7));
        Synth.Mix(x, Synth.Envelope(Synth.Sweep(n, 2600, 500), 0.002, 0.05));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(63, n), 3000), 7000), 0.002, 0.035), 0.3));
        return Finish(Synth.LowPass(x, 8000));
    }

    /// <summary>A round striking home: a low thud with a crunch of mid noise and a smaller one after it.</summary>
    static float[] Impact()
    {
        var x = Synth.Silence(0.35); var n = x.Length;
        Synth.Mix(x, Synth.Envelope(Synth.Sweep(n, 220, 70), 0.002, 0.05));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(71, n), 500), 3500), 0.001, 0.03), 0.8));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(72, n), 700), 3000), 0.001, 0.02), 0.4), 0.03);
        return Finish(x);
    }

    /// <summary>A ricochet: a tick off stone, then a whistle falling from 3.4 to 1.5 kHz with a flutter and a breath of whizz.</summary>
    static float[] Miss()
    {
        var x = Synth.Silence(0.5); var n = x.Length;
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(81, n), 2500), 6500), 0.001, 0.006), 0.5));
        Synth.Mix(x, Synth.Tremolo(Synth.Envelope(Synth.Sweep(n, 3400, 1500), 0.012, 0.15), 27, 0.3));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(82, n), 1500), 6000), 0.03, 0.08), 0.3));
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

    /// <summary>A blade through air: band noise swelling for 140 ms, then fading.</summary>
    static float[] Whiff()
    {
        var x = Synth.Silence(0.4); var n = x.Length;
        Synth.Mix(x, Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(91, n), 400), 2500), 0.14, 0.15));
        return Finish(x);
    }

    /// <summary>Armour taking a blow: a low, long-ringing clang of inharmonic partials over a knock.</summary>
    static float[] Block()
    {
        var x = Synth.Silence(0.65); var n = x.Length;
        (double Hz, double Amp, double Tau)[] partials = [(310, 1.0, 0.22), (817, 0.7, 0.16), (1290, 0.55, 0.12), (1960, 0.35, 0.08), (2730, 0.2, 0.05)];
        foreach (var (hz, amp, tau) in partials) Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sine(n, hz), 0.002, tau), amp));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.Noise(101, n), 1200), 0.001, 0.01), 0.9));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sweep(n, 160, 80), 0.002, 0.04), 0.5));
        return Finish(Synth.LowPass(x, 9000));
    }

    /// <summary>A dull thud: a falling low tone with a muffled noise body.</summary>
    static float[] Wound()
    {
        var x = Synth.Silence(0.3); var n = x.Length;
        Synth.Mix(x, Synth.Envelope(Synth.Sweep(n, 125, 48), 0.004, 0.08));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.Noise(53, n), 600), 0.003, 0.04), 0.7));
        return Finish(x);
    }

    /// <summary>A blessing: a rising major arpeggio of bell tones (E5, G#5, B5, E6) under a soft shimmer.</summary>
    static float[] CastFriendly()
    {
        var x = Synth.Silence(0.8); var n = x.Length;
        double[] notes = [659.3, 830.6, 987.8, 1318.5];
        for (var k = 0; k < notes.Length; k++)
        {
            var at = 0.07 * k; var len = n - (int)(at * 44100);
            var tone = Synth.Sine(len, notes[k]); Synth.Mix(tone, Synth.Scale(Synth.Sine(len, notes[k] * 2), 0.3));
            Synth.Mix(x, Synth.Scale(Synth.Envelope(tone, 0.01, 0.25), 0.6), at);
        }
        Synth.Mix(x, Synth.Scale(Synth.Tremolo(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(111, n), 5000), 9000), 0.2, 0.15), 18, 0.6), 0.25));
        return Finish(x);
    }

    /// <summary>A curse: low tones a tritone apart falling to a growl, throbbing, over a dark rush of air.</summary>
    static float[] CastEnemy()
    {
        var x = Synth.Silence(0.8); var n = x.Length;
        var tone = Synth.Sweep(n, 420, 110);
        Synth.Mix(tone, Synth.Scale(Synth.Sweep(n, 594, 155), 0.7));
        Synth.Mix(tone, Synth.Scale(Synth.Sweep(n, 210, 55), 0.6));
        Synth.Mix(x, Synth.Tremolo(Synth.Envelope(tone, 0.03, 0.3), 11, 0.5));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.Noise(121, n), 900), 0.08, 0.2), 0.5));
        return Finish(x);
    }

    /// <summary>A spell that fails: a falling sigh broken by sputtering crackles that die away.</summary>
    static float[] Fizzle()
    {
        var x = Synth.Silence(0.55); var n = x.Length;
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.Sweep(n, 700, 140), 0.005, 0.12), 0.2));
        double[] at = [0, 0.05, 0.12, 0.17, 0.26, 0.33], gain = [1, 0.8, 0.7, 0.5, 0.4, 0.25];
        for (var k = 0; k < at.Length; k++)
        {
            var pop = Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(131 + k, (int)(0.03 * 44100)), 1500), 4000), 0.001, 0.008);
            Synth.Mix(x, Synth.Scale(pop, 3 * gain[k]), at[k]);
        }
        return Finish(x);
    }

    /// <summary>Two soft scuffs of footsteps.</summary>
    static float[] Move()
    {
        var x = Synth.Silence(0.3); var n = x.Length;
        Synth.Mix(x, Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(37, n), 250), 1500), 0.012, 0.035));
        Synth.Mix(x, Synth.Scale(Synth.Envelope(Synth.LowPass(Synth.HighPass(Synth.Noise(38, n), 250), 1300), 0.012, 0.04), 0.8), 0.12);
        return Finish(x);
    }
}
