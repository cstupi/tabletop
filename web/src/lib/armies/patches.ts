import type { Base, Guid, ModelSlot, PatchArmyRequest, PropertyInstance } from '$lib/api/types';

const slot = (slotId: Guid, over: Partial<{ assetId: Guid | null; clearAsset: boolean; base: Base | null; height: number | null }>): PatchArmyRequest =>
  ({ slots: [{ slotId, assetId: null, clearAsset: false, base: null, height: null, ...over }] });

export const unitDefault = (unitId: Guid, assetId: Guid | null): PatchArmyRequest => ({ units: [{ unitId, assetId }] });
export const slotMesh = (slotId: Guid, assetId: Guid | null): PatchArmyRequest => slot(slotId, assetId ? { assetId } : { clearAsset: true });
export const slotBase = (slotId: Guid, base: Base): PatchArmyRequest => slot(slotId, { base });
export const slotHeight = (slotId: Guid, height: number): PatchArmyRequest => slot(slotId, { height });
export const rename = (name: string): PatchArmyRequest => ({ name });

const prop = (p: PropertyInstance) => (p.parameter === undefined ? p.name : `${p.name}(${p.parameter})`);
export function slotSummary(s: ModelSlot): string {
  const weapons = s.weapons.map((w) => `${w.name} (${[w.stats['Range'], w.stats['Attacks'] ? `A${w.stats['Attacks']}` : undefined, ...w.properties.map(prop)].filter(Boolean).join(', ')})`).join(', ');
  const props = s.properties.map(prop).join(', ');
  return [weapons, props].filter(Boolean).join(' · ');
}
