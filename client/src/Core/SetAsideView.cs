using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>A unit's set-aside chip during deployment: the rule's name as the server sends it, and the tooltip (the rule's text, then how).</summary>
public sealed record SetAsideChip(string Name, string Tip);

/// <summary>
/// Which units may be held off the table at deployment, and by which rule (ModelFacts.SetAsideRules): the chip on the roster row and on the deploy
/// pick's tile, and the deploy pick's Set aside beside a unit. The client names no rule: the name and text are the server's, and the server names
/// none once round one starts, so the chip goes.
/// </summary>
public static class SetAsideView
{
    public const string How = "Right-click → Set aside";
    public const string HowBack = "Set aside · right-click → Return to deployment";

    public static SetAsideChip? Of(Projection p, Guid unitId, IReadOnlyDictionary<Guid, SetAsideRule> rules)
    {
        if (!rules.TryGetValue(unitId, out var rule)) return null;
        var how = Held(p, unitId) ? HowBack : How;
        var said = FirstSentence(rule.Text);
        return new SetAsideChip(rule.Name, said.Length == 0 ? how : $"{said}\n{how}");
    }

    /// <summary>The rule text's first sentence ("May be set aside before deployment."): what choosing it does, short enough for a tooltip line;
    /// the unit card's rule chip carries the whole text.</summary>
    public static string FirstSentence(string text)
    {
        var at = text.IndexOf(". ", StringComparison.Ordinal);
        return (at < 0 ? text : text[..(at + 1)]).Trim();
    }

    /// <summary>The deploy pick's Set aside: the roster menu's command (MenuModel.ForRosterUnit) for a unit the server names that is not held yet; else null.</summary>
    public static Command? SetAside(Projection p, Guid unitId, IReadOnlySet<Guid> maySetAside) =>
        maySetAside.Contains(unitId) && p.Units.ContainsKey(unitId) && !Held(p, unitId) ? Toggle(unitId, true) : null;

    /// <summary>Set the unit aside (true) or return it to deployment (false): the one command both controls send.</summary>
    public static SetCondition Toggle(Guid unitId, bool aside) => new(new Subject(SubjectKind.Unit, unitId), MenuModel.SetAside, aside);

    /// <summary>What the deploy pick draws from the facts, as a key: the units that may be set aside and their rules, in order. The box draws an
    /// Ask again when it changes (facts that arrive after the Ask), and only then.</summary>
    public static string DrawnKey(IReadOnlySet<Guid> maySetAside, IReadOnlyDictionary<Guid, SetAsideRule> rules) =>
        string.Join(";", maySetAside.Order().Select(u => $"{u}:{(rules.TryGetValue(u, out var r) ? r.Name : "")}"));

    static bool Held(Projection p, Guid unitId) => p.Units.GetValueOrDefault(unitId)?.Conditions.Contains(MenuModel.SetAside) == true;
}

/// <summary>
/// The unit card while I place a unit (the owner: "I have no idea what the stats or info of the unit I am placing is anywhere"): the unit picked
/// holds the card while the selection stands as it was at the pick, or is cleared; a unit selected since takes it. The unit card feeds my activating
/// unit the same way when I place nothing (SheetFocus.Actor). Feed it every render.
/// </summary>
public sealed class PlacingFocus
{
    Guid? _placing, _selectedAtPick;

    public Guid? Placing(Guid? placing, Guid? selected)
    {
        if (placing != _placing) (_placing, _selectedAtPick) = (placing, selected);
        return placing is not null && (selected is null || selected == _selectedAtPick) ? placing : null;
    }
}
