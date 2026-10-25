import { describe, expect, it } from 'vitest';
import { answeredBy, confirmLabel, modeOf, mine, spendableBy, waitingFor } from '../src/lib/play/askView';
import { Names } from '../src/lib/play/names';
import type { Ask, AskAnswer, EventPayload, LegalSet } from '../src/lib/protocol/wire.generated';
import { CONTAINER, P1, TEAM1, TEAM2, askOf, option, projectionOf, sample } from './play';

const none = new Set<string>();
const EMPTY = '00000000-0000-0000-0000-000000000000';
const SPENDER = '0198b7ac-1111-7000-8000-000000000001';

function fixture() {
  const s = sample();
  const activate = askOf({ kind: 'options', choices: [option(`${s.squad.id}:Advance`, 'Advance', [s.squad.id]), option(`${s.squad.id}:Hold`, 'Hold', [s.squad.id])] });
  const spend = askOf({ kind: 'token_spend', maxByModel: { [s.model.id]: 2, [SPENDER]: 1 } },
    { question: 'spend_tokens', teams: [TEAM1, TEAM2], sealed: true, windowKind: 'casting' });
  const endRound = askOf({ kind: 'confirmation' }, { question: 'end_round', prompt: 'End the round', teams: [TEAM1, TEAM2], allowPass: false });
  const applied = (...p: EventPayload[]) => projectionOf(s.game, s.board, p);
  return { s, activate, spend, endRound, applied };
}
const open = (ask: Ask): EventPayload => ({ kind: 'ask_opened', ask });
const answered = (ask: Ask, teamId: string, answer?: AskAnswer): EventPayload =>
  ({ kind: 'ask_answered', askId: ask.id, teamId, answer, outsideLegalSet: false });

describe('AskViewModel', () => {
  it('my ask is the one addressed to my team and others are a wait', () => {
    const f = fixture();
    const a = f.applied(open(f.activate));
    const view = mine(a.projection, f.s.game, TEAM1, none)!;
    expect([view.mode, view.autoPass]).toEqual(['choose', false]);
    expect(view.highlights).toEqual([f.s.squad.id]);
    expect(mine(a.projection, f.s.game, TEAM2, none)).toBeNull();
    expect(waitingFor(a.projection, f.s.game, TEAM2)).toBe('Team 1');
    expect(mine(a.projection, f.s.game, null, none)).toBeNull();
  });

  it('answered by names a teammate\'s live answer for my team only', () => {
    const f = fixture();
    const a = f.applied(open(f.spend), answered(f.spend, TEAM2));
    const n = new Names(f.s.game, a.projection);
    expect(answeredBy(a.log, f.spend.id, TEAM2, EMPTY, n)).toBe('Clay');
    expect(answeredBy(a.log, f.spend.id, TEAM1, EMPTY, n)).toBeNull();
    expect(answeredBy(a.log, f.spend.id, TEAM2, P1, n)).toBeNull();
    expect(answeredBy(a.log, f.spend.id, null, EMPTY, n)).toBeNull();
    const undone = f.applied(open(f.spend), answered(f.spend, TEAM2), { kind: 'undone', toSeq: 1 });
    expect(answeredBy(undone.log, f.spend.id, TEAM2, EMPTY, new Names(f.s.game, undone.projection))).toBeNull();
  });

  it('a muted window auto passes and a simultaneous spend shows who answered without what', () => {
    const f = fixture();
    const a = f.applied(open(f.spend), answered(f.spend, TEAM2));
    const view = mine(a.projection, f.s.game, TEAM1, new Set(['casting']))!;
    expect(view.autoPass).toBe(true);
    expect(view.mode).toBe('tokenSpend');
    expect(view.highlights).toEqual([f.s.model.id]);
    expect(spendableBy(a.projection, f.s.game, TEAM1, f.spend.legal as never)).toEqual(new Map([[f.s.model.id, 2]]));
    expect(view.answeredTeams).toEqual(['Team 2']);
    expect(mine(a.projection, f.s.game, TEAM2, none)).toBeNull();
    expect(modeOf({ kind: 'confirmation' })).toBe('confirm');
    const region = (containment?: object): LegalSet => ({ kind: 'move_region', modelIds: [f.s.model.id], allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [],
      stoppable: false, passesModels: false, ignoresTerrain: false, ...(containment ? { containment } : {}) } as LegalSet);
    expect(modeOf(region())).toBe('move');
    expect(modeOf(region({ enterable: [CONTAINER] }))).toBe('move');
    const embark = askOf(region({ enterable: [CONTAINER] }), { question: 'move', frameId: f.s.game.id });
    expect(mine(f.applied(open(embark)).projection, f.s.game, TEAM1, none)!.highlights).toEqual([f.s.model.id, CONTAINER]);
    const exit = askOf(region({ enterable: [], exitingId: CONTAINER, exitWithin: 6 }), { question: 'disembark' });
    expect(mine(f.applied(open(exit)).projection, f.s.game, TEAM1, none)!.highlights).toEqual([f.s.model.id, CONTAINER]);
    const gate = askOf({ kind: 'confirmation' }, { question: 'confirm', prompt: 'Continue: move', allowPass: false });
    expect([gate, f.endRound].map(confirmLabel)).toEqual(['Continue', 'End the round']);
    const both = f.applied(open(f.endRound));
    expect(mine(both.projection, f.s.game, TEAM2, none)!.mode).toBe('confirm');
    expect(waitingFor(both.projection, f.s.game, TEAM1)).toBe('Team 2');
  });

  it('the spend map keeps my own models only', () => {
    const f = fixture();
    const a = f.applied();
    const legal = { kind: 'token_spend', maxByModel: { [f.s.model.id]: 2, [f.s.rival.id]: 1, [SPENDER]: 1 } } as const;
    expect(spendableBy(a.projection, f.s.game, TEAM1, legal)).toEqual(new Map([[f.s.model.id, 2]]));
    expect(spendableBy(a.projection, f.s.game, TEAM2, legal)).toEqual(new Map([[f.s.rival.id, 1]]));
  });

  it('a move\'s highlights add the containers it offers', () => {
    const f = fixture();
    const ask = askOf({ kind: 'move_region', modelIds: [f.s.model.id], allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false,
      passesModels: false, ignoresTerrain: false, containment: { enterable: [CONTAINER, CONTAINER] } } as LegalSet, { question: 'move' });
    expect(mine(f.applied(open(ask)).projection, f.s.game, TEAM1, none)!.highlights).toEqual([f.s.model.id, CONTAINER]);
  });

  it('end_round confirms as End the round', () => {
    const f = fixture();
    expect(confirmLabel(f.endRound)).toBe('End the round');
  });

  it('a pieces ask highlights its eligible, an assignment its targets, an allocation its targets', () => {
    const f = fixture();
    const p = askOf({ kind: 'pieces', eligible: [f.s.squad.id], min: 1, max: 1 });
    const al = askOf({ kind: 'allocation', targets: [f.s.model.id], amount: 2 });
    const asg = askOf({ kind: 'assignment', items: [option('g', 'G', [f.s.model.id])], targets: [f.s.rivals.id], maxDistinctTargets: 1 });
    expect(mine(f.applied(open(p)).projection, f.s.game, TEAM1, none)!.highlights).toEqual([f.s.squad.id]);
    expect(mine(f.applied(open(al)).projection, f.s.game, TEAM1, none)!.highlights).toEqual([f.s.model.id]);
    expect(mine(f.applied(open(asg)).projection, f.s.game, TEAM1, none)!.highlights).toEqual([f.s.rivals.id]);
  });
});
