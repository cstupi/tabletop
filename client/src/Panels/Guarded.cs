using Godot;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>The error-handling pattern for panel code that awaits management calls or sends edits: nothing escapes, nothing is left unobserved.</summary>
public static class Guarded
{
    public static async void Run(Func<Task> work, Action<string> report)
    {
        try { await work(); }
        catch (ManagementException e) { report(e.Message); }
        catch (HttpRequestException e) { report($"Cannot reach the management API: {e.Message}"); }
        catch (Exception e) { report(e.Message); }
    }

    /// <summary>A socket send that fails is logged, never thrown into the engine.</summary>
    public static async void Send(Session s, Edit edit)
    {
        try { await s.SendEditAsync(edit); }
        catch (Exception e) { GD.PushWarning($"Edit send failed: {e.Message}"); }
    }
}
