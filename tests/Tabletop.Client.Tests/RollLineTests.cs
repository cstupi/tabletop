using Tabletop.Client.Core;
using Tabletop.Protocol;
using Tabletop.Protocol.Tests;

namespace Tabletop.Client.Tests;

/// <summary>One line per roll (combat-log spec → The log → Roll), read off RollEntry.Of directly.</summary>
public class RollLineTests
{
    static readonly Guid Frame = Guid.CreateVersion7();
    static readonly SequenceContext Context = new(null, [], [], null);
    static readonly Names N = new(Samples.Game, null);

    static Die D(int raw, bool success, params string[] triggered) => new(raw, raw, success, triggered.Length == 0 ? null : triggered);
    static ThresholdBasis Basis(string stat, int printed) => new(stat, printed, []);

    static RollRecord Record(string roll, int? threshold, IReadOnlyList<Die> dice, ThresholdBasis? basis = null, int? requiredRaw = null, params RollModifier[] modifiers) =>
        new(Guid.CreateVersion7(), roll, threshold, modifiers, dice, requiredRaw, basis);

    static RollEntry Entry(RollRecord r, string? sequence = "shooting")
    {
        var step = new StepCompleted(Frame, "roll", null, [r], []);
        return RollEntry.Of(new GameEvent(1, null, step, null), false, step, r, Context, sequence, [], N);
    }

    static string Line(RollRecord r, string? sequence = "shooting") => Entry(r, sequence).Collapsed(CombatLog.Pip);

    [Fact]
    public void A_raw_face_roll_reads_unmodified()
    {
        Assert.Equal("Hit · unmodified 6+ · 2 dice: ⚅ ⚄ · 1 hit", Line(Record("hit", 6, [D(6, true), D(5, false)], null, 6)));
    }

    [Fact]
    public void A_roll_every_face_passes_reads_no_target()
    {
        Assert.Equal("Hit · 2 dice: ⚅ ⚀ · 2 hits", Line(Record("hit", 1, [D(6, true), D(1, true)], null, 1)));
    }

    [Fact]
    public void A_roll_in_a_deal_wounds_frame_counts_wounds_ignored_and_standing()
    {
        Assert.Equal("Regeneration · 5+ · 3 wounds: ⚀ ⚅ ⚀ · 1 ignored, 2 wounds stand",
            Line(Record("Regeneration", 5, [D(1, false), D(6, true), D(1, false)]), "deal-wounds"));
        Assert.Equal("Regeneration · 5+ · 2 wounds: ⚅ ⚁ · 1 ignored, 1 wound stands",
            Line(Record("Regeneration", 5, [D(6, true), D(2, false)]), "deal-wounds"));
    }

    [Fact]
    public void A_single_die_test_reads_passed_or_failed_then_its_consequence()
    {
        var morale = Entry(Record("morale", 4, [D(1, false)], Basis("Quality", 4)), "morale-test") with { Consequence = "Shaken" };
        Assert.Equal("Morale · Quality 4+ · ⚀ · failed → Shaken", morale.Collapsed(CombatLog.Pip));
        Assert.Equal("Morale failed → Shaken", morale.Chain);
    }

    [Fact]
    public void A_passed_casting_roll_reads_cast()
    {
        Assert.Equal("Casting · 4+ (tokens +1) → needs 3 · ⚅ · cast", Line(Record("casting", 4, [D(6, true)], null, null, new RollModifier("tokens", 1)), "casting"));
        Assert.Equal("Casting · 4+ · ⚀ · failed", Line(Record("casting", 4, [D(1, false)]), "casting"));
    }

    [Fact]
    public void Dangerous_terrain_fails_on_a_one()
    {
        Assert.Equal("Dangerous terrain · 1 fails · 2 dice: ⚁ ⚀ · 1 wound", Line(Record("dangerous-terrain", 2, [D(2, true), D(1, false)]), "move"));
    }

    [Fact]
    public void Two_rules_share_one_pair_of_brackets()
    {
        var r = Record("hit", 4, [D(6, true, "Rending", "Furious +1 hit"), D(6, true, "Rending"), D(5, true)], Basis("Quality", 4));
        Assert.EndsWith(" · 4 hits (2 sixes: Rending; 1 six: Furious +1 hit)", Line(r));
    }

    [Fact]
    public void A_roll_off_reads_each_die_after_its_team_and_the_winner_after_the_faces()
    {
        var r = Entry(Record("roll-off", null, [D(4, false), D(1, false)]), "roll-off") with { DieLabels = ["Team 1", "Team 2"], Consequence = "Team 1 wins" };
        Assert.Equal("Roll-off · Team 1 ⚃ · Team 2 ⚀ · Team 1 wins", r.Collapsed(CombatLog.Pip));
    }

    [Fact]
    public void Roll_labels()
    {
        Assert.Equal(["Hit", "Block", "Morale", "Casting", "Roll-off", "Dangerous terrain", "Regeneration"],
            new[] { "hit", "defense", "morale", "casting", "roll-off", "dangerous-terrain", "Regeneration" }.Select(RollWords.Label));
    }
}
