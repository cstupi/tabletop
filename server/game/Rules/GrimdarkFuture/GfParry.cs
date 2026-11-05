using Tabletop.GameServer.Effects;
using Tabletop.GameServer.Interpreter;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Rules.GrimdarkFuture;

/// <summary>
/// Parry (Army Forge's common rules): "When this unit takes hits in melee, roll X dice per model with this rule in it, up to as many dice as hits taken.
/// For each 6+ one hit is ignored (defender picks which)." The first step of resolve-hits under a melee (an attack-sequence the melee pushed, and Impact's
/// hits). roll-to-block reads the hits less the parry's successes as its dice now stand (Parried), taken from the hits with the most per-die grants.
/// </summary>
public static class GfParry
{
    public sealed class ParryStep : IStep
    {
        public string Name => "parry";
        public StepOutcome Run(StepContext ctx)
        {
            var parent = ctx.Frame.Parent;
            if (parent is null || !InMelee(parent) || ctx.Frame.Context.ActorUnitId is not { } attacker) return new Complete([]);
            var hits = GfAttack.HitsFrom(parent);
            var dice = Math.Min(DiceCount(ctx, attacker, ctx.Frame.Context.TargetUnitIds[0]), Math.Max(0, hits));
            if (dice == 0) return new Complete([]);
            ctx.Applied.Add(GfNames.Parry);
            var record = ctx.Roll(new Dice.RollPlan(GfNames.Parry, 6, [], ctx.Rules.RollSpec(GfNames.Hit).Convention, null, new HashSet<int>()), dice);
            return new Complete([], new Dictionary<string, int> { ["parried"] = Dice.RollOutcome.Successes(record) });
        }

        /// <summary>A resolve-hits frame is the melee's when the melee pushed it (Impact) or pushed the attack-sequence that did.</summary>
        static bool InMelee(FrameView parent) =>
            parent.Frame.Sequence == GfNames.Melee || (parent.Frame.Sequence == GfNames.AttackSequence && parent.Parent?.Frame.Sequence == GfNames.Melee);

        /// <summary>"X dice per model with this rule in it": each living model's highest Parry(X) among its melee weapons, as the live set leaves it.</summary>
        static int DiceCount(StepContext ctx, Guid attacker, Guid defender)
        {
            var c = ctx.Frame.Context;
            var weapons = GfMelee.MeleeWeapons(ctx.State, defender);
            LiveSet Live(IEnumerable<Guid> models, IEnumerable<Guid> held) => LiveProperties.Settle(GfAttack.AttackInteraction(ctx, attacker, defender, c.WeaponIds, AttackKind.Melee, c.Action)
                with { Defender = Party.Of(ctx.State, defender, models, held) });
            var models = Lookup.LivingModels(ctx.State, defender);
            if (Live(models, weapons).Parameter(Side.Defender, GfNames.Parry) is null) return 0;
            return models.Sum(m =>
            {
                var own = ctx.Projection.Models[m].Slot.Weapons.Select(w => w.Id).Where(weapons.Contains).ToList();
                return Live([m], own).On(Side.Defender).Where(p => p.Entry.Name == GfNames.Parry).Select(p => Math.Max(0, p.Parameter ?? 0)).DefaultIfEmpty(0).Max();
            });
        }
    }

    /// <summary>The hits the frame's parry ignored, read off its dice as they now stand; 0 when it rolled none.</summary>
    public static int Parried(FrameView frame) =>
        frame.Frame.Steps.Where(s => s.Step == "parry").SelectMany(s => s.Rolls).Where(r => r.Roll == GfNames.Parry).Sum(Dice.RollOutcome.Successes);
}
