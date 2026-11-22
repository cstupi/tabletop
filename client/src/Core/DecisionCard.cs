using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>The card's title band: the Ask's icon key, its title, the unit it is about, and the prompt as a body line when the title is not it.</summary>
public sealed record CardHead(string Icon, string Title, string? Subject = null, string? Prompt = null);

/// <summary>One option as a tile: the word after the label's colon (the whole label with no unit), its icon key (null: none), its number key (0: none).</summary>
public sealed record OptionTile(string OptionId, int Key, string Word, string? Icon, IReadOnlyList<Guid> Pieces, string Label);

/// <summary>A run of tiles: one unit's options under its name, pips and team band (Unit null: options naming no one unit), and the run's note once.</summary>
public sealed record OptionGroup(Guid? UnitId, string? Unit, int TeamOrdinal, string? Pips, IReadOnlyList<OptionTile> Tiles, string? Note);

/// <summary>A move's allowance as a bar: the farthest held model's distance over what the move allows, and the least it must go.</summary>
public sealed record AllowanceBar(double Used, double Allowance, double? Minimum)
{
    public bool Over => Used > Allowance + 1e-6;
    public double Fraction => Allowance <= 0 ? (Used > 0 ? 1 : 0) : Math.Clamp(Used / Allowance, 0, 1);
    public double? MinimumFraction => Minimum is { } m && Allowance > 0 ? Math.Clamp(m / Allowance, 0, 1) : null;
    public string Text => $"{DecisionCard.Inches(Used)} of {DecisionCard.Inches(Allowance)}";
}

/// <summary>An open Ask waiting on a team, as a quiet band: whose it is and what it does. Mine: a team this client holds.</summary>
public sealed record QuietBand(Guid TeamId, int Ordinal, string Team, string Doing, bool Mine);

/// <summary>One team's answer to a sealed Ask, as its band and a chip.</summary>
public sealed record RevealChip(int Ordinal, string Team, string Choice);

/// <summary>
/// The decision box as a card (UX redesign part 2 → Decision box): the title band, the option tiles grouped by unit, the move's allowance bar,
/// the key caps, and the quiet band for an Ask that is not mine. Words are the server's: a tile's word is its label after the colon.
/// </summary>
public static class DecisionCard
{
    /// <summary>The key caps drawn on the buttons.</summary>
    public const string Enter = "⏎", Esc = "Esc";
    /// <summary>Stop here's key cap, from the current bindings.</summary>
    public static string Stop => Hotkeys.Bindings.Cap(Intent.StopHere);

    /// <summary>The questions whose options are one unit's actions.</summary>
    public static readonly IReadOnlySet<string> Activations = new HashSet<string>(StringComparer.Ordinal) { AskQuestions.Activate, AskQuestions.ActOrEndPhase };

    static readonly IReadOnlySet<string> Deploys = new HashSet<string>(StringComparer.Ordinal)
        { AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher, AskQuestions.DeployReinforcement };

    const int ShortTitle = 36;

    public static string Inches(double d) => $"{Math.Round(d, 1):0.#}\"";

    public static string KeyCap(int key) => key > 0 ? key.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";

    /// <summary>The icon of an action word the rulebook uses; null for any other.</summary>
    public static string? ActionIcon(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "hold" => "hold", "advance" => "advance", "rush" => "rush", "charge" => "charge", "shoot" => "shoot",
        _ => null,
    };

    /// <summary>A label's word after the colon the scheduler prints ("Red unit 1: Hold" is "Hold"); the label itself without one.</summary>
    public static string Word(string label)
    {
        var at = label.IndexOf(": ", StringComparison.Ordinal);
        return at < 0 ? label : label[(at + 2)..];
    }

    public static CardHead Head(Ask ask, Projection p, Names n)
    {
        var acting = HeaderView.ActionFrame(p)?.Context;
        string? Actor() => acting?.ActorUnitId is { } u ? n.Unit(u) : null;
        if (ask.Legal is MoveRegion m)
        {
            var unit = m.ModelIds.Count > 0 && p.Models.GetValueOrDefault(m.ModelIds[0]) is { } lead ? n.Unit(lead.UnitId) : Actor();
            if (MoveRuler.IsDisembark(m)) return new CardHead("advance", "Disembark", unit);
            var action = acting?.Action;
            return new CardHead(ActionIcon(action) ?? "advance", action ?? "Move", unit);
        }
        if (ask.Legal is Assignment) return new CardHead("shoot", "Shoot", Actor());
        var icon = ask.Question switch
        {
            _ when Activations.Contains(ask.Question) => "activation",
            _ when Deploys.Contains(ask.Question) => "army",
            "remove_casualties" => "wound",
            "charge_target" => "charge",
            "takedown_target" or "strike_back" or "counter" => "melee",
            "shoot_or_not" => "shoot",
            "cast" or "spell_targets" or AskQuestions.SpendTokens => "cast",
            AskQuestions.EndRound or AskQuestions.Confirm or AskQuestions.GameOver => "ready",
            _ => "activation",
        };
        if (ask.Question == AskQuestions.GameOver) return new CardHead(icon, "End or continue?", null, ask.Prompt);
        return ask.Prompt.Length <= ShortTitle && !ask.Prompt.Contains('\n')
            ? new CardHead(icon, ask.Prompt)
            : new CardHead(icon, RosterView.Words(ask.Question.Replace('_', ' ')), null, ask.Prompt);
    }

    /// <summary>
    /// The options as tiles. An activation's options are grouped by unit, each run of options naming the same pieces with the same note one
    /// group, its note once; any other Ask's options are one group. The keys are the numbered list's, so they continue across groups.
    /// </summary>
    public static IReadOnlyList<OptionGroup> Groups(Ask ask, IReadOnlyList<AskOption> shown, IReadOnlyList<(int Key, string Label, string OptionId)> numbered,
        Projection p, GameInfo game, Names n, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var activation = Activations.Contains(ask.Question);
        int KeyOf(AskOption c) => numbered.FirstOrDefault(x => x.OptionId == c.Id).Key;
        OptionTile Tile(AskOption c, bool byUnit)
        {
            var word = byUnit ? Word(c.Label) : c.Label;
            var icon = ActionIcon(word) ?? (activation && byUnit ? "activation" : ask.Question == "cast" ? "cast" : null);
            return new OptionTile(c.Id, KeyOf(c), word, icon, c.Pieces, c.Label);
        }
        if (!activation) return shown.Count == 0 ? [] : [new OptionGroup(null, null, 0, null, shown.Select(c => Tile(c, false)).ToList(), null)];
        var groups = new List<OptionGroup>();
        for (var i = 0; i < shown.Count;)
        {
            var j = i + 1;
            while (j < shown.Count && shown[j].Note == shown[i].Note && shown[j].Pieces.SequenceEqual(shown[i].Pieces)) j++;
            var run = shown.Skip(i).Take(j - i).ToList();
            var unit = DecisionView.UnitNamed(run[0].Pieces, p);
            var ordinal = unit is { } u && n.TeamOfUnit(u) is { } t ? game.Teams.FirstOrDefault(x => x.Id == t)?.Ordinal ?? 0 : 0;
            groups.Add(new OptionGroup(unit, unit is { } id ? n.Unit(id) : null, ordinal, unit is { } uid ? UnitPips(p, uid, maxWounds) : null,
                run.Select(c => Tile(c, unit is not null)).ToList(), run[0].Note));
            i = j;
        }
        return groups;
    }

    /// <summary>The unit's wound pips as the roster draws them: wounds left over its models not removed.</summary>
    public static string UnitPips(Projection p, Guid unitId, IReadOnlyDictionary<Guid, int> maxWounds)
    {
        var models = p.Models.Values.Where(m => m.UnitId == unitId).Select(m => (M: m, Max: maxWounds.GetValueOrDefault(m.Id, 1))).ToList();
        var left = models.Where(x => !(x.M.Location is OnShelf && x.M.Wounds >= x.Max)).Sum(x => Math.Max(0, x.Max - x.M.Wounds));
        return RosterView.Pips(left, models.Sum(x => x.Max));
    }

    /// <summary>The bar over the held models' readouts (MoveRuler.Read): the farthest one's distance.</summary>
    public static AllowanceBar Bar(IEnumerable<RulerReadout> readouts, double allowance, double? minimum) =>
        new(readouts.Select(r => r.Distance).DefaultIfEmpty(0).Max(), allowance, minimum);

    /// <summary>One band per team an open Ask waits on, in Ask order, each team and question once; my teams' first.</summary>
    public static IReadOnlyList<QuietBand> Quiet(Projection p, GameInfo game, IReadOnlyCollection<Guid> mine)
    {
        var n = new Names(game, p);
        return p.OpenAsks.Values.OrderBy(a => a.Ask.Id)
            .SelectMany(a => a.Ask.Teams.Where(t => !a.Answers.ContainsKey(t)).Select(t => (Team: t, a.Ask.Question)))
            .Distinct()
            .Select(x => new QuietBand(x.Team, game.Teams.FirstOrDefault(t => t.Id == x.Team)?.Ordinal ?? 0, n.Team(x.Team), Doing(x.Question), mine.Contains(x.Team)))
            .OrderByDescending(b => b.Mine).ToList();
    }

    /// <summary>What a team is doing while its Ask is open, as the waiting band says it.</summary>
    public static string Doing(string question) => question switch
    {
        AskQuestions.Activate or AskQuestions.ActOrEndPhase => "activating",
        AskQuestions.Deploy or AskQuestions.DeployScouts or AskQuestions.DeployAmbusher or AskQuestions.DeployReinforcement => "deploying",
        "move" => "moving",
        "declare_targets" => "declaring targets",
        "remove_casualties" => "removing casualties",
        AskQuestions.SpendTokens => "spending tokens",
        AskQuestions.EndRound => "ending the round",
        "cast" => "casting",
        _ => question.Replace('_', ' '),
    };
}
