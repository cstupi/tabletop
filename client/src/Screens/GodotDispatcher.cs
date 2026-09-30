using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Screens;

/// <summary>Session events arrive on socket and thread-pool threads; Godot nodes may only be touched on the main thread.</summary>
public sealed class GodotDispatcher : IDispatcher
{
    public void Post(Action action) => Callable.From(action).CallDeferred();
}
