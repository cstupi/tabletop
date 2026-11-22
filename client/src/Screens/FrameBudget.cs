using Godot;

namespace Tabletop.Client.Screens;

/// <summary>
/// The main thread's work budget per frame for what can wait a frame: the dispatcher's queue and the meshes' templates. Work
/// past it goes on in the next frame, so a burst keeps the window drawing (spec 2026-10-25-loading-design.md → Entering a game).
/// 8 ms leaves a 60 Hz frame's 16.7 ms room for the frame's own process and draw. One action is never split: a single long one
/// still takes its frame. Main thread only.
/// </summary>
public static class FrameBudget
{
    public const double Ms = 8;
    static ulong _frame;
    static double _spent;

    static void Roll() { var f = Engine.GetProcessFrames(); if (f != _frame) { _frame = f; _spent = 0; } }

    public static bool Spent { get { Roll(); return _spent >= Ms; } }
    public static double Left { get { Roll(); return Math.Max(0, Ms - _spent); } }
    public static double ClockMs() => Time.GetTicksUsec() / 1000.0;

    /// <summary>Runs the work and counts its time against this frame.</summary>
    public static T Use<T>(Func<T> work)
    {
        Roll();
        var start = ClockMs();
        try { return work(); }
        finally { Roll(); _spent += ClockMs() - start; }
    }

    /// <summary>Calls the action at the start of the next frame.</summary>
    public static void Next(Action action) =>
        ((SceneTree)Engine.GetMainLoop()).Connect(SceneTree.SignalName.ProcessFrame, Callable.From(action), (uint)GodotObject.ConnectFlags.OneShot);

    /// <summary>Returns once this frame has budget left, waiting for later frames while it has none.</summary>
    public static async Task Turn()
    {
        while (Spent) await ((SceneTree)Engine.GetMainLoop()).ToSignal((SceneTree)Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);
    }
}
