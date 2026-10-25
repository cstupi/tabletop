// viewer/src/lib/play/ghostStash.ts
// client/src/Core/GhostStash.cs: a move's or a deploy's ghosts across a reconnect, which drops the projection. Keyed by the Ask id (a move)
// or the unit id (a deploy placement); the first render after the reconnect takes it, and only the same key gets the ghosts back.

import type { Point3, Yaw } from '../protocol/wire.generated';

/** A ghost as it stood: where, and the facing the player turned it to (null when never turned). */
export interface KeptGhost { readonly position: Point3; readonly yaw: Yaw | null }

export class GhostStash {
  #key: string | null = null;
  #ghosts: Map<string, KeptGhost> | null = null;

  keep(key: string, positions: ReadonlyMap<string, Point3>, yaws: ReadonlyMap<string, Yaw>): void {
    this.#key = key;
    this.#ghosts = new Map([...positions].map(([id, position]) => [id, { position, yaw: yaws.get(id) ?? null }] as const));
  }

  take(key: string): Map<string, KeptGhost> | null {
    const ghosts = this.#key === key ? this.#ghosts : null;
    this.#key = null; this.#ghosts = null;
    return ghosts;
  }
}
