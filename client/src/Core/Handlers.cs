using System.Runtime.ExceptionServices;

namespace Tabletop.Client.Core;

/// <summary>An event's subscribers called one by one: one that throws does not skip the rest or what follows them (a frame's render
/// mark), and its exception is rethrown after them, so it still reaches the log.</summary>
public static class Handlers
{
    public static void Raise(Delegate? handlers, Action<Delegate> call, Action after)
    {
        ExceptionDispatchInfo? first = null;
        foreach (var handler in handlers?.GetInvocationList() ?? [])
            try { call(handler); } catch (Exception e) { first ??= ExceptionDispatchInfo.Capture(e); }
        after();
        first?.Throw();
    }
}
