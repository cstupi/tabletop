using Tabletop.Content;
using Tabletop.GameServer.Rules.GrimdarkFuture;
using Tabletop.Protocol;
using Tabletop.Sim.Answers;
using Tabletop.Sim.Suite;

namespace Tabletop.Sim.Lifecycle;

/// <summary>What a whole game did, read from the log the server sent: the line a whole-list shape prints.</summary>
public sealed record WholeGameRecord(int Rounds, int ShootingAttacks, int MeleeAttacks, int MoraleTests, int Wounds, int Casualties,
    int UnitsDestroyed, int ReinforcementCopies, int ReinforcementsPlaced, IReadOnlyDictionary<string, int> AsksByQuestion)
{
    public override string ToString() =>
        $"rounds {Rounds}, attacks {ShootingAttacks + MeleeAttacks} (shooting {ShootingAttacks}, melee {MeleeAttacks}), wounds {Wounds}, casualties {Casualties}, " +
        $"units destroyed {UnitsDestroyed}, reinforcement copies {ReinforcementCopies}, placed {ReinforcementsPlaced}, morale tests {MoraleTests}; asks answered: " +
        string.Join(", ", AsksByQuestion.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"));
}

/// <summary>
/// The checks a whole-list game takes after play, each null or the reason, over the log and the board's army snapshots alone: every unit came on,
/// the game was a game (shooting, melee, morale, four rounds, an end), nothing was played as a warned breach, and Reinforcement's copies came at the
/// one round its text gives and on their originals' bodies. Nothing here computes a rule's outcome.
/// </summary>
public static class WholeGame
{
    /// <summary>The round each event of the log was emitted in (0 before round one).</summary>
    public static IReadOnlyDictionary<long, int> Rounds(IReadOnlyList<GameEvent> log)
    {
        var round = 0;
        var rounds = new Dictionary<long, int>();
        foreach (var e in log)
        {
            if (e.Payload is RoundStarted r) round = r.Round;
            rounds[e.Seq] = round;
        }
        return rounds;
    }

    static HashSet<Guid> LeftTheShelf(LogQueries log) =>
        log.Moves.Where(m => m.Move is { From: OnShelf, To: not OnShelf }).Select(m => m.Move.ModelId).ToHashSet();

    /// <summary>
    /// Every unit that is not a reserve copy came on: each of its models left the shelf at some point (a casualty returns to it), a set-aside
    /// unit by deploy_scouts before round one or deploy_ambusher from round two on (round one with Rapid Ambush), every other unit by deploy.
    /// </summary>
    public static IReadOnlyList<string> Deployed(LogQueries log, IReadOnlyList<ArmyInPlay> armies, IReadOnlySet<Guid> setAside)
    {
        var failures = new List<string>();
        var placed = LeftTheShelf(log);
        var rounds = Rounds(log.Events);
        foreach (var army in armies)
            foreach (var unit in army.Snapshot.Units.Where(u => u.Reserve is null))
            {
                var on = unit.Models.Count(m => placed.Contains(m.Id));
                if (on < unit.Models.Count) failures.Add($"{army.Snapshot.Name}'s {unit.Name} never came on: {on} of {unit.Models.Count} models left the shelf");
                var by = log.Deployments.Where(d => d.Unit == unit.Id).Select(d => (d.Question, Round: rounds.GetValueOrDefault(d.Seq))).ToList();
                var lawful = setAside.Contains(unit.Id)
                    ? by.Count > 0 && by.All(d => d is (AskQuestions.DeployScouts, 0) || d.Question == AskQuestions.DeployAmbusher && d.Round >= (RapidAmbush(unit) ? 1 : 2))
                    : by.Count > 0 && by.All(d => d is (AskQuestions.Deploy, 0));
                if (!lawful)
                    failures.Add($"{army.Snapshot.Name}'s {unit.Name}{(setAside.Contains(unit.Id) ? " (set aside)" : "")} was picked by " +
                        (by.Count == 0 ? "no deploy Ask" : string.Join(", ", by.Select(d => $"{d.Question} in round {d.Round}"))));
            }
        return failures;
    }

    /// <summary>Page 13 Ambush deploys "at the start of any round after the first"; an army book's Rapid Ambush "at the start of any round, including the first".</summary>
    static bool RapidAmbush(Unit unit) =>
        unit.Properties.Any(p => p.Name == "Rapid Ambush") || unit.Models.Any(m => m.Properties.Any(p => p.Name == "Rapid Ambush"));

    /// <summary>A reserve copy's original: the unit its reserve names, or the unit holding the slot it names.</summary>
    static Unit? OriginalOf(ArmySnapshot army, Unit copy) =>
        copy.Reserve is not { } r ? null : army.Units.FirstOrDefault(u => u.Id == r.Of) ?? army.Units.FirstOrDefault(u => u.Models.Any(m => m.Id == r.Of));

    static IEnumerable<(ArmySnapshot Army, Unit Copy, Unit Original)> Copies(IReadOnlyList<ArmyInPlay> armies) =>
        from a in armies
        from u in a.Snapshot.Units
        where u.Reserve is { Property: GfReserves.Reinforcement } && OriginalOf(a.Snapshot, u) is not null
        select (a.Snapshot, u, OriginalOf(a.Snapshot, u)!);

    /// <summary>
    /// 3.6.0 Reinforcement: "At the beginning of the next round after a unit where all models have this rule is destroyed, after Ambushers have
    /// been deployed, you may place a new copy of it". An original destroyed (its last model off the table) in round r of 1..3 has its copy offered
    /// by deploy_reinforcement in round r + 1 and in no other round; one not destroyed before round 4 never has it offered. A copy placed came off
    /// the shelf by that Ask.
    /// </summary>
    public static IReadOnlyList<string> ReinforcementTiming(LogQueries log, IReadOnlyList<ArmyInPlay> armies)
    {
        var failures = new List<string>();
        var rounds = Rounds(log.Events);
        foreach (var (army, copy, original) in Copies(armies))
        {
            var models = original.Models.Select(m => m.Id).ToHashSet();
            var onTable = new HashSet<Guid>();
            long? destroyed = null;
            foreach (var (seq, move) in log.Moves.Where(m => models.Contains(m.Move.ModelId)))
            {
                var before = onTable.Count;
                if (move.To is OnTable) onTable.Add(move.ModelId); else onTable.Remove(move.ModelId);
                if (before > 0 && onTable.Count == 0) destroyed = seq;
                if (onTable.Count > 0) destroyed = null;
            }
            var offered = log.AsksOf(AskQuestions.DeployReinforcement).Where(a => a.Ask.Legal is PieceSelection p && p.Eligible.Contains(copy.Id))
                .Select(a => rounds.GetValueOrDefault(a.Seq)).Distinct().Order().ToList();
            var at = destroyed is { } d ? rounds.GetValueOrDefault(d) : (int?)null;
            var name = $"{army.Name}'s {original.Name}";
            if (at is >= 1 and <= 3)
            {
                if (!offered.SequenceEqual([at.Value + 1]))
                    failures.Add($"{name}, destroyed in round {at}, had its copy offered in round(s) [{string.Join(", ", offered)}], not round {at + 1} alone");
            }
            else if (offered.Count > 0)
                failures.Add($"{name}, {(at is null ? "not destroyed" : $"destroyed in round {at}")}, had its copy offered in round(s) [{string.Join(", ", offered)}]");
            var arrivals = log.Deployments.Where(x => x.Unit == copy.Id).ToList();
            if (copy.Models.Any(m => LeftTheShelf(log).Contains(m.Id)) && (arrivals.Count == 0 || arrivals.Any(x => x.Question != AskQuestions.DeployReinforcement)))
                failures.Add($"{name}'s copy came on by {(arrivals.Count == 0 ? "no deploy Ask" : string.Join(", ", arrivals.Select(x => x.Question)))}");
        }
        return failures;
    }

    /// <summary>The game server bodies a copy's slots from its original's, slot for slot (Army.WithCopiesBodied): mesh, base and height.</summary>
    public static IReadOnlyList<string> CopiesBodied(IReadOnlyList<ArmyInPlay> armies)
    {
        var failures = new List<string>();
        foreach (var (army, copy, original) in Copies(armies))
        {
            if (copy.Models.Count != original.Models.Count) { failures.Add($"{army.Name}'s {original.Name} copy holds {copy.Models.Count} models, the original {original.Models.Count}"); continue; }
            for (var i = 0; i < copy.Models.Count; i++)
                if (copy.Models[i] is var c && original.Models[i] is var o && (c.AssetId != o.AssetId || c.Base != o.Base || Math.Abs(c.Height - o.Height) > 1e-9))
                    failures.Add($"{army.Name}'s {original.Name} copy slot {i + 1} stands on {c.Base} h{c.Height} mesh {c.AssetId?.ToString() ?? "none"}, the original's on {o.Base} h{o.Height} mesh {o.AssetId?.ToString() ?? "none"}");
        }
        return failures;
    }

    /// <summary>A warned breach (warn enforcement accepting a command the rules forbid): the script played against a rule.</summary>
    public static IReadOnlyList<string> Warnings(IReadOnlyList<GameEvent> log) =>
        log.Where(e => e.Warning is not null).Select(e => $"seq {e.Seq} {e.Payload.GetType().Name} was accepted with the warning: {e.Warning}").ToList();

    public static WholeGameRecord Record(LogQueries log, IReadOnlyList<ArmyInPlay> armies, IReadOnlyList<AnsweredAsk> answered)
    {
        var attacks = log.FramesOf(GfNames.AttackSequence).ToList();
        var placed = LeftTheShelf(log);
        var units = armies.SelectMany(a => a.Snapshot.Units).ToList();
        return new WholeGameRecord(
            Rounds: log.Events.Select(e => e.Payload).OfType<RoundStarted>().Select(r => r.Round).DefaultIfEmpty(0).Max(),
            ShootingAttacks: attacks.Count(f => f.Nearest(GfNames.Shooting, GfNames.Melee)?.Sequence == GfNames.Shooting),
            MeleeAttacks: attacks.Count(f => f.Nearest(GfNames.Shooting, GfNames.Melee)?.Sequence == GfNames.Melee),
            MoraleTests: log.FramesOf(GfNames.MoraleTest).Count(),
            Wounds: log.Rolls(GfNames.DefenseRoll).Sum(r => r.Dice.Count(d => !d.Success)),
            Casualties: log.ModelsRemoved.Count,
            UnitsDestroyed: units.Count(u => u.Models.Any(m => placed.Contains(m.Id)) && u.Models.All(m => log.Final.Models[m.Id].Location is OnShelf)),
            ReinforcementCopies: Copies(armies).Count(),
            ReinforcementsPlaced: log.Deployments.Count(d => d.Question == AskQuestions.DeployReinforcement),
            AsksByQuestion: answered.GroupBy(a => a.Question).ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>The game was a game: four rounds and an end, at least one shooting attack, one melee attack and one morale test.</summary>
    public static IReadOnlyList<string> Played(LogQueries log, WholeGameRecord record)
    {
        var failures = new List<string>();
        if (record.Rounds != 4) failures.Add($"the game reached round {record.Rounds}, not 4");
        if (!log.Events.Any(e => e.Payload is GameEnded)) failures.Add("the log holds no game_ended");
        if (record.ShootingAttacks == 0) failures.Add("no shooting attack was made");
        if (record.MeleeAttacks == 0) failures.Add("no melee attack was made");
        if (record.MoraleTests == 0) failures.Add("no morale test was taken");
        return failures;
    }
}
