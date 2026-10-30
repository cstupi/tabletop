using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>Lobby edits: written to the game's rows, broadcast, never logged. Shown at lobby and setup.</summary>
public partial class LobbyPanel : VBoxContainer
{
    Label _error = null!;
    /// <summary>The game screen's Leave, which asks first; lobby and setup have no Hud menu to hold it.</summary>
    public Action? Leave { get; init; }
    public required SetupFolds Folds { get; init; }
    /// <summary>Where the game screen puts the team cards, inside Players.</summary>
    public VBoxContainer TeamCards { get; } = new();

    /// <summary>Two folding sections (spec 2026-10-27-godot-setup-screen-design.md): Players (seat, teams, the Players list, my armies, the
    /// team cards, Leave game) and Game settings. Start setup and End setup are the fixed head's (SidePanelHead).</summary>
    public override void _Ready()
    {
        var s = Main.Instance.Session; var game = s.Game!;
        AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        var players = new Fold(Folds, SetupFold.Players, game.Status, () => SetupFolds.Title(SetupFold.Players, s.Draft, s.Game));
        var settings = new Fold(Folds, SetupFold.Game, game.Status, () => SetupFolds.Title(SetupFold.Game, s.Draft, s.Game));
        foreach (var fold in SetupFolds.Order(game.Status))
            if (fold == SetupFold.Players) AddChild(players); else if (fold == SetupFold.Game) AddChild(settings);
        var body = players.Body;
        body.AddChild(Wrapping(LobbyView.Seat(game, s.ParticipantId)));
        var teams = new HFlowContainer(); body.AddChild(teams);
        // An empty team is joined; a held one names its holder and, for anyone but the creator, says to ask the creator.
        foreach (var choice in LobbyView.Teams(game, s.ParticipantId, s.IsCreator))
        {
            var b = Btn(choice.Text, () => Guarded.Send(s, choice.Edit));
            b.Disabled = !choice.Enabled; b.TooltipText = choice.Why;
            teams.AddChild(b);
        }
        teams.AddChild(Btn("Watch", () => Guarded.Send(s, LobbyEdits.Watch())));
        teams.AddChild(Btn("Create team", () => Guarded.Send(s, LobbyEdits.CreateTeam())));
        body.AddChild(Seats(s, game));
        // My own army: one picker per team I hold (an army a side when I play both), or one for my teamless row. Teammates pick theirs.
        var pickers = LobbyView.Armies(game, s.ParticipantId).Select(seat => (Seat: seat, Box: new OptionButton())).ToList();
        foreach (var (seat, box) in pickers) { box.AddItem("No army"); body.AddChild(Named(seat.Label, box)); }
        body.AddChild(TeamCards);
        if (Leave is { } leave && s.ParticipantId is { } me && me != Guid.Empty) body.AddChild(Btn("Leave game", leave));   // gives the seat up, asked first
        Guarded.Run(async () =>
        {
            var list = (await s.Management.ListArmies(null)).Items;
            if (!IsInstanceValid(this)) return;
            var options = LobbyView.ArmyOptions(game, list);
            foreach (var (seat, box) in pickers)
            {
                AddArmies(box, options);
                if (seat.ArmyId is { } picked) { var at = options.ToList().FindIndex(o => o.Army.Id == picked); if (at >= 0) box.Select(at + 1); }
                box.ItemSelected += i => Guarded.Send(s, seat.Pick(i == 0 ? null : options[(int)i - 1].Army.Id));
            }
        }, Problem);
        var config = settings.Body;
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
    }

    /// <summary>Every participant, teamless ones included. The creator's rows carry a team picker that places that participant (PickTeam with its id).</summary>
    static Container Seats(Session s, GameInfo game)
    {
        var list = new VBoxContainer();
        list.AddChild(new Label { Text = "Players" });
        foreach (var seat in LobbyView.Players(game))
        {
            // One line: the name takes what the picker leaves and clips (a wrapping label in a flow row measures one letter a line).
            var row = new HBoxContainer(); list.AddChild(row);
            var text = seat.Army is { } army ? $"{seat.Name} ⚔ {army}" : seat.Name;
            row.AddChild(new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = text, MouseFilter = MouseFilterEnum.Pass });
            if (!s.IsCreator) { row.AddChild(new Label { Text = seat.Seat }); continue; }
            var picker = new OptionButton(); foreach (var c in LobbyView.SeatChoices(game)) picker.AddItem(c.Label);
            picker.Select(seat.Selected);
            picker.ItemSelected += i => Guarded.Send(s, LobbyView.Place(game, seat.ParticipantId, (int)i));
            row.AddChild(picker);
        }
        return list;
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
