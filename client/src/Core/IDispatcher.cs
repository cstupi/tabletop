namespace Tabletop.Client.Core;

/// <summary>Where Session raises its events. Godot posts to the main thread (CallDeferred); tests run the action at once.</summary>
public interface IDispatcher { void Post(Action action); }

public sealed class ImmediateDispatcher : IDispatcher { public void Post(Action action) => action(); }
