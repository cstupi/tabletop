import type { Base, Guid, ModelSlot, PatchArmyRequest, PropertyInstance } from '$lib/api/types';

/** One slot, or every slot named (a unit's "every model" row). */
const slot = (ids: Guid | Guid[], over: Partial<{ assetId: Guid | null; clearAsset: boolean; base: Base | null; height: number | null }>): PatchArmyRequest =>
  ({ slots: (Array.isArray(ids) ? ids : [ids]).map((slotId) => ({ slotId, assetId: null, clearAsset: false, base: null, height: null, ...over })) });

export const unitDefault = (unitId: Guid, assetId: Guid | null): PatchArmyRequest => ({ units: [{ unitId, assetId }] });
export const slotMesh = (slotId: Guid, assetId: Guid | null): PatchArmyRequest => slot(slotId, assetId ? { assetId } : { clearAsset: true });
export const slotBase = (ids: Guid | Guid[], base: Base): PatchArmyRequest => slot(ids, { base });
export const slotHeight = (ids: Guid | Guid[], height: number): PatchArmyRequest => slot(ids, { height });
export const rename = (name: string): PatchArmyRequest => ({ name });

const prop = (p: PropertyInstance) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`);
export function slotSummary(s: ModelSlot): string {
  const weapons = s.weapons.map((w) => `${w.name} (${[w.stats['Range'], w.stats['Attacks'] ? `A${w.stats['Attacks']}` : undefined, ...w.properties.map(prop)].filter(Boolean).join(', ')})`).join(', ');
  const props = s.properties.map(prop).join(', ');
  return [weapons, props].filter(Boolean).join(' · ');
}
