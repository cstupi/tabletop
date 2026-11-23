// The move path agreement: tests/fixtures/movePaths.json is written by tests/Tabletop.Client.Tests/MovePathsViewerFixtureTests.cs from Godot's
// MovePaths over recorded logs (the kept last move after each move step and at the next activation) and the Flayer Cavalry rush whose first rider's
// base edge clips a toxic smog, with the marks each path is drawn with (pathMarks: prints, treads), their shapes and colours. Each is computed again
// through the browser's port and the WebAssembly Geometry and must agree.
import { readFileSync } from 'node:fs';
import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import {
  KeptPathCache, kept, last, lines, markColour, marks, measured, RIM_SCALE, rimColour, volumeName, type ModelPath,
} from '../src/lib/play/movePaths';
import { BOOT, marksOf, TREAD_LINK, type PathMark } from '../src/lib/play/pathMarks';
import type { Base, Board, GameEvent, GameInfo, ModelRelocation, MoveRegion, TableSize, TerrainPlacement } from '../src/lib/protocol/wire.generated';
import type { ModelBody } from '../src/lib/geometry';
import { parseCatalog } from '../src/lib/setup/catalog';
import packedCatalog from '@builtin-catalog';
import { loadTestGeometry } from './geometry';

interface PathCase {
  modelId: string; route: [number, number][]; footprints: { at: [number, number]; dangerous: boolean }[]; dangerous: string[]; stretches: [number, number][]; end: number | null;
}
/** [x, y, facing, left, dangerous, station, size] */
type Mark = [number, number, number, boolean, boolean, number, number];
interface LogCase { log: string; upTo: number; segments: string[][]; paths: PathCase[]; marks: Record<string, string>; lines: string[] }
interface Fixture {
  cases: LogCase[];
  flayer: { terrain: TerrainPlacement[]; size: TableSize; legal: MoveRegion; moves: ModelRelocation[]; body: ModelBody; names: Record<string, string>;
    paths: PathCase[]; marks: Record<string, string>; lines: string[]; prints: Mark[][]; treads: Mark[][] };
  routes: { base: Base; start: number | null; end: number | null; vehicle: boolean; route: [number, number][]; stretches: [number, number][]; marks: Mark[] }[];
  shapes: { boot: [number, number][][]; tread: [number, number][] };
  team: number[];
  rims: number[][]; rimScale: number;
  colours: { dangerous: boolean; ink: boolean; kept: boolean; colour: number[] }[];
}

const fixture = JSON.parse(readFileSync('tests/fixtures/movePaths.json', 'utf8')) as Fixture;
const read = <T>(name: string, s: string): T => JSON.parse(readFileSync(`tests/fixtures/${name}.${s}.json`, 'utf8')) as T;
const catalog = parseCatalog(packedCatalog);
const round = (n: number): number => Math.round(n * 1e6) / 1e6;
const shape = (paths: readonly ModelPath[]): PathCase[] => paths.map((p) => ({
  modelId: p.modelId, route: p.route.map((q) => [round(q.x), round(q.y)] as [number, number]),
  footprints: p.footprints.map((f) => ({ at: [round(f.at.x), round(f.at.y)] as [number, number], dangerous: f.dangerous })), dangerous: [...p.dangerous],
  stretches: p.stretches.map((s) => [round(s.from), round(s.to)] as [number, number]), end: p.end ?? null,
}));
const markShape = (ms: readonly PathMark[]): Mark[] => ms.map((m) => [round(m.at.x), round(m.at.y), round(m.facing), m.left, m.dangerous, round(m.station), round(m.size)]);
const roundedMarks = (ms: Mark[]): Mark[] => ms.map(([x, y, f, l, d, s, z]) => [round(x), round(y), round(f), l, d, round(s), round(z)]);
const rounded = (paths: PathCase[]): PathCase[] => paths.map((p) => ({
  ...p, route: p.route.map(([x, y]) => [round(x), round(y)] as [number, number]),
  footprints: p.footprints.map((f) => ({ at: [round(f.at[0]), round(f.at[1])] as [number, number], dangerous: f.dangerous })),
  stretches: p.stretches.map(([a, b]) => [round(a), round(b)] as [number, number]),
}));

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

describe.each(fixture.cases.map((c, i) => ({ ...c, i })))('move paths agreement', (c) => {
  it(`${c.log}@${c.upTo} #${c.i}`, () => {
    const game = read<{ game: GameInfo }>(c.log, 'welcome').game;
    const applier = new Applier(read<{ board: Board }>(c.log, 'board').board, game);
    applier.applyAll(read<{ events: GameEvent[] }>(c.log, 'events').events.filter((e) => e.seq <= c.upTo));
    const p = applier.projection;
    const segments = last(applier.log);
    expect(segments.map((s) => s.moves.map((m) => m.modelId))).toEqual(c.segments);
    const paths = kept(g, segments, p);
    expect(shape(paths)).toEqual(rounded(c.paths));
    expect(Object.fromEntries(marks(paths))).toEqual(c.marks);
    expect(lines(paths, (id) => p.models.get(id)!.slot.name, (v) => volumeName(p.board.table, v, catalog))).toEqual(c.lines);
  });
});

it('the Flayer Cavalry rush warns for rider 1 alone, as Godot does', () => {
  const f = fixture.flayer;
  const paths = measured(g, { legal: f.legal, moves: f.moves }, () => f.body, f.terrain, f.size, []);
  expect(shape(paths)).toEqual(rounded(f.paths));
  expect(Object.fromEntries(marks(paths))).toEqual(f.marks);
  expect(lines(paths, (id) => f.names[id]!, (v) => volumeName({ terrain: f.terrain }, v, catalog))).toEqual(f.lines);
  expect(f.lines).toEqual(['Flayer Cavalry 1 crosses Toxic Smog: Dangerous terrain test']);
  const of = (p: ModelPath, vehicle: boolean) => markShape(marksOf(p.route, p.start.body.base, p.stretches, vehicle, p.start.placement.yaw.degrees, p.end));
  expect(paths.map((p) => of(p, false))).toEqual(f.prints.map(roundedMarks));
  expect(paths.map((p) => of(p, true))).toEqual(f.treads.map(roundedMarks));
  expect(f.prints[0]!.some((m) => m[4])).toBe(true);
});

it('marks along a cornered route agree for every base shape, prints and treads', () => {
  for (const r of fixture.routes) {
    const ms = marksOf(r.route.map(([x, y]) => ({ x, y })), r.base, r.stretches.map(([from, to]) => ({ from, to })), r.vehicle, r.start ?? undefined, r.end ?? undefined);
    expect(markShape(ms)).toEqual(roundedMarks(r.marks));
  }
});

it('mark shapes agree', () => {
  const pts = (ps: readonly { x: number; y: number }[]) => ps.map((p) => [round(p.x), round(p.y)]);
  expect(BOOT.map(pts)).toEqual(fixture.shapes.boot.map((o) => o.map(([x, y]) => [round(x), round(y)])));
  expect(pts(TREAD_LINK)).toEqual(fixture.shapes.tread.map(([x, y]) => [round(x), round(y)]));
});

it('mark colours agree', () => {
  const hex = (c: number[]): string => '#' + c.slice(0, 3).map((v) => Math.round(v * 255).toString(16).padStart(2, '0')).join('');
  // Godot's colours are floats, the browser's bytes from a #rrggbb team colour: each channel within one step of 255.
  const parts = (s: string): number[] => s.slice(5, -1).split(',').map(Number);
  const near = (css: string, c: number[]): void => {
    const [r, g, b, a] = parts(css);
    [r, g, b].forEach((v, i) => expect(Math.abs(v! - c[i]! * 255)).toBeLessThanOrEqual(1));
    expect(a).toBeCloseTo(c[3]!, 6);
  };
  const team = hex(fixture.team);
  for (const c of fixture.colours) near(markColour({ dangerous: c.dangerous }, team, c.ink, c.kept), c.colour);
  near(rimColour(false), fixture.rims[0]!);
  near(rimColour(true), fixture.rims[1]!);
  expect(RIM_SCALE).toBeCloseTo(fixture.rimScale, 6);
});

it('the kept path is measured once per move and dropped at the next activation', () => {
  const c = fixture.cases.find((x) => x.paths.length > 0)!;
  const next = fixture.cases[fixture.cases.indexOf(c) + 1]!;
  const game = read<{ game: GameInfo }>(c.log, 'welcome').game;
  const events = read<{ events: GameEvent[] }>(c.log, 'events').events;
  const applier = new Applier(read<{ board: Board }>(c.log, 'board').board, game);
  applier.applyAll(events.filter((e) => e.seq <= c.upTo));
  const cache = new KeptPathCache();
  const first = cache.of(g, applier.log, applier.projection);
  expect(first.length).toBe(c.paths.length);
  expect(cache.of(g, [...applier.log], applier.projection)).toBe(first);
  applier.applyAll(events.filter((e) => e.seq > c.upTo && e.seq <= next.upTo));
  expect(cache.of(g, applier.log, applier.projection)).toEqual([]);
});

it('the fixture keeps paths and clears them', () => {
  expect(fixture.cases.some((c) => c.paths.length > 0)).toBe(true);
  expect(fixture.cases.some((c) => c.segments.length === 0)).toBe(true);
});
