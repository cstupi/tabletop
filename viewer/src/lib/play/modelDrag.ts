// viewer/src/lib/play/modelDrag.ts
// TableInput's model drag (OnLeftDown's pick-up, Place, Arrange, Turn, OnLeftUp) over DragBodies, in the browser: held models are always
// lifted (spec → Heights: no level keys), each landed by Geometry.landModels against the stationary models, the walls and the terrain. Alt
// places freely: straight to the cursor, resting on the highest surface. There is no reach outline (spec): a target is not clamped.

import type { Geometry, HeldEnd, HeldModel, PlacedModel, Placement, Point3, XY } from '../geometry';
import { wallsOf } from '../geometry';
import type { Projection } from '../live/apply';
import type { MoveRegion } from '../protocol/wire.generated';
import { arrange as formation } from './formation';

const wrap = (degrees: number): number => ((degrees % 360) + 360) % 360;
const EPS = 1e-6;

/** DragBodies.Stationary: every model not held, where it stands: a ghost where it is drawn, any other model on the table. */
export function stationary(p: Projection, held: ReadonlySet<string>, ghosts: ReadonlyMap<string, Placement> = new Map()): PlacedModel[] {
  const out: PlacedModel[] = [];
  for (const m of p.models.values()) {
    if (held.has(m.id)) continue;
    const at = ghosts.get(m.id) ?? (m.location.kind === 'table' ? m.location.placement : undefined);
    if (at !== undefined) out.push({ body: { base: m.slot.base, height: m.slot.height }, placement: at });
  }
  return out;
}

/** DragBodies.Grabs: where each picked-up model starts: a proposed model where it is drawn, anything else where the projection has it. */
export function grabs(
  models: readonly string[], proposing: ReadonlySet<string>, drawn: (id: string) => Placement | null, projected: (id: string) => Placement,
): Map<string, Placement> {
  return new Map(models.map((id) => [id, (proposing.has(id) ? drawn(id) : null) ?? projected(id)] as const));
}

export interface Held {
  readonly ids: readonly string[];                       // drag order
  readonly grabs: ReadonlyMap<string, Placement>;        // where each stood when picked up (a ghost where it is drawn)
  readonly origins: ReadonlyMap<string, Placement>;      // the projection's placements: rulers and allowance (none off the table)
  readonly yaws: ReadonlyMap<string, { readonly degrees: number }>; readonly rows: number;   // rows 0: no formation
  readonly ends: ReadonlyMap<string, HeldEnd>;           // where each is drawn and lands now
}

export class ModelDrag {
  readonly #geometry: Geometry;
  #held: Held | null = null;
  #start: XY = { x: 0, y: 0 };
  #cursor: XY = { x: 0, y: 0 };
  #proposing: ReadonlySet<string> = new Set();
  #turn = 0;
  #moved = false;

  constructor(geometry: Geometry) { this.#geometry = geometry; }

  get held(): Held | null { return this.#held; }

  /** The clicked model alone with Shift, else every model of its unit the board draws (on the table, or ghosts). */
  start(p: Projection, modelId: string, at: XY, shift: boolean, proposing: ReadonlySet<string>, ghosts: ReadonlyMap<string, Placement>): Held {
    const clicked = p.models.get(modelId);
    const ids = shift || clicked === undefined ? [modelId]
      : [...p.models.values()].filter((m) => m.unitId === clicked.unitId && (m.location.kind === 'table' || ghosts.has(m.id))).map((m) => m.id);
    const projected = (id: string): Placement | null => {
      const m = p.models.get(id);
      if (m === undefined) return null;
      if (m.location.kind === 'table') return m.location.placement;
      return m.location.kind === 'inside' ? placementOf(p, m.location.containerId) : null;   // a disembark ghost starts on the container
    };
    const grabbed = grabs(ids, proposing, (id) => ghosts.get(id) ?? null, (id) => projected(id) ?? ORIGIN);
    const origins = new Map<string, Placement>();
    for (const id of ids) {
      const o = p.models.get(id)?.location;
      if (o?.kind === 'table') origins.set(id, o.placement);
    }
    this.#start = at; this.#cursor = at; this.#proposing = proposing; this.#turn = 0; this.#moved = false;
    this.#held = {
      ids, grabs: grabbed, origins, yaws: new Map([...grabbed].map(([id, g]) => [id, g.yaw] as const)), rows: 0,
      ends: new Map([...grabbed].map(([id, g]) => [id, { id, drawn: g.position, lands: g.position }] as const)),
    };
    return this.#held;
  }

  /** Each held model to its grab plus the cursor's travel (or its formation spot), landed by landModels against stationary(p, held, ghosts),
   *  walls (wallsOf(terrain, region?.impassableVolumeIds)) and terrain; with Alt, straight there and restModel. */
  move(p: Projection, at: XY, alt: boolean, region: MoveRegion | null, ghosts: ReadonlyMap<string, Placement>): Held | null {
    if (this.#held === null) return null;
    this.#cursor = at;
    this.#moved = true;
    const dx = at.x - this.#start.x, dy = at.y - this.#start.y;
    return this.#place(p, alt, region, ghosts, () => this.#targets(p, (id) => {
      const g = this.#held!.grabs.get(id)!.position;
      return { x: g.x + dx, y: g.y + dy };
    }));
  }

  /** Q/E: each held model about its own centre, or with rows the formation; ±15°, Shift ±30° (the caller's `degrees`). */
  turn(p: Projection, degrees: number, region: MoveRegion | null, ghosts: ReadonlyMap<string, Placement>): Held | null {
    const held = this.#held;
    if (held === null) return null;
    this.#moved = true;
    if (held.rows > 0) this.#turn += degrees;
    else this.#held = { ...held, yaws: new Map([...held.yaws].map(([id, y]) => [id, { degrees: wrap(y.degrees + degrees) }] as const)) };
    return this.#place(p, false, region, ghosts, () => this.#targets(p, (id) => {
      const l = this.#held!.ends.get(id)!.lands;
      return { x: l.x, y: l.y };
    }));
  }

  /** 1–5 while holding: Formation.arrange about the cursor, facing yaw 0 turned by Q/E. */
  arrange(p: Projection, rows: number, region: MoveRegion | null, ghosts: ReadonlyMap<string, Placement>): Held | null {
    if (this.#held === null) return null;
    this.#held = { ...this.#held, rows };
    this.#moved = true;
    return this.#place(p, false, region, ghosts, () => this.#targets(p, () => this.#cursor));
  }

  /** The landings with their yaws; null when nothing moved (DragBodies' `moved` test). Ends the drag. */
  end(): Map<string, Placement> | null {
    const held = this.#held;
    this.cancel();
    if (held === null) return null;
    const out = new Map<string, Placement>();
    let moved = false;
    for (const id of held.ids) {
      const lands = held.ends.get(id)!.lands, yaw = held.yaws.get(id)!, grab = held.grabs.get(id)!;
      out.set(id, { position: lands, yaw });
      if (Math.abs(lands.x - grab.position.x) > EPS || Math.abs(lands.y - grab.position.y) > EPS || Math.abs(lands.z - grab.position.z) > EPS
        || wrap(yaw.degrees) !== wrap(grab.yaw.degrees)) moved = true;
    }
    return moved ? out : null;
  }

  cancel(): void { this.#held = null; this.#moved = false; this.#turn = 0; }

  /** The targets: the formation about the cursor when rows are set (each model takes its facing), else `free`'s. */
  #targets(p: Projection, free: (id: string) => XY): Map<string, XY> {
    const held = this.#held!;
    if (held.rows === 0) return new Map(held.ids.map((id) => [id, free(id)] as const));
    const yaw = { degrees: this.#turn };
    this.#held = { ...held, yaws: new Map(held.ids.map((id) => [id, yaw] as const)) };
    const bases = held.ids.map((id) => p.models.get(id)!.slot.base);
    const at = formation(this.#geometry, bases, held.rows, { x: this.#cursor.x, y: this.#cursor.y, z: 0 }, yaw);
    return new Map(held.ids.map((id, i) => [id, { x: at[i]!.x, y: at[i]!.y }] as const));
  }

  #place(p: Projection, alt: boolean, region: MoveRegion | null, ghosts: ReadonlyMap<string, Placement>, targets: () => Map<string, XY>): Held | null {
    const held = this.#held!;
    // A held model the projection no longer has, or that left the table outside a proposal, cancels the drag.
    for (const id of held.ids) {
      const m = p.models.get(id);
      if (m === undefined || (m.location.kind !== 'table' && !this.#proposing.has(id))) { this.cancel(); return null; }
    }
    const to = targets();
    const now = this.#held!;                                                    // formation targets set the yaws
    const terrain = p.board.table.terrain;
    const bodyOf = (id: string, at: Point3): PlacedModel => {
      const slot = p.models.get(id)!.slot;
      return { body: { base: slot.base, height: slot.height }, placement: { position: at, yaw: now.yaws.get(id)! } };
    };
    let ends: HeldEnd[];
    if (alt) {
      ends = now.ids.map((id) => {
        const t = to.get(id)!;
        const z = this.#geometry.restModel(bodyOf(id, { x: t.x, y: t.y, z: 0 }), terrain);
        const end = { x: t.x, y: t.y, z };
        return { id, drawn: end, lands: end };
      });
    } else {
      const heldModels: HeldModel[] = now.ids.map((id) => ({ id, at: bodyOf(id, now.ends.get(id)!.lands), to: to.get(id)! }));
      ends = this.#geometry.landModels(heldModels, stationary(p, new Set(now.ids), ghosts), wallsOf(terrain, region?.impassableVolumeIds), terrain);
    }
    this.#held = { ...now, ends: new Map(ends.map((e) => [e.id, e] as const)) };
    return this.#held;
  }
}

const ORIGIN: Placement = { position: { x: 0, y: 0, z: 0 }, yaw: { degrees: 0 } };
const placementOf = (p: Projection, id: string): Placement | null => {
  const l = p.models.get(id)?.location;
  return l?.kind === 'table' ? l.placement : null;
};
