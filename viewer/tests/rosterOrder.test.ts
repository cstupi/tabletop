import { describe, expect, it } from 'vitest';
import { Names } from '../src/lib/play/names';
import { markOf, rosterOf, sorted, type RosterRow } from '../src/lib/play/rosterView';
import type { EventPayload } from '../src/lib/protocol/wire.generated';
import { P1, P2, TEAM1, at, boardOf, gameOf, newId, projectionOf, unitOf } from './play';

const row = (name: string, activated: boolean, offTable = false): RosterRow =>
  ({ unitId: newId(), name, activated, left: 1, max: 1, offTable, conditions: [], inside: null, reserve: null, acting: false, setAside: null });

describe('RosterView order (RosterView.Sorted)', () => {
  it('may act, then activated, then off the table, each by name', () => {
    expect(sorted([row('Zeta', false), row('Gone', false, true), row('Beta', true), row('alpha', false)]).map((r) => [r.name, markOf(r)])).toEqual([
      ['alpha', 'mayAct'], ['Zeta', 'mayAct'], ['Beta', 'activated'], ['Gone', 'offTable']]);
  });

  it('the active unit is acting and mine marks every team I hold', () => {
    const squad = unitOf('Squad'); const reserve = unitOf('Reserve'); const orcs = unitOf('Orcs');
    const game = gameOf();
    const board = boardOf([{ participantId: P1, units: [squad, reserve] }, { participantId: P2, units: [orcs] }]);
    const payloads: EventPayload[] = [
      { kind: 'models_moved', moves: [{ modelId: squad.models[0]!.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } }] },
      { kind: 'sequence_started', frameId: newId(), sequence: 'advance', context: { actorUnitId: squad.id, targetUnitIds: [], weaponIds: [], action: 'Advance' } },
    ];
    const p = projectionOf(game, board, payloads).projection;
    const out = rosterOf(p, game, new Names(game, p), TEAM1, new Map(), [TEAM1]);
    expect(out[0]!.rows.map((r) => [r.name, r.acting])).toEqual([['Squad', true], ['Reserve', false]]);
    expect(out.map((t) => t.mine)).toEqual([true, false]);
  });
});
