namespace Tabletop.Client.Core;

/// <summary>One line of the key overlay: the keys as caps, then what they do in four words or so.</summary>
public sealed record KeyRow(IReadOnlyList<string> Keys, string Does)
{
    public bool Equals(KeyRow? o) => o is not null && Does == o.Does && Keys.SequenceEqual(o.Keys);
    public override int GetHashCode() => HashCode.Combine(Does, Keys.Count);
}

/// <summary>A captioned group of the key overlay.</summary>
public sealed record KeyGroup(string Title, string Icon, IReadOnlyList<KeyRow> Rows);

/// <summary>
/// The key overlay (UX redesign part 2 → Key overlay): the open Ask's keys first (<c>NOW · </c> and its title), then Camera, Measure and
/// Table. Every key here is one Hotkeys.Of maps, or a mouse gesture TableInput reads.
/// </summary>
public static class KeyOverlayView
{
    static KeyRow R(string does, params string[] keys) => new(keys, does);

    public static KeyGroup Camera { get; } = new("Camera", "camera",
    [
        R("move", "W", "A", "S", "D"), R("orbit", "Q", "E"), R("zoom", "Wheel"), R("top-down", "Home"), R("level", "End"),
        R("to the active unit", "Space"), R("next of mine", "Tab"), R("previous of mine", "⇧", "Tab"),
    ]);

    public static KeyGroup Measure { get; } = new("Measure", "measure",
    [
        R("ruler from the point", "Ctrl"), R("measure tool", "M"), R("2D or 3D", "H"), R("sight check", "L"),
    ]);

    public static KeyGroup Table { get; } = new("Table", "table",
    [
        R("range rings", "V"), R("model hitboxes", "B"), R("terrain hitboxes", "⇧", "B"), R("roster", "R"), R("log", "G"),
        R("undo", "Ctrl", "Z"), R("ping", "P"), R("remove (setup)", "Del"), R("this overlay", "F1"),
    ]);

    /// <summary>The overlay's groups: the open Ask's keys first when there is one, then Camera, Measure and Table.</summary>
    public static IReadOnlyList<KeyGroup> Of(AskMode? mode, string? title, bool stoppable)
    {
        var now = mode is { } m ? Now(m, stoppable) : [];
        return now.Count == 0 ? [Camera, Measure, Table] : [new KeyGroup($"Now · {title}", "key", now), Camera, Measure, Table];
    }

    static IReadOnlyList<KeyRow> Now(AskMode mode, bool stoppable) => mode switch
    {
        AskMode.Choose => [R("pick an option", "1–9")],
        AskMode.Confirm => [R("confirm", "⏎")],
        AskMode.Move =>
        [
            R("commit the move", "⏎"), .. stoppable ? [R("stop here", "C")] : Array.Empty<KeyRow>(), R("reset", "Esc"),
            R("rows while holding", "1–5"), R("turn held models", "Q", "E"), R("pick a model", "⇧", "Click"), R("box models", "⇧", "Drag"),
            R("place freely", "Alt"), R("lower or raise held", "PgDn", "PgUp"),
        ],
        AskMode.Assign => [R("aim at a target", "Click"), R("fire", "⏎"), R("clear", "Esc")],
        AskMode.Allocate => [R("pick a model", "Click"), R("confirm", "⏎")],
        AskMode.PickPieces => [R("pick a unit", "Click"), R("pick by number", "1–9"), R("confirm", "⏎"), R("place freely", "Alt")],
        AskMode.TokenSpend => [R("confirm", "⏎")],
        _ => [],
    };
}
