// The move path agreement: tests/fixtures/movePaths.json is written by tests/Tabletop.Client.Tests/MovePathsViewerFixtureTests.cs from Godot's
// MovePaths over recorded logs (the kept last move after each move step and at the next activation) and the Flayer Cavalry rush whose first rider's
// base edge clips a toxic smog. Each is computed again through the browser's port and the WebAssembly Geometry and must agree.
import { readFileSync } from 'node:fs';
import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import {
  footprintColour, lineColour, KeptPathCache, kept, last, LINE_ALPHA, lines, marks, measured, outline, volumeName, type ModelPath,
} from '../src/lib/play/movePaths';
import type { Base, Board, GameEvent, GameInfo, ModelRelocation, MoveRegion, TableSize, TerrainPlacement } from '../src/lib/protocol/wire.generated';
import type { ModelBody } from '../src/lib/geometry';
import { parseCatalog } from '../src/lib/setup/catalog';
import packedCatalog from '@builtin-catalog';
import { loadTestGeometry } from './geometry';

interface PathCase { modelId: string; route: [number, number][]; footprints: { at: [number, number]; dangerous: boolean }[]; dangerous: string[] }
interface LogCase { log: string; upTo: number; segments: string[][]; paths: PathCase[]; marks: Record<string, string>; lines: string[] }
interface Fixture {
  cases: LogCase[];
  flayer: { terrain: TerrainPlacement[]; size: TableSize; legal: MoveRegion; moves: ModelRelocation[]; body: ModelBody; names: Record<string, string>;
    paths: PathCase[]; marks: Record<string, string>; lines: string[] };
  outlines: { base: Base; at: [number, number]; yaw: number; outline: [number, number][] }[];
  colours: { team: number[]; footprint: number[]; dangerous: number[]; line: number[]; lineAlpha: number };
}

const fixture = JSON.parse(readFileSync('tests/fixtures/movePaths.json', 'utf8')) as Fixture;
const read = <T>(name: string, s: string): T => JSON.parse(readFileSync(`tests/fixtures/${name}.${s}.json`, 'utf8')) as T;
const catalog = parseCatalog(packedCatalog);
const round = (n: number): number => Math.round(n * 1e6) / 1e6;
const shape = (paths: readonly ModelPath[]): PathCase[] => paths.map((p) => ({
  modelId: p.modelId, route: p.route.map((q) => [round(q.x), round(q.y)] as [number, number]),
  footprints: p.footprints.map((f) => ({ at: [round(f.at.x), round(f.at.y)] as [number, number], dangerous: f.dangerous })), dangerous: [...p.dangerous],
}));
const rounded = (paths: PathCase[]): PathCase[] => paths.map((p) => ({
  ...p, route: p.route.map(([x, y]) => [round(x), round(y)] as [number, number]),
  footprints: p.footprints.map((f) => ({ at: [round(f.at[0]), round(f.at[1])] as [number, number], dangerous: f.dangerous })),
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
});

it('footprint outlines agree', () => {
  for (const o of fixture.outlines)
    expect(outline(o.base, { x: o.at[0], y: o.at[1] }, o.yaw).map((q) => [round(q.x), round(q.y)])).toEqual(o.outline.map(([x, y]) => [round(x), round(y)]));
});

it('footprint colours agree', () => {
  const hex = (c: number[]): string => '#' + c.slice(0, 3).map((v) => Math.round(v * 255).toString(16).padStart(2, '0')).join('');
  // Godot's colours are floats, the browser's bytes from a #rrggbb team colour: each channel within one step of 255.
  const parts = (s: string): number[] => s.slice(5, -1).split(',').map(Number);
  const near = (css: string, c: number[]): void => {
    const [r, g, b, a] = parts(css);
    [r, g, b].forEach((v, i) => expect(Math.abs(v! - c[i]! * 255)).toBeLessThanOrEqual(1));
    expect(a).toBeCloseTo(c[3]!, 6);
  };
  const team = hex(fixture.colours.team);
  near(footprintColour({ at: { x: 0, y: 0 }, dangerous: false }, team), fixture.colours.footprint);
  near(footprintColour({ at: { x: 0, y: 0 }, dangerous: true }, team), fixture.colours.dangerous);
  near(lineColour(team), fixture.colours.line);
  expect(LINE_ALPHA).toBe(fixture.colours.lineAlpha);
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
