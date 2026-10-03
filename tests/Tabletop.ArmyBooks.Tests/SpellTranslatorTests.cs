using Tabletop.GameServer.Properties;

namespace Tabletop.ArmyBooks.Tests;

public class SpellTranslatorTests
{
    public static AfBook Book(string uid) => ArmyForgeJson.ReadBook(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books", uid + ".json")));
    static EntryDocument Spell(string uid, string name) { var b = Book(uid); return SpellTranslator.Translate(b, b.Spells.Single(s => s.Name.Trim() == name)); }

    [Fact]
    public void Damage_is_number_digit_times_the_spells_value_with_its_weapon_rules_as_children()
    {
        var terror = Spell("78qp9l5alslt6yj8", "Psychic Terror");                  // value 3, numberDigit 3: "takes 9 hits with Bane"
        Assert.Equal((3, EffectKind.AddHit, 9), (terror.Cost, terror.Effects.Single().Kind, terror.Effects.Single().Amount));
        Assert.Equal([("Bane", (int?)null)], terror.Effects.Single().Children.Select(c => (c.TargetEntry!, c.Amount)));
        Assert.Equal(new SpellTargeting(TargetSide.Enemy, TargetGrain.Unit, 1, 6), terror.Targeting);
        Assert.Null(terror.Faction);
    }

    [Fact]
    public void A_spell_rolls_its_hits_only_when_it_prints_the_roll_sentence()
    {
        Assert.Null(Spell("k5T0f2XIfpmPO28h", "Zap!").Effects.Single().TargetRoll);
        Assert.Equal("hit", Spell("jpc0kyil0juwy602", "Electric Tempest").Effects.Single().TargetRoll);
    }

    [Fact]
    public void A_model_target_and_its_unit_of_one()
    {
        var expel = Spell("z65fgu0l29i4lnlu", "Expel Threat");
        Assert.Equal((TargetGrain.Model, 6), (expel.Targeting!.Grain, expel.Effects.Single().Amount));
    }

    [Theory]
    [InlineData("z65fgu0l29i4lnlu", "Psy-Injected Courage", EffectKind.GrantProperty, EffectSubject.Self, "Hold the Line Boost")]
    [InlineData("z65fgu0l29i4lnlu", "Calculated Foresight", EffectKind.GrantProperty, EffectSubject.Opponent, "Relentless")]
    [InlineData("78qp9l5alslt6yj8", "Advanced Sight", EffectKind.GrantProperty, EffectSubject.Opponent, "Unstoppable")]
    [InlineData("78qp9l5alslt6yj8", "Blessed Ammo", EffectKind.GrantProperty, EffectSubject.Self, "Shred")]
    [InlineData("7el7k3hgy5pb9o9i", "Corrode Weapons", EffectKind.ModifyParameter, EffectSubject.Self, "AP")]
    public void Grants_are_once_rows_on_the_side_the_text_names(string uid, string name, EffectKind kind, EffectSubject subject, string entry)
    {
        var row = Spell(uid, name).Effects.Single();
        Assert.Equal((kind, subject, entry, EffectDuration.Once), (row.Kind, row.Subject, row.TargetEntry, row.Duration));
    }

    [Fact]
    public void Attack_types_become_conditions_and_loses_is_minus_the_rating()
    {
        Assert.Equal(["shooting"], Spell("78qp9l5alslt6yj8", "Blessed Ammo").Effects.Single().Conditions.Single().Values);
        var corrode = Spell("7el7k3hgy5pb9o9i", "Corrode Weapons").Effects.Single();
        Assert.Equal(-1, corrode.Amount);
    }

    [Theory]
    [InlineData("7oi8zeiqfamiur21", "Burn the Heretic", "casting", -3, EffectSubject.Self)]
    [InlineData("k5T0f2XIfpmPO28h", "Shroud Field", "defense", 1, EffectSubject.Self)]
    [InlineData("jpc0kyil0juwy602", "Psy-Injected Courage", "morale", 1, EffectSubject.Self)]
    [InlineData("dnthspt7c0klhmt8", "Brain Infestation", "hit", -1, EffectSubject.Self)]
    [InlineData("q11la9v8h1heu9ja", "Raiding Drugs", "hit", 1, EffectSubject.Opponent)]
    public void Roll_modifiers(string uid, string name, string roll, int amount, EffectSubject subject)
    {
        var row = Spell(uid, name).Effects.Single();
        Assert.Equal((EffectKind.ModifyRoll, roll, amount, subject, EffectDuration.Once), (row.Kind, row.TargetRoll!, row.Amount!.Value, row.Subject, row.Duration));
    }

    [Fact]
    public void Speed_range_terrain_and_the_two_tests()
    {
        Assert.Equal([("Advance", 2), ("Rush", 4), ("Charge", 4)], Spell("z65fgu0l29i4lnlu", "Shock Speed").Effects.Select(e => (e.TargetAction!, e.Amount!.Value)));
        Assert.Equal([("Advance", -2), ("Rush", -4), ("Charge", -4)], Spell("fk1mkbp8apvltu0z", "Deceleration Rune").Effects.Select(e => (e.TargetAction!, e.Amount!.Value)));
        Assert.Equal((EffectSubject.Self, 6), (Spell("fk1mkbp8apvltu0z", "Battle Rune").Effects.Single().Subject, Spell("fk1mkbp8apvltu0z", "Battle Rune").Effects.Single().Amount!.Value));
        Assert.Equal(EffectSubject.Opponent, Spell("vux1Y5vvULmaxZ8P", "Eternal Guidance").Effects.Single().Subject);
        Assert.Equal(TargetTerrain.Dangerous, Spell("7o6om21wxlvvy3hq", "Cursed Stride").Effects.Single().TargetTerrain);
        var fury = Spell("iV_3U33NE1_ZTXBB", "Terrifying Fury").Effects.Single();
        Assert.Equal((EffectKind.TakeTest, "morale", "fatigued"), (fury.Kind, fury.TargetRoll!, fury.Children.Single().TargetCondition!));
        var hypnosis = Spell("zz3kp5ry7ks6mxcx", "Deep Hypnosis").Effects.Single();
        Assert.Equal((EffectKind.Move, 6), (hypnosis.Children.Single().Kind, hypnosis.Children.Single().Amount!.Value));
    }

    [Fact]
    public void Every_fixture_spell_translates_and_agrees_with_its_text()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books")))
        {
            var book = ArmyForgeJson.ReadBook(File.ReadAllText(file));
            foreach (var s in book.Spells) Assert.Empty(TextCheck.Problems(s, SpellTranslator.Translate(book, s)));
        }
    }

    [Fact]
    public void A_text_that_disagrees_with_its_generation_is_named()
    {
        var b = Book("78qp9l5alslt6yj8");
        var s = b.Spells.Single(x => x.Name == "Psychic Terror");
        var bad = s with { Effect = s.Effect.Replace("9 hits", "8 hits") };
        Assert.Contains(TextCheck.Problems(bad, SpellTranslator.Translate(b, bad)), p => p.Contains("9 hits"));
    }
}
