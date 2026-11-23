import { describe, expect, it } from 'vitest';
import { enemyStripOf, measuredUnit, stripShows } from '../src/lib/play/enemyStrip';
import type { SheetWeapon, UnitSheetModel } from '../src/lib/play/unitSheet';
import { TEAM_A, activating, blue, blue2, focusGame, red } from './focusSamples';
import { newId } from './play';

// EnemyStripTests
const w = (name: string, range: string): SheetWeapon => ({ name, count: 1, range, attacks: '1', ap: '-', apRule: null, rules: [] });
const sheet = (name: string, ...weapons: SheetWeapon[]): UnitSheetModel =>
  ({ unitId: newId(), team: 'Team 1', name, alive: 2, size: 3, quality: '4+', defense: '5+', chips: [], weapons, rules: [], models: [] });

describe('EnemyStrip', () => {
  it('the hovered unit\'s own ranges compare the printed number with the distance', () => {
    const s = enemyStripOf(sheet('Red', w('Rifle', '24"'), w('Pistol', '12"'), w('CCW', '-')), 12.0);
    expect([s.name, s.defense, s.alive, s.distance]).toEqual(['Red', '5+', 2, 12.0]);
    expect(s.ranges).toEqual([{ weapon: 'Rifle', range: '24"', within: true }, { weapon: 'Pistol', range: '12"', within: true }]);   // equal is within; melee left out
    expect(enemyStripOf(sheet('Red', w('Pistol', '12"')), 12.5).ranges[0]!.within).toBe(false);
  });

  it('without a distance no range is judged', () => {
    expect(enemyStripOf(sheet('Red', w('Rifle', '24"')), null).ranges[0]!.within).toBeNull();
  });

  it('only an enemy hovered beside my unit shows a strip', () => {
    const p = activating(null);
    expect(stripShows(p, focusGame, TEAM_A, blue.id, red.id)).toBe(true);
    expect(stripShows(p, focusGame, TEAM_A, blue.id, blue2.id)).toBe(false);   // my own unit hovered
    expect(stripShows(p, focusGame, TEAM_A, red.id, blue.id)).toBe(false);     // the card shows an enemy
    expect(stripShows(p, focusGame, null, blue.id, red.id)).toBe(false);       // a watcher has no units
  });

  it('measures my acting unit whatever the card shows', () => {
    expect(measuredUnit(activating(blue.id), focusGame, TEAM_A, blue2.id)).toBe(blue.id);
    expect(measuredUnit(activating(blue.id), focusGame, TEAM_A, red.id)).toBe(blue.id);
    expect(measuredUnit(activating(null), focusGame, TEAM_A, blue2.id)).toBe(blue2.id);
    expect(measuredUnit(activating(red.id), focusGame, TEAM_A, blue2.id)).toBe(blue2.id);
    expect(measuredUnit(activating(blue.id), focusGame, null, blue2.id)).toBe(blue2.id);
  });
});
