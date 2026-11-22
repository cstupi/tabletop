import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import { sentence } from '../src/lib/play/contactCheck';
import { KeepAwayCache, keepAwayBreaches, keepAwayFor, keepAwayLabel, keepAwayRingOf, withKeepAway } from '../src/lib/play/keepAwayArea';
import type { KeepAway } from '../src/lib/protocol/wire.generated';
import { P1, TEAM1, askOf, at, boardOf, gameOf, projectionOf, unitOf } from './play';
import { loadTestGeometry } from './geometry';

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

// Godot's KeepAwayAreaTests: an enemy on a 1" base at (40, 30); the unit being placed on the shelf.
const enemy = unitOf('Orcs'), mine = unitOf('Reserve'), beacon = unitOf('Beacon');
const nine: KeepAway = { units: [{ unitId: enemy.id, over: 9 }] };
const slot = (u: typeof enemy) => u.models[0]!;
const table = (x = 40, y = 30, beaconAt?: [number, number]) => projectionOf(gameOf(), boardOf([{ participantId: P1, units: [enemy, mine, beacon] }]), [
  { kind: 'models_moved', moves: [
    { modelId: slot(enemy).id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(x, y) } },
    ...(beaconAt === undefined ? [] : [{ modelId: slot(beacon).id, from: { kind: 'shelf' } as const, to: { kind: 'table' as const, placement: at(...beaconAt) } }]),
  ] },
]).projection;
const base = slot(enemy).base.a / 2;

describe('keepAwayArea through the WebAssembly', () => {
  it('the area is each listed unit\'s bases grown by its distance', () => {
    const ring = keepAwayRingOf(g, table(), nine)!;
    expect(ring.kind).toBe('keepAway');
    expect(ring.label).toBe('Too close 9"');
    for (const s of ring.segments) expect(Math.hypot(s.a.x - 40, s.a.y - 30)).toBeCloseTo(9 + base, 2);
  });

  it('units kept at different distances read the rule\'s own, the shortest', () => expect(keepAwayLabel([9, 12, 9])).toBe('Too close 9"'));

  it('no listed model on the table draws nothing', () => {
    const p = projectionOf(gameOf(), boardOf([{ participantId: P1, units: [enemy, mine] }])).projection;
    expect(keepAwayRingOf(g, p, nine)).toBeNull();
  });

  it('the cache keeps the ring until the enemy moves', () => {
    const cache = new KeepAwayCache();
    const first = cache.of(g, table(), nine);
    expect(cache.of(g, table(), nine)).toBe(first);
    const moved = cache.of(g, table(50, 30), nine)!;
    expect(moved).not.toBe(first);
    for (const s of moved.segments) expect(Math.hypot(s.a.x - 50, s.a.y - 30)).toBeCloseTo(9 + base, 2);
  });

  it('a ghost within the distance is marked and one over it is not', () => {
    const p = table();
    const gap = 9 + 2 * base;
    const near = keepAwayBreaches(g, p, nine, new Map([[slot(mine).id, at(40, 30 - gap + 1)]]), () => 'Orcs');
    expect(near.get(slot(mine).id)).toBe('placed within 9" of Orcs');
    expect(sentence(near.get(slot(mine).id)!, 'M1')).toBe('M1 would be placed within 9" of Orcs');
    expect(keepAwayBreaches(g, p, nine, new Map([[slot(mine).id, at(40, 30 - gap - 0.1)]]), () => 'Orcs').size).toBe(0);
    const contacts = new Map([[slot(mine).id, 'overlaps a base']]);
    expect(withKeepAway(contacts, g, p, nine, new Map([[slot(mine).id, at(40, 25)]]), () => 'Orcs').get(slot(mine).id)).toBe('overlaps a base');
  });

  it('a lifting model clears a ghost within its reach and cuts the area', () => {
    const p = table(40, 30, [40, 18]);
    const keep: KeepAway = { ...nine, ignoredNear: [slot(beacon).id], ignoredWithin: 6 };
    expect(keepAwayBreaches(g, p, keep, new Map([[slot(mine).id, at(40, 23)]]), () => 'Orcs').size).toBe(0);
    expect(keepAwayRingOf(g, p, keep)!.segments.length).not.toBe(keepAwayRingOf(g, p, nine)!.segments.length);
  });

  it('the keep-away is the deploy pick\'s for its unit, else the open move Ask\'s', () => {
    const mid = { unitId: mine.id, question: 'deploy_ambusher', shelvedModelIds: [slot(mine).id], keepAway: nine };
    expect(keepAwayFor(mid, null)).toBe(nine);
    const move = askOf({ kind: 'move_region', modelIds: [slot(mine).id], allowance: 9, impassableVolumeIds: [], difficultVolumeIds: [], keepAway: nine } as never,
      { question: 'move', prompt: 'Vanguard', teams: [TEAM1], allowPass: true });
    expect(keepAwayFor(null, move)).toBe(nine);
    expect(keepAwayFor(null, null)).toBeNull();
  });
});
