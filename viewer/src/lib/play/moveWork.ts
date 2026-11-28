// viewer/src/lib/play/moveWork.ts
// The move Ask's geometry the decision box reads (Godot's MoveRegions): each listed model's area in its bands, and each drag frame's routes, the
// unit's allowance (p.11's cap once any route goes in difficult ground) and the ghosts' contacts. `inlineMoveWork` asks the page's Geometry at once;
// the play page's `MoveWorkers` (geometry/moveWorkers.ts) keeps each Ask's reaches in Web Workers and answers later, so the tab never waits on them.

import { placed, type Contact, type Geometry, type MoveBands, type RouteFound, type RoutesRequest, type XY } from '../geometry';
import type { Projection } from '../live/apply';
import type { MoveRegion, Placement } from '../protocol/wire.generated';
import { move as contactsOfMove } from './contactCheck';
import { stationary, stationaryUnits, unitsOf } from './modelDrag';

/** One frame's answer: the unit's allowance, each moved model's route, the ghosts' contacts, and where each ghost was when asked (`targets`), so a
 *  reader takes a route only for a ghost still there (`routeAt`). */
export interface MoveFrameAnswer {
  readonly allowance: number;
  readonly routes: ReadonlyMap<string, RouteFound>;
  readonly contacts: ReadonlyMap<string, Contact>;
  readonly targets: ReadonlyMap<string, XY>;
}

export interface MoveWork {
  /** The Ask is drawn: keep its reaches under `key` (its id). Opening the key again with the same region keeps them. */
  open(key: string, legal: MoveRegion, p: Projection): void;
  /** The Ask is no longer drawn. */
  close(key: string): void;
  /** The model's area in its bands, built once per Ask; null until it is built (`onChanged` then fires). */
  area(key: string, id: string): MoveBands | null;
  /** The answer for the ghosts at these placements, or the latest one while that is being worked out (null before the first). `ask` false
   *  only reads: nothing is worked out for these placements. */
  frame(key: string, ghosts: ReadonlyMap<string, Placement>, ask?: boolean): MoveFrameAnswer | null;
  /** An area or a frame answer landed. */
  onChanged(listener: () => void): () => void;
}

const xy = (p: { x: number; y: number }): XY => ({ x: p.x, y: p.y });
const same = (a: XY, b: XY): boolean => Math.abs(a.x - b.x) <= 1e-6 && Math.abs(a.y - b.y) <= 1e-6;

/** The answer's route for a ghost at `to`, when the answer was worked out with it there; undefined otherwise (a straight line reads it then). */
export function routeAt(answer: MoveFrameAnswer | null, id: string, to: XY): RouteFound | undefined {
  const asked = answer?.targets.get(id);
  return asked !== undefined && same(asked, to) ? answer!.routes.get(id) : undefined;
}

/** Whether the answer was worked out for exactly these ghosts. */
export function answers(answer: MoveFrameAnswer | null, ghosts: ReadonlyMap<string, Placement>): boolean {
  if (answer === null || answer.targets.size !== ghosts.size) return false;
  for (const [id, g] of ghosts) { const t = answer.targets.get(id); if (t === undefined || !same(t, g.position)) return false; }
  return true;
}

/** The Ask's moved models from where each stands (a ghost where its model stands has not moved). */
export function movedOf(p: Projection, ghosts: ReadonlyMap<string, Placement>): { id: string; start: ReturnType<typeof placed>; to: XY }[] {
  return [...ghosts].flatMap(([id, g]) => {
    const m = p.models.get(id);
    if (m === undefined || m.location.kind !== 'table') return [];
    const from = m.location.placement.position, to = xy(g.position);
    return Math.hypot(to.x - from.x, to.y - from.y) <= 1e-6 ? [] : [{ id, start: placed(m.slot, m.location.placement), to }];
  });
}

/** The Ask's parameters the routes and areas read (MoveRegion's volume ids, cap, dangers), the standing models with their units (none when the Ask
 *  passes models; DragBodies.Units). */
export function regionTerms(legal: MoveRegion, p: Projection): Omit<RoutesRequest, 'models'> {
  return {
    allowance: Math.max(0, legal.allowance), impassable: legal.impassableVolumeIds, difficult: legal.difficultVolumeIds, size: p.board.table.size,
    stationary: legal.passesModels ? [] : stationary(p, new Set(legal.modelIds)),
    ...(legal.passesModels ? {} : { units: unitsOf(p, legal, stationaryUnits(p, new Set(legal.modelIds))) }),
    ...(legal.difficultCap === undefined ? {} : { difficultCap: legal.difficultCap }),
    ...(legal.steepVolumeIds === undefined ? {} : { steep: legal.steepVolumeIds }),
    ...(legal.dangerousVolumeIds === undefined ? {} : { dangerous: legal.dangerousVolumeIds }),
    ...(legal.unstableVolumeIds === undefined ? {} : { unstable: legal.unstableVolumeIds }),
    ...(legal.unstableOver === undefined ? {} : { unstableOver: legal.unstableOver }),
  };
}

/** Every answer worked out at once on the page's Geometry: one-off routes (Movement.Measured), bands and contacts per call, the last frame kept
 *  for the same ghosts. The tests' box and a page without Web Workers. */
export function inlineMoveWork(geometry: () => Geometry | null): MoveWork {
  const asks = new Map<string, { legal: MoveRegion; p: Projection; areas: Map<string, MoveBands | null>; last: { key: string; answer: MoveFrameAnswer } | null }>();
  return {
    open(key, legal, p) {
      const kept = asks.get(key);
      if (kept === undefined || kept.legal !== legal || kept.p !== p) asks.set(key, { legal, p, areas: kept?.legal === legal ? kept.areas : new Map(), last: null });
    },
    close(key) { asks.delete(key); },
    area(key, id) {
      const ask = asks.get(key), g = geometry();
      if (ask === undefined || g === null) return null;
      if (!ask.areas.has(id)) {
        const m = ask.p.models.get(id);
        ask.areas.set(id, m === undefined || m.location.kind !== 'table' ? null : g.moveBands({
          start: placed(m.slot, m.location.placement), ...regionTerms(ask.legal, ask.p),
          ...(ask.legal.minDistance === undefined ? {} : { minDistance: ask.legal.minDistance }),
        }, ask.p.board.table.terrain));
      }
      return ask.areas.get(id) ?? null;
    },
    frame(key, ghosts, ask = true) {
      const kept = asks.get(key), g = geometry();
      if (kept === undefined || g === null) return null;
      const memo = JSON.stringify([...ghosts].map(([id, at]) => [id, at.position.x, at.position.y, at.position.z, at.yaw.degrees]));
      if (kept.last !== null && kept.last.key === memo) return kept.last.answer;
      const { legal, p } = kept;
      const terms = regionTerms(legal, p);
      const models = movedOf(p, ghosts);
      let found = models.length === 0 ? [] : g.moveRoutes({ ...terms, models }, p.board.table.terrain);
      let allowance = terms.allowance;
      if (legal.difficultCap !== undefined && legal.difficultCap < allowance && found.some((r) => r.entersDifficult)) {
        allowance = legal.difficultCap;
        found = g.moveRoutes({ ...terms, models, capped: true }, p.board.table.terrain);
      }
      const answer: MoveFrameAnswer = {
        allowance, routes: new Map(found.map((r) => [r.id, r])), contacts: ask ? contactsOfMove(g, p, legal, ghosts, p.board.table.size) : new Map(),
        targets: new Map([...ghosts].map(([id, at]) => [id, xy(at.position)])),
      };
      if (ask) kept.last = { key: memo, answer };                         // a read (a ruler's) works out no contacts: it is not the frame's answer
      return answer;
    },
    onChanged() { return () => {}; },
  };
}
