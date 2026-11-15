using System.Text.Json.Nodes;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public static partial class CallCases
{
    static readonly string[] DeployQuestions = [AskQuestions.Deploy, AskQuestions.DeployScouts, AskQuestions.DeployAmbusher, AskQuestions.DeployReinforcement];

    static JsonNode Ids(IEnumerable<Guid> ids) => new JsonArray(ids.Select(i => (JsonNode?)JsonValue.Create(i.ToString())).ToArray());
    static string S(Guid id) => id.ToString();

    /// <summary>The answer cases: for each team in each AskClosed, the state before that team answered and the choice the logged answer was.</summary>
    static IEnumerable<CallCase> Asks()
    {
        var counts = new Dictionary<string, int>();
        foreach (var name in LogNames)
        {
            var log = Log(name);
            var opened = log.Events.Where(e => e.Payload is AskOpened).ToDictionary(e => ((AskOpened)e.Payload).Ask.Id, e => (AskOpened)e.Payload);
            foreach (var closed in log.Events.Where(e => e.Payload is AskClosed))
            {
                var close = (AskClosed)closed.Payload;
                var ask = opened[close.AskId].Ask;
                foreach (var ta in close.Answers)
                {
                    var answered = log.Events.FirstOrDefault(e => e.Payload is AskAnswered a && a.AskId == close.AskId && a.TeamId == ta.TeamId);
                    var upTo = (answered?.Seq ?? closed.Seq) - 1;
                    var player = PlayerOf(log.Game, ta.TeamId);
                    var probe = CallState.Playing(log, upTo, player);
                    if (probe.Mine?.Ask.Id != ask.Id) continue;
                    bool Take(string kind) => (counts[$"{name}|{ask.Question}|{kind}"] = counts.GetValueOrDefault($"{name}|{ask.Question}|{kind}") + 1) <= 2;
                    foreach (var c in AskCases(log, ask, close, ta, upTo, player, Take)) yield return c;
                }
            }
        }
        foreach (var c in AutoPass(counts)) yield return c;
        foreach (var c in Passes()) yield return c;
    }

    static IEnumerable<CallCase> AskCases(CallLog log, Ask ask, AskClosed close, TeamAnswer ta, long upTo, Guid player, Func<string, bool> take)
    {
        var selfPlay = log.Name == "calls/selfplay";
        CallCase Make(string kind, JsonObject choice, Func<CallState, JsonNode?> build, Action<CallState, JsonObject>? decide = null,
            IReadOnlyDictionary<Guid, int>? maxWounds = null, Func<CallState, AskAnswer?>? answerOf = null, string suffix = "")
        {
            var c = Playing(selfPlay ? "selfplay/answer" : kind, log, upTo, player, choice, build, decide,
                suffix: ((selfPlay ? kind + " " : "") + suffix + (close.Answers.Count > 1 ? " team" + log.Game.Teams.First(t => t.Id == ta.TeamId).Ordinal : "")).Trim(), maxWounds: maxWounds);
            if (answerOf is not null) c.RebuildsLogged = Same(answerOf(c.State), ta.Answer);
            return c;
        }
        JsonNode Send(CallState s, AskAnswer a) => Command(AskAnswers.Answer(ask, a, s.Acting));
        JsonNode? SendOrNull(CallState s, AskAnswer? a) => a is null ? null : Send(s, a);

        switch (ask.Legal, ta.Answer)
        {
            case (Options o, Chosen ch) when ask.Question == AskQuestions.GameOver:
                foreach (var option in o.Choices.OrderBy(c => c.Id == ch.OptionId ? 0 : 1))
                    if (take("gameOver"))
                        yield return Make("gameOver", Choice("gameOver", ("optionId", option.Id)),
                            s => Command(GameOverView.Answer(GameOverView.Of(s.P!, s.Game, s.Acting), option.Id, s.Acting)),
                            answerOf: option.Id == ch.OptionId ? _ => new Chosen(option.Id) : null, suffix: option.Id);
                break;

            case (Options o, Chosen ch):
                var picked = o.Choices.First(c => c.Id == ch.OptionId);
                if (take("choose/option"))
                    yield return Make("choose/option", Choice("option", ("optionId", ch.OptionId)), s => Send(s, new Chosen(ch.OptionId)), answerOf: _ => ch);
                var numbered = (CallState s) => DecisionView.Of(s.P!, s.Game, s.Names, s.Acting, s.MutedSet).Numbered;
                if (Reachable(numbered(CallState.Playing(log, upTo, player)), ch.OptionId) is { } entry && take("choose/key"))
                    yield return Make("choose/key", Choice("key", ("key", entry.Key)), s => Send(s, new Chosen(ch.OptionId)),
                        (s, d) => d["numbered"] = NumberedOf(numbered(s)), answerOf: _ => ch);
                var shared = o.Choices.Count(c => c.Pieces.Intersect(picked.Pieces).Any()) > 1;
                if (shared && picked.Pieces.Count > 0)
                {
                    var state = CallState.Playing(log, upTo, player);
                    var unit = state.P!.Units.ContainsKey(picked.Pieces[0]) ? picked.Pieces[0] : state.P.Models[picked.Pieces[0]].UnitId;
                    var here = DecisionView.ChoicesOf(o, state.P, unit);
                    var narrowed = DecisionView.Number(here);
                    if (Reachable(narrowed, ch.OptionId) is not null && take("choose/unitKey"))
                        yield return Make("choose/unitKey", Choice("unitKey", ("unitId", S(unit)), ("key", narrowed.First(n => n.OptionId == ch.OptionId).Key)),
                            s => Send(s, new Chosen(ch.OptionId)), (s, d) => d["numbered"] = NumberedOf(DecisionView.Number(DecisionView.ChoicesOf(o, s.P!, unit))),
                            answerOf: _ => ch);
                    if (take("choose/clickNone"))
                        yield return Make("choose/clickNone", Choice("click", ("pieceId", S(picked.Pieces[0]))),
                            s => AskAnswers.ChosenByClick(o, s.P!, picked.Pieces[0], ask.Question) is { } hit ? Send(s, hit) : null);
                }
                else if (picked.Pieces.FirstOrDefault(pc => AskAnswers.ChosenByClick(o, CallState.Playing(log, upTo, player).P!, pc, ask.Question)?.OptionId == ch.OptionId) is var piece && piece != Guid.Empty
                    && take("choose/click"))
                    yield return Make("choose/click", Choice("click", ("pieceId", S(piece))),
                        s => AskAnswers.ChosenByClick(o, s.P!, piece, ask.Question) is { } hit ? Send(s, hit) : null, answerOf: _ => ch);
                break;

            case (_, Passed) when ask.AllowPass:
                if (take("pass")) yield return Make("pass", Choice("pass"), s => Send(s, new Passed()), answerOf: _ => new Passed());
                break;

            case (Confirmation, Confirmed):
                var endRound = ask.Question == AskQuestions.EndRound;
                if (take(endRound ? "confirm/endRound" : "confirm"))
                    yield return Make(endRound ? "confirm/endRound" : "confirm", Choice("confirm"), s => Send(s, new Confirmed()), answerOf: _ => new Confirmed());
                break;

            case (TokenSpend, TokensSpent spent):
                if (take("spend"))
                    yield return Make("spend", Choice("spend", ("values", J(spent.Counts))), s => Send(s, AskAnswers.Spent(spent.Counts)), answerOf: _ => AskAnswers.Spent(spent.Counts));
                break;

            case (PieceSelection sel, SelectedPieces chosen):
                var deploy = DeployQuestions.Contains(ask.Question);
                var kind = !deploy ? "pick" : ask.Question == AskQuestions.DeployAmbusher ? "pick/ambusher" : "pick/deploy";
                AskAnswer? Pick(CallState s, bool done)
                {
                    var eligible = deploy ? DeployView.Pickable(s.P!, ask) : sel.Eligible;
                    var pick = new PiecePick(sel, eligible);
                    foreach (var id in chosen.PieceIds) pick.Toggle(id);
                    return pick.AnswersAtOnce || done ? pick.Answer() : null;
                }
                var done = !(sel.Max == 1 && chosen.PieceIds.Count == 1);
                if (take(kind))
                    yield return Make(kind, Choice("pick", ("clicks", Ids(chosen.PieceIds)), ("done", done)), s => SendOrNull(s, Pick(s, done)),
                        (s, d) => d["pickable"] = Ids(DeployView.Pickable(s.P!, ask)), answerOf: s => Pick(s, done));
                if (deploy && chosen.PieceIds.Count == 1
                    && DecisionView.Of(CallState.Playing(log, upTo, player).P!, log.Game, CallState.Playing(log, upTo, player).Names, CallState.Playing(log, upTo, player).Acting, new HashSet<string>())
                        .Numbered is var keyed && Reachable(keyed, chosen.PieceIds[0].ToString()) is { } key && take("pickKey"))
                    yield return Make("pickKey", Choice("pickKey", ("key", key.Key)), s => SendOrNull(s, Pick(s, false)),
                        (s, d) => { d["numbered"] = NumberedOf(DecisionView.Of(s.P!, s.Game, s.Names, s.Acting, s.MutedSet).Numbered); d["pickable"] = Ids(DeployView.Pickable(s.P!, ask)); },
                        answerOf: s => Pick(s, false));
                break;

            case (Allocation alloc, Allocated allocated):
                var wounds = allocated.Counts.ToDictionary(kv => kv.Key, kv => CallState.Playing(log, upTo, player).P!.Models[kv.Key].Wounds + kv.Value);
                AskAnswer? Casualties(CallState s, IEnumerable<(string, Guid)> steps, IReadOnlyDictionary<Guid, int> max)
                {
                    var left = alloc.Targets.ToDictionary(id => id, id => Math.Max(0, max.GetValueOrDefault(id, 1) - (s.P!.Models.GetValueOrDefault(id)?.Wounds ?? 0)));
                    var pick = new CasualtyPick(alloc, left);
                    foreach (var (step, id) in steps)
                        switch (step) { case "click": pick.Click(id); break; case "remove": pick.Remove(id); break; case "auto": pick.Auto(); break; case "clear": pick.Clear(); break; }
                    return pick.Answer();
                }
                var clicks = allocated.Counts.SelectMany(kv => Enumerable.Repeat(("click", kv.Key), kv.Value)).ToList();
                JsonArray Steps(IEnumerable<(string, Guid)> steps) => new(steps.Select(x => (JsonNode?)(x.Item1 == "click" || x.Item1 == "remove"
                    ? Obj((x.Item1, S(x.Item2))) : Obj((x.Item1, true)))).ToArray());
                if (take("casualties"))
                    yield return Make("casualties", Choice("casualties", ("steps", Steps(clicks))), s => SendOrNull(s, Casualties(s, clicks, wounds)),
                        maxWounds: wounds, answerOf: s => Casualties(s, clicks, wounds));
                var variant = new List<(string, Guid)> { ("click", allocated.Counts.Keys.First()), ("remove", allocated.Counts.Keys.First()), ("clear", Guid.Empty), ("auto", Guid.Empty) };
                if (take("casualties"))
                    yield return Make("casualties", Choice("casualties", ("steps", Steps(variant))), s => SendOrNull(s, Casualties(s, variant, wounds)), maxWounds: wounds,
                        suffix: "auto");
                break;

            case (Assignment legal, Assigned assigned):
                var steps2 = new List<(string, string)>();
                var groups = legal.Items.Select(i => i.Id).ToList();
                if (assigned.Targets.Count == groups.Count && assigned.Targets.Values.Distinct().Count() == 1) steps2.Add(("aim", S(assigned.Targets.Values.First())));
                else foreach (var (group, target) in assigned.Targets) { steps2.Add(("pick", group)); steps2.Add(("aim", S(target))); }
                AskAnswer? Assign(CallState s)
                {
                    var t = new TargetAssignment(legal);
                    foreach (var (step, v) in steps2)
                        switch (step) { case "pick": t.PickOnly(v); break; case "aim": t.Aim(Guid.Parse(v)); break; case "clear": t.Clear(); break; }
                    return t.Answer();
                }
                if (take("assign"))
                    yield return Make("assign", Choice("assign", ("steps", new JsonArray(steps2.Select(x => (JsonNode?)Obj((x.Item1, x.Item1 == "pick" || x.Item1 == "aim" ? x.Item2 : "true"))).ToArray()))),
                        s => SendOrNull(s, Assign(s)), answerOf: Assign);
                break;

            case (MoveRegion region, Destinations dest):
                foreach (var c in MoveCases(log, ask, region, dest, upTo, player, take, Make)) yield return c;
                break;
        }
    }

    delegate CallCase Maker(string kind, JsonObject choice, Func<CallState, JsonNode?> build, Action<CallState, JsonObject>? decide = null,
        IReadOnlyDictionary<Guid, int>? maxWounds = null, Func<CallState, AskAnswer?>? answerOf = null, string suffix = "");

    static IEnumerable<CallCase> MoveCases(CallLog log, Ask ask, MoveRegion region, Destinations dest, long upTo, Guid player, Func<string, bool> take,
        Maker make)
    {
        var state = CallState.Playing(log, upTo, player);
        var p = state.P!;
        JsonNode Send(CallState s, AskAnswer a) => Command(AskAnswers.Answer(ask, a, s.Acting));

        if (dest.Moves.Count > 0 && dest.Moves.All(m => m.To is Inside))
        {
            var container = ((Inside)dest.Moves[0].To).ContainerId;
            if (take("embark"))
                yield return make("embark", Choice("embark", ("containerId", S(container))), s => Send(s, AskAnswers.Embark(s.P!, region, container)), null, null,
                    s => AskAnswers.Embark(s.P!, region, container));
            yield break;
        }
        var disembark = region.Containment?.ExitingId is not null;
        var logged = dest.Moves.Where(m => m.To is OnTable).ToDictionary(m => m.ModelId, m => ((OnTable)m.To).Placement);
        var ghosts = region.ModelIds.ToDictionary(id => id, id => logged.TryGetValue(id, out var pl) ? pl : ((OnTable)p.Models[id].Location).Placement);
        ExitSite? Exit(CallState s) => !disembark ? null
            : new ExitSite(region.Containment!.ExitWithin ?? 0, s.Log.Board.Table.Terrain.SelectMany(t => t.WorldVolumes()).ToList(), s.Log.Board.Table.Size);
        AskAnswer Move(CallState s, bool stop) => AskAnswers.Move(s.P!, region, ghosts.ToDictionary(kv => kv.Key, kv => kv.Value.Position),
            ghosts.ToDictionary(kv => kv.Key, kv => kv.Value.Yaw), stop, Exit(s));
        JsonObject Choose(bool stop) => Choice("move", ("ghosts", J(ghosts)), ("stop", stop));
        var kind = disembark ? "move/disembark" : "move";
        if (take(kind))
            yield return make(kind, Choose(dest.Continues), s => Send(s, Move(s, dest.Continues)), null, null, s => Move(s, dest.Continues));
        if (disembark && take("move/disembark/turned"))
        {
            // A ghost the player turned: Godot answers the facing that fits at the drop (p.7), never the ghost's.
            var turned = ghosts.ToDictionary(kv => kv.Key, kv => kv.Value with { Yaw = new Yaw(135) });
            AskAnswer Turned(CallState s) => AskAnswers.Move(s.P!, region, turned.ToDictionary(kv => kv.Key, kv => kv.Value.Position),
                turned.ToDictionary(kv => kv.Key, kv => kv.Value.Yaw), false, Exit(s));
            yield return make("move/disembark", Choice("move", ("ghosts", J(turned)), ("stop", false)), s => Send(s, Turned(s)), null, null, null, "turned");
        }
        if (region.Stoppable && !disembark && take("move/stop"))
            yield return make("move/stop", Choose(true), s => Send(s, Move(s, true)), null, null, null);
    }

    /// <summary>A muted window kind answers itself with Pass: one case per window kind that has an Ask the log opened.</summary>
    static IEnumerable<CallCase> AutoPass(Dictionary<string, int> counts)
    {
        foreach (var name in LogNames)
        {
            var log = Log(name);
            foreach (var e in log.Events.Where(e => e.Payload is AskOpened { Ask: { WindowKind: not null, AllowPass: true } }).Take(1))
            {
                var ask = ((AskOpened)e.Payload).Ask;
                var player = PlayerOf(log.Game, ask.Teams[0]);
                if (CallState.Playing(log, e.Seq, player, [ask.WindowKind!]).Mine?.Ask.Id != ask.Id) continue;
                var c = Playing("autoPass", log, e.Seq, player, Choice("autoPass"),
                    s => Command(AskAnswers.Answer(ask, new Passed(), s.Acting)), muted: [ask.WindowKind!]);
                yield return c;
            }
        }
    }

    /// <summary>The numbered entry for this option, when its key reaches it: entries past nine all carry key 0 and the key picks the first.</summary>
    static (int Key, string Label, string OptionId)? Reachable(IEnumerable<(int Key, string Label, string OptionId)> numbered, string optionId)
    {
        var list = numbered.ToList();
        return list.FirstOrDefault(n => n.OptionId == optionId) is { OptionId: not null } e && list.First(n => n.Key == e.Key).OptionId == optionId ? e : null;
    }

    /// <summary>Equal answers; a disembarking model's facing is not compared (Godot answers the exit facing, not the logged one).</summary>
    static bool Same(AskAnswer? a, AskAnswer b)
    {
        if (a is Destinations x && b is Destinations y && y.Moves.Any(m => m.From is Inside))
            return x.Continues == y.Continues && x.Moves.Count == y.Moves.Count && x.Moves.Zip(y.Moves).All(z =>
                z.First.ModelId == z.Second.ModelId && z.First.From == z.Second.From
                && z.First.To is OnTable t1 && z.Second.To is OnTable t2 && t1.Placement.Position == t2.Placement.Position);
        if (a is TokensSpent && b is TokensSpent z) b = AskAnswers.Spent(z.Counts);   // an answer's zero counts say nothing
        return a is not null && AnswerJson(a) == AnswerJson(b);
    }

    /// <summary>Pass: the first Ask each log opens that allows it, whatever the log answered.</summary>
    static IEnumerable<CallCase> Passes()
    {
        foreach (var name in LogNames)
        {
            var log = Log(name);
            foreach (var e in log.Events.Where(e => e.Payload is AskOpened { Ask.AllowPass: true }).Take(1))
            {
                var ask = ((AskOpened)e.Payload).Ask;
                var player = PlayerOf(log.Game, ask.Teams[0]);
                if (CallState.Playing(log, e.Seq, player).Mine?.Ask.Id != ask.Id) continue;
                yield return Playing("pass", log, e.Seq, player, Choice("pass"), s => Command(AskAnswers.Answer(ask, new Passed(), s.Acting)), suffix: "open");
            }
        }
    }
    static string AnswerJson(AskAnswer a) => WireJson.Serialize<AskAnswer>(a);
}
