using Godot;
using Tabletop.Client.Core;
using Tabletop.Client.Theming;
using Tabletop.Protocol;

namespace Tabletop.Client.Panels;

/// <summary>Lobby edits: written to the game's rows, broadcast, never logged. Shown at lobby and setup.</summary>
public partial class LobbyPanel : VBoxContainer
{
    Label _error = null!; Button? _endSetup; Session _session = null!; Action? _onBoard;
    /// <summary>The game screen's Leave, which asks first; lobby and setup have no Hud menu to hold it.</summary>
    public Action? Leave { get; init; }

    public override void _ExitTree()
    {
        if (_session is null || _onBoard is null) return;
        _session.BoardChanged -= _onBoard;
    }

    static string EndText(Session s)
    {
        var off = s.Draft is { } d ? d.Terrain.Count(t => !Tabletop.Geometry.Table.ContainsPoint(d.Size, t.Position.Horizontal)) : 0;   // the game server's DropOffSlab rule
        return off == 0 ? "End setup" : $"End setup ({off} off-slab pieces will be dropped)";
    }

    public override void _Ready()
    {
        var s = _session = Main.Instance.Session; var game = s.Game!;
        AddChild(new Label { Text = "Lobby" });
        AddChild(Wrapping(LobbyView.Seat(game, s.ParticipantId)));
        if (LobbyView.WaitingForCreator(game, s.IsCreator) is { } waiting) AddChild(Wrapping(waiting, Variation.Accent));
        var teams = new HFlowContainer(); AddChild(teams);
        // An empty team is joined; a held one names its holder and, for anyone but the creator, says to ask the creator.
        foreach (var choice in LobbyView.Teams(game, s.ParticipantId, s.IsCreator))
        {
            var b = Btn(choice.Text, () => Guarded.Send(s, new PickTeam(choice.TeamId)));
            b.Disabled = !choice.Enabled; b.TooltipText = choice.Why;
            teams.AddChild(b);
        }
        teams.AddChild(Btn("Watch", () => Guarded.Send(s, new PickTeam(null))));
        teams.AddChild(Btn("Create team", () => Guarded.Send(s, new CreateTeam())));
        AddChild(Seats(s, game));
        var armies = new OptionButton(); armies.AddItem("No army"); AddChild(Named("Army", armies));
        AddChild(_error = new Label { ThemeTypeVariation = Variation.Warning, AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false });
        Guarded.Run(async () =>
        {
            var list = (await s.Management.ListArmies(null)).Items;
            if (!IsInstanceValid(this)) return;
            foreach (var a in list) armies.AddItem(a.Name);
            var mine = game.Participants.FirstOrDefault(p => p.Id == s.ParticipantId)?.ArmyId;
            if (mine is not null) { var at = list.ToList().FindIndex(a => a.Id == mine); if (at >= 0) armies.Select(at + 1); }
            armies.ItemSelected += i => Guarded.Send(s, new PickArmy(i == 0 ? null : list[(int)i - 1].Id));
        }, Problem);
        var config = new VBoxContainer(); AddChild(config);
        if (!s.IsCreator) config.AddChild(Wrapping(LobbyView.CreatorOnly, Variation.Dim));
        var name = new LineEdit { Text = game.Name, MaxLength = TextLimits.Name, PlaceholderText = "game name" }; config.AddChild(Named("Game name", name));
        var shape = new OptionButton(); shape.AddItem("Alternating activation"); shape.AddItem("Player turn"); shape.Select(game.SchedulerShape == SchedulerShape.PlayerTurn ? 1 : 0); config.AddChild(Named("Activation", shape));
        var enforcement = new OptionButton(); foreach (var e in Enum.GetNames<Enforcement>()) enforcement.AddItem(e); enforcement.Select((int)game.Enforcement); config.AddChild(Named("Enforcement", enforcement));
        var chosen = s.Options.Options.ToDictionary(o => o.Key, o => RulesOptions.ValueOf(o, game.OptionalRules));
        OptionalRules(s.Options, chosen, enforcement, config);
        var apply = Btn("Apply configuration", () =>
        {
            if (TextLimits.NameProblem(name.Text, "A game name") is { } problem) { Problem(problem); return; }
            Problem(null);
            Guarded.Send(s, new SetConfiguration(name.Text.Trim(), game.PropertiesHash,
                shape.Selected == 0 ? SchedulerShape.AlternatingActivation : SchedulerShape.PlayerTurn, (Enforcement)enforcement.Selected, WaitsBetweenSteps: false,
                RulesOptions.MapOf(s.Options, chosen)));
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
        if (Leave is { } leave && s.ParticipantId is { } me && me != Guid.Empty) AddChild(Btn("Leave game", leave));   // gives the seat up, asked first
        if (s.IsCreator && game.Status == GameStatus.Lobby) AddChild(Btn("Start setup", () => Guarded.Send(s, new StartSetup())));
        if (s.IsCreator && game.Status == GameStatus.Setup)
        {
            AddChild(_endSetup = Btn(EndText(s), () => Guarded.Send(s, new EndSetup()))); _endSetup.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _onBoard = () => { if (IsInstanceValid(_endSetup)) _endSetup!.Text = EndText(s); };
            s.BoardChanged += _onBoard;
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
            row.AddChild(new Label { Text = seat.Name + (seat.HasArmy ? " ⚔" : ""), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = seat.Name, MouseFilter = MouseFilterEnum.Pass });
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
