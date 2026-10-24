namespace Tabletop.Sounds;

/// <summary>A 16-bit mono PCM WAV file.</summary>
public static class Wav
{
    public const int Rate = 44100;

    public static byte[] Of(float[] samples)
    {
        var data = samples.Length * 2;
        using var ms = new MemoryStream(44 + data);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
