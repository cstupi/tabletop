using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

/// <summary>The 3.6 hand part (server/game/Content/grimdark-future-3.6/army-rules.part.json) against the 3.6 dump's books: every name it defines is one a
/// 3.6 book describes or carries, and its text is that book's. Skips without the dump (git-ignored).</summary>
public class ArmyRules36Tests
{
    /// <summary>Entries still holding their 3.5.1 text and effects: 3.6.0 changed the text, and Task 7.5 authors each against it.</summary>
    static readonly string[] AwaitingTheir36Text =
    [
        "Bane Mark", "Bane in Melee Aura", "Bane when Shooting", "Bane when Shooting Aura", "Battleborn", "Bounding", "Changebound", "Darkborn",
        "Dash", "Defensive Frenzy", "Defensive Growth", "Destructive", "Devout", "Disintegrate", "Entrenched", "Evasive", "Fanatic", "Ferocious",
        "Fortified", "Fortified Growth", "Grounded Precision", "Guarded", "Guardian", "Guerrilla", "Harassing", "Havocbound", "Hazardous",
        "Highborn", "Hit & Run", "Hit & Run Fighter", "Hit & Run Shooter", "Hive Bond", "Hold the Line", "Honor Code", "Increased Shooting Range",
        "Increased Shooting Range Aura", "Increased Shooting Range Buff", "Increased Shooting Range Mark", "Indirect when Shooting Aura",
        "Infected", "Inquisitorial Agent", "Knightborn", "Lacerate", "Lustbound", "Machine-Fog", "Martial Prowess", "Melee Evasion",
        "Melee Shrouding", "Melee Slayer", "Mind Control", "Piercing Debuff", "Piercing Fighter Aura", "Piercing Frenzy", "Piercing Growth",
        "Piercing Shooter Aura", "Piercing Shooting Mark", "Piercing Spotter", "Piercing Tag", "Piercing Target", "Plaguebound",
        "Precision Frenzy", "Precision Growth", "Precision Spotter", "Precision Tag", "Precision Target", "Primeborn", "Protected",
        "Protection Feat", "Ranged Shrouding", "Ranged Slayer", "Rapid Blink", "Ravage", "Regenerative Strength", "Reinforcement",
        "Rending in Melee", "Rending in Melee Aura", "Rending when Shooting Aura", "Screened", "Scurry", "Self-Repair", "Sergeant", "Shatter",
        "Shielded", "Shred Mark", "Shred in Melee", "Shred in Melee Aura", "Shred when Shooting", "Shred when Shooting Aura", "Slayer", "Spawn",
        "Speed Debuff", "Speed Feat", "Split", "Steadfast", "Sturdy", "Takedown Shot", "Takedown Strike", "Targeting Visor",
        "Thrust in Melee Aura", "Unstoppable in Melee", "Unstoppable in Melee Aura", "Unstoppable when Shooting Aura", "Unwieldy Debuff",
        "Vanguard", "Vengeance", "Versatile Defense", "Versatile Reach", "Warbound", "Watchborn", "Wolfborn"
    ];

    static string? Up(string relative)
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, relative))) return Path.Combine(d.FullName, relative);
        return null;
    }

    static (List<AfBook> Books, PropertySetDocument Hand) Load()
    {
        var index = Up(Path.Combine(".superpowers", "armyforge", "3.6", "index.json"));
        Skip.If(index is null, ".superpowers/armyforge/3.6/index.json not found above the working directory (the dump is git-ignored)");
        var books = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(index)!, "books"), "*.json").Select(f => ArmyForgeJson.ReadBook(File.ReadAllText(f))).ToList();
        var repo = Path.GetDirectoryName(Up("Tabletop.sln"))!;
        return (books, PropertiesJson.Parse(File.ReadAllText(Path.Combine(repo, "server", "game", "Content", "grimdark-future-3.6", "army-rules.part.json"))));
    }

    [SkippableFact]
    public void Every_3_6_hand_entry_text_matches_a_3_6_book()
    {
        var (books, hand) = Load();
        var texts = books.SelectMany(b => b.SpecialRules ?? []).ToLookup(r => r.Name, r => (r.Description ?? "").Trim(), StringComparer.Ordinal);
        var differing = hand.Entries.Where(e => texts.Contains(e.Name) && !texts[e.Name].Contains(e.RuleText)).Select(e => e.Name);
        Assert.Equal(AwaitingTheir36Text.Order(StringComparer.Ordinal), differing.Order(StringComparer.Ordinal));
    }

    [SkippableFact]
    public void No_hand_entry_names_a_rule_no_3_6_book_uses()
    {
        var (books, hand) = Load();
        var used = books.SelectMany(b => b.SpecialRules ?? []).Select(r => r.Name).Concat(books.SelectMany(b => RuleWriter.NamesInScope(b).Keys)).ToHashSet(StringComparer.Ordinal);
        Assert.Equal([], hand.Entries.Select(e => e.Name).Where(n => !used.Contains(n)));
    }
}
