import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry, Placement } from '../src/lib/geometry';
import { coherencyOf, coherencyWarnings, NO_COHERENCY, withCoherency } from '../src/lib/play/coherencyView';
import { shortWords } from '../src/lib/play/markLabels';
import { terrainHoverShown } from '../src/lib/play/terrainLabel';
import { P1, at, boardOf, gameOf, projectionOf, slotOf, unitOf } from './play';
import { loadTestGeometry } from './geometry';

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

// Godot's CoherencyViewTests: Squad's three 1" bases at (10..14, 10), 1" apart edge to edge; p.7's 1" and 9", p.11's 3".
const opr = { chain: 1, elevated: 3, spread: 9 };
const squad = unitOf('Squad', [slotOf({ name: 'M1' }), slotOf({ name: 'M2' }), slotOf({ name: 'M3' })]);
const S = (i: number): string => squad.models[i]!.id;
const name = (id: string): string => squad.models.find((m) => m.id === id)?.name ?? id;
const deployed = (m3: [number, number] = [14, 10]) => projectionOf(gameOf(), boardOf([{ participantId: P1, units: [squad] }]), [
  { kind: 'models_moved', moves: [[10, 10], [12, 10], m3].map(([x, y], i) => ({ modelId: S(i), from: { kind: 'shelf' } as const, to: { kind: 'table' as const, placement: at(x!, y!) } })) },
]).projection;
const of = (placed: [number, Placement][], judged = true, c = opr) => coherencyOf(g, deployed(), new Map(placed.map(([i, p]) => [S(i), p])), c, name, judged);

describe('coherencyView through the WebAssembly', () => {
  it('each ghost has a ring one inch out from its base edge', () => {
    const picture = of([[2, at(13.9, 10)]]);
    expect(picture.rings).toHaveLength(1);
    expect(picture.rings[0]!.modelId).toBe(S(2));
    for (const s of picture.rings[0]!.segments) expect(Math.hypot(s.a.x - 13.9, s.a.y - 10)).toBeCloseTo(1.5, 3);
    expect(picture.lines).toEqual([]);
    expect(picture.words.size).toBe(0);
  });

  it('a straggler gets a line to its nearest model and words saying what to do', () => {
    const picture = of([[2, at(15.4, 10)]]);
    expect(picture.lines).toHaveLength(1);
    const line = picture.lines[0]!;
    expect([line.modelId, line.toId, line.break]).toEqual([S(2), S(1), 'chain']);
    expect(line.from.x).toBeCloseTo(14.9, 6); expect(line.to.x).toBeCloseTo(12.5, 6);
    expect(line.text).toBe('2.4" apart, needs 1"');
    expect(picture.words.get(S(2))).toBe('M3 out of coherency: move within 1" of another model');
    expect(shortWords(picture.words.get(S(2))!)).toBe('Out of coherency');
    expect(picture.blocks).toBe(true);
  });

  it('a spread break gets a line to the farthest model', () => {
    const far = of([[0, at(4, 10)], [1, at(9.5, 10)], [2, at(15, 10)]], true, { chain: 5, elevated: 5, spread: 9 });
    expect(far.lines.map((l) => [l.modelId, l.toId])).toEqual([[S(0), S(2)], [S(2), S(0)]]);
    expect(far.lines.every((l) => l.break === 'spread')).toBe(true);
    expect(far.lines[0]!.text).toBe('10.0" apart, needs 9"');
    expect(far.words.get(S(0))).toBe('M1 out of coherency: move within 9" of M3');
  });

  it('an unjudged placement only warns', () => {
    const picture = of([[2, at(15.4, 10)]], false);
    expect(picture.blocks).toBe(false);
    expect(withCoherency(new Map(), picture).size).toBe(0);
    expect(coherencyWarnings(new Map(), picture).get(S(2))).toBe(picture.words.get(S(2)));
  });

  it('a unit out of coherency before the move only warns', () => {
    const picture = coherencyOf(g, deployed([20, 10]), new Map([[S(2), at(19, 10)]]), opr, name, true);
    expect(picture.words.size).toBeGreaterThan(0);
    expect(picture.blocks).toBe(false);
  });

  it('a contact mark comes before the coherency words', () => {
    const picture = of([[2, at(15.4, 10)]]);
    expect(withCoherency(new Map([[S(2), 'overlaps a base']]), picture).get(S(2))).toBe('overlaps a base');
  });

  it('no coherency from the rule set measures nothing', () => expect(coherencyOf(g, deployed(), new Map([[S(2), at(30, 10)]]), null, name, true)).toBe(NO_COHERENCY));

  it.each([[false, false, false, true], [false, true, false, false], [false, true, true, true], [true, true, true, false], [true, false, false, false]])(
    'the terrain hover panel keeps off placed and held models (held %s, placing %s, Alt %s)', (held, placing, alt, shown) =>
      expect(terrainHoverShown(held, placing, alt)).toBe(shown));
});
