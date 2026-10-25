// viewer/src/lib/play/commitHold.ts
// client/src/Core/CommitHold.cs: the last move or placement commit, held until the server answers it. A refusal of that command hands
// back its ghosts so the player finds them where they were dropped, marked with the server's reason. Any other send in between drops it.
// The command id is a local token per send (sendCommand settles with its own Refused).

import type { ModelRelocation, Point3, Yaw } from '../protocol/wire.generated';

/** A refused commit's ghosts: where each stood and faced, keyed as GhostStash keys them (a move's Ask id, a deployment's unit id), and the
 *  models it moved (every model when none did), which carry the server's reason. */
export interface HeldCommit {
  readonly key: string; readonly at: ReadonlyMap<string, Point3>; readonly yaws: ReadonlyMap<string, Yaw>; readonly moved: readonly string[];
}

export class CommitHold {
  #held: HeldCommit | null = null;
  #command: string | null = null;

  /** The commit about to go: its relocations, before its token is known. */
  remember(key: string, moves: readonly ModelRelocation[]): void {
    const at = new Map<string, Point3>(), yaws = new Map<string, Yaw>(), moved: string[] = [];
    for (const m of moves) {
      if (m.to.kind !== 'table') continue;
      at.set(m.modelId, m.to.placement.position); yaws.set(m.modelId, m.to.placement.yaw);
      const from = m.from;
      if (from.kind !== 'table' || Math.hypot(from.placement.position.x - m.to.placement.position.x, from.placement.position.y - m.to.placement.position.y) > 1e-6)
        moved.push(m.modelId);
    }
    this.#held = { key, at, yaws, moved: moved.length > 0 ? moved : [...at.keys()] };
    this.#command = null;
  }

  /** A send went out: the remembered commit if it is still waiting for one, else nothing is held. */
  sent(token: string): void {
    if (this.#held !== null && this.#command === null) this.#command = token;
    else { this.#held = null; this.#command = null; }
  }

  /** A send was refused: the commit it carried, once; null for any other. */
  refused(token: string): HeldCommit | null {
    if (this.#held === null || this.#command !== token) return null;
    const held = this.#held;
    this.#held = null; this.#command = null;
    return held;
  }
}
