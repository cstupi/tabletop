// viewer/src/lib/play/sightKey.ts
// client/src/Core/SightKey.cs: what a sight query between two units depends on, as the client knows it: the pair, where every model stands
// (another unit's models screen lines), and the terrain. Equal keys give the same server answer, so the earlier answer stands.

import type { Projection } from '../live/apply';
import type { TerrainPlacement } from '../protocol/wire.generated';

export class SightKey {
  readonly #from: string;
  readonly #to: string;
  readonly #models: string;
  readonly #terrain: readonly TerrainPlacement[];

  private constructor(from: string, to: string, models: string, terrain: readonly TerrainPlacement[]) {
    this.#from = from;
    this.#to = to;
    this.#models = models;
    this.#terrain = terrain;
  }

  static of(p: Projection, from: string, to: string): SightKey {
    const models = [...p.models.values()].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)).map((m) => [m.id, m.location]);
    return new SightKey(from, to, JSON.stringify(models), p.board.table.terrain);
  }

  equals(o: SightKey | null): boolean {
    return o !== null && this.#from === o.#from && this.#to === o.#to && this.#models === o.#models
      && (this.#terrain === o.#terrain || JSON.stringify(this.#terrain) === JSON.stringify(o.#terrain));
  }
}

/** The key each assigned target was last asked about: a target is asked again only when its key changed or it is new, and a target no
 * longer assigned is forgotten. */
export class SightCache {
  readonly #asked = new Map<string, SightKey>();

  needsAsk(target: string, key: SightKey): boolean {
    const old = this.#asked.get(target);
    return old === undefined || !old.equals(key);
  }
  asked(target: string, key: SightKey): void { this.#asked.set(target, key); }
  forget(target: string): void { this.#asked.delete(target); }
  /** Drops every target not in `targets`. */
  keep(targets: Iterable<string>): void {
    const keep = new Set(targets);
    for (const t of [...this.#asked.keys()]) if (!keep.has(t)) this.#asked.delete(t);
  }
}
