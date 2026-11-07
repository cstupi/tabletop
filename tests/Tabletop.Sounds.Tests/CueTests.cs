using Tabletop.Sounds;

namespace Tabletop.Sounds.Tests;

public class CueTests
{
    static readonly string Shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/content/sounds"));
    static readonly string Source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/Sounds"));

    public static TheoryData<string> Names() { var d = new TheoryData<string>(); foreach (var (name, _, _) in Cues.All) d.Add(name); return d; }

    static short[] Pcm(byte[] wav) => Enumerable.Range(0, (wav.Length - 44) / 2).Select(i => BitConverter.ToInt16(wav, 44 + 2 * i)).ToArray();

    [Fact]
    public void The_cues_are_the_files_the_client_loads() =>
        Assert.Equal(["shoot-1", "shoot-2", "shoot-3", "shoot-4", "shoot-5", "impact", "miss", "melee", "whiff", "block", "wound", "cast-friendly",
            "cast-enemy", "fizzle", "move"], Cues.All.Select(c => c.Name));

    [Theory, MemberData(nameof(Names))]
    public void A_cue_is_the_same_bytes_every_build(string name) =>
        Assert.Equal(Wav.Of(Cues.Build(name)), Wav.Of(Cues.Build(name)));

    [Theory, MemberData(nameof(Names))]
    public void A_cue_is_a_16_bit_mono_wav_under_a_second_and_100_KB(string name)
    {
        var wav = Wav.Of(Cues.Build(name));
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4)); Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal((short)1, BitConverter.ToInt16(wav, 20));          // PCM
        Assert.Equal((short)1, BitConverter.ToInt16(wav, 22));          // mono
        Assert.Equal(44100, BitConverter.ToInt32(wav, 24));
        Assert.Equal((short)16, BitConverter.ToInt16(wav, 34));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4)); Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
        Assert.InRange(wav.Length, 1000, 100 * 1024);
        Assert.True(Pcm(wav).Length < 44100);
    }

    [Theory, MemberData(nameof(Names))]
    public void A_cue_is_mastered_to_its_loudness_under_a_minus_1_dBFS_ceiling(string name)
    {
        var target = Cues.All.Single(c => c.Name == name).Lufs;
        var m = Measure.Of(Measure.Read(Wav.Of(Cues.Build(name))));
        Assert.InRange(m.MomentaryMaxLufs, target - 0.5, target + 0.5);
        Assert.True(m.PeakDb <= -1, $"peak {m.PeakDb:F2} dBFS");
    }

    [Fact]
    public void The_loudness_targets_rank_the_cues_a_heavy_gun_first_and_footsteps_last()
    {
        double L(string n) => Cues.All.Single(c => c.Name == n).Lufs;
        Assert.All(Cues.All, c => Assert.InRange(c.Lufs, -22, -12));
        Assert.True(L("shoot-4") > L("shoot-2") && L("wound") > L("whiff") && L("impact") > L("miss"));
        Assert.Equal(Cues.All.Min(c => c.Lufs), L("move"));
    }

    [Theory, MemberData(nameof(Names))]
    public void A_cue_has_no_DC_and_starts_and_ends_at_zero_with_no_click(string name)
    {
        var x = Cues.Build(name); var pcm = Pcm(Wav.Of(x));
        Assert.True(Math.Abs(x.Average(v => (double)v)) < 1e-3, $"DC {x.Average(v => (double)v):E2}");
        Assert.Equal(0, pcm[0]); Assert.Equal(0, pcm[^1]);
        // The 2 ms fade in: the first millisecond stays under half scale. The 15 ms fade out: the last millisecond under a tenth.
        Assert.True(pcm.Take(44).All(s => Math.Abs((int)s) < 32767 / 2));
        Assert.True(pcm.TakeLast(44).All(s => Math.Abs((int)s) < 32767 / 10));
    }

    [Fact]
    public void The_cues_differ()
    {
        var all = Cues.All.Select(c => Convert.ToHexString(Wav.Of(Cues.Build(c.Name)))).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void The_shipped_sounds_are_the_generators_output()
    {
        Assert.Equal(Cues.All.Select(c => c.Name).Order(), Directory.GetFiles(Shipped, "*.wav").Select(Path.GetFileNameWithoutExtension).Order());
        var stamp = File.ReadAllText(Path.Combine(Source, Stamp.FileName));
        Assert.True(stamp == Stamp.Of(Source, Cues.All.Select(c => (c.Name, File.ReadAllBytes(Path.Combine(Shipped, $"{c.Name}.wav"))))),
            "the shipped sounds are stale: run scripts/sounds");
    }
}
