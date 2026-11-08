import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import type { Geometry, PlacedModel } from '../src/lib/geometry';
import { HoverDistances, hoverOf, woundsLine } from '../src/lib/play/hoverView';
import { Names } from '../src/lib/play/names';
import type { EventPayload, GameInfo, ModelRelocation, ModelSlot } from '../src/lib/protocol/wire.generated';
import { at, boardOf, ev, gameOf, newId, slotOf, unitOf } from './play';

// HoverViewTests
const TA = newId(), TB = newId(), PA = newId(), PB = newId(), PW = newId();
const slot = (ordinal: number): ModelSlot => slotOf({ name: 'Trooper', ordinal });
const unitWith = (name: string, models: number) => unitOf(name, Array.from({ length: models }, (_, i) => slot(i + 1)));
const blue = unitWith('Blue Warriors', 3), red = unitWith('Red Warriors', 3), loose = unitWith('Loose Warriors', 1);
const game: GameInfo = gameOf({ teams: [{ id: TA, ordinal: 1 }, { id: TB, ordinal: 2 }] }, [
  { id: PA, accountId: newId(), name: 'Clay', teamId: TA, armyId: newId(), armyName: 'A' },
  { id: PB, accountId: newId(), name: 'Sam', teamId: TB, armyId: newId(), armyName: 'B' },
  { id: PW, accountId: newId(), name: 'Watcher', armyId: newId(), armyName: 'W' },
]);
const board = boardOf([{ participantId: PA, units: [blue] }, { participantId: PB, units: [red] }, { participantId: PW, units: [loose] }]);

/** Circles: the base's `a` is its diameter, as Base.Circle(1) is. */
const edgeToEdge = (a: PlacedModel, b: PlacedModel) =>
  Math.hypot(a.placement.position.x - b.placement.position.x, a.placement.position.y - b.placement.position.y) - (a.body.base.a + b.body.base.a) / 2;
let nearestCalls = 0;
/** The facade's NearestPair: the first least EdgeToEdge over the pairs, by index. */
const geometry = {
  edgeToEdge,
  nearestPair: (from: readonly PlacedModel[], to: readonly PlacedModel[]) => {
    nearestCalls++;
    let best = { distance: Infinity, from: -1, to: -1 };
    from.forEach((a, i) => to.forEach((b, j) => { const d = edgeToEdge(a, b); if (d < best.distance) best = { distance: d, from: i, to: j }; }));
    return best.from < 0 ? null : best;
  },
} as unknown as Geometry;

const put = (s: ModelSlot, x: number, y: number): ModelRelocation => ({ modelId: s.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(x, y) } });

/** Blue in a row at y = 10, Red at y = 20 with its first model straight across from Blue's first: 10" centre to centre, 9" edge to edge. */
function table(...more: EventPayload[]) {
  const a = new Applier(board, game);
  const history: EventPayload[] = [{ kind: 'models_moved', moves: [
    put(blue.models[0]!, 10, 10), put(blue.models[1]!, 11.5, 10), put(blue.models[2]!, 13, 10),
    put(red.models[0]!, 10, 20), put(red.models[1]!, 20, 20), put(red.models[2]!, 21.5, 20)] }, ...more];
  history.forEach((p, i) => a.apply(ev(i + 1, p, PA)));
  return a.projection;
}
const noFacts: ReadonlyMap<string, number> = new Map();

describe('HoverView', () => {
  it("hover names the unit in its team's ordinal", () => {
    const p = table();
    const info = hoverOf(geometry, p, game, new Names(game, p), red.id, null, noFacts)!;
    expect([info.unitId, info.name, info.teamOrdinal]).toEqual([red.id, 'Red Warriors', 2]);
    expect(info.distance).toBeNull();
    expect(info.wounds).toEqual([]);
  });

  // p.5: "When measuring the distance between two units you always measure from/to the closest model in each unit".
  it('with a unit selected the distance is the closest models base edge to base edge', () => {
    const p = table();
    const info = hoverOf(geometry, p, game, new Names(game, p), red.id, blue.id, noFacts)!;
    expect(info.distance).toBeCloseTo(9.0, 6);
  });

  it('one NearestPair per pair of units and placements: the same placements ask again for nothing', () => {
    const p = table();
    const distances = new HoverDistances();
    nearestCalls = 0;
    const ask = (q: typeof p) => hoverOf(geometry, q, game, new Names(game, q), red.id, blue.id, noFacts, distances)!.distance;
    expect(ask(p)).toBeCloseTo(9.0, 6);
    expect(ask(p)).toBeCloseTo(9.0, 6);
    expect(hoverOf(geometry, p, game, new Names(game, p), blue.id, red.id, noFacts, distances)!.distance).toBeCloseTo(9.0, 6);
    expect(nearestCalls).toBe(2);                                              // red from blue, blue from red
    const moved = table({ kind: 'models_moved', moves: [{ modelId: red.models[0]!.id, from: { kind: 'table', placement: at(10, 20) }, to: { kind: 'table', placement: at(10, 15) } }] });
    expect(ask(moved)).toBeCloseTo(4.0, 6);
    expect(nearestCalls).toBe(3);
  });

  it('no distance to itself or from a unit off the table', () => {
    const p = table();
    expect(hoverOf(geometry, p, game, new Names(game, p), red.id, red.id, noFacts)!.distance).toBeNull();
    expect(hoverOf(geometry, p, game, new Names(game, p), red.id, loose.id, noFacts)!.distance).toBeNull();   // Loose is on the shelf
  });

  it('a unit with no model on the table or unknown gives nothing', () => {
    const p = table();
    expect(hoverOf(geometry, p, game, new Names(game, p), loose.id, blue.id, noFacts)).toBeNull();
    expect(hoverOf(geometry, p, game, new Names(game, p), newId(), blue.id, noFacts)).toBeNull();
  });

  it('wounds show for models that take more than one by what is left', () => {
    const p = table({ kind: 'wounds_set', modelId: red.models[0]!.id, wounds: 1 }, { kind: 'wounds_set', modelId: red.models[1]!.id, wounds: 1 });
    const facts = new Map([[red.models[0]!.id, 3], [red.models[1]!.id, 1]]);   // red.models[2] absent: 1
    const info = hoverOf(geometry, p, game, new Names(game, p), red.id, null, facts)!;
    expect(info.wounds).toEqual([{ modelId: red.models[0]!.id, left: 2, max: 3 }]);
  });

  it('a unit whose participant has no team is ordinal zero', () => {
    const p = table({ kind: 'models_moved', moves: [put(loose.models[0]!, 40, 40)] });
    expect(hoverOf(geometry, p, game, new Names(game, p), loose.id, null, noFacts)!.teamOrdinal).toBe(0);
  });

  it('the wounds are one line under the name, like ones counted', () => {
    const w = (left: number, max: number) => ({ modelId: newId(), left, max });
    expect(woundsLine([w(3, 3), w(1, 3), w(3, 3), w(2, 3)])).toBe('3/3 ×2 · 1/3 · 2/3');
    expect(woundsLine([w(2, 2)])).toBe('2/2');
    expect(woundsLine([])).toBeNull();
  });
});
