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
    static KeyRow R(string does, IEnumerable<string> keys) => new(keys.ToList(), does);

    public static KeyGroup Camera(KeyBindings? keys = null)
    {
        var k = keys ?? Hotkeys.Bindings;
        return new("Camera", "camera",
        [
            R("move", [.. k.CapsOf(Intent.CameraForward), .. k.CapsOf(Intent.CameraLeft), .. k.CapsOf(Intent.CameraBack), .. k.CapsOf(Intent.CameraRight)]),
            R("orbit", [.. k.CapsOf(Intent.TurnLeft), .. k.CapsOf(Intent.TurnRight)]), R("zoom", "Wheel"),
            R("top-down", k.CapsOf(Intent.CameraTop)), R("level", k.CapsOf(Intent.CameraLevel)),
            R("to the active unit", k.CapsOf(Intent.FocusCamera)), R("next of mine", k.CapsOf(Intent.NextUnit)), R("previous of mine", k.CapsOf(Intent.PreviousUnit)),
        ]);
    }

    public static KeyGroup Measure(KeyBindings? keys = null)
    {
        var k = keys ?? Hotkeys.Bindings;
        return new("Measure", "measure",
        [
            R("ruler from the point", k.CapsOf(Intent.MeasureHeld)), R("measure tool", k.CapsOf(Intent.Measure)), R("2D or 3D", k.CapsOf(Intent.MeasureMode)), R("sight check", k.CapsOf(Intent.Sight)),
        ]);
    }

    public static KeyGroup Table(KeyBindings? keys = null)
    {
        var k = keys ?? Hotkeys.Bindings;
        return new("Table", "table",
        [
            R("range rings", k.CapsOf(Intent.ToggleRangeRings)), AllThreats(k), R("model hitboxes", k.CapsOf(Intent.ToggleHitboxes)),
            R("terrain hitboxes", k.CapsOf(Intent.ToggleTerrainHitboxes)), R("roster", k.CapsOf(Intent.ToggleRoster)), R("log", k.CapsOf(Intent.ToggleLog)),
            R("undo", k.CapsOf(Intent.Undo)), R("ping", k.CapsOf(Intent.Ping)), R("remove (setup)", k.CapsOf(Intent.Remove)), R("this overlay", k.CapsOf(Intent.KeyOverlay)),
        ]);
    }

    /// <summary>The overlay's groups: the open Ask's keys first when there is one (the card's actions by their numbers, CardActions.Numbered,
    /// then its gestures), ending with T for every enemy threat (deployment and play), then Camera, Measure and Table. Caps come from
    /// <paramref name="keys"/>, else the current bindings.</summary>
    public static IReadOnlyList<KeyGroup> Of(AskMode? mode, string? title, IReadOnlyList<CardAction> actions, KeyBindings? keys = null)
    {
        var k = keys ?? Hotkeys.Bindings;
        IReadOnlyList<KeyRow> rows = mode is { } m
            ? [.. actions.Where(a => a.Key > 0).Select(a => R(a.Word.ToLowerInvariant(), DecisionCard.KeyCap(a.Key))), .. Now(m, k)] : [];
        KeyRow[] now = rows.Count == 0 ? [] : [.. rows, AllThreats(k)];
        KeyGroup[] rest = [Camera(k), Measure(k), Table(k)];
        return now.Length == 0 ? rest : [new KeyGroup($"Now · {title}", "key", now), .. rest];
    }

    static KeyRow AllThreats(KeyBindings k) => R("all enemy threats", k.CapsOf(Intent.ToggleAllThreats));

    static IReadOnlyList<KeyRow> Now(AskMode mode, KeyBindings k) => mode switch
    {
        AskMode.Choose => [R("pick an option", "1–9")],
        AskMode.Move =>
        [
            R("reset", "Esc"),
            R("rows while holding", "1–5"), R("turn the held unit", [.. k.CapsOf(Intent.TurnLeft), .. k.CapsOf(Intent.TurnRight)]),
            R("turn each held model", ["⇧", .. k.CapsOf(Intent.TurnLeft), .. k.CapsOf(Intent.TurnRight)]), R("pick a model", "⇧", "Click"), R("box models", "⇧", "Drag"),
            R("place freely", "Alt"), R("lower or raise held", [.. k.CapsOf(Intent.Lower), .. k.CapsOf(Intent.Raise)]),
        ],
        AskMode.Assign => [R("aim at a target", "Click")],
        AskMode.Allocate => [R("pick a model", "Click")],
        AskMode.PickPieces => [R("pick a unit", "Click"), R("pick by number", "1–9"), R("place freely", "Alt")],
        _ => [],
    };
}
