using Tabletop.Sounds;

namespace Tabletop.Sounds.Tests;

/// <summary>What each cue sounds like, measured off its samples: when it peaks, how long it rings, where its energy sits in the spectrum
/// and which way that moves, how many attacks it holds.</summary>
public class CharacterTests
{
    const double Rate = Wav.Rate;
    static float[] Build(string name) => Cues.Build(name);

    static double Seconds(float[] x) => x.Length / Rate;
    static double PeakAt(float[] x) => Array.IndexOf(x, x.MaxBy(Math.Abs)) / Rate;
    static float[] Window(float[] x, double from, double to) => x[(int)(from * Rate)..Math.Min(x.Length, (int)(to * Rate))];
    static double Rms(float[] x) => Math.Sqrt(x.Average(v => (double)v * v));

    /// <summary>Goertzel power at one frequency.</summary>
    static double Power(float[] x, double hz)
    {
        var w = 2 * Math.PI * hz / Rate; var c = 2 * Math.Cos(w); double s1 = 0, s2 = 0;
        foreach (var v in x) { var s0 = v + c * s1 - s2; s2 = s1; s1 = s0; }
        return s1 * s1 + s2 * s2 - c * s1 * s2;
    }

    static readonly double[] Bins = Enumerable.Range(2, 160).Select(i => i * 50.0).ToArray();   // 100 Hz to 8 kHz
    static double Centroid(float[] x) { var p = Bins.Select(f => Power(x, f)).ToArray(); return Bins.Zip(p, (f, q) => f * q).Sum() / p.Sum(); }
    static double Dominant(float[] x) => Bins.MaxBy(f => Power(x, f));

    /// <summary>The share of the energy between two frequencies, from the whole spectrum (Measure.Power).</summary>
    static double Share(float[] x, double from, double to)
    {
        var p = Measure.Power(x); double all = 0, part = 0;
        for (var k = 1; k < p.Length; k++)
        {
            var hz = k * Rate / (2.0 * (p.Length - 1)); if (hz < 20) continue;
            all += p[k]; if (hz >= from && hz < to) part += p[k];
        }
        return part / all;
    }

    /// <summary>Attacks: 5 ms blocks whose level jumps to over twice the block before and over a tenth of the loudest.</summary>
    static int Onsets(float[] x)
    {
        var size = (int)(0.005 * Rate);
        var blocks = Enumerable.Range(0, x.Length / size).Select(i => Rms(x[(i * size)..((i + 1) * size)])).ToList();
        var top = blocks.Max();
        return Enumerable.Range(1, blocks.Count - 1).Count(i => blocks[i] > 2 * blocks[i - 1] && blocks[i] > 0.1 * top) + (blocks[0] > 0.1 * top ? 1 : 0);
    }

    [Theory, InlineData("shoot-1"), InlineData("shoot-2"), InlineData("shoot-3"), InlineData("shoot-5")]
    public void A_shot_cracks_at_once_and_is_down_to_its_room_tail_within_a_quarter_second(string name)
    {
        var x = Build(name);
        Assert.InRange(Seconds(x), 0.4, 0.75);
        Assert.InRange(PeakAt(x), 0, 0.03);
        Assert.True(Rms(Window(x, 0.25, Seconds(x))) < 0.15 * Rms(Window(x, 0, 0.05)), "over before the next shot of a volley lands on it");
        Assert.True(Rms(Window(x, 0.25, Seconds(x))) > 0.002 * Rms(Window(x, 0, 0.05)), "a room tail, not a dead stop");
    }

    [Theory, InlineData("shoot-1"), InlineData("shoot-2"), InlineData("shoot-3"), InlineData("shoot-4"), InlineData("shoot-5"), InlineData("impact"), InlineData("wound"), InlineData("melee")]
    public void A_report_or_a_hit_has_a_body_a_small_speaker_plays_not_only_a_sub_thump(string name)
    {
        var body = Share(Window(Build(name), 0, 0.15), 250, 6000);
        Assert.True(body > 0.25, $"{body:P0} of the first 150 ms between 250 Hz and 6 kHz");
    }

    [Theory, InlineData("shoot-2"), InlineData("shoot-4"), InlineData("impact"), InlineData("wound")]
    public void A_ballistic_report_or_a_hit_lands_with_weight_under_200_Hz(string name)
    {
        var low = Share(Window(Build(name), 0, 0.15), 20, 200);
        Assert.True(low > 0.15, $"{low:P0} under 200 Hz");
    }

    [Fact]
    public void The_plasma_bolt_discharges_downward_the_las_zaps_highest_and_the_heavy_gun_booms_lowest_and_longest()
    {
        var plasma = Build("shoot-1"); var ballistic = Build("shoot-2"); var las = Build("shoot-3"); var heavy = Build("shoot-4");
        Assert.True(Centroid(Window(plasma, 0.005, 0.04)) > Centroid(Window(plasma, 0.1, 0.2)));
        Assert.True(Dominant(Window(las, 0.003, 0.03)) > 1500);
        Assert.True(Centroid(Window(las, 0.003, 0.03)) > Centroid(Window(las, 0.05, 0.12)));
        Assert.True(Centroid(Window(las, 0, 0.1)) > Centroid(Window(ballistic, 0, 0.1)), $"las {Centroid(Window(las, 0, 0.1)):F0} Hz, ballistic {Centroid(Window(ballistic, 0, 0.1)):F0} Hz");
        Assert.True(Centroid(Window(heavy, 0, 0.2)) < Centroid(Window(ballistic, 0, 0.2)));
        Assert.True(Seconds(heavy) > 0.85);
        Assert.True(Measure.Of(heavy).TailAt > Measure.Of(ballistic).TailAt + 0.2);
    }

    [Fact]
    public void A_flamer_roars_noisy_and_long_with_crackle()
    {
        var x = Build("shoot-5");
        Assert.True(Rms(Window(x, 0.1, 0.2)) > 0.3 * Rms(Window(x, 0, 0.1)), "a roar, not a single report");
        Assert.True(Measure.Of(x).Flatness > 3 * Measure.Of(Build("shoot-3")).Flatness);
    }

    [Fact]
    public void An_impact_knocks_at_once_and_settles_with_debris()
    {
        var x = Build("impact");
        Assert.InRange(PeakAt(x), 0, 0.01);
        Assert.True(Rms(Window(x, 0.25, 0.35)) < 0.15 * Rms(Window(x, 0, 0.05)));
        Assert.True(Onsets(x) >= 1);
    }

    [Fact]
    public void A_miss_is_a_high_whistle_that_falls()
    {
        var x = Build("miss");
        Assert.True(Dominant(Window(x, 0.03, 0.15)) > 1800);
        Assert.True(Centroid(Window(x, 0.03, 0.09)) > Centroid(Window(x, 0.2, 0.35)));
        Assert.True(Centroid(Window(x, 0, 0.3)) > Centroid(Window(Build("impact"), 0, 0.3)) + 500);
    }

    [Fact]
    public void A_whiff_swells_before_it_peaks()
    {
        var x = Build("whiff");
        Assert.InRange(PeakAt(x), 0.08, 0.2);
        Assert.True(Rms(Window(x, 0, 0.03)) < 0.4 * Rms(Window(x, 0.1, 0.18)));
    }

    [Fact]
    public void A_block_clangs_lower_and_rings_longer_than_a_melee_strike()
    {
        var block = Build("block"); var melee = Build("melee");
        Assert.InRange(PeakAt(block), 0, 0.05);
        Assert.True(Dominant(Window(block, 0.1, 0.3)) < 1000);
        double Ring(float[] x) => Rms(Window(x, 0.25, 0.35)) / Rms(Window(x, 0, 0.05));
        Assert.True(Ring(block) > 0.15, $"block rings {Ring(block):F3}");
        Assert.True(Ring(block) > 3 * Ring(melee));
    }

    [Fact]
    public void A_friendly_cast_rises_bright_and_an_enemy_cast_falls_dark()
    {
        var friendly = Build("cast-friendly"); var enemy = Build("cast-enemy");
        Assert.True(Centroid(Window(friendly, 0.01, 0.06)) < Centroid(Window(friendly, 0.22, 0.3)));
        Assert.True(Dominant(Window(friendly, 0.22, 0.4)) > 900, $"{Dominant(Window(friendly, 0.22, 0.4))} Hz");
        Assert.True(Centroid(Window(enemy, 0.03, 0.13)) > Centroid(Window(enemy, 0.45, 0.6)));
        Assert.True(Dominant(Window(enemy, 0.05, 0.4)) < 600, $"{Dominant(Window(enemy, 0.05, 0.4))} Hz");
        Assert.True(Centroid(Window(friendly, 0, 0.5)) > 2 * Centroid(Window(enemy, 0, 0.5)));
        Assert.InRange(Seconds(friendly), 0.6, 0.99); Assert.InRange(Seconds(enemy), 0.6, 0.99);
    }

    [Fact]
    public void A_fizzle_sputters()
    {
        var x = Build("fizzle");
        Assert.True(Onsets(x) >= 4, $"{Onsets(x)} attacks");
        Assert.True(Seconds(x) < 0.7);
    }

    [Fact]
    public void A_move_is_several_footfalls()
    {
        var x = Build("move");
        Assert.True(Onsets(x) >= 4, $"{Onsets(x)} footfalls");
    }
}
