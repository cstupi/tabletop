namespace Tabletop.GameServer.Session;

/// <summary>A rate in frames per second and the burst a bucket holds when full.</summary>
public sealed record FrameRate(double PerSecond, int Burst);

/// <summary>One connection's allowance for one kind of frame. Not thread-safe: the connection's read loop is its only caller.</summary>
public sealed class TokenBucket(FrameRate rate, TimeProvider time)
{
    private double _tokens = rate.Burst;
    private long _stamp = time.GetTimestamp();

    private void Refill()
    {
        var now = time.GetTimestamp();
        _tokens = Math.Min(rate.Burst, _tokens + time.GetElapsedTime(_stamp, now).TotalSeconds * rate.PerSecond);
        _stamp = now;
    }

    /// <summary>Takes a token if one is there.</summary>
    public bool TryTake()
    {
        Refill();
        if (_tokens < 1) return false;
        _tokens -= 1;
        return true;
    }

    /// <summary>How long until a token is there; zero when one is.</summary>
    public TimeSpan Wait()
    {
        Refill();
        return _tokens >= 1 ? TimeSpan.Zero : TimeSpan.FromSeconds((1 - _tokens) / rate.PerSecond);
    }

    /// <summary>Takes a token, waiting for one first: the read loop stops reading, so a flooding peer is slowed by its own TCP window.</summary>
    public async Task TakeAsync(CancellationToken ct)
    {
        while (Wait() is var wait && wait > TimeSpan.Zero) await Task.Delay(wait, time, ct);
        _tokens -= 1;
    }
}
