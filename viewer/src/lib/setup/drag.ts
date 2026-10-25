// viewer/src/lib/setup/drag.ts
// TableInput's setup drag: a terrain piece lifted to the cursor and landed by Geometry.landPiece (so a drop lands where Godot's would),
// a marker moved on the table plane. The browser always drops lifted (spec → Heights): no level keys, no restPiece in setup.
// Nothing under components imports this file: it names lib/geometry.

import type { BoardHit } from '../board/interaction';
import type { Geometry, PieceEnd, XY } from '../geometry';
import type { MoveSetupMarker, MoveTerrain, Point3, TableState, Yaw } from '../protocol/wire.generated';

export type Held =
  | { readonly kind: 'terrain'; readonly id: string; readonly offset: XY; readonly yaw: Yaw; readonly end: PieceEnd }
  | { readonly kind: 'marker'; readonly id: string; readonly offset: XY; readonly yaw: Yaw; readonly at: Point3 };

const SAME = 1e-6;
const same = (a: Point3, b: Point3): boolean => Math.abs(a.x - b.x) < SAME && Math.abs(a.y - b.y) < SAME && Math.abs(a.z - b.z) < SAME;
const wrap = (degrees: number): number => ((degrees % 360) + 360) % 360;

export class SetupDrag {
  #held: Held | null = null;
  #cursor: XY = { x: 0, y: 0 };
  #moved = false;

  constructor(private readonly geometry: Geometry) {}

  get held(): Held | null { return this.#held; }

  /** Picks up the hit piece; the offset keeps the grab point under the cursor. False for a model or an unknown id. */
  start(draft: TableState, hit: BoardHit, at: XY): boolean {
    this.#held = null;
    this.#moved = false;
    this.#cursor = at;
    if (hit.kind === 'terrain') {
      const piece = draft.terrain.find((t) => t.id === hit.id);
      if (piece === undefined) return false;
      this.#held = {
        kind: 'terrain', id: piece.id, offset: { x: at.x - piece.position.x, y: at.y - piece.position.y }, yaw: piece.yaw,
        end: { drawn: piece.position, lands: piece.position },
      };
      return true;
    }
    if (hit.kind === 'marker') {
      const marker = draft.markers.find((m) => m.id === hit.id);
      if (marker === undefined) return false;
      this.#held = {
        kind: 'marker', id: marker.id, offset: { x: at.x - marker.position.x, y: at.y - marker.position.y }, yaw: marker.yaw,
        at: marker.position,
      };
      return true;
    }
    return false;
  }

  /** Recomputes against `draft` (the latest board). Cancels when the piece left the draft; returns the held state or null. */
  move(draft: TableState, at: XY): Held | null {
    if (this.#held === null) return null;
    this.#cursor = at;
    this.#moved = true;
    return this.#reland(draft);
  }

  /** Q/E while held: a turn by `degrees` (counter-clockwise positive), then re-lands at the last cursor. */
  turn(draft: TableState, degrees: number): Held | null {
    const held = this.#held;
    if (held === null) return null;
    this.#held = { ...held, yaw: { degrees: wrap(held.yaw.degrees + degrees) } };
    this.#moved = true;
    return this.#reland(draft);
  }

  /** The drop: MoveTerrain(id, end.lands, yaw) / MoveSetupMarker(id, at, yaw); null when nothing is held or it did not move. */
  end(draft: TableState): MoveTerrain | MoveSetupMarker | null {
    const before = this.#held;
    if (before === null || !this.#moved) { this.cancel(); return null; }
    const held = this.#reland(draft);        // against the draft current at the drop, never a stale one
    this.#held = null;
    this.#moved = false;
    if (held === null) return null;
    if (held.kind === 'terrain') {
      const piece = draft.terrain.find((t) => t.id === held.id)!;
      if (same(held.end.lands, piece.position) && wrap(held.yaw.degrees) === wrap(piece.yaw.degrees)) return null;
      return { kind: 'move_terrain', placementId: held.id, position: held.end.lands, yaw: held.yaw };
    }
    const marker = draft.markers.find((m) => m.id === held.id)!;
    if (same(held.at, marker.position) && wrap(held.yaw.degrees) === wrap(marker.yaw.degrees)) return null;
    return { kind: 'move_setup_marker', markerId: held.id, position: held.at, yaw: held.yaw };
  }

  cancel(): void {
    this.#held = null;
    this.#moved = false;
  }

  #reland(draft: TableState): Held | null {
    const held = this.#held;
    if (held === null) return null;
    const target = { x: this.#cursor.x - held.offset.x, y: this.#cursor.y - held.offset.y };
    if (held.kind === 'terrain') {
      const piece = draft.terrain.find((t) => t.id === held.id);
      if (piece === undefined) { this.cancel(); return null; }
      const end = this.geometry.landPiece(draft.terrain, held.id, { position: piece.position, yaw: held.yaw }, target);
      this.#held = { ...held, end };
      return this.#held;
    }
    if (!draft.markers.some((m) => m.id === held.id)) { this.cancel(); return null; }
    this.#held = { ...held, at: { x: target.x, y: target.y, z: 0 } };
    return this.#held;
  }
}
