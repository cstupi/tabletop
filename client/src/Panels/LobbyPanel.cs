using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>Game settings: written to the game's rows, broadcast, never logged. The lobby shows it as a column beside the team columns, setup in its
/// Game tool (spec 2026-11-21-setup-screen-layout-design.md → Lobby): **Game** (name, then the rule set, activation and enforcement in a fold
/// headed with their values, closed until opened, kept in <see cref="SetupFolds"/>), then **Optional rules** (preset, options). In the lobby
/// the creator's edits stay local (<see cref="LobbySettings"/>) until **Start setup** at the foot sends them and then starts setup; the rule
/// set alone applies on pick. In setup each change sends at once. Everyone sees it; only the creator's controls are enabled, and in the
/// lobby everyone else reads who starts setup at the foot. In the lobby the column has a fixed height (the game screen sets it): its title
/// stays at the top, the settings scroll inside it, and the foot stays in view, Space.L under them.</summary>
public partial class LobbyPanel : VBoxContainer
{
    Label _error = null!;

    /// <summary>The game screen's: the rule set fold's state and the lobby's local edits outlive each rebuild.</summary>
    public SetupFolds Folds { get; init; } = new();
    public LobbySettings Draft { get; init; } = new();
    public Action<string> Report { get; init; } = _ => { };

    public override void _Ready()
    {
        var s = Main.Instance.Session; var game = s.Game!;
        var lobby = game.Status == GameStatus.Lobby;
        AddThemeConstantOverride("separation", Space.L);
        VBoxContainer body = this;
        if (lobby)
        {
            AddChild(new Label { Text = "Game settings", ThemeTypeVariation = Variation.Display });
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve,
                SizeFlagsVertical = SizeFlags.ExpandFill };
            AddChild(scroll);
            scroll.AddChild(body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            body.AddThemeConstantOverride("separation", Space.XL);
        }
        body.AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        var config = Group(body, "Game");
        if (!s.IsCreator) config.AddChild(Wrapping(LobbyView.CreatorOnly, Variation.Dim));
        var editable = new List<Control>();
        var name = new LineEdit { Text = lobby ? Draft.NameOf(game) : game.Name, MaxLength = TextLimits.Name, PlaceholderText = "game name" };
        config.AddChild(Named("Game name", name)); editable.Add(name);
        var ruleSet = new OptionButton();
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn");
        shape.Select((lobby ? Draft.ShapeOf(game) : game.SchedulerShape) == SchedulerShape.PlayerTurn ? 1 : 0);
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e);
        enforcement.Select((int)(lobby ? Draft.EnforcementOf(game) : game.Enforcement));
        editable.AddRange([ruleSet, shape, enforcement]);
        SchedulerShape Shape() => shape.Selected == 1 ? SchedulerShape.PlayerTurn : SchedulerShape.AlternatingActivation;
        var fold = new Fold(Folds, SetupFold.GameRules, game.Status,
            () => LobbySettings.FoldTitle(HeaderView.RuleSetLine(game), Shape(), (Enforcement)enforcement.Selected), upper: false);
        fold.Body.AddThemeConstantOverride("separation", Space.M);
        config.AddChild(fold);
        fold.Body.AddChild(Named("Rule set", ruleSet)); fold.Body.AddChild(Named("Activation", shape)); fold.Body.AddChild(Named("Enforcement", enforcement));
        IReadOnlyList<PropertiesSummary> sets = [];
        Guarded.Run(async () =>
        {
            var list = await s.Management.ListProperties();
            if (!IsInstanceValid(this)) return;
            sets = list;
            foreach (var x in list) ruleSet.AddItem(CreateDefaults.PackLabel(x));
            var at = list.ToList().FindIndex(x => x.ContentHash == game.PropertiesHash); if (at >= 0) ruleSet.Select(at);
        }, Problem);
        var chosen = lobby ? Draft.ChosenOf(game, s.Options) : s.Options.Options.ToDictionary(o => o.Key, o => RulesOptions.ValueOf(o, game.OptionalRules));

        // Setup: a change sends the whole configuration at once, the name only when it passes (else the game's).
        void SendNow()
        {
            var named = TextLimits.NameProblem(name.Text, "A game name") is null ? name.Text : game.Name;
            Guarded.Send(s, LobbyEdits.Configure(game, named, Shape(), (Enforcement)enforcement.Selected, s.Options, chosen));
        }
        // The rule set decides the edition, and the edition which armies every picker offers: it applies on pick, in the lobby too.
        ruleSet.ItemSelected += i =>
        {
            if (i < 0 || i >= sets.Count || sets[(int)i].ContentHash == game.PropertiesHash) return;
            Draft.ClearOptions();
            Guarded.Send(s, LobbySettings.RuleSet(game, s.Options, sets[(int)i].ContentHash));
        };
        shape.ItemSelected += _ => { if (lobby) Draft.Shape = Shape(); else SendNow(); fold.Refresh(); };
        enforcement.ItemSelected += _ => { if (lobby) Draft.Enforcement = (Enforcement)enforcement.Selected; else SendNow(); fold.Refresh(); };
        name.TextChanged += text => { if (lobby) Draft.Name = text; Problem(TextLimits.NameProblem(text, "A game name")); };
        void CommitName()
        {
            if (lobby || !s.IsCreator || TextLimits.NameProblem(name.Text, "A game name") is not null || name.Text.Trim() == game.Name) return;
            SendNow();
        }
        name.TextSubmitted += _ => CommitName();
        name.FocusExited += CommitName;
        if (s.Options.Options.Count > 0)
            editable.AddRange(OptionalRules(s.Options, chosen, enforcement, Group(body, "Optional rules"), () =>
            {
                if (!lobby) { SendNow(); return; }
                foreach (var (key, value) in chosen) Draft.Choose(key, value);
                Draft.Enforcement = (Enforcement)enforcement.Selected;   // a preset may set it
                fold.Refresh();
            }));
        if (lobby)
        {
            if (s.IsCreator)
            {
                var start = new Button { Text = "Start setup", ThemeTypeVariation = Variation.Primary, FocusMode = FocusModeEnum.None };
                start.Pressed += () => StartSetup(s, start);
                AddChild(start);
            }
            else AddChild(Wrapping($"{TeamColumns.CreatorName(game)} starts setup", Variation.Dim));
        }
        // The server takes the configuration from the creator alone: everyone else sees it, read-only, with the reason.
        if (!s.IsCreator)
            foreach (var control in editable)
            {
                if (control is BaseButton b) b.Disabled = true;
                if (control is LineEdit l) l.Editable = false;
                control.TooltipText = LobbyView.CreatorOnly;
            }
    }

    /// <summary>Start setup: the local edits first, when they differ from the game's, and setup starts only once the server took them (a
    /// refusal the game screen toasts; a bad name shows here).</summary>
    void StartSetup(Session s, Button start)
    {
        var game = s.Game!;
        if (TextLimits.NameProblem(Draft.NameOf(game), "A game name") is { } problem) { Problem(problem); return; }
        start.Disabled = true;
        var draft = Draft;
        Guarded.Run(async () =>
        {
            if (draft.Pending(game, s.Options) is { } configure)
            {
                if (await s.SendGameEditAsync(configure) is not null) { if (IsInstanceValid(start)) start.Disabled = false; return; }
                draft.Clear();
            }
            await s.SendEditAsync(LobbyEdits.StartSetup());
        }, Report);
    }

    /// <summary>A group: its heading over its rows, rows a step apart.</summary>
    static VBoxContainer Group(Container into, string title)
    {
        var group = new VBoxContainer(); group.AddThemeConstantOverride("separation", Space.M); into.AddChild(group);
        group.AddChild(new Label { Text = title.ToUpperInvariant(), ThemeTypeVariation = Variation.Accent });
        return group;
    }

    /// <summary>The warning line, shown only while it has something to say: an empty label still takes a row.</summary>
    void Problem(string? text) { if (!IsInstanceValid(_error)) return; _error.Text = text ?? ""; _error.Visible = text is { Length: > 0 }; }

    /// <summary>The armies after a picker's first item: another edition's disabled, its reason in the text and the tooltip.</summary>
    internal static void AddArmies(OptionButton box, IReadOnlyList<ArmyOption> options, Func<ArmyOption, string>? text = null)
    {
        foreach (var o in options)
        {
            box.AddItem(text?.Invoke(o) ?? o.Text);
            var at = box.ItemCount - 1;
            box.SetItemDisabled(at, !o.Enabled);
            if (o.DisabledReason is { } why) box.SetItemTooltip(at, why);
        }
    }

    /// <summary>A field under its name.</summary>
    static Control Named(string label, Control field)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Space.XS);
        box.AddChild(new Label { Text = label, ThemeTypeVariation = Variation.Dim }); box.AddChild(field); return box;
    }
    static Label Wrapping(string text, string? variation = null) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = variation ?? "" };

    /// <summary>
    /// The optional rules (optional-rules spec → Lobby): a preset button, then one button per catalog option labelled "Label (Page)". Picking a preset
    /// sets the option buttons and, when it names one, the enforcement; picking a value shows the preset it matches, else Custom. Writes into chosen
    /// and calls changed after each pick; returns the pickers. A catalog with no options adds nothing.
    /// </summary>
    static List<Control> OptionalRules(RulesOptionCatalog catalog, Dictionary<string, string> chosen, OptionButton enforcement, Container into, Action changed)
    {
        if (catalog.Options.Count == 0) return [];
        var preset = new OptionButton(); foreach (var p in catalog.Presets) preset.AddItem(p.Name); preset.AddItem(RulesOptions.Custom);
        into.AddChild(Named("Preset", preset));
        var buttons = new List<OptionButton>();
        void ShowPreset()
        {
            var name = RulesOptions.PresetOf(catalog, chosen);
            var at = catalog.Presets.ToList().FindIndex(p => p.Name == name);
            preset.Select(at >= 0 ? at : catalog.Presets.Count);
        }
        foreach (var option in catalog.Options)
        {
            var button = new OptionButton(); foreach (var v in option.Values) button.AddItem(v.Label);
            var row = Named(option.Label, button); ((Label)row.GetChild(0)).AutowrapMode = TextServer.AutowrapMode.WordSmart; into.AddChild(row);
            button.Select(option.Values.ToList().FindIndex(v => v.Value == chosen[option.Key]));
            button.ItemSelected += i => { chosen[option.Key] = option.Values[(int)i].Value; ShowPreset(); changed(); };
            buttons.Add(button);
        }
        preset.ItemSelected += i =>
        {
            if (i >= catalog.Presets.Count) return;                                                     // Custom: the buttons stay as they are
            var picked = catalog.Presets[(int)i];
            foreach (var (option, button) in catalog.Options.Zip(buttons))
            {
                chosen[option.Key] = RulesOptions.ValueOf(option, picked.OptionalRules);
                button.Select(option.Values.ToList().FindIndex(v => v.Value == chosen[option.Key]));
            }
            if (picked.Enforcement is { } e) enforcement.Select((int)e);
            changed();
        };
        ShowPreset();
        return [preset, .. buttons];
    }
}
