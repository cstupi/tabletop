using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

/// <summary>Session events arrive on socket and thread-pool threads; Godot nodes may only be touched on the main thread. Posted work
/// runs in order at the end of the frame, within the frame's work budget (<see cref="FrameBudget"/>): a burst, such as entering a
/// game (the welcome, the board, the whole log, the first render), goes on in the next frames instead of stopping one.</summary>
public sealed class GodotDispatcher : IDispatcher
{
    readonly DispatchQueue _queue = new();
    bool _waiting;   // main thread: a run is connected to the next frame

    public void Post(Action action) { if (_queue.Post(action)) Callable.From(Run).CallDeferred(); }

    void Run()
    {
        _waiting = false;
        if (FrameBudget.Spent) { NextFrame(); return; }
        try { if (FrameBudget.Use(() => _queue.Run(FrameBudget.ClockMs, FrameBudget.Left))) NextFrame(); }
        finally { if (_queue.Pending && !_waiting) NextFrame(); }   // an action threw: the rest still run
    }

    void NextFrame()
    {
        if (_waiting) return;
        _waiting = true;
        FrameBudget.Next(Run);
    }
}
