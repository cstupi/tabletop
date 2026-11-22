using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>Game settings: written to the game's rows, broadcast, never logged. The lobby shows it as a column beside the team columns, setup in its
/// Game tool (spec 2026-11-21-setup-screen-layout-design.md): **Game** (the rule set line, name, rule set, activation, enforcement), then
/// **Optional rules** (preset, options), Apply at the foot. Everyone sees it; only the creator's controls are enabled.</summary>
public partial class LobbyPanel : VBoxContainer
{
    Label _error = null!;

    public override void _Ready()
    {
        var s = Main.Instance.Session; var game = s.Game!;
        var lobby = game.Status == GameStatus.Lobby;
        AddThemeConstantOverride("separation", lobby ? Space.XL : Space.L);
        if (lobby) AddChild(new Label { Text = "Game settings", ThemeTypeVariation = Variation.Display });
        AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        var config = Group(this, "Game");
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
        if (s.Options.Options.Count > 0) OptionalRules(s.Options, chosen, enforcement, Group(this, "Optional rules"));
        var apply = Btn("Apply configuration", () =>
        {
            if (TextLimits.NameProblem(name.Text, "A game name") is { } problem) { Problem(problem); return; }
            Problem(null);
            Guarded.Send(s, LobbyEdits.Configure(game, name.Text,
                shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, (Enforcement)enforcement.Selected, s.Options, chosen,
                ruleSet.Selected >= 0 && ruleSet.Selected < sets.Count ? sets[ruleSet.Selected].ContentHash : null));
        });
        apply.ThemeTypeVariation = Variation.Primary;
        AddChild(apply);
        // The server takes the configuration from the creator alone: everyone else sees it, read-only, with the reason.
        if (!s.IsCreator)
            foreach (var control in FindChildren("*", owned: false).OfType<Control>())
            {
                if (control is BaseButton b) b.Disabled = true;
                if (control is LineEdit l) l.Editable = false;
                if (control is BaseButton or LineEdit) control.TooltipText = LobbyView.CreatorOnly;
            }
    }

    /// <summary>A group: its heading over its rows, rows a step apart.</summary>
    static VBoxContainer Group(Container into, string title)
    {
        var group = new VBoxContainer(); group.AddThemeConstantOverride("separation", Space.M); into.AddChild(group);
        group.AddChild(new Label { Text = title.ToUpperInvariant(), ThemeTypeVariation = Variation.Accent });
        return group;
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
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", Space.XS);
        box.AddChild(new Label { Text = label, ThemeTypeVariation = Variation.Dim }); box.AddChild(field); return box;
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
            button.ItemSelected += i => { chosen[option.Key] = option.Values[(int)i].Value; ShowPreset(); };
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
        };
        ShowPreset();
    }
}
