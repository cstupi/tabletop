namespace Tabletop.Client.Core;

/// <summary>
/// A muted window kind (ClientSettings.MutedWindowKinds, the Ask's WindowKind, e.g. "before-attacking") in words, and the muted kinds as the
/// Settings dialog lists them, each with its Unmute: a muted window auto-passes every time it opens, so the list is where it is undone.
/// </summary>
public static class MutedPrompts
{
    /// <summary>"before-attacking" → "before attacking": for "Mute before attacking" and "Auto-passed: before attacking".</summary>
    public static string Words(string kind) => kind.Replace('-', ' ').Replace('_', ' ');

    /// <summary>"before-attacking" → "Before attacking": a row of the muted list.</summary>
    public static string Label(string kind) => Words(kind) is { Length: > 0 } w ? char.ToUpperInvariant(w[0]) + w[1..] : kind;

    /// <summary>Every muted kind with its label, in ordinal order.</summary>
    public static IReadOnlyList<(string Kind, string Label)> Of(IEnumerable<string> muted) =>
        muted.Order(StringComparer.Ordinal).Select(k => (k, Label(k))).ToList();
}
