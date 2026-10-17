namespace Tabletop.Geometry;

/// <summary>How many samples a swept check takes along a length. Capped, so a length no table holds (a forged destination, a stat
/// parameter in the millions) costs a bounded loop: past the cap the samples spread further apart rather than multiply.</summary>
public static class Sampling
{
    public const int MaxSamples = 4096;

    /// <summary>ceil(length / step), at least 1 and at most MaxSamples; a non-finite length is one sample.</summary>
    public static int Steps(double length, double step) =>
        !double.IsFinite(length) || length <= 0 ? 1 : (int)Math.Clamp(Math.Ceiling(length / step), 1, MaxSamples);
}
