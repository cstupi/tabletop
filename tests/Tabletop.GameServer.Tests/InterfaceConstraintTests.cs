using System.Reflection;
using Tabletop.GameServer.Dice;
using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.GameServer.Rules;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.GameServer.Tests.Infrastructure;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Action sequences → What the interface must not assume. Each test names the constraint it pins; 5 (granularity) and 8 (interrupts) are pinned by shape in Tasks 8 and 9.</summary>
public class InterfaceConstraintTests
{
    static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    static IEnumerable<string> GenericSources() => new[] { "Interpreter", "Effects", "Dice", "Engine", "Properties" }
        .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, "server/game", d), "*.cs", SearchOption.AllDirectories));

    [Fact]
    public void Constraint_1_roll_names_are_rows_and_the_generic_half_names_none()
    {
        Assert.Equal(4, TestProperties.Core.Rolls.Count);
        foreach (var file in GenericSources())
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "\"hit\"", "\"defense\"", "\"morale\"", "\"casting\"", "\"Quality\"", "\"Defense\"", "\"Hold\"", "\"Advance\"", "\"Rush\"", "\"Charge\"", "\"fatigued\"", "\"shaken\"", "Regeneration", "Tough", "Transport" })
                Assert.False(text.Contains(forbidden), $"{Path.GetFileName(file)} names {forbidden}");
        }
        // The two weapon stat names StatReader needs are the one exception, whitelisted (Task 7).
        Assert.Contains("\"Range\"", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Effects/StatReader.cs")));
    }

    [Fact]
    public void Constraints_2_3_4_threshold_is_a_function_and_convention_and_cap_are_per_roll()
    {
        Assert.IsAssignableFrom<Delegate>(typeof(RollSpec).GetProperty(nameof(RollSpec.Threshold))!.GetValue(GrimdarkFutureRules.Instance.RollSpec(GfNames.Hit)));
        var specs = TestProperties.Core.Rolls.Select(r => GrimdarkFutureRules.Instance.RollSpec(r.Name)).ToList();
        Assert.All(specs, s => Assert.Equal(new RollConvention(6, null), s.Convention));
        var capped = new RollPlan("x", 4, [new RollModifier("a", 2), new RollModifier("b", 1)], new RollConvention(6, 1), null, new HashSet<int>());
        Assert.Equal(1, capped.NetModifier);                                                     // the cap is the convention's, applied after summing
        Assert.Equal(3, (capped with { Convention = new RollConvention(6, null) }).NetModifier);
        Assert.DoesNotContain(typeof(Roller).GetMethods(), m => m.Name.Contains("Six") || m.Name.Contains("D6"));
    }

    [Fact]
    public void Constraint_6_deal_wounds_reads_a_capacity_not_a_rule_name_outside_the_ruleset()
    {
        var generic = string.Join("\n", GenericSources().Select(File.ReadAllText));
        Assert.DoesNotContain("Tough", generic);
        Assert.Contains("ModelParameter(ctx.State, modelId, \"Tough\") ?? 1", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Rules/GrimdarkFuture/GfAttack.cs")));
    }

    [Fact]
    public void Constraint_7_morale_is_a_sequence_with_ruleset_owned_triggers()
    {
        Assert.Equal(GfNames.MoraleTest, GrimdarkFutureRules.Instance.Sequence(GfNames.MoraleTest).Name);
        Assert.DoesNotContain(GrimdarkFutureRules.Instance.Sequence(GfNames.AttackSequence).Steps, s => s.Name.Contains("morale"));
        Assert.DoesNotContain(GrimdarkFutureRules.Instance.Sequence(GfNames.ResolveHits).Steps, s => s.Name.Contains("morale"));
        Assert.Contains(typeof(IGameRules).GetMethods(), m => m.Name == nameof(IGameRules.EndOfTurnSequences));
    }

    [Fact]
    public void Constraint_9_an_ask_names_its_team_and_the_interpreter_never_assumes_the_actor()
    {
        Assert.Equal(typeof(IReadOnlyList<Guid>), typeof(Ask).GetProperty(nameof(Ask.Teams))!.PropertyType);
        var interpreter = File.ReadAllText(Path.Combine(RepoRoot, "server/game/Interpreter/SequenceInterpreter.cs"));
        Assert.DoesNotContain("Defender", interpreter); Assert.DoesNotContain("Attacker", interpreter);
        Assert.Contains("Guid team", File.ReadAllText(Path.Combine(RepoRoot, "server/game/Interpreter/StepContext.cs")));   // Ask(..., Guid team, ...)
    }
}
