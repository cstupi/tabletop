// viewer/src/lib/play/markerLocks.ts
// client/src/Core/MarkerLocks.cs and TerrainLock: objective markers are locked from play on (Playing or Ended): a drag on one does not
// start until the marker's menu unlocks it, and it stays unlocked until locked again. Tokens and setup are free. Local to this client;
// nothing on the wire. Terrain is placed during setup and locked from then on.

import type { Projection } from '../live/apply';
import type { GameStatus } from '../protocol/wire.generated';

const inPlay = (status: GameStatus | null): boolean => status === 'playing' || status === 'ended';

export class MarkerLocks {
  readonly #unlocked = new Set<string>();
  readonly #listeners = new Set<() => void>();

  /** Changed. */
  subscribe(listener: () => void): () => void {
    this.#listeners.add(listener);
    return () => { this.#listeners.delete(listener); };
  }

  /** True for an objective, from setup or placed in play, while the game is Playing or Ended and it has not been unlocked. */
  isLocked(status: GameStatus | null, p: Projection | null, id: string): boolean {
    return inPlay(status) && isObjective(p, id) && !this.#unlocked.has(id);
  }

  mayDrag(status: GameStatus | null, p: Projection | null, id: string): boolean { return !this.isLocked(status, p, id); }

  /** "Unlock" on a locked objective, "Lock" on an unlocked one, null otherwise. The item applies itself (Godot's ItemFor and Chosen pair);
   *  the marker menu shows it last. */
  itemFor(status: GameStatus | null, p: Projection | null, id: string): { label: 'Unlock' | 'Lock'; apply(): void } | null {
    if (!inPlay(status) || !isObjective(p, id)) return null;
    const unlock = !this.#unlocked.has(id);
    return {
      label: unlock ? 'Unlock' : 'Lock',
      apply: () => {
        if (unlock) this.#unlocked.add(id); else this.#unlocked.delete(id);
        for (const l of [...this.#listeners]) l();
      },
    };
  }
}

function isObjective(p: Projection | null, id: string): boolean {
  const marker = p?.markers.get(id) ?? p?.board.table.markers.find((m) => m.id === id);
  return marker?.kind === 'objective';
}

/** A drag on a terrain piece starts only while the game is in Setup. */
export const terrainMayDrag = (status: GameStatus | null): boolean => status === 'setup';
