// viewer/src/lib/play/pendingDrops.ts
// client/src/Core/PendingDrops.cs: models the player dropped whose MoveModels the server has not answered. The board leaves them where
// they were dropped; the move's event, or a refusal, ends the hold.

import type { ModelRelocation, Point3 } from '../protocol/wire.generated';

export class PendingDrops {
  readonly #held = new Map<string, { token: string; to: Point3 }>();

  holds(modelId: string): boolean { return this.#held.has(modelId); }

  /** Where a held model was dropped. */
  at(modelId: string): Point3 | null { return this.#held.get(modelId)?.to ?? null; }

  add(token: string, moves: readonly ModelRelocation[]): void {
    for (const m of moves) if (m.to.kind === 'table') this.#held.set(m.modelId, { token, to: m.to.placement.position });
  }

  /** A move arrived for the model: the hold ends. True when it landed where this client dropped it (compared exactly, as Godot's `==`). */
  consume(moved: ModelRelocation): boolean {
    const h = this.#held.get(moved.modelId);
    if (h === undefined) return false;
    this.#held.delete(moved.modelId);
    if (moved.to.kind !== 'table') return false;
    const p = moved.to.placement.position;
    return p.x === h.to.x && p.y === h.to.y && p.z === h.to.z;
  }

  /** The send was refused or never went: the models it held, now back to the projection's truth. */
  refuse(token: string): string[] {
    const ids = [...this.#held].filter(([, h]) => h.token === token).map(([id]) => id);
    for (const id of ids) this.#held.delete(id);
    return ids;
  }
}
