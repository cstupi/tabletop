using Tabletop.Sounds;

namespace Tabletop.Sounds.Tests;

public class CueTests
{
    static readonly string Shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../client/content/sounds"));
    static readonly string Source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scripts/Sounds"));

    public static TheoryData<string> Names() { var d = new TheoryData<string>(); foreach (var (name, _) in Cues.All) d.Add(name); return d; }

    static float[] Build(string name) => Cues.All.Single(c => c.Name == name).Build();

    static short[] Pcm(byte[] wav) => Enumerable.Range(0, (wav.Length - 44) / 2).Select(i => BitConverter.ToInt16(wav, 44 + 2 * i)).ToArray();

    [Fact]
    public void The_four_cues_are_shoot_melee_move_and_wound() =>
        Assert.Equal(["shoot", "melee", "move", "wound"], Cues.All.Select(c => c.Name));

    [Theory, MemberData(nameof(Names))]
    public void A_cue_is_the_same_bytes_every_build(string name) =>
        Assert.Equal(Wav.Of(Build(name)), Wav.Of(Build(name)));

    [Theory, MemberData(nameof(Names))]
    public void A_cue_is_a_16_bit_mono_wav_under_a_second_and_100_KB(string name)
    {
        var wav = Wav.Of(Build(name));
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
    public void A_cue_peaks_near_minus_6_dBFS_and_is_not_silent(string name)
    {
        var pcm = Pcm(Wav.Of(Build(name)));
        var peakDb = 20 * Math.Log10(pcm.Max(s => Math.Abs((int)s)) / 32767.0);
        Assert.InRange(peakDb, -6.5, -5.5);
        Assert.True(Math.Sqrt(pcm.Average(s => (double)s * s)) / 32767 > 0.01);
    }

    [Theory, MemberData(nameof(Names))]
    public void A_cue_fades_in_and_out_with_no_click(string name)
    {
        var x = Build(name); var pcm = Pcm(Wav.Of(x));
        Assert.Equal(0, pcm[0]); Assert.Equal(0, pcm[^1]);
        // 2 ms in: no sample in the first millisecond is above half of the ramp's end value's bound; the last 5 ms stay quiet at the end.
        Assert.True(pcm.Take(44).All(s => Math.Abs((int)s) < 32767 * 0.5 * 44 / 88 + 1));
        Assert.True(pcm.TakeLast(44).All(s => Math.Abs((int)s) < 32767 * 0.5 * 44 / 530 * 2 + 1));
        var step = Enumerable.Range(1, pcm.Length - 1).Max(i => Math.Abs(pcm[i] - pcm[i - 1]));
        Assert.True(step < 32767 * 0.45, $"largest step between neighbours {step}");
    }

    [Fact]
    public void The_cues_differ()
    {
        var all = Cues.All.Select(c => Convert.ToHexString(Wav.Of(c.Build()))).ToList();
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
