import { describe, expect, it } from 'vitest';
import { Names } from '../src/lib/play/names';
import { pips, rosterOf, woundsText, words, type RosterRow } from '../src/lib/play/rosterView';
import type { EventPayload, Location } from '../src/lib/protocol/wire.generated';
import { P1, P2, TEAM1, TEAM2, at, boardOf, gameOf, projectionOf, slotOf, unitOf } from './play';

const table = (id: string, x: number, y: number, from: Location = { kind: 'shelf' }): EventPayload =>
  ({ kind: 'models_moved', moves: [{ modelId: id, from, to: { kind: 'table', placement: at(x, y) } }] });

function rig() {
  const squad = unitOf('Squad', [slotOf({ name: 'M', ordinal: 1 }), slotOf({ name: 'M', ordinal: 2 }), slotOf({ name: 'M', ordinal: 3 })]);
  const reserve = unitOf('Reserve');
  const orcs = unitOf('Orcs');
  const game = gameOf();
  const board = boardOf([{ participantId: P1, units: [squad, reserve] }, { participantId: P2, units: [orcs] }]);
  const rows = (payloads: EventPayload[], my: string | null, max = new Map<string, number>()) => {
    const p = projectionOf(game, board, payloads).projection;
    return rosterOf(p, game, new Names(game, p), my, max);
  };
  return { squad, reserve, orcs, game, board, rows };
}

describe('RosterView', () => {
  it('my team first with activation, summed wounds, conditions and off table units', () => {
    const r = rig();
    const [a, b, c] = r.squad.models.map((m) => m.id);
    const out = r.rows([
      table(a!, 10, 10), table(b!, 12, 10), table(r.orcs.models[0]!.id, 40, 30),
      { kind: 'action_recorded', unitId: r.squad.id, action: 'Advance' }, { kind: 'wounds_set', modelId: a!, wounds: 1 },
      { kind: 'condition_set', subject: { kind: 'unit', id: r.squad.id }, condition: 'shaken', present: true },
      { kind: 'condition_set', subject: { kind: 'unit', id: r.squad.id }, condition: 'moved this round', present: true },
      { kind: 'wounds_set', modelId: c!, wounds: 1 },
    ], TEAM2, new Map([[a!, 3], [b!, 3]]));
    expect(out.map((t) => t.name)).toEqual(['Team 2', 'Team 1']);
    const squad = out[1]!.rows[0]!;
    // 2 + 3 + a removed model's 0 (the third is on the shelf with its one wound taken); the bookkeeping moved this round is left out
    expect([squad.name, squad.activated, squad.left, squad.max, squad.offTable, squad.conditions]).toEqual(['Squad', true, 5, 7, false, ['shaken']]);
    expect(out[1]!.rows[1]).toMatchObject({ name: 'Reserve', offTable: true });
    expect(out[0]!.rows[0]).toMatchObject({ left: 1, max: 1, activated: false });
    expect([pips(2, 3), words('shaken')]).toEqual(['●●○', 'Shaken']);
    expect(words('set-aside')).toBe('Set aside');
  });

  it('the roster marks a passenger and dims it while its Transport is off the table', () => {
    const r = rig();
    const hull = r.reserve.models[0]!.id;
    const inside: EventPayload = { kind: 'models_moved', moves: r.squad.models.map((m) => ({ modelId: m.id, from: { kind: 'shelf' }, to: { kind: 'inside', containerId: hull } })) };
    const row = (extra: EventPayload[]) => r.rows([inside, ...extra], TEAM1)[0]!.rows.find((x) => x.name === 'Squad')!;
    expect([row([]).inside, row([]).offTable]).toEqual(['Reserve', true]);
    const deployed = row([table(hull, 10, 10)]);
    expect([deployed.inside, deployed.offTable]).toEqual(['Reserve', false]);
    expect(r.rows([], TEAM1)[0]!.rows[0]!.inside).toBeNull();
  });

  it('wounds read as pips up to twelve, else as a count', () => {
    const row = (left: number, max: number): RosterRow => ({ unitId: 'u', name: 'U', activated: false, left, max, offTable: false, conditions: [], inside: null, reserve: null, acting: false });
    expect(woundsText(row(2, 3))).toBe('●●○');
    expect(woundsText(row(12, 12))).toBe('●●●●●●●●●●●●');
    expect(woundsText(row(9, 13))).toBe('9/13');
  });
});
