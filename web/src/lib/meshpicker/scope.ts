// web/src/lib/meshpicker/scope.ts — which slots a pick sets (mesh-picker spec → Apply to).
import type { Army, Guid, ModelSlot, PatchArmyRequest, Unit } from '$lib/api/types';

export type ScopeKind = 'row' | 'type' | 'army';
export interface Scope { kind: ScopeKind; label: string; slotIds: Guid[] }
/** Where the picker was opened: a unit's shared "every model" row, a per-model row under it, or any other row. */
export type RowKind = 'shared' | 'sub' | 'single';

/** A slot's model name: the import numbers each slot (`Orc Boyz 3`), so the number goes. */
export const modelName = (slotName: string): string => slotName.replace(/\s+\d+$/, '');

const list = (names: string[]) => names.length <= 2 ? names.join(' and ') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;

/** The narrowest first; a scope naming the same slots as a narrower one is left out. */
export function scopes(army: Army, unit: Unit, rowSlots: ModelSlot[], row: RowKind): Scope[] {
  const names = [...new Set(rowSlots.map((s) => modelName(s.name)))];
  const named = (m: ModelSlot) => names.includes(modelName(m.name));
  const all: Scope[] = [
    { kind: 'row', label: row === 'shared' ? 'every model' : 'this model', slotIds: rowSlots.map((s) => s.id) },
    { kind: 'type', label: `every ${list(names)} in ${unit.name}`, slotIds: unit.models.filter(named).map((m) => m.id) },
    { kind: 'army', label: `every ${list(names)} in the army`, slotIds: army.units.flatMap((u) => u.models).filter(named).map((m) => m.id) },
  ];
  const key = (s: Scope) => [...s.slotIds].sort().join();
  return all.filter((s, i) => all.findIndex((t) => key(t) === key(s)) === i);
}

/** The shared row and the rows under it set just themselves; any other row sets every model of its name in the unit. */
export function defaultScope(available: Scope[], row: RowKind): ScopeKind {
  return row === 'single' && available.some((s) => s.kind === 'type') ? 'type' : 'row';
}

/** One SlotPatch per slot; null clears. */
export function scopePatch(scope: Scope, assetId: Guid | null): PatchArmyRequest {
  return { slots: scope.slotIds.map((slotId) => ({ slotId, assetId, clearAsset: assetId === null, base: null, height: null })) };
}

export interface Change { models: number; units: string[]; /** Slots in the scope now holding a different mesh. */ replaces: number }

export function scopeChange(army: Army, scope: Scope, assetId: Guid | null): Change {
  const ids = new Set(scope.slotIds);
  const units: string[] = []; let replaces = 0;
  for (const u of army.units) {
    const hit = u.models.filter((m) => ids.has(m.id));
    if (hit.length && !units.includes(u.name)) units.push(u.name);
    replaces += hit.filter((m) => m.assetId && m.assetId !== assetId).length;
  }
  return { models: ids.size, units, replaces };
}

/** `3 models in Orc Boyz, Orc Mob · replaces 2 meshes`. */
export function changeLine(c: Change): string {
  const models = `${c.models} model${c.models === 1 ? '' : 's'}`;
  const units = c.units.length > 3 ? `${c.units.length} units` : c.units.join(', ');
  return `${models} in ${units}${c.replaces ? ` · replaces ${c.replaces} mesh${c.replaces === 1 ? '' : 'es'}` : ''}`;
}
