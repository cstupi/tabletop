namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>A clock that moves only when a test advances it.</summary>
public sealed class ManualTime : TimeProvider
{
    long _ticks;
    DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) { _ticks += by.Ticks; _now += by; }
}
