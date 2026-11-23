// viewer/src/lib/play/movePaths.ts
// client/src/Core/MovePaths.cs: the move path drawn as marks (pathMarks.ts) and the dangerous terrain warning (spec
// 2026-11-21-move-path-and-dangerous-warning-design; owner ruling: "Any part of the base counts"). Each moving model's route, its base's
// footprints along it and the volumes the Ask lists as dangerous that its swept base enters or crosses come from Geometry.moveRoutes (SweptPath
// over Movement.VolumesOnPath, the server's test); drawn while a move Ask is open and, after it, for the unit's last move until the next
// activation starts (last, kept), for every player and in a replay.

import type { Base, GameEvent, ModelRelocation, MoveRegion, Placement, TableSize, TerrainPlacement, TableState } from '../protocol/wire.generated';
import { placed, type Geometry, type PathFootprint, type PathStretch, type PlacedModel, type RouteFound, type RoutesRequest, type XY } from '../geometry';
import type { Projection } from '../live/apply';
import { nameOf, type Catalog } from '../setup/catalog';
import { effectOf } from './terrainEffects';
import type { PathMark } from './pathMarks';

export const TEST_WORDS = 'Dangerous terrain test';
/** A mark where the base touches dangerous ground: the terrain's dangerous colour at this opacity. */
export const DANGEROUS_ALPHA = 0.9;
/** The marks' look: Fantasy stamps them in an ink of the team colour, Sci-fi traces them in the team colour lifted halfway to white, at these
 *  opacities; the kept last move's marks at KEPT_FADE of those. */
export const INK_ALPHA = 0.7, TRACE_ALPHA = 0.75, INK_SHADE = 0.4, KEPT_FADE = 0.6;
/** Under every mark a dark rim (the shape at RIM_SCALE, RIM_ALPHA black), so a light trace reads on a light board and a dangerous mark on the
 *  dangerous band. */
export const RIM_SCALE = 1.45, RIM_ALPHA = 0.35;
/** The rim under a mark, as a CSS rgba(). */
export const rimColour = (kept: boolean): string => `rgba(0, 0, 0, ${kept ? RIM_ALPHA * KEPT_FADE : RIM_ALPHA})`;

/** One model's move path: where it stood (its base at the start's facing), the route, the footprints and the dangerous volumes crossed, and
 *  the facing it ends at in degrees, where known (the ghost's or the answer's). */
export interface ModelPath {
  readonly modelId: string; readonly start: PlacedModel; readonly route: readonly XY[];
  readonly footprints: readonly PathFootprint[]; readonly dangerous: readonly string[]; readonly stretches: readonly PathStretch[];
  readonly end?: number;
}

/** One answered move Ask: its parameters and the models it moved, from where to where. */
export interface MoveSegment { readonly legal: MoveRegion; readonly moves: readonly ModelRelocation[] }

const rgb = (hex: string): [number, number, number] => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16)) as [number, number, number];
const rgba = (hex: string, a: number): string => { const [r, g, b] = rgb(hex); return `rgba(${r}, ${g}, ${b}, ${a})`; };
/** The team colour lifted halfway to white: the trace is drawn over the move's clear band, which is the team colour itself. */
const light = (hex: string, a: number): string => { const [r, g, b] = rgb(hex).map((c) => Math.round((c + 255) / 2)); return `rgba(${r}, ${g}, ${b}, ${a})`; };
const shaded = (hex: string, a: number): string => { const [r, g, b] = rgb(hex).map((c) => Math.round(c * INK_SHADE)); return `rgba(${r}, ${g}, ${b}, ${a})`; };

/** A mark's colour as a CSS rgba(), for a team colour given as #rrggbb, in the theme's look (`ink`: Fantasy); `kept`, the last move's. */
export function markColour(m: Pick<PathMark, 'dangerous'>, team: string, ink: boolean, kept: boolean): string {
  const fade = kept ? KEPT_FADE : 1;
  if (m.dangerous) return rgba(effectOf('dangerous').colour, DANGEROUS_ALPHA * fade);
  return ink ? shaded(team, INK_ALPHA * fade) : light(team, TRACE_ALPHA * fade);
}

/** Whether the model is a vehicle (its slot's `vehicle`, from the army list), whose path is drawn as treads. */
export const isVehicle = (p: Projection, modelId: string): boolean => p.models.get(modelId)?.slot.vehicle === true;

/** A path from a route Geometry.moveRoutes found for the model starting at `start`, ending facing `end` degrees. */
export const pathOf = (start: PlacedModel, route: RouteFound, end?: number): ModelPath => ({
  modelId: route.id, start, route: route.points, footprints: route.footprints, dangerous: route.dangerous, stretches: route.stretches,
  ...(end === undefined ? {} : { end }),
});

/** The label each crossing model carries (beside the contact marks): the test it owes. */
export function marks(paths: readonly ModelPath[]): Map<string, string> {
  return new Map(paths.filter((p) => p.dangerous.length > 0).map((p) => [p.modelId, TEST_WORDS] as const));
}

/** The decision box's gate lines, one per crossing model and volume: "Flayer Cavalry 1 crosses Toxic Smog: Dangerous terrain test". */
export function lines(paths: readonly ModelPath[], model: (id: string) => string, volume: (id: string) => string): string[] {
  return paths.flatMap((p) => p.dangerous.map((v) => `${model(p.modelId)} crosses ${volume(v)}: ${TEST_WORDS}`));
}

/** The name of the terrain piece a volume belongs to, "terrain" when no piece holds it. */
export function volumeName(table: Pick<TableState, 'terrain'>, volumeId: string, catalog: Catalog): string {
  const piece = table.terrain.find((t: TerrainPlacement) => t.volumes.some((v) => v.id === volumeId));
  return piece === undefined ? 'terrain' : nameOf(piece, catalog);
}

type Destinations = { readonly kind: 'destinations'; readonly moves: readonly ModelRelocation[] };
const tableMoves = (e: GameEvent): readonly ModelRelocation[] | null => {
  if (e.payload.kind !== 'step_completed') return null;
  const answer = e.payload.answer as Destinations | null | undefined;
  if (answer?.kind !== 'destinations' || !answer.moves.some((m) => m.from.kind === 'table' && m.to.kind === 'table')) return null;
  return answer.moves;
};

/** The last activation's move, while it stands: every move step after the last action_recorded before one, each with the move Ask opened before
 *  it. Empty once an action_recorded follows the last move step (the next activation has started) or with no move step in the log. */
export function last(log: readonly GameEvent[]): MoveSegment[] {
  let lastMove = -1;
  for (let i = log.length - 1; i >= 0 && lastMove < 0; i--) {
    if (log[i]!.payload.kind === 'action_recorded') return [];
    if (tableMoves(log[i]!) !== null) lastMove = i;
  }
  if (lastMove < 0) return [];
  let from = 0;
  for (let i = lastMove; i >= 0; i--) if (log[i]!.payload.kind === 'action_recorded') { from = i; break; }
  const segments: MoveSegment[] = [];
  let legal: MoveRegion | null = null;
  for (let i = from; i <= lastMove; i++) {
    const e = log[i]!;
    if (e.payload.kind === 'ask_opened' && e.payload.ask.legal.kind === 'move_region') legal = e.payload.ask.legal;
    else if (legal !== null) {
      const moves = tableMoves(e);
      if (moves !== null) segments.push({ legal, moves: moves.filter((m) => m.from.kind === 'table' && m.to.kind === 'table') });
    }
  }
  return segments;
}

/** A segment's paths as the server measured them (Geometry.moveRoutes: Movement.Measured over the Ask's volumes), each model's base from `bodyOf`;
 *  `standing`, the models on the table that did not move. A model with no body, or not moved, is left out. */
export function measured(g: Geometry, segment: MoveSegment, bodyOf: (id: string) => { base: Base; height: number } | null, terrain: readonly TerrainPlacement[],
  size: TableSize, standing: readonly PlacedModel[]): ModelPath[] {
  const legal = segment.legal;
  const starts = new Map<string, PlacedModel>();
  const ends = new Map<string, number>();
  const models = segment.moves.flatMap((m) => {
    const body = bodyOf(m.modelId);
    if (m.from.kind !== 'table' || m.to.kind !== 'table' || body === null) return [];
    const from: Placement = m.from.placement, to = m.to.placement.position;
    if (Math.hypot(to.x - from.position.x, to.y - from.position.y) <= 1e-6) return [];
    const start = placed(body, from);
    starts.set(m.modelId, start);
    ends.set(m.modelId, m.to.placement.yaw.degrees);
    return [{ id: m.modelId, start, to: { x: to.x, y: to.y } }];
  });
  if (models.length === 0) return [];
  const request: RoutesRequest = {
    models, allowance: Math.max(0, legal.allowance), impassable: legal.impassableVolumeIds, difficult: legal.difficultVolumeIds, size,
    stationary: legal.passesModels ? [] : standing,
    ...(legal.difficultCap === undefined ? {} : { difficultCap: legal.difficultCap }),
    ...(legal.steepVolumeIds === undefined ? {} : { steep: legal.steepVolumeIds }),
    ...(legal.dangerousVolumeIds === undefined ? {} : { dangerous: legal.dangerousVolumeIds }),
    ...(legal.unstableVolumeIds === undefined ? {} : { unstable: legal.unstableVolumeIds }),
    ...(legal.unstableOver === undefined ? {} : { unstableOver: legal.unstableOver }),
  };
  return g.moveRoutes(request, terrain).map((r) => pathOf(starts.get(r.id)!, r, ends.get(r.id)));
}

/** LastMovePath: the last move's paths for a log state, measured again only when its kept segments change (a new move step, the next activation). */
export class KeptPathCache {
  #segments: readonly MoveSegment[] = [];
  #paths: ModelPath[] = [];

  of(g: Geometry, log: readonly GameEvent[], p: Projection): ModelPath[] {
    const segments = last(log);
    const same = segments.length === this.#segments.length && segments.every((s, i) => {
      const o = this.#segments[i]!;
      return s.legal === o.legal && s.moves.length === o.moves.length && s.moves.every((m, j) => m === o.moves[j]);
    });
    if (!same) { this.#segments = segments; this.#paths = kept(g, segments, p); }
    return this.#paths;
  }
}

/** The kept segments' paths (MovePaths.KeptLater): each moved model's base from its slot, the standing models every model on the table that none of
 *  the segments moves. */
export function kept(g: Geometry, segments: readonly MoveSegment[], p: Projection): ModelPath[] {
  const moving = new Set(segments.flatMap((s) => s.moves.map((m) => m.modelId)));
  const standing = [...p.models.values()].flatMap((m) => (moving.has(m.id) || m.location.kind !== 'table' ? [] : [placed(m.slot, m.location.placement)]));
  const bodyOf = (id: string) => { const m = p.models.get(id); return m === undefined ? null : { base: m.slot.base, height: m.slot.height }; };
  return segments.flatMap((s) => measured(g, s, bodyOf, p.board.table.terrain, p.board.table.size, standing));
}
