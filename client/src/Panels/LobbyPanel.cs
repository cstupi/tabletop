using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>Game settings: written to the game's rows, broadcast, never logged. The lobby shows it as a column beside the team columns, setup as its
/// Game settings fold (spec 2026-11-07-lobby-and-setup-redesign-design.md). Leave game at its foot.</summary>
public partial class LobbyPanel : VBoxContainer
{
    Label _error = null!;
    /// <summary>The game screen's Leave, which asks first; lobby and setup have no Hud menu to hold it.</summary>
    public Action? Leave { get; init; }
    public required SetupFolds Folds { get; init; }

    /// <summary>Lobby: a heading over the settings. Setup: the Game settings fold, closed until opened. Start setup and End setup are the fixed
    /// head's (SidePanelHead); the teams are TeamColumnsPanel.</summary>
    public override void _Ready()
    {
        var s = Main.Instance.Session; var game = s.Game!;
        AddThemeConstantOverride("separation", 10);
        AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        VBoxContainer config;
        if (game.Status == GameStatus.Setup)
        {
            var settings = new Fold(Folds, SetupFold.Game, game.Status, () => SetupFolds.Title(SetupFold.Game, s.Draft, s.Game));
            AddChild(settings); config = settings.Body;
        }
        else
        {
            AddChild(new Label { Text = "Game settings", ThemeTypeVariation = Variation.Display });
            AddChild(config = new VBoxContainer());
        }
        config.AddThemeConstantOverride("separation", 8);
        config.AddChild(Wrapping(HeaderView.RuleSetLine(game), Variation.Dim));
        if (!s.IsCreator) config.AddChild(Wrapping(LobbyView.CreatorOnly, Variation.Dim));
        var name = new LineEdit { Text = game.Name, MaxLength = TextLimits.Name, PlaceholderText = "game name" }; config.AddChild(Named("Game name", name));
        var ruleSet = new OptionButton(); config.AddChild(Named("Rule set", ruleSet));
        IReadOnlyList<PropertiesSummary> sets = [];
        Guarded.Run(async () =>
        {
            var list = await s.Management.ListProperties();
            if (!IsInstanceValid(this)) return;
            sets = list;
            foreach (var x in list) ruleSet.AddItem(CreateDefaults.PackLabel(x));
            var at = list.ToList().FindIndex(x => x.ContentHash == game.PropertiesHash); if (at >= 0) ruleSet.Select(at);
        }, Problem);
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); shape.Select(game.SchedulerShape == SchedulerShape.PlayerTurn ? 1 : 0); config.AddChild(Named("Activation", shape));
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select((int)game.Enforcement); config.AddChild(Named("Enforcement", enforcement));
        var chosen = s.Options.Options.ToDictionary(o => o.Key, o => RulesOptions.ValueOf(o, game.OptionalRules));
        OptionalRules(s.Options, chosen, enforcement, config);
        var apply = Btn("Apply configuration", () =>
        {
            if (TextLimits.NameProblem(name.Text, "A game name") is { } problem) { Problem(problem); return; }
            Problem(null);
            Guarded.Send(s, LobbyEdits.Configure(game, name.Text,
                shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, (Enforcement)enforcement.Selected, s.Options, chosen,
                ruleSet.Selected >= 0 && ruleSet.Selected < sets.Count ? sets[ruleSet.Selected].ContentHash : null));
        });
        config.AddChild(apply);
        // The server takes the configuration from the creator alone: everyone else sees it, read-only, with the reason.
        if (!s.IsCreator)
            foreach (var control in config.FindChildren("*", owned: false).OfType<Control>())
            {
                if (control is BaseButton b) b.Disabled = true;
                if (control is LineEdit l) l.Editable = false;
                if (control is BaseButton or LineEdit) control.TooltipText = LobbyView.CreatorOnly;
            }
        if (Leave is { } leave && s.ParticipantId is { } me && me != Guid.Empty)   // gives the seat up, asked first
        {
            config.AddChild(new HSeparator());
            config.AddChild(Btn("Leave game", leave));
        }
    }

    static Button Btn(string text, Action a) { var b = new Button { Text = text }; b.Pressed += a; return b; }

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
        var box = new VBoxContainer(); box.AddChild(new Label { Text = label, ThemeTypeVariation = Variation.Dim }); box.AddChild(field); return box;
    }
    static Label Wrapping(string text, string? variation = null) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = variation ?? "" };

    /// <summary>
    /// The optional rules (optional-rules spec → Lobby): a preset button, then one button per catalog option labelled "Label (Page)". Picking a preset
    /// sets the option buttons and, when it names one, the enforcement; picking a value shows the preset it matches, else Custom. Writes into chosen;
    /// a catalog with no options adds nothing.
    /// </summary>
    static void OptionalRules(RulesOptionCatalog catalog, Dictionary<string, string> chosen, OptionButton enforcement, Container into)
    {
        if (catalog.Options.Count == 0) return;
        var preset = new OptionButton(); foreach (var p in catalog.Presets) preset.AddItem(p.Name); preset.AddItem(RulesOptions.Custom);
        into.AddChild(new Label { Text = "Optional rules" }); into.AddChild(Named("Preset", preset));
        var buttons = new List<OptionButton>();
        void ShowPreset()
        {
            var name = RulesOptions.PresetOf(catalog, chosen);
            var at = catalog.Presets.ToList().FindIndex(p => p.Name == name);
            preset.Select(at >= 0 ? at : catalog.Presets.Count);
        }
        foreach (var option in catalog.Options)
        {
            into.AddChild(new Label { Text = $"{option.Label} ({option.Page})", AutowrapMode = TextServer.AutowrapMode.WordSmart });
            var button = new OptionButton(); foreach (var v in option.Values) button.AddItem(v.Label);
            button.Select(option.Values.ToList().FindIndex(v => v.Value == chosen[option.Key]));
            button.ItemSelected += i => { chosen[option.Key] = option.Values[(int)i].Value; ShowPreset(); };
            into.AddChild(button); buttons.Add(button);
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
        };
        ShowPreset();
    }
}
