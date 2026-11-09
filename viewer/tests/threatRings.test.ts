import { describe, expect, it, vi } from 'vitest';
import type { Geometry, PlacedModel, Segment } from '../src/lib/geometry';
import { acting, teamsOf } from '../src/lib/lobby/seats';
import { AllThreatCache, ThreatRingCache, allThreatsOf, ofAnotherTeam, pinnedThreat, shownThreats, threatRingsOf } from '../src/lib/play/threatRings';
import type { EventPayload, UnitThreat, Weapon } from '../src/lib/protocol/wire.generated';
import { P1, P2, askOf, at, boardOf, gameOf, projectionOf, slotOf, unitOf, weaponOf } from './play';

const gun = (name: string, range?: string): Weapon => ({ ...weaponOf(name), stats: range === undefined ? {} : { Range: range } });
const squad = unitOf('Squad', [
  slotOf({ name: 'A', ordinal: 1, weapons: [gun('Rifle', '24"')] }),
  slotOf({ name: 'B', ordinal: 2, weapons: [gun('Rifle', '24"')] }),
  slotOf({ name: 'C', ordinal: 3, weapons: [gun('Pistol', '12"')] }),
]);
const orcs = unitOf('Orcs', [slotOf({ name: 'Orc', weapons: [gun('Choppa')] })]);
const game = gameOf();
const board = boardOf([{ participantId: P1, units: [squad] }, { participantId: P2, units: [orcs] }]);
const place = (id: string, x: number, y: number): EventPayload => ({ kind: 'models_moved', moves: [{ modelId: id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(x, y) } }] });
const deploy = [place(squad.models[0]!.id, 10, 10), place(squad.models[1]!.id, 12, 10), place(squad.models[2]!.id, 14, 10), place(orcs.models[0]!.id, 40, 30)];
const squadThreat = (charge?: number, shootMove?: number): UnitThreat =>
  ({ charge, shootMove, ranges: { [squad.models[0]!.id]: 24, [squad.models[1]!.id]: 24, [squad.models[2]!.id]: 12 } });

function fake() {
  const calls: { models: readonly PlacedModel[]; range: number; ranges?: readonly number[] }[] = [];
  const rangeOutline = vi.fn((models: readonly PlacedModel[], range: number, ranges?: readonly number[]): Segment[] => {
    calls.push(ranges === undefined ? { models, range } : { models, range, ranges });
    return [{ a: { x: range, y: 0 }, b: { x: range, y: 1 } }];
  });
  return { g: { rangeOutline } as unknown as Geometry, calls, rangeOutline };
}

describe('ThreatRings', () => {
  it('a charge ring round every model and a shoot ring per reach round its models', () => {
    const { g, calls } = fake();
    const p = projectionOf(game, board, deploy).projection;
    const rings = threatRingsOf(g, p, squad.id, squadThreat(12, 6));
    expect(rings.map((r) => [r.inches, r.label])).toEqual([[12, 'Charge 12"'], [18, 'Shoot 18"'], [30, 'Shoot 30"']]);
    expect(calls.map((c) => [c.range, c.models.map((m) => m.placement.position.x)])).toEqual([[12, [10, 12, 14]], [30, [10, 12]], [18, [14]]]);
  });

  it('no charge and no shot draw nothing; a unit without ranges draws only its charge', () => {
    const { g } = fake();
    const p = projectionOf(game, board, deploy).projection;
    expect(threatRingsOf(g, p, squad.id, squadThreat())).toEqual([]);
    expect(threatRingsOf(g, p, squad.id, squadThreat(undefined, 0)).map((r) => r.label)).toEqual(['Shoot 12"', 'Shoot 24"']);
    expect(threatRingsOf(g, p, orcs.id, { charge: 12, shootMove: 6, ranges: {} }).map((r) => r.label)).toEqual(['Charge 12"']);
  });

  it('the cache recomputes only when the unit, its placements or its threat change', () => {
    const { g, rangeOutline } = fake();
    const a = projectionOf(game, board, deploy);
    const cache = new ThreatRingCache();
    const first = cache.of(g, a.projection, squad.id, squadThreat(12, 6));
    expect(cache.of(g, a.projection, squad.id, squadThreat(12, 6))).toBe(first);
    const slower = cache.of(g, a.projection, squad.id, squadThreat(8, 4));
    expect(slower).not.toBe(first);
    expect(slower[0]!.inches).toBe(8);
    const calls = rangeOutline.mock.calls.length;
    a.apply({ seq: 99, actor: P1, payload: { kind: 'models_moved', moves: [{ modelId: squad.models[2]!.id, from: { kind: 'table', placement: at(14, 10) }, to: { kind: 'table', placement: at(20, 10) } }] } } as never);
    expect(cache.of(g, a.projection, squad.id, squadThreat(8, 4))).not.toBe(slower);
    expect(rangeOutline.mock.calls.length).toBeGreaterThan(calls);
  });

  const team1 = game.participants.find((x) => x.id === P1)!.teamId!, team2 = game.participants.find((x) => x.id === P2)!.teamId!;
  const orcThreat: UnitThreat = { charge: 16, shootMove: 6, ranges: {} };
  const both = new Map<string, UnitThreat>([[squad.id, squadThreat(12, 6)], [orcs.id, orcThreat]]);

  it('another team is one other than the team acted for, and a watcher sees every unit\'s', () => {
    const p = projectionOf(game, board, deploy).projection;
    expect([ofAnotherTeam(game, p, team1, orcs.id), ofAnotherTeam(game, p, team1, squad.id), ofAnotherTeam(game, p, team2, squad.id),
      ofAnotherTeam(game, p, team2, orcs.id), ofAnotherTeam(game, p, null, squad.id), ofAnotherTeam(game, p, team1, 'nope')])
      .toEqual([true, false, true, false, true, false]);
  });

  it('the selected unit and the hovered one both show, each once, only of another team with a threat', () => {
    const p = projectionOf(game, board, deploy).projection;
    expect(shownThreats(game, p, team1, orcs.id, null, both)).toEqual([orcs.id]);
    expect(shownThreats(game, p, team1, orcs.id, orcs.id, both)).toEqual([orcs.id]);
    expect(shownThreats(game, p, team1, squad.id, null, both)).toEqual([]);
    expect(shownThreats(game, p, team1, orcs.id, null, new Map())).toEqual([]);
    expect(shownThreats(game, p, null, squad.id, orcs.id, both)).toEqual([squad.id, orcs.id]);
    expect(shownThreats(game, p, team2, orcs.id, squad.id, both)).toEqual([squad.id]);          // solo, acting for team 2
  });

  it('the selected enemy stays pinned while a unit of mine or terrain is selected; an empty selection clears it', () => {
    const p = projectionOf(game, board, deploy).projection;
    const pinned = pinnedThreat(game, p, team1, orcs.id, null);
    expect(pinned).toBe(orcs.id);
    expect(pinnedThreat(game, p, team1, squad.id, pinned)).toBe(orcs.id);           // a unit of mine, picked to place or move
    expect(pinnedThreat(game, p, team1, 'terrain', pinned)).toBe(orcs.id);
    expect(pinnedThreat(game, p, team1, null, pinned)).toBeNull();
    expect(pinnedThreat(game, p, team1, squad.id, null)).toBeNull();
  });

  it('while one account places a unit in a deploy ask, the other team\'s threat draws', () => {
    const reserve = unitOf('Reserve', [slotOf({ name: 'Trooper', weapons: [gun('Rifle', '24"')] })]);
    const solo = gameOf({}, game.participants.map((x) => ({ ...x, accountId: 'solo' })));
    const withReserve = boardOf([{ participantId: P1, units: [squad, reserve] }, { participantId: P2, units: [orcs] }]);
    const ask = askOf({ kind: 'pieces', eligible: [reserve.id], min: 1, max: 1 }, { question: 'deploy', teams: [team1], allowPass: false });
    const p = projectionOf(solo, withReserve, [...deploy, { kind: 'ask_opened', ask }]).projection;
    const mine = acting(p, teamsOf(solo, P1));
    expect(mine).toBe(team1);
    const pinned = pinnedThreat(solo, p, mine, reserve.id, pinnedThreat(solo, p, mine, orcs.id, null));      // the unit being placed selected
    expect(shownThreats(solo, p, mine, pinned, null, both)).toEqual([orcs.id]);
    expect(allThreatsOf(fake().g, solo, p, mine, both).map((t) => t.teamId)).toEqual([team2]);
  });

  it('all is per enemy team one charge area and one shoot area, each the union with a range per model', () => {
    const { g, calls } = fake();
    const p = projectionOf(game, board, deploy).projection;
    const seen = allThreatsOf(g, game, p, team2, both);
    expect(seen.map((t) => [t.teamId, t.ordinal, t.rings.map((r) => [r.kind, r.label])])).toEqual([[team1, 1, [['charge', 'Charge 12"'], ['shoot', 'Shoot 18–30"']]]]);
    expect(calls.map((c) => c.ranges)).toEqual([[12, 12, 12], [30, 30, 18]]);
    expect(allThreatsOf(g, game, p, team1, both).map((t) => t.rings.map((r) => r.label))).toEqual([['Charge 16"']]);
    expect(allThreatsOf(g, game, p, null, both).length).toBe(2);
    expect(allThreatsOf(g, game, p, team1, new Map([[orcs.id, { ranges: {} }]]))).toEqual([]);
  });

  it('the all cache recomputes only when the team acted for, a unit, its placements or its threat change', () => {
    const { g } = fake();
    const a = projectionOf(game, board, deploy);
    const cache = new AllThreatCache();
    const first = cache.of(g, game, a.projection, team2, both);
    expect(cache.of(g, game, a.projection, team2, new Map(both))).toBe(first);
    expect(cache.of(g, game, a.projection, team1, both)).not.toBe(first);
    const back = cache.of(g, game, a.projection, team2, both);
    expect(cache.of(g, game, a.projection, team2, new Map([[squad.id, squadThreat(8, 4)], [orcs.id, orcThreat]]))).not.toBe(back);
    const now = cache.of(g, game, a.projection, team2, both);
    a.apply({ seq: 99, actor: P1, payload: { kind: 'models_moved', moves: [{ modelId: squad.models[2]!.id, from: { kind: 'table', placement: at(14, 10) }, to: { kind: 'table', placement: at(20, 10) } }] } } as never);
    expect(cache.of(g, game, a.projection, team2, both)).not.toBe(now);
  });
});
