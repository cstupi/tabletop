import { describe, expect, it, vi } from 'vitest';
import { Names } from '../src/lib/play/names';
import { SightCheck, type SightBox } from '../src/lib/play/sightCheck';
import type { EventPayload, SightAnswer } from '../src/lib/protocol/wire.generated';
import { ASK_ID, FRAME, P1, P2, TEAM1, askOf, at, boardOf, gameOf, newId, option, projectionOf, slotOf, unitOf } from './play';

const squad = unitOf('Squad', [slotOf({ name: 'S1' }), slotOf({ name: 'S2', ordinal: 2 })]);
const orcs = unitOf('Orcs');
const game = gameOf();
const place = (id: string, x: number, y: number): EventPayload => ({ kind: 'models_moved', moves: [{ modelId: id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(x, y) } }] });
const deploy = [place(squad.models[0]!.id, 10, 10), place(squad.models[1]!.id, 12, 10), place(orcs.models[0]!.id, 40, 30)];
const board = boardOf([{ participantId: P1, units: [squad] }, { participantId: P2, units: [orcs] }]);
const assign = askOf({ kind: 'assignment', items: [option('Rifle', 'Rifle', [])], targets: [orcs.id], maxDistinctTargets: 2 } as never,
  { id: ASK_ID, question: 'declare_targets', teams: [TEAM1], frameId: FRAME });
const open: EventPayload[] = [
  { kind: 'sequence_started', frameId: FRAME, sequence: 'shooting', context: { actorUnitId: squad.id, targetUnitIds: [], weaponIds: [] } },
  { kind: 'ask_opened', ask: assign },
];
const at1 = (more: EventPayload[] = []) => projectionOf(game, board, [...deploy, ...more]).projection;
const names = (p: ReturnType<typeof at1>) => new Names(game, p);
const answer = (clear: boolean, from = squad.models[0]!.id, to = orcs.models[0]!.id, blockedBy: string[] = []): SightAnswer =>
  ({ kind: 'sight_answer', queryId: newId(), lines: [{ fromModelId: from, toModelId: to, clear, screened: false, blockedBy }], canShoot: clear ? [from] : [] });
const tick = () => new Promise((r) => setTimeout(r, 0));
const none: SightBox = { targets: null, renderedAsk: null };
const picking = (assigned: Map<string, string> = new Map()): SightBox => ({ targets: { groups: [option('Rifle', 'Rifle', [])], assigned }, renderedAsk: ASK_ID });

function rig(reply: (o: string, t: string) => Promise<SightAnswer> = () => Promise.resolve(answer(true))) {
  const query = vi.fn(reply);
  return { query, check: new SightCheck(query) };
}

describe('SightCheck', () => {
  it('under a target pick the shooter observes the hovered unit', () => {
    const { query, check } = rig();
    const p = at1(open);
    check.update(p, game, picking(), orcs.id, null, false);
    expect(query).toHaveBeenCalledWith(squad.id, orcs.id);
  });

  it('with L held the selected unit observes', () => {
    const { query, check } = rig();
    const p = at1();
    check.update(p, game, none, orcs.id, squad.id, false);
    expect(query).not.toHaveBeenCalled();
    check.update(p, game, none, orcs.id, squad.id, true);
    expect(query).toHaveBeenCalledWith(squad.id, orcs.id);
  });

  it('nothing is asked of a unit about itself or with no observer', () => {
    const { query, check } = rig();
    const p = at1();
    check.update(p, game, none, squad.id, squad.id, true);
    check.update(p, game, none, orcs.id, null, true);
    check.update(p, game, none, null, squad.id, true);
    expect(query).not.toHaveBeenCalled();
  });

  it('only the newest hovered answer is drawn', async () => {
    const pending: ((a: SightAnswer) => void)[] = [];
    const { check } = rig(() => new Promise((resolve) => { pending.push(resolve); }));
    const p = at1();
    check.update(p, game, none, orcs.id, squad.id, true);
    check.update(p, game, none, null, squad.id, true);
    check.update(p, game, none, orcs.id, squad.id, true);
    expect(pending).toHaveLength(2);
    pending[1]!(answer(false));
    pending[0]!(answer(true));
    await tick();
    expect(check.view(p, game, names(p), false).lines.map((l) => l.tone)).toEqual(['blocked']);
  });

  it('an assigned target is asked again only after its key changed', async () => {
    const { query, check } = rig();
    const p = at1(open);
    const assigned = new Map([['Rifle', orcs.id]]);
    check.update(p, game, picking(assigned), null, null, false);
    check.update(p, game, picking(assigned), null, null, false);
    expect(query).toHaveBeenCalledTimes(1);
    await tick();
    const moved = at1([...open, { kind: 'models_moved', moves: [{ modelId: squad.models[0]!.id, from: { kind: 'table', placement: at(10, 10) }, to: { kind: 'table', placement: at(11, 10) } }] }]);
    check.update(moved, game, picking(assigned), null, null, false);
    expect(query).toHaveBeenCalledTimes(2);
    check.update(moved, game, picking(), null, null, false);          // unassigned: forgotten, asked afresh when assigned again
    check.update(moved, game, picking(assigned), null, null, false);
    expect(query).toHaveBeenCalledTimes(3);
  });

  it('a failed query is asked again next time', async () => {
    let fail = true;
    const { query, check } = rig(() => (fail ? Promise.reject(new Error('refused')) : Promise.resolve(answer(true))));
    const p = at1();
    check.update(p, game, none, orcs.id, squad.id, true);
    await tick();
    fail = false;
    check.update(p, game, none, orcs.id, squad.id, true);
    expect(query).toHaveBeenCalledTimes(2);
  });

  it('the view colours lines by clear, screened and blocked', async () => {
    const [a, b] = squad.models.map((m) => m.id) as [string, string];
    const o = orcs.models[0]!.id;
    const reply: SightAnswer = {
      kind: 'sight_answer', queryId: newId(), canShoot: [a],
      lines: [
        { fromModelId: a, toModelId: o, clear: true, screened: false, blockedBy: [] },
        { fromModelId: b, toModelId: o, clear: true, screened: true, blockedBy: [] },
        { fromModelId: b, toModelId: o, clear: false, screened: false, blockedBy: ['wall'] },
      ],
    };
    const { check } = rig(() => Promise.resolve(reply));
    const p = at1(open);
    check.update(p, game, picking(), orcs.id, null, false);
    await tick();
    const v = check.view(p, game, names(p), true);
    expect(v.lines.map((l) => l.tone)).toEqual(['clear', 'screened', 'blocked']);
    expect(v.blockers).toEqual(['wall']);
    expect(v.summary?.text).toBe('2 of 2 models can see Orcs · 1 of 2 clear lines screened · 1 can shoot');
    const orc = p.models.get(o)!.location as { placement: { position: { y: number } } };
    expect(v.summary?.at.y).toBe(orc.placement.position.y - 2);                       // under the target, clear of the hover label over it
    expect(v.dimmed).toEqual(new Set([b]));
  });
});
