namespace Tabletop.Client.Core;

/// <summary>A key and the modifier states it needs: null matches either state (options-screen spec → Controls).</summary>
public sealed record KeyBinding(HotKey Key, bool? Shift = null, bool? Ctrl = false)
{
    /// <summary>Whether a press of the key with these modifiers is this binding.</summary>
    public bool Matches(HotKey key, bool shift, bool ctrl) => Key == key && (Shift is null || Shift == shift) && (Ctrl is null || Ctrl == ctrl);
    /// <summary>Whether some one press is both bindings.</summary>
    public bool Overlaps(KeyBinding o) => Key == o.Key && (Shift is null || o.Shift is null || Shift == o.Shift) && (Ctrl is null || o.Ctrl is null || Ctrl == o.Ctrl);
    /// <summary>How many modifier states it names: a press resolves to its most specific match.</summary>
    internal int Rank => (Shift is null ? 0 : 1) + (Ctrl is null ? 0 : 1);
}

/// <summary>A captioned group of the Controls tab.</summary>
public sealed record BindingGroup(string Title, IReadOnlyList<Intent> Intents);

/// <summary>
/// Which key asks for which intent: the default table (equal to the hotkey switch it replaced, KeyBindingsTests) with the player's
/// overrides (ClientSettings.KeyOverrides) laid over it, one list of bindings per intent. A press resolves to the most specific binding
/// that matches it. The key overlay and every key cap read their caps from here, so a rebind shows everywhere.
/// </summary>
public sealed class KeyBindings
{
    static KeyBinding B(HotKey k, bool? shift = null, bool? ctrl = false) => new(k, shift, ctrl);

    static readonly IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> Defaults = new Dictionary<Intent, IReadOnlyList<KeyBinding>>
    {
        [Intent.Option1] = [B(HotKey.Key1)], [Intent.Option2] = [B(HotKey.Key2)], [Intent.Option3] = [B(HotKey.Key3)],
        [Intent.Option4] = [B(HotKey.Key4)], [Intent.Option5] = [B(HotKey.Key5)], [Intent.Option6] = [B(HotKey.Key6)],
        [Intent.Option7] = [B(HotKey.Key7)], [Intent.Option8] = [B(HotKey.Key8)], [Intent.Option9] = [B(HotKey.Key9)],
        [Intent.Confirm] = [B(HotKey.Enter), B(HotKey.KpEnter)],
        [Intent.Cancel] = [B(HotKey.Escape)],
        [Intent.SingleModel] = [B(HotKey.Shift)],
        [Intent.Sight] = [B(HotKey.L)],
        [Intent.Measure] = [B(HotKey.M)],
        [Intent.MeasureHeld] = [B(HotKey.Ctrl, null, null)],
        [Intent.Ping] = [B(HotKey.P)],
        [Intent.NextUnit] = [B(HotKey.Tab, false)],
        [Intent.PreviousUnit] = [B(HotKey.Tab, true), B(HotKey.Backtab)],
        [Intent.FocusCamera] = [B(HotKey.Space)],
        [Intent.Undo] = [B(HotKey.Z, null, true)],
        [Intent.ToggleLog] = [B(HotKey.G)],
        [Intent.CameraTop] = [B(HotKey.Home)],
        [Intent.CameraLevel] = [B(HotKey.End)],
        [Intent.KeyOverlay] = [B(HotKey.F1), B(HotKey.Question), B(HotKey.Slash, true)],
        [Intent.TurnLeft] = [B(HotKey.Q)],
        [Intent.TurnRight] = [B(HotKey.E)],
        [Intent.StopHere] = [B(HotKey.C)],
        [Intent.CameraForward] = [B(HotKey.W)],
        [Intent.CameraBack] = [B(HotKey.S)],
        [Intent.CameraLeft] = [B(HotKey.A)],
        [Intent.CameraRight] = [B(HotKey.D)],
        [Intent.ToggleRoster] = [B(HotKey.R)],
        [Intent.ToggleRangeRings] = [B(HotKey.V)],
        [Intent.ToggleAllThreats] = [B(HotKey.T)],
        [Intent.Remove] = [B(HotKey.Delete), B(HotKey.Backspace)],
        [Intent.MeasureMode] = [B(HotKey.H, null, null)],          // with or without Ctrl, so it switches while the measure key is held
        [Intent.ToggleHitboxes] = [B(HotKey.B, false)],
        [Intent.ToggleTerrainHitboxes] = [B(HotKey.B, true)],
        [Intent.Lower] = [B(HotKey.Pagedown)],                    // held pieces lift over spec: the held pieces one surface down, or up
        [Intent.Raise] = [B(HotKey.Pageup)],
    };

    public static KeyBindings Default { get; } = new(new Dictionary<Intent, IReadOnlyList<KeyBinding>>());

    /// <summary>The player's overrides, by intent: each replaces that intent's whole default list.</summary>
    public IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> Overrides { get; }
    readonly (KeyBinding Binding, Intent Intent)[] _all;

    KeyBindings(IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> overrides)
    {
        Overrides = overrides;
        _all = Enum.GetValues<Intent>().SelectMany(i => Of(i).Select(b => (b, i))).ToArray();
    }

    public static KeyBindings With(IReadOnlyDictionary<Intent, IReadOnlyList<KeyBinding>> overrides) =>
        overrides.Count == 0 ? Default : new(overrides.Where(kv => Rebindable(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

    /// <summary>This table with the intent's binding at the slot replaced (or added past its last).</summary>
    public KeyBindings With(Intent intent, int slot, KeyBinding? binding)
    {
        var list = Of(intent).ToList();
        if (binding is null) { if (slot < list.Count) list.RemoveAt(slot); }
        else if (slot < list.Count) list[slot] = binding; else list.Add(binding);
        var next = Overrides.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (list.SequenceEqual(Defaults.GetValueOrDefault(intent) ?? [])) next.Remove(intent); else next[intent] = list;
        return With(next);
    }

    public IReadOnlyList<KeyBinding> Of(Intent intent) => Overrides.TryGetValue(intent, out var o) ? o : Defaults.GetValueOrDefault(intent) ?? [];

    /// <summary>The intent of a press: the most specific binding that matches it, else none.</summary>
    public Intent? Of(HotKey key, bool shift, bool ctrl)
    {
        Intent? best = null; var rank = -1;
        foreach (var (b, i) in _all)
            if (b.Matches(key, shift, ctrl) && b.Rank > rank) { best = i; rank = b.Rank; }
        return best;
    }

    /// <summary>Another intent with a binding some press shares with this one, and that binding's slot.</summary>
    public (Intent Intent, int Slot)? Conflict(Intent intent, KeyBinding binding)
    {
        foreach (var other in Enum.GetValues<Intent>())
        {
            if (other == intent) continue;
            var list = Of(other);
            for (var s = 0; s < list.Count; s++) if (list[s].Overlaps(binding)) return (other, s);
        }
        return null;
    }

    /// <summary>The binding a press in the Controls tab captures: Shift and Ctrl when held; unheld, Shift either way and Ctrl as the
    /// intent's default treats it (the measure keys work with Ctrl down). A modifier key ignores its own flag.</summary>
    public static KeyBinding Capture(Intent intent, HotKey key, bool shift, bool ctrl)
    {
        if (key == HotKey.Shift) shift = false;
        if (key == HotKey.Ctrl) return new(key, null, null);
        var ctrlAny = Defaults.GetValueOrDefault(intent) is [{ Ctrl: null }, ..];
        return new(key, shift ? true : null, ctrl ? true : ctrlAny ? null : false);
    }

    /// <summary>Option1–9, Confirm and Cancel are listed in the Controls tab but keep their keys.</summary>
    public static bool Rebindable(Intent intent) => intent is not ((>= Intent.Option1 and <= Intent.Option9) or Intent.Confirm or Intent.Cancel);

    /// <summary>A binding as caps: Ctrl, ⇧, then the key.</summary>
    public static IReadOnlyList<string> Caps(KeyBinding b)
    {
        var caps = new List<string>(3);
        if (b.Ctrl == true) caps.Add("Ctrl");
        if (b.Shift == true) caps.Add("⇧");
        caps.Add(Cap(b.Key));
        return caps;
    }

    /// <summary>The intent's first binding as one cap string ("Ctrl+Z", "⇧+B"), empty when it has none.</summary>
    public string Cap(Intent intent) => Of(intent) is [var b, ..] ? string.Join("+", Caps(b)) : "";

    /// <summary>The intent's first binding as caps, empty when it has none.</summary>
    public IReadOnlyList<string> CapsOf(Intent intent) => Of(intent) is [var b, ..] ? Caps(b) : [];

    public static string Cap(HotKey k) => k switch
    {
        >= HotKey.Key1 and <= HotKey.Key9 => ((int)(k - HotKey.Key1) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        HotKey.Key0 => "0",
        HotKey.Enter or HotKey.KpEnter => "⏎",
        HotKey.Escape => "Esc", HotKey.Shift => "⇧", HotKey.Backtab => "Tab", HotKey.Delete => "Del", HotKey.Insert => "Ins",
        HotKey.Pageup => "PgUp", HotKey.Pagedown => "PgDn", HotKey.Question => "?", HotKey.Slash => "/",
        HotKey.Left => "←", HotKey.Right => "→", HotKey.Up => "↑", HotKey.Down => "↓",
        HotKey.Comma => ",", HotKey.Period => ".", HotKey.Minus => "-", HotKey.Equal => "=", HotKey.Semicolon => ";", HotKey.Apostrophe => "'",
        HotKey.Bracketleft => "[", HotKey.Bracketright => "]", HotKey.Backslash => "\\", HotKey.Quoteleft => "`",
        _ => k.ToString(),
    };

    /// <summary>The Controls tab's groups, every intent once.</summary>
    public static IReadOnlyList<BindingGroup> Groups { get; } =
    [
        new("Decisions", [Intent.Option1, Intent.Option2, Intent.Option3, Intent.Option4, Intent.Option5, Intent.Option6, Intent.Option7, Intent.Option8, Intent.Option9,
            Intent.Confirm, Intent.Cancel, Intent.StopHere]),
        new("Units", [Intent.NextUnit, Intent.PreviousUnit, Intent.SingleModel, Intent.TurnLeft, Intent.TurnRight, Intent.Lower, Intent.Raise]),
        new("Camera", [Intent.CameraForward, Intent.CameraBack, Intent.CameraLeft, Intent.CameraRight, Intent.CameraTop, Intent.CameraLevel, Intent.FocusCamera]),
        new("Measure and sight", [Intent.MeasureHeld, Intent.Measure, Intent.MeasureMode, Intent.Sight]),
        new("Table and setup", [Intent.ToggleRangeRings, Intent.ToggleAllThreats, Intent.ToggleHitboxes, Intent.ToggleTerrainHitboxes, Intent.Remove, Intent.Ping, Intent.Undo]),
        new("Interface", [Intent.ToggleRoster, Intent.ToggleLog, Intent.KeyOverlay]),
    ];

    /// <summary>The Controls tab's name for an intent.</summary>
    public static string Name(Intent i) => i switch
    {
        >= Intent.Option1 and <= Intent.Option9 => $"Option {i - Intent.Option1 + 1}",
        Intent.Confirm => "Confirm", Intent.Cancel => "Cancel or clear", Intent.StopHere => "Stop here",
        Intent.NextUnit => "Next unit", Intent.PreviousUnit => "Previous unit", Intent.SingleModel => "Pick single models",
        Intent.TurnLeft => "Turn or orbit left", Intent.TurnRight => "Turn or orbit right", Intent.Lower => "Lower held pieces", Intent.Raise => "Raise held pieces",
        Intent.CameraForward => "Camera forward", Intent.CameraBack => "Camera back", Intent.CameraLeft => "Camera left", Intent.CameraRight => "Camera right",
        Intent.CameraTop => "Top-down view", Intent.CameraLevel => "Table-level view", Intent.FocusCamera => "Centre on the active unit",
        Intent.MeasureHeld => "Ruler while held", Intent.Measure => "Measure tool", Intent.MeasureMode => "Ruler 2D or 3D", Intent.Sight => "Sight check",
        Intent.ToggleRangeRings => "Range rings", Intent.ToggleAllThreats => "All enemy threats", Intent.ToggleHitboxes => "Model hitboxes",
        Intent.ToggleTerrainHitboxes => "Terrain hitboxes", Intent.Remove => "Remove (setup)", Intent.Ping => "Ping", Intent.Undo => "Undo",
        Intent.ToggleRoster => "Roster", Intent.ToggleLog => "Combat log", Intent.KeyOverlay => "Key overlay",
        _ => i.ToString(),
    };
}
