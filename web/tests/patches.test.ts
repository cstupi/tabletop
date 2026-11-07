import { describe, expect, it } from 'vitest';
import { rename, slotBase, slotHeight, slotMesh, slotSummary, unitDefault } from '../src/lib/armies/patches';

describe('patches', () => {
  it('shapes each edit as the PATCH management accepts', () => {
    expect(unitDefault('u1', 'a1')).toEqual({ units: [{ unitId: 'u1', assetId: 'a1' }] });
    expect(unitDefault('u1', null)).toEqual({ units: [{ unitId: 'u1', assetId: null }] });
    expect(slotMesh('s1', 'a2')).toEqual({ slots: [{ slotId: 's1', assetId: 'a2', clearAsset: false, base: null, height: null }] });
    expect(slotMesh('s1', null)).toEqual({ slots: [{ slotId: 's1', assetId: null, clearAsset: true, base: null, height: null }] });
    expect(slotBase('s1', { shape: 'oval', a: 2.36, b: 1.38 })).toEqual({ slots: [{ slotId: 's1', assetId: null, clearAsset: false, base: { shape: 'oval', a: 2.36, b: 1.38 }, height: null }] });
    expect(slotHeight('s1', 2)).toEqual({ slots: [{ slotId: 's1', assetId: null, clearAsset: false, base: null, height: 2 }] });
    expect(rename('Daemons')).toEqual({ name: 'Daemons' });
    expect(slotHeight(['s1', 's2'], 2)).toEqual({ slots: ['s1', 's2'].map((slotId) => ({ slotId, assetId: null, clearAsset: false, base: null, height: 2 })) });
    expect(slotBase(['s1', 's2'], { shape: 'circle', a: 1, b: 1 }).slots).toHaveLength(2);
  });
  it('summarises a slot the way the rulebook prints a profile', () => {
    expect(slotSummary({ id: 's', ordinal: 1, name: 'Rider 1', stats: {}, properties: [{ name: 'Tough', parameter: 3 }], base: { shape: 'circle', a: 1, b: 1 }, height: 1.5,
      weapons: [{ id: 'w', name: 'Heavy Rifle', stats: { Range: '24"', Attacks: '1' }, properties: [{ name: 'AP', parameter: 1 }] }, { id: 'x', name: 'CCW', stats: { Attacks: '2' }, properties: [] }] }))
      .toBe('Heavy Rifle (24", A1, AP(1)), CCW (A2) · Tough(3)');
  });
});
