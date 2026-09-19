namespace Tabletop.GameServer.Tests.Infrastructure;

/// <summary>Polls a condition that becomes true asynchronously — the registry noticing a detach, a draft row the broadcast
/// does not wait for — instead of sleeping a fixed guess. Fails with a clear message rather than the bare assertion that
/// follows it timing out first.</summary>
public static class Wait
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(5);

    public static Task UntilAsync(Func<bool> condition, string message, TimeSpan? timeout = null) =>
        UntilAsync(() => Task.FromResult(condition()), message, timeout);

    public static async Task UntilAsync(Func<Task<bool>> condition, string message, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Default);
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline) throw new Xunit.Sdk.XunitException(message);
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
