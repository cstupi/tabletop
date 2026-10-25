// viewer/src/lib/play/names.ts
// client/src/Core/Names.cs, member for member: ids to words. Falls back to the id's first eight characters rather than throwing.

import type { Projection } from '../live/apply';
import type { GameInfo, Team } from '../protocol/wire.generated';

export class Names {
  readonly #game: GameInfo;
  readonly #projection: Projection | null;

  constructor(game: GameInfo, projection: Projection | null) {
    this.#game = game;
    this.#projection = projection;
  }

  participant(id: string | null | undefined): string {
    if (id === null || id === undefined) return 'the game';
    return this.#game.participants.find((p) => p.id === id)?.name ?? Names.short(id);
  }

  /** The game's teams by ordinal. */
  get teams(): readonly Team[] { return [...this.#game.teams].sort((a, b) => a.ordinal - b.ordinal); }

  team(id: string): string {
    const t = this.#game.teams.find((x) => x.id === id);
    return t === undefined ? Names.short(id) : `Team ${t.ordinal}`;
  }

  unit(id: string): string { return this.#projection?.units.get(id)?.definition.name ?? Names.short(id); }

  model(id: string): string {
    const m = this.#projection?.models.get(id);
    return m === undefined ? Names.short(id) : `${this.unit(m.unitId)} (${m.slot.name})`;
  }

  /** The team of the participant whose army the model belongs to. */
  teamOfModel(modelId: string): string | null {
    const m = this.#projection?.models.get(modelId);
    const u = m === undefined ? undefined : this.#projection?.units.get(m.unitId);
    if (u === undefined) return null;
    return this.#game.participants.find((x) => x.id === u.participantId)?.teamId ?? null;
  }

  /** A weapon by its snapshot name, from the slot that carries it. */
  weapon(id: string): string {
    if (this.#projection !== undefined && this.#projection !== null)
      for (const m of this.#projection.models.values()) {
        const w = m.slot.weapons.find((x) => x.id === id);
        if (w !== undefined) return w.name;
      }
    return Names.short(id);
  }

  teamOfUnit(unitId: string): string | null {
    const u = this.#projection?.units.get(unitId);
    if (u === undefined) return null;
    return this.#game.participants.find((x) => x.id === u.participantId)?.teamId ?? null;
  }

  unitOfModel(modelId: string): string | null { return this.#projection?.models.get(modelId)?.unitId ?? null; }

  piece(id: string): string {
    const p = this.#projection;
    if (p === null) return Names.short(id);
    if (p.units.has(id)) return this.unit(id);
    if (p.models.has(id)) return this.model(id);
    return p.markers.get(id)?.label ?? Names.short(id);
  }

  /** A marker by its label: placed in play, or else from setup. */
  marker(id: string): string {
    const m = this.#projection?.markers.get(id) ?? this.#projection?.board.table.markers.find((x) => x.id === id);
    return m?.label ?? Names.short(id);
  }

  /** The id without dashes, first eight characters (Guid.ToString("N")[..8]). */
  static short(id: string): string { return id.replaceAll('-', '').slice(0, 8); }
}
