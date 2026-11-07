namespace Tabletop.Sounds;

/// <summary>What a cue measures: loudness (ITU-R BS.1770 K-weighting; the loudest 400 ms window, "momentary max"), sample peak, DC,
/// where its peak and its tail fall, and its spectrum (centroid, the share below 150 Hz, flatness: 1 is white noise, 0 a pure tone).</summary>
public sealed record Measures(double Seconds, double MomentaryMaxLufs, double PeakDb, double Dc, double PeakAt, double TailAt,
    double CentroidHz, double LowShare, double Flatness, double CrestDb)
{
    public override string ToString() =>
        $"{Seconds,5:F2}s  {MomentaryMaxLufs,6:F1} LUFS  peak {PeakDb,5:F1} dBFS  crest {CrestDb,4:F1} dB  peak@{PeakAt * 1000,4:F0}ms  " +
        $"-40dB@{TailAt * 1000,4:F0}ms  centroid {CentroidHz,5:F0} Hz  <150Hz {LowShare * 100,4:F1}%  flatness {Flatness:F2}  dc {Dc:E1}";
}

public static class Measure
{
    const double Rate = Wav.Rate;

    public static Measures Of(float[] x)
    {
        var peak = x.Max(Math.Abs);
        var peakAt = Array.IndexOf(x, x.MaxBy(Math.Abs)) / Rate;
        var rms = Math.Sqrt(x.Average(v => (double)v * v));
        var spectrum = Power(x);
        double total = 0, low = 0, weighted = 0, logSum = 0; var bins = 0;
        for (var k = 1; k < spectrum.Length; k++)
        {
            var hz = k * Rate / (2.0 * (spectrum.Length - 1));
            if (hz < 20 || hz > 16000) continue;
            total += spectrum[k]; weighted += hz * spectrum[k]; if (hz < 150) low += spectrum[k];
            logSum += Math.Log(spectrum[k] + 1e-20); bins++;
        }
        var flatness = Math.Exp(logSum / bins) / (total / bins);
        return new(x.Length / Rate, MomentaryMax(x), Db(peak), x.Average(v => (double)v), peakAt, TailAt(x, peak),
            weighted / total, low / total, flatness, Db(peak) - Db(rms));
    }

    public static double Db(double linear) => 20 * Math.Log10(Math.Max(linear, 1e-12));

    /// <summary>The loudest 400 ms window of the K-weighted signal, in LUFS; a clip shorter than a window is measured as if padded with silence.</summary>
    public static double MomentaryMax(float[] x)
    {
        var k = KWeighted(x); var window = (int)(0.4 * Rate); var step = (int)(0.01 * Rate);
        var sums = new double[k.Length + 1];
        for (var i = 0; i < k.Length; i++) sums[i + 1] = sums[i] + k[i] * k[i];
        var best = 0.0;
        for (var start = 0; start == 0 || start + window <= k.Length; start += step)
            best = Math.Max(best, (sums[Math.Min(k.Length, start + window)] - sums[start]) / window);
        return -0.691 + 10 * Math.Log10(Math.Max(best, 1e-20));
    }

    /// <summary>Where the envelope (5 ms RMS blocks) last stands above peak - 40 dB.</summary>
    static double TailAt(float[] x, double peak)
    {
        var size = (int)(0.005 * Rate); var floor = peak * 0.01; var last = 0;
        for (var b = 0; (b + 1) * size <= x.Length; b++)
        {
            double s = 0; for (var i = b * size; i < (b + 1) * size; i++) s += x[i] * x[i];
            if (Math.Sqrt(s / size) > floor) last = b + 1;
        }
        return last * size / Rate;
    }

    /// <summary>The BS.1770 pre-filter (a high shelf, +4 dB above ~1.7 kHz) and RLB high pass (~38 Hz), designed for this rate.</summary>
    public static double[] KWeighted(float[] x)
    {
        var y = x.Select(v => (double)v).ToArray();
        y = Biquad(y, Shelf(1681.974450955533, 3.999843853973347, 0.7071752369554196));
        y = Biquad(y, HighPass(38.13547087602444, 0.5003270373238773));
        return y;
    }

    static (double B0, double B1, double B2, double A1, double A2) Shelf(double f0, double gainDb, double q)
    {
        var K = Math.Tan(Math.PI * f0 / Rate); var Vh = Math.Pow(10, gainDb / 20); var Vb = Math.Pow(Vh, 0.4996667741545416);
        var a0 = 1 + K / q + K * K;
        return ((Vh + Vb * K / q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / q + K * K) / a0, 2 * (K * K - 1) / a0, (1 - K / q + K * K) / a0);
    }

    static (double B0, double B1, double B2, double A1, double A2) HighPass(double f0, double q)
    {
        var K = Math.Tan(Math.PI * f0 / Rate); var a0 = 1 + K / q + K * K;
        return (1, -2, 1, 2 * (K * K - 1) / a0, (1 - K / q + K * K) / a0);
    }

    static double[] Biquad(double[] x, (double B0, double B1, double B2, double A1, double A2) c)
    {
        var y = new double[x.Length]; double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var v = c.B0 * x[i] + c.B1 * x1 + c.B2 * x2 - c.A1 * y1 - c.A2 * y2;
            x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v;
        }
        return y;
    }

    /// <summary>The power spectrum of the whole clip, zero-padded to a power of two, Hann-windowed: n/2 + 1 bins.</summary>
    public static double[] Power(float[] x)
    {
        var n = 1; while (n < x.Length) n <<= 1;
        var re = new double[n]; var im = new double[n];
        for (var i = 0; i < x.Length; i++) re[i] = x[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (x.Length - 1)));
        Fft(re, im);
        var p = new double[n / 2 + 1];
        for (var k = 0; k < p.Length; k++) p[k] = re[k] * re[k] + im[k] * im[k];
        return p;
    }

    static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var ang = -2 * Math.PI / len; var wr = Math.Cos(ang); var wi = Math.Sin(ang);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var j = 0; j < len / 2; j++)
                {
                    var ur = re[i + j]; var ui = im[i + j];
                    var vr = re[i + j + len / 2] * cr - im[i + j + len / 2] * ci; var vi = re[i + j + len / 2] * ci + im[i + j + len / 2] * cr;
                    re[i + j] = ur + vr; im[i + j] = ui + vi; re[i + j + len / 2] = ur - vr; im[i + j + len / 2] = ui - vi;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }

    /// <summary>A 16-bit mono WAV's samples, as <see cref="Wav.Of"/> writes them.</summary>
    public static float[] Read(byte[] wav) => Enumerable.Range(0, (wav.Length - 44) / 2).Select(i => BitConverter.ToInt16(wav, 44 + 2 * i) / 32767f).ToArray();
}
