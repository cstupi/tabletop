using Tabletop.Protocol;

namespace Tabletop.Client.Core;

/// <summary>
/// The sight check drawn from the server's answer (in-game UX spec → Sight check): the summary over the target and the models dimmed
/// under a target pick. It counts lines and reads CanShoot and Ranges; whether a unit is in cover is the rules' and is never decided here.
/// </summary>
public static class SightSummary
{
    /// <summary>"4 of 5 models can see Red Warriors · 2 of 3 clear lines screened", and while targets are declared how many can shoot and each range
    /// a rule changed against this target: "· 1 can shoot · Carbine 18" → 14" (Shadowborn)".</summary>
    public static string Text(SightAnswer answer, string target, bool shooting)
    {
        var observers = answer.Lines.Select(l => l.FromModelId).Distinct().Count();
        var seeing = answer.Lines.Where(l => l.Clear).Select(l => l.FromModelId).Distinct().Count();
        var clear = answer.Lines.Count(l => l.Clear);
        var text = $"{seeing} of {observers} models can see {target}";
        if (clear > 0) text += $" · {answer.Lines.Count(l => l.Clear && l.Screened)} of {clear} clear lines screened";
        if (!shooting) return text;
        text += $" · {answer.CanShoot.Count} can shoot";
        var changed = (answer.Ranges ?? []).Where(r => r.Inches != r.Printed).ToList();
        if (changed.Count == 0) return text;
        text += " · " + string.Join(", ", changed.Select(r => $"{r.Weapon} {Inches(r.Printed)}\" → {Inches(r.Inches)}\""));
        var rules = changed.SelectMany(r => r.By).Distinct().ToList();
        if (rules.Count > 0) text += $" ({string.Join(", ", rules)})";
        return text;
    }

    static string Inches(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The models to dim: the observers that cannot shoot the hovered target, and each assigned group's shooters that cannot shoot
    /// the group's target. A group's shooters carry one of its weapons (the group's pieces are weapon ids). A target with no answer yet dims nothing.
    /// </summary>
    public static IReadOnlySet<Guid> Dimmed(Projection p, SightAnswer? hovered, IReadOnlyList<AskOption> groups, IReadOnlyDictionary<string, Guid> assigned,
        IReadOnlyDictionary<Guid, SightAnswer> answers)
    {
        var dim = new HashSet<Guid>();
        if (hovered is not null) dim.UnionWith(hovered.Lines.Select(l => l.FromModelId).Where(m => !hovered.CanShoot.Contains(m)));
        foreach (var group in groups)
        {
            if (!assigned.TryGetValue(group.Id, out var target) || !answers.TryGetValue(target, out var answer)) continue;
            dim.UnionWith(p.Models.Values.Where(m => m.Slot.Weapons.Any(w => group.Pieces.Contains(w.Id)) && !answer.CanShoot.Contains(m.Id)).Select(m => m.Id));
        }
        return dim;
    }
}
