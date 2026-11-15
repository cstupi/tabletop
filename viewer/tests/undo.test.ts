import { describe, expect, it } from 'vitest';
import type { ConnectionState, SendOutcome } from '../src/lib/connection/gameConnection';
import { Applier, projectTo } from '../src/lib/live/apply';
import { build, pip } from '../src/lib/play/combatLog';
import { describe as describeEvent } from '../src/lib/play/logText';
import { Names } from '../src/lib/play/names';
import { collapsed, type RollEntry } from '../src/lib/play/rollEntry';
import { NOTHING_TO_UNDO, UndoPrediction, undoPlanOf } from '../src/lib/play/undo';
import type { Command, EventPayload } from '../src/lib/protocol/wire.generated';
import { BoardSample, D, Frame, GameSample, Model, P1, Round1, TEAM2, Unit, ctx, deploy, edit, hit, move, reroll, started, step, undone } from './logSamples';
import { P2, TEAM1, askOf, ev, option } from './play';

// UndoPlanTests. Each entry: who acted (null for the engine) and what.
type H = readonly [string | null, EventPayload];
const engine = (p: EventPayload): H => [null, p];
const me = (p: EventPayload): H => [P1, p];
const them = (p: EventPayload): H => [P2, p];
const mv = move(deploy);
const score: EventPayload = { kind: 'score_set', teamId: TEAM2, score: 1 };
const phase: EventPayload = { kind: 'phase_started', phase: 'x' };
const ask = askOf({ kind: 'options', choices: [option('a', 'Advance', [Unit])] }, { question: 'activate' });

function plan(...history: H[]) {
  const a = new Applier(BoardSample, GameSample);
  history.forEach(([actor, p], i) => a.apply(ev(i + 1, p, actor ?? undefined)));
  const n = new Names(GameSample, a.projection);
  return { plan: undoPlanOf(a.log, n), log: a.log, n };
}
const rollOf = (log: readonly import('../src/lib/protocol/wire.generated').GameEvent[], n: Names): RollEntry =>
  build(log, n).find((e): e is RollEntry => e.type === 'roll')!;

describe('UndoPlan', () => {
  it('nothing undoable is null', () => {
    expect(plan().plan).toBeNull();
    expect(plan(engine(Round1), engine({ kind: 'phase_started', phase: 'deployment' })).plan).toBeNull();
  });

  it('the last action is undone whoever took it', () => {
    const { plan: p, log, n } = plan(engine(Round1), me(mv), them(score));
    expect(p).not.toBeNull();
    expect(p!.toSeq).toBe(2);
    expect(p!.action.seq).toBe(3);
    expect(p!.action.actor).toBe(P2);
    expect(p!.tooltip).toBe('Undo: ' + describeEvent(log[2]!, n));
  });

  it('a first action undoes to zero', () => {
    const p = plan(me(mv), engine(phase)).plan!;
    expect(p.toSeq).toBe(0);
    expect(p.action.seq).toBe(1);
  });

  it('engine follow-ups after an action are inside the range', () => {
    const p = plan(engine(Round1), me(mv), engine(phase), engine({ kind: 'turn_started', teamId: TEAM1 })).plan!;
    expect(p.toSeq).toBe(1);
    expect(p.action.seq).toBe(2);
  });

  // Undo, then undo again: the marker is never the action, the undone action is skipped, and the target is a live seq.
  it('each press steps back one more action', () => {
    const p = plan(engine(Round1), me(mv), engine(phase), them(score), me(undone(3))).plan!;
    expect(p.toSeq).toBe(1);
    expect(p.action.seq).toBe(2);
    expect(plan(engine(Round1), me(mv), engine(phase), them(score), me(undone(3)), me(undone(1))).plan).toBeNull();
  });

  // A pass on a window is not the action Undo steps back to: a muted window is passed again at once, so the press would do nothing.
  it('a pass on a window is not an action and goes with the one before', () => {
    const window = askOf({ kind: 'options', choices: [option('cast', 'Cast a spell', [Unit])] }, { question: 'window', windowKind: 'before-attacking' });
    const passed = { kind: 'passed' as const };
    const p = plan(engine(Round1), me(mv), engine({ kind: 'ask_opened', ask: window }),
      me({ kind: 'ask_answered', askId: window.id, teamId: TEAM1, answer: passed, outsideLegalSet: false }),
      me({ kind: 'ask_closed', askId: window.id, answers: [{ teamId: TEAM1, answer: passed }] }), engine(phase)).plan!;
    expect([p.toSeq, p.action.seq]).toEqual([1, 2]);                                // the move, not the pass after it
    const chosen = { kind: 'chosen' as const, optionId: 'cast' };
    const cast = plan(engine(Round1), me(mv), engine({ kind: 'ask_opened', ask: window }),
      me({ kind: 'ask_answered', askId: window.id, teamId: TEAM1, answer: chosen, outsideLegalSet: false })).plan!;
    expect([cast.toSeq, cast.action.seq]).toEqual([3, 4]);
    const pass = plan(engine(Round1), me(mv), engine({ kind: 'ask_opened', ask }),
      me({ kind: 'ask_answered', askId: ask.id, teamId: TEAM1, answer: passed, outsideLegalSet: false })).plan!;
    expect([pass.toSeq, pass.action.seq]).toEqual([3, 4]);                          // a pass on an Ask that is no window is an action
  });

  // The server stamps an Ask's close with the participant whose answer closed it; the answer is the action, so the Ask reopens unanswered.
  it('an answer and the close it caused are one action', () => {
    const answer = { kind: 'chosen' as const, optionId: 'rush' };
    const p = plan(engine(Round1), engine({ kind: 'ask_opened', ask }),
      me({ kind: 'ask_answered', askId: ask.id, teamId: TEAM1, answer, outsideLegalSet: false }),
      me({ kind: 'ask_closed', askId: ask.id, answers: [{ teamId: TEAM1, answer }] }), engine(phase)).plan!;
    expect(p.toSeq).toBe(2);
    expect(p.action.seq).toBe(3);
  });

  it('a range with no rolls loses none', () => {
    expect(plan(engine(Round1), me(mv), engine(phase)).plan!.lostRolls).toEqual([]);
  });

  it("a roll in the range is listed in the log's collapsed words", () => {
    const { plan: p, log, n } = plan(engine(Round1), me(mv), engine(started(Frame, 'x', ctx(Unit))), engine(step(Frame, 'roll-to-hit', [hit()])));
    expect(p!.lostRolls).toEqual([collapsed(rollOf(log, n), pip)]);
  });

  it('a reroll or a dice edit in the range counts', () => {
    const before: EventPayload[] = [Round1, started(Frame, 'x', ctx(Unit)), step(Frame, 'roll-to-hit', [hit()])];
    const one = plan(...before.map(engine), me(reroll(1, D(2, 2, false), D(5, 5, true))));
    expect(one.plan!.toSeq).toBe(3);
    expect(one.plan!.lostRolls).toEqual([collapsed(rollOf(one.log, one.n), pip)]);      // the line already ends with its live amendments
    expect(one.plan!.lostRolls[0]!.endsWith(' · rerolled ⚁ → ⚄')).toBe(true);

    const two = plan(...before.map(engine), me(edit(0, D(6, 6, true), D(1, 1, false))));
    expect(two.plan!.lostRolls).toEqual([collapsed(rollOf(two.log, two.n), pip)]);
    expect(two.plan!.lostRolls[0]!.endsWith(' · set by hand ⚅ → ⚀')).toBe(true);
  });

  it('a roll and its reroll both in the range are listed once', () => {
    const { plan: p, log, n } = plan(engine(Round1), me(mv), engine(started(Frame, 'x', ctx(Unit))),
      engine(step(Frame, 'roll-to-hit', [hit()])), engine(reroll(1, D(2, 2, false), D(5, 5, true))));
    expect(p!.toSeq).toBe(1);
    expect(p!.lostRolls).toEqual([collapsed(rollOf(log, n), pip)]);
  });

  it('an undone roll before the target is not lost again', () => {
    const p = plan(engine(Round1), me(mv), engine(started(Frame, 'x', ctx(Unit))), engine(step(Frame, 'roll-to-hit', [hit()])),
      me(undone(2)), them(score)).plan!;
    expect(p.toSeq).toBe(2);
    expect(p.lostRolls).toEqual([]);
  });
});

// Session.UndoToAsync's prediction.
function stateOf(...history: H[]): ConnectionState {
  const a = new Applier(BoardSample, GameSample);
  history.forEach(([actor, p], i) => a.apply(ev(i + 1, p, actor ?? undefined)));
  return { phase: 'connected', game: GameSample, participantId: P1, board: BoardSample, log: a.log, projection: a.projection } as unknown as ConnectionState;
}
const ok = (id = 'm1'): SendOutcome => ({ id, refused: null });

describe('UndoPrediction', () => {
  it('the prediction shows projectTo until the matching undone lands', async () => {
    const state = stateOf(engine(Round1), me(mv), me({ kind: 'score_set', teamId: TEAM1, score: 3 }));
    const p = new UndoPrediction();
    let sent: Command | null = null;
    expect(p.projection(state)).toBe(state.projection);
    await p.undoTo(state, 2, async (c) => { sent = c; return ok(); });
    expect(sent).toEqual({ kind: 'undo', toSeq: 2 });
    expect(p.pendingUndoTo).toBe(2);
    const shown = p.projection(state)!;
    expect(shown).not.toBe(state.projection);
    expect(shown.scores.get(TEAM1)).toBe(0);
    expect(shown.lastSeq).toBe(projectTo(state.board!, state.game!, state.log, 2).lastSeq);
    p.events([ev(9, { kind: 'score_set', teamId: TEAM1, score: 1 })]);
    expect(p.pendingUndoTo).toBe(2);
    p.events([ev(10, undone(1))]);                       // another undone does not end it
    expect(p.pendingUndoTo).toBe(2);
    p.events([ev(11, undone(2))]);
    expect(p.pendingUndoTo).toBeNull();
    expect(p.projection(state)).toBe(state.projection);
  });

  it('a refused or failed undo drops the prediction', async () => {
    const state = stateOf(engine(Round1), me(mv));
    const refused = new UndoPrediction();
    const outcome: SendOutcome = { id: 'm2', refused: { kind: 'refused', messageId: 'm2', reason: 'illegal', detail: 'no' } as never };
    await refused.undoTo(state, 1, async () => outcome);
    expect(refused.pendingUndoTo).toBeNull();
    expect(refused.projection(state)).toBe(state.projection);

    const late = new UndoPrediction();
    await late.undoTo(state, 1, async () => ok('m3'));
    expect(late.pendingUndoTo).toBe(1);
    late.refused('other');
    expect(late.pendingUndoTo).toBe(1);
    late.refused('m3');                                   // a Refused that arrives after the send settled
    expect(late.pendingUndoTo).toBeNull();

    const failed = new UndoPrediction();
    await expect(failed.undoTo(state, 1, async () => { throw new Error('Disconnected'); })).rejects.toThrow('Disconnected');
    expect(failed.pendingUndoTo).toBeNull();
  });

  it("a watcher's state never predicts", async () => {
    const state = { ...stateOf(engine(Round1), me(mv)), board: null, projection: null } as ConnectionState;
    const p = new UndoPrediction();
    let calls = 0;
    await p.undoTo(state, 1, async () => { calls++; return ok(); });
    expect(calls).toBe(0);
    expect(p.pendingUndoTo).toBeNull();
    expect(p.projection(state)).toBeNull();
  });

  it('subscribers hear each change', async () => {
    const state = stateOf(engine(Round1), me(mv));
    const p = new UndoPrediction();
    let heard = 0;
    const stop = p.subscribe(() => { heard++; });
    await p.undoTo(state, 1, async () => ok());
    expect(heard).toBeGreaterThan(0);
    const before = heard;
    p.events([ev(5, undone(1))]);
    expect(heard).toBe(before + 1);
    stop();
    expect(NOTHING_TO_UNDO).toBe('Nothing to undo');
  });
});
