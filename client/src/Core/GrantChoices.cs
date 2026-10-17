namespace Tabletop.Client.Core;

/// <summary>One effect the menu's Add a grant offers: its label, and the entry and effect the AddGrant names.</summary>
public sealed record GrantChoice(string Label, string EntryName, Guid EffectId);

/// <summary>The pinned set's grantable effects as menu choices, in the server's order: the entry's name, numbered when the entry has several.</summary>
public static class GrantChoices
{
    public static IReadOnlyList<GrantChoice> Of(IReadOnlyList<GrantableEffectDto> effects) =>
        effects.GroupBy(e => e.EntryName, StringComparer.Ordinal).SelectMany(g =>
        {
            var list = g.ToList();
            return list.Select((e, i) => new GrantChoice(list.Count == 1 ? e.EntryName : $"{e.EntryName} ({i + 1} of {list.Count})", e.EntryName, e.EffectId));
        }).ToList();
}
