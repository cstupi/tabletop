// viewer/src/lib/play/names.ts
// client/src/Core/Names.cs, member for member: ids to words. Falls back to the id's first eight characters rather than throwing.

import type { Projection } from '../live/apply';
import type { GameInfo, Team, Weapon } from '../protocol/wire.generated';

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

  /** Names.WeaponsOf: each weapon found, in the order asked, with the model whose slot carries it; an id no slot carries is left out. */
  weaponsOf(ids: readonly string[]): { modelId: string; weapon: Weapon }[] {
    const p = this.#projection;
    if (p === undefined || p === null) return [];
    const out: { modelId: string; weapon: Weapon }[] = [];
    for (const id of ids)
      for (const m of p.models.values()) {
        const w = m.slot.weapons.find((x) => x.id === id);
        if (w !== undefined) { out.push({ modelId: m.id, weapon: w }); break; }
      }
    return out;
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
    return p.markers.has(id) || p.board.table.markers.some((m) => m.id === id) ? this.marker(id) : Names.short(id);
  }

  /** A marker by its label: placed in play, or else from setup. One with no label is its kind and its place among the markers of
   *  that kind, setup's order first ("Objective 2"), never an id. */
  marker(id: string): string {
    const p = this.#projection;
    const m = p?.markers.get(id) ?? p?.board.table.markers.find((x) => x.id === id);
    if (p === null || p === undefined || m === undefined) return Names.short(id);
    if (m.label !== undefined && m.label !== null && m.label.trim() !== '') return m.label;
    const table = p.board.table.markers;
    const placed = [...p.markers.values()].filter((x) => !table.some((t) => t.id === x.id)).sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
    const kin = [...table, ...placed].filter((x) => x.kind === m.kind).map((x) => x.id);
    return `${m.kind === 'objective' ? 'Objective' : 'Token'} ${kin.indexOf(id) + 1}`;
  }

  /** The id without dashes, first eight characters (Guid.ToString("N")[..8]). */
  static short(id: string): string { return id.replaceAll('-', '').slice(0, 8); }
}
