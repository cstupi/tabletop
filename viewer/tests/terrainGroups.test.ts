import { describe, expect, it } from 'vitest';
import packed from '@builtin-catalog';
import { parseCatalog } from '../src/lib/setup/catalog';
import { OPEN_GROUP, matches, terrainGroups, terrainSummary, textureLabel, wordsOfName, zoneStyleLabel } from '../src/lib/setup/terrainGroups';
import { ZONE_STYLES } from '../src/lib/setup/edits';

const catalog = parseCatalog(packed);

describe('the terrain list', () => {
  it('groups every piece once, in the order a table is balanced', () => {
    const groups = terrainGroups(catalog);
    expect(groups.map((g) => g.title)).toEqual(['Blocks line of sight', 'Impassable', 'Dangerous', 'Cover', 'Difficult', OPEN_GROUP]);
    expect(groups.flatMap((g) => g.entries).length).toBe(catalog.terrain.size);
    const titleOf = (id: string) => groups.find((g) => g.entries.some((e) => e.id === id))!.title;
    expect(titleOf('large-forest')).toBe('Blocks line of sight');
    expect(titleOf('large-lake')).toBe('Impassable');
    expect(titleOf('mine-field')).toBe('Dangerous');
    expect(titleOf('quicksand')).toBe('Dangerous');
    expect(titleOf('hill')).toBe('Cover');
    expect(titleOf('shallow-river')).toBe('Difficult');
    expect(titleOf('hill-small')).toBe(OPEN_GROUP);
  });

  it('sorts each group by name', () => {
    for (const g of terrainGroups(catalog)) {
      const labels = g.entries.map((e) => e.label);
      expect(labels).toEqual([...labels].sort((a, b) => a.localeCompare(b)));
    }
  });

  it('searches names and declaration words by word start, leaving out empty groups', () => {
    const forest = terrainGroups(catalog, 'forest');
    expect(forest.flatMap((g) => g.entries.map((e) => e.label))).toEqual(['Forest', 'Large Forest']);
    expect(terrainGroups(catalog, 'LARGE for').flatMap((g) => g.entries.map((e) => e.id))).toEqual(['large-forest']);
    expect(terrainGroups(catalog, 'zzz')).toEqual([]);
    expect(matches(catalog.terrain.get('barricade')!, 'diff')).toBe(true);
    expect(matches(catalog.terrain.get('barricade')!, 'arricade')).toBe(false);
  });

  it('says what a piece does in words', () => {
    expect(terrainSummary(catalog.terrain.get('large-forest')!)).toBe('Cover, Difficult, Blocks line of sight, See in and out');
    expect(terrainSummary(catalog.terrain.get('hill-small')!)).toBe('Open');
  });

  it('names textures and zone styles in words', () => {
    expect(textureLabel('grass')).toBe('Grass');
    expect(wordsOfName('large-forest')).toBe('Large forest');
    expect(ZONE_STYLES.map(zoneStyleLabel)).toEqual(['Frontline', 'Ground War', 'Side Battle', 'Disordered', 'Spearhead', 'Opposing Forces', 'Free-for-all']);
  });
});
