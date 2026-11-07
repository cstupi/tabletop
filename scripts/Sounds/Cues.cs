using static Tabletop.Sounds.Synth;

namespace Tabletop.Sounds;

/// <summary>The table sounds, by the file name the client loads (spec 2026-10-24-sound-effects → Sounds), each with the loudness its
/// master stage sets (the loudest 400 ms window, LUFS). Each is seeded, so a build is the same bytes every time. A sound is built in
/// layers: a transient (the first milliseconds' crack or click), a body (the blast, the zap, the ring), weight (a low thump saturated
/// so its harmonics carry on small speakers), then a slap echo and a room for the tail.</summary>
public static class Cues
{
    public static IReadOnlyList<(string Name, double Lufs, Func<float[]> Build)> All { get; } =
    [
        ("shoot-1", -15, Plasma), ("shoot-2", -15, Ballistic), ("shoot-3", -16, Las), ("shoot-4", -13, Heavy), ("shoot-5", -16, Flame),
        ("impact", -14, Impact), ("miss", -18, Miss), ("melee", -15, Melee), ("whiff", -19, Whiff), ("block", -15, Block),
        ("wound", -14, Wound), ("cast-friendly", -17, CastFriendly), ("cast-enemy", -16, CastEnemy), ("fizzle", -19, Fizzle),
        ("move", -21, Move),
    ];

    public static float[] Build(string name) => All.Single(c => c.Name == name) is var c ? Master(c.Build(), c.Lufs) : [];

    /// <summary>Noise between two corners at an RMS of 0.5, so a band's gain reads like a sine's whatever its width.</summary>
    static float[] Band(int seed, int n, double low, double high) => Rms(Filter(Filter(Noise(seed, n), Mode.High, low), Mode.Low, high), 0.5);
    static float[] Decay(float[] x, double attack, double tau, double gain) => Scale(Envelope(x, attack, tau), gain);

    /// <summary>A shot's crack: a 3 ms burst of bright noise, the front of every report.</summary>
    static float[] Crack(int seed, int n, double low, double tau) => Decay(Band(seed, n, low, 12000), 0.0002, tau, 1);

    /// <summary>A low thump falling in pitch, saturated: the weight under a shot or a hit.</summary>
    static float[] Thump(int n, double from, double to, double glide, double tau, double drive) =>
        Saturate(Envelope(Tone(n, Glide(from, to, glide)), 0.001, tau), drive);

    /// <summary>A plasma bolt: a crack, a discharge (FM buzz falling from 900 to 110 Hz, its index dying), a sizzle swept down from
    /// 7 kHz with crackle, a thump, then a short room (0.6 s).</summary>
    static float[] Plasma()
    {
        var x = Silence(0.6); var n = x.Length;
        Mix(x, Scale(Crack(11, n, 2500, 0.003), 0.9));
        Mix(x, Decay(Saturate(Fm(n, Glide(900, 110, 0.12), 1.41, t => 5 * Math.Exp(-t / 0.05) + 0.4), 2), 0.001, 0.07, 0.75));
        Mix(x, Decay(Swept(Noise(12, n), Mode.Band, Glide(7000, 900, 0.18), 2.5), 0.001, 0.09, 0.8));
        Mix(x, Scale(Grains(13, n, 14, 0.005, 0.16, 0.0015, 2000, 9000), 0.5));
        Mix(x, Scale(Thump(n, 120, 42, 0.08, 0.05, 2.5), 0.2));
        return Room(Echo(x, 0.085, 0.18, 2500), 0.55, 0.72, 0.45, 0.2);
    }

    /// <summary>A ballistic round (rifle, bolter, gun): a sharp crack, a saturated blast of mid noise, a deep thump, the round's
    /// rocket hiss, a slap echo and a room (0.6 s).</summary>
    static float[] Ballistic()
    {
        var x = Silence(0.6); var n = x.Length;
        Mix(x, Crack(21, n, 2000, 0.003));
        Mix(x, Decay(Saturate(Filter(Band(22, n, 150, 5000), Mode.Band, 900, 0.8), 3), 0.0005, 0.045, 1.0));
        Mix(x, Decay(Filter(Brown(23, n), Mode.Low, 700), 0.001, 0.05, 0.4));
        Mix(x, Scale(Thump(n, 110, 45, 0.06, 0.05, 3), 0.3));
        Mix(x, Decay(Swept(Noise(24, n), Mode.Band, Glide(3500, 1200, 0.1), 3), 0.01, 0.05, 0.2), 0.012);
        return Room(Echo(x, 0.09, 0.2, 2000), 0.6, 0.72, 0.5, 0.2);
    }

    /// <summary>A las shot: a snap, a bright FM zap falling from 3.2 kHz to 800 Hz in 80 ms, a short ring, a light thump, a room (0.5 s).</summary>
    static float[] Las()
    {
        var x = Silence(0.5); var n = x.Length;
        Mix(x, Scale(Crack(31, n, 4000, 0.002), 0.7));
        Mix(x, Decay(Fm(n, Glide(3200, 800, 0.08), 2, t => 2 * Math.Exp(-t / 0.03)), 0.0005, 0.045, 0.9));
        Mix(x, Decay(Sine(n, 1850), 0.0005, 0.03, 0.25));
        Mix(x, Decay(Band(32, n, 3000, 9000), 0.0005, 0.02, 0.35));
        Mix(x, Scale(Thump(n, 160, 70, 0.03, 0.03, 2), 0.12));
        return Room(Echo(x, 0.07, 0.15, 3000), 0.5, 0.7, 0.4, 0.2);
    }

    /// <summary>A heavy weapon (cannon, launcher, missile, mortar): a crack, a hard-saturated blast, a sub boom falling from 75 to 30 Hz,
    /// a rolling rumble, debris, a long slap and a large room (0.95 s).</summary>
    static float[] Heavy()
    {
        var x = Silence(0.95); var n = x.Length;
        Mix(x, Scale(Crack(41, n, 1500, 0.004), 0.5));
        Mix(x, Decay(Saturate(Band(42, n, 80, 1500), 4), 0.001, 0.09, 1.0));
        Mix(x, Scale(Thump(n, 75, 30, 0.15, 0.14, 3.5), 0.4));
        Mix(x, Decay(Filter(Brown(43, n), Mode.Low, 400), 0.02, 0.22, 0.5));
        Mix(x, Scale(Grains(44, n, 18, 0.04, 0.4, 0.004, 400, 2500), 0.25));
        return Room(Echo(x, 0.14, 0.25, 1200), 0.9, 0.78, 0.55, 0.25);
    }

    /// <summary>A flamer: a whump of ignition, then a roaring band of noise with crackle, swept up then down (0.7 s).</summary>
    static float[] Flame()
    {
        var x = Silence(0.7); var n = x.Length;
        Mix(x, Scale(Thump(n, 90, 45, 0.08, 0.05, 2), 0.35));
        var roar = Swept(Noise(51, n), Mode.Band, t => t < 0.08 ? 400 + 15000 * t : 1600 * Math.Exp(-(t - 0.08) / 0.4) + 300, 1.2);
        Mix(x, Tremolo(Decay(roar, 0.03, 0.18, 1.2), 23, 0.35));
        Mix(x, Decay(Filter(Brown(52, n), Mode.Low, 500), 0.02, 0.15, 0.6));
        Mix(x, Scale(Grains(53, n, 30, 0.02, 0.45, 0.0015, 1500, 8000), 0.45));
        return Room(x, 0.5, 0.68, 0.5, 0.15);
    }

    /// <summary>A round striking home: a hard knock, a saturated low punch, a crunch of mid noise, then debris pattering down (0.5 s).</summary>
    static float[] Impact()
    {
        var x = Silence(0.5); var n = x.Length;
        Mix(x, Decay(Band(71, n, 1200, 7000), 0.0002, 0.004, 0.8));
        Mix(x, Scale(Thump(n, 180, 55, 0.05, 0.05, 3), 0.3));
        Mix(x, Decay(Saturate(Band(72, n, 300, 3000), 2), 0.0005, 0.045, 1.0));
        Mix(x, Scale(Grains(73, n, 16, 0.03, 0.3, 0.003, 800, 5000), 0.35));
        return Room(x, 0.45, 0.68, 0.5, 0.18);
    }

    /// <summary>A ricochet: a tick off stone and a spit of dirt, then a whistle falling from 3.4 to 1.4 kHz with a flutter (0.6 s).</summary>
    static float[] Miss()
    {
        var x = Silence(0.6); var n = x.Length;
        Mix(x, Decay(Band(81, n, 2500, 9000), 0.0002, 0.004, 0.7));
        Mix(x, Decay(Band(82, n, 300, 2500), 0.001, 0.02, 0.4));
        Mix(x, Tremolo(Decay(Fm(n, Glide(3400, 1400, 0.3), 0.5, _ => 0.3), 0.015, 0.14, 0.7), 27, 0.3), 0.012);
        Mix(x, Decay(Swept(Noise(83, n), Mode.Band, Glide(4000, 1500, 0.3), 4), 0.03, 0.1, 0.25), 0.012);
        return Room(x, 0.6, 0.7, 0.45, 0.2);
    }

    /// <summary>A blow landing: a whoosh into a heavy hit (a saturated thump and crunch) with the bright clash of a blade on armour (0.5 s).</summary>
    static float[] Melee()
    {
        var x = Silence(0.5); var n = x.Length;
        Mix(x, Decay(Band(91, n, 2000, 9000), 0.0002, 0.004, 0.8));
        Mix(x, Scale(Thump(n, 150, 55, 0.05, 0.05, 3), 0.35));
        Mix(x, Decay(Saturate(Band(92, n, 400, 3500), 2), 0.0005, 0.035, 0.8));
        (double Hz, double Amp, double Tau)[] partials = [(620, 0.7, 0.09), (1530, 0.55, 0.07), (2470, 0.4, 0.05), (3390, 0.25, 0.035)];
        foreach (var (hz, amp, tau) in partials) Mix(x, Decay(Sine(n, hz), 0.001, tau, amp));
        return Room(x, 0.45, 0.68, 0.5, 0.15);
    }

    /// <summary>A blade through air: noise through a resonant band swept up from 500 Hz to 2.4 kHz and back, swelling for 120 ms (0.45 s).</summary>
    static float[] Whiff()
    {
        var x = Silence(0.45); var n = x.Length;
        var swish = Swept(Noise(101, n), Mode.Band, t => 500 + 1900 * Math.Sin(Math.PI * Math.Min(1, t / 0.28)), 3);
        var env = Enumerable.Range(0, n).Select(i => { var t = i / (double)Wav.Rate; return t < 0.12 ? Math.Pow(t / 0.12, 2) : Math.Exp(-(t - 0.12) / 0.06); }).ToArray();
        Mix(x, swish.Select((v, i) => (float)(v * env[i])).ToArray());
        return Room(x, 0.4, 0.6, 0.5, 0.12);
    }

    /// <summary>Armour taking a blow: a knock, a low thump, a long clang of inharmonic partials gliding a little flat, and a room (0.8 s).</summary>
    static float[] Block()
    {
        var x = Silence(0.8); var n = x.Length;
        Mix(x, Decay(Filter(Noise(111, n), Mode.Low, 2500), 0.0003, 0.008, 0.9));
        Mix(x, Scale(Thump(n, 170, 80, 0.04, 0.04, 2), 0.5));
        (double Hz, double Amp, double Tau)[] partials = [(287, 1.0, 0.25), (731, 0.7, 0.18), (1190, 0.55, 0.13), (1843, 0.4, 0.09), (2610, 0.25, 0.06), (3477, 0.15, 0.04)];
        foreach (var (hz, amp, tau) in partials) Mix(x, Decay(Tone(n, Glide(hz * 1.01, hz, 0.05)), 0.001, tau, amp * 0.6));
        return Room(Saturate(x, 1.5), 0.6, 0.72, 0.5, 0.2);
    }

    /// <summary>A model taking a wound: a heavy body hit (saturated thump falling to 40 Hz), a wet crunch of grains, a crack of armour,
    /// and a short room (0.5 s).</summary>
    static float[] Wound()
    {
        var x = Silence(0.5); var n = x.Length;
        Mix(x, Scale(Thump(n, 130, 40, 0.08, 0.07, 3.5), 0.3));
        Mix(x, Decay(Saturate(Filter(Band(121, n, 200, 2500), Mode.Band, 700, 1.2), 2.5), 0.001, 0.06, 1.3));
        Mix(x, Scale(Grains(122, n, 22, 0.0, 0.12, 0.003, 400, 3500), 0.6));
        Mix(x, Decay(Band(123, n, 1500, 8000), 0.0003, 0.006, 0.5), 0.004);
        return Room(x, 0.45, 0.66, 0.55, 0.15);
    }

    /// <summary>A blessing: a rising major arpeggio (E5, G#5, B5, E6) of detuned bell pairs over a swelling chord pad and a shimmer, in a
    /// large room (0.95 s).</summary>
    static float[] CastFriendly()
    {
        var x = Silence(0.95); var n = x.Length;
        double[] notes = [659.3, 830.6, 987.8, 1318.5];
        for (var k = 0; k < notes.Length; k++)
        {
            var at = 0.07 * k; var len = n - Samples(at);
            var tone = Sine(len, notes[k]); Mix(tone, Sine(len, notes[k] * 1.004)); Mix(tone, Scale(Sine(len, notes[k] * 2.76), 0.25));
            Mix(x, Decay(tone, 0.005, 0.22, 0.3 + 0.1 * k), at);
        }
        var pad = Sine(n, 987.8); Mix(pad, Sine(n, 1318.5)); Mix(pad, Sine(n, 1661.2));
        Mix(x, pad.Select((v, i) => v * (float)(0.06 * Math.Min(1, i / (0.3 * Wav.Rate)) * Math.Exp(-i / (0.5 * Wav.Rate)))).ToArray());
        Mix(x, Scale(Tremolo(Envelope(Band(131, n, 5000, 11000), 0.2, 0.2), 18, 0.6), 0.2));
        return Room(x, 0.9, 0.75, 0.4, 0.3);
    }

    /// <summary>A curse: low FM tones a tritone apart falling to a growl, throbbing, over a dark rush of air and a sub, in a large room (0.95 s).</summary>
    static float[] CastEnemy()
    {
        var x = Silence(0.95); var n = x.Length;
        var tone = Fm(n, Glide(420, 110, 0.6), 0.5, t => 0.8 + 2.5 * Math.Exp(-t / 0.25));
        Mix(tone, Scale(Fm(n, Glide(594, 155, 0.6), 0.5, _ => 1.5), 0.7));
        Mix(tone, Scale(Sweep(n, 105, 40), 0.4));
        Mix(x, Tremolo(Decay(Saturate(tone, 1.5), 0.03, 0.3, 0.8), 11, 0.5));
        Mix(x, Decay(Swept(Noise(141, n), Mode.Band, Glide(1800, 200, 0.6), 1.5), 0.08, 0.25, 0.6));
        return Room(x, 0.9, 0.75, 0.55, 0.25);
    }

    /// <summary>A spell that fails: a falling sigh broken by electric sputters that die away (0.6 s).</summary>
    static float[] Fizzle()
    {
        var x = Silence(0.6); var n = x.Length;
        Mix(x, Decay(Fm(n, Glide(700, 140, 0.3), 1.5, t => 2 * Math.Exp(-t / 0.1)), 0.005, 0.12, 0.25));
        double[] at = [0, 0.05, 0.12, 0.17, 0.26, 0.33], gain = [1, 0.8, 0.7, 0.5, 0.4, 0.25];
        for (var k = 0; k < at.Length; k++)
        {
            var pop = Envelope(Band(151 + k, Samples(0.03), 1500, 6000), 0.0005, 0.007);
            Mix(x, Scale(Saturate(pop, 2), 1.5 * gain[k]), at[k]);
            Mix(x, Scale(Grains(161 + k, n, 5, at[k], at[k] + 0.03, 0.0007, 3000, 10000), 0.6 * gain[k]));
        }
        return Room(x, 0.5, 0.65, 0.45, 0.15);
    }

    /// <summary>A squad on the move: five boot falls on gravel in a loose rhythm, each a soft thump under a crunch of grains, with a
    /// clink of kit (0.75 s).</summary>
    static float[] Move()
    {
        var x = Silence(0.75); var n = x.Length;
        double[] steps = [0, 0.11, 0.25, 0.37, 0.52]; double[] gain = [1, 0.75, 0.9, 0.7, 0.6];
        for (var k = 0; k < steps.Length; k++)
        {
            var len = Samples(0.12);
            Mix(x, Scale(Thump(len, 120, 60, 0.03, 0.02, 1.5), 0.12 * gain[k]), steps[k]);
            Mix(x, Decay(Band(171 + k, len, 250, 1800), 0.004, 0.025, 0.5 * gain[k]), steps[k]);
            Mix(x, Scale(Grains(181 + k, n, 10, steps[k], steps[k] + 0.05, 0.002, 1200, 7000), 0.35 * gain[k]));
        }
        Mix(x, Decay(Sine(Samples(0.1), 3150), 0.001, 0.015, 0.06), 0.27);
        return Room(x, 0.4, 0.6, 0.5, 0.12);
    }
}
