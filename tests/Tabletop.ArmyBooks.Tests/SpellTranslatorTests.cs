using System.Text.Json;
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
    public void An_unknown_gets_loses_ignores_verb_is_refused_rather_than_read_as_a_grant()
    {
        var b = Book("78qp9l5alslt6yj8");
        var blessed = b.Spells.Single(x => x.Name == "Blessed Ammo");
        var verb = System.Text.Json.JsonDocument.Parse("\"ignores\"").RootElement;
        var ignoring = blessed with { Generation = blessed.Generation with { Effect = blessed.Generation.Effect with {
            Parameters = blessed.Generation.Effect.Parameters.Select(p => p.Name == "gets/loses/ignores" ? p with { Value = verb } : p).ToList() } } };
        Assert.Contains("ignores", Assert.ThrowsAny<Exception>(() => SpellTranslator.Translate(b, ignoring)).Message);
    }

    [Fact]
    public void A_text_that_disagrees_with_its_generation_is_named()
    {
        var b = Book("78qp9l5alslt6yj8");
        var s = b.Spells.Single(x => x.Name == "Psychic Terror");
        var bad = s with { Effect = s.Effect.Replace("9 hits", "8 hits") };
        Assert.Contains(TextCheck.Problems(bad, SpellTranslator.Translate(b, bad)), p => p.Contains("9 hits"));
    }

    [Fact]
    public void Speed_rows_and_weapon_rule_children_get_distinct_ordinals()
    {
        var spells = SpellTranslatorTests.AllTranslated().ToList();
        var speed = spells.Where(s => s.Effects.Count == 3 && s.Effects.All(r => r.Kind == EffectKind.ModifyStat && r.TargetAction is not null)).ToList();
        Assert.NotEmpty(speed);
        Assert.All(speed, s => Assert.Equal([10, 20, 30], s.Effects.Select(r => r.Ordinal).ToList()));
        var twoRules = spells.Where(s => s.Effects.Any(r => r.Children.Count == 2 && r.Kind == EffectKind.AddHit)).ToList();
        Assert.NotEmpty(twoRules);
        Assert.All(twoRules, s => Assert.Equal([20, 30], s.Effects.Single().Children.Select(c => c.Ordinal).ToList()));
    }

    [Fact]
    public void When_attacking_lists_its_values_in_ordinal_order()
    {
        var attacking = AllTranslated().SelectMany(s => s.Effects).SelectMany(r => r.Conditions).Where(c => c.Values.Count == 2).ToList();
        Assert.NotEmpty(attacking);
        Assert.All(attacking, c => Assert.Equal(["melee", "shooting"], c.Values));
    }

    [Fact]
    public void A_spell_whose_assignment_says_in_line_of_sight_keeps_its_line_of_sight()
    {
        var b = Book("78qp9l5alslt6yj8"); var terror = b.Spells.Single(s => s.Name.Trim() == "Psychic Terror");
        Assert.Null(SpellTranslator.Translate(b, terror).Targeting!.LineOfSight);
        var sight = new AfParam("inLoS", JsonDocument.Parse("\"in line of sight\"").RootElement.Clone());
        var seen = terror with { Generation = terror.Generation with { Assignment = terror.Generation.Assignment with { Parameters = [.. terror.Generation.Assignment.Parameters, sight] } } };
        Assert.True(SpellTranslator.Translate(b, seen).Targeting!.LineOfSight);
    }

    static AfSpell With(AfSpell s, string effect, string? effectId = null, params AfParam[] more) =>
        s with { Effect = effect, Generation = s.Generation with { Effect = s.Generation.Effect with { Id = effectId ?? s.Generation.Effect.Id, Parameters = [.. s.Generation.Effect.Parameters, .. more] } } };

    [Fact]
    public void The_printed_range_beats_the_generations()
    {
        var b = Book("78qp9l5alslt6yj8"); var terror = b.Spells.Single(s => s.Name.Trim() == "Psychic Terror");
        Assert.Null(SpellTranslator.RangeCorrection(terror));
        var longer = terror with { Effect = terror.Effect.Replace("within 6\"", "within 12\"") };
        Assert.Equal((6, 12), SpellTranslator.RangeCorrection(longer));
        Assert.Equal(12, SpellTranslator.Translate(b, longer).Targeting!.Range);
        Assert.Empty(TextCheck.Problems(longer, SpellTranslator.Translate(b, longer)));
    }

    [Fact]
    public void A_token_loss_effect_or_an_empty_attack_type_falls_back_to_a_text_only_spell()
    {
        var b = Book("78qp9l5alslt6yj8"); var terror = b.Spells.Single(s => s.Name.Trim() == "Psychic Terror");
        var loss = With(terror, terror.Effect, "Urj6dU6Tua8B");
        var empty = With(terror, terror.Effect, "i1BVqztsr1V6", new AfParam("attackType", JsonDocument.Parse("\"\"").RootElement.Clone()));
        foreach (var s in new[] { loss, empty })
        {
            var entry = SpellTranslator.Translate(b, s);
            Assert.True(SpellTranslator.IsTextOnly(s));
            Assert.Equal((false, 0), (entry.Played, entry.Effects.Count));
        }
        Assert.False(SpellTranslator.IsTextOnly(terror));
    }

    public static IEnumerable<EntryDocument> AllTranslated() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "books")).Order(StringComparer.Ordinal)
            .Select(f => ArmyForgeJson.ReadBook(File.ReadAllText(f))).SelectMany(b => b.Spells.Select(s => SpellTranslator.Translate(b, s)));
}
