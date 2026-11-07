import { describe, expect, it } from 'vitest';
import packed from '@builtin-catalog';
import { effectOf } from '../src/lib/play/terrainEffects';
import { terrainHoverOf } from '../src/lib/play/terrainHover';
import { terrainLabelOf } from '../src/lib/play/terrainLabel';
import type { TableState, TerrainPlacement, TerrainVolume } from '../src/lib/protocol/wire.generated';
import { parseCatalog } from '../src/lib/setup/catalog';
import { terrainCardOf } from '../src/lib/setup/terrainCard';

// TerrainLabelTests
const catalog = parseCatalog(packed);
const v = (bottom: number, top: number, declarations: string): TerrainVolume => ({
  id: `v${bottom}${top}`, volume: { kind: 'area', footprint: { kind: 'rect', a: 6, b: 6 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom, top, declarations },
} as TerrainVolume);
const piece = (mesh: TerrainPlacement['mesh'], volumes: TerrainVolume[], z = 0, scale = 1, id = 'p1'): TerrainPlacement =>
  ({ id, mesh, position: { x: 10, y: 10, z }, yaw: { degrees: 0 }, scale, volumes });
const tableOf = (...terrain: TerrainPlacement[]): TableState =>
  ({ id: 't', size: { width: 72, length: 48 }, texture: { builtin: 'grass' }, zones: [], markers: [], terrain } as unknown as TableState);

describe('TerrainLabel', () => {
  it('a built-in piece reads its label then each volume\'s declarations and heights', () => {
    const ruin = piece({ builtin: 'ruin' }, [v(0, 4, 'cover, difficult, blocks_line_of_sight, see_in_and_out')]);
    expect(terrainLabelOf(ruin, catalog)).toBe('Ruin · Cover, Difficult, Blocks line of sight, See in and out · 0–4"');
  });

  it('book ruins read cover and dangerous when rushing or charging', () => {
    const ruins = piece({ builtin: 'ruins' }, [v(0, 4, 'cover, dangerous_on_rush_charge')]);
    expect(terrainLabelOf(ruins, catalog)).toBe('Ruins or Debris · Cover, Dangerous when rushing or charging · 0–4"');
  });

  it('uses the words the server gave, and its own for a name not given', () => {
    const words = { see_in_and_out: 'Dense', blocks_line_of_sight: 'Sight blocking', dangerous_on_rush_charge: 'Unstable', steep: 'Steep' };
    const ruin = piece({ builtin: 'ruin' }, [v(0, 4, 'cover, dangerous_on_rush_charge, steep, see_in_and_out')]);
    expect(terrainLabelOf(ruin, catalog, words)).toBe('Ruin · Cover, Unstable, Steep, Dense · 0–4"');
    expect(terrainLabelOf(ruin, catalog, {})).toBe('Ruin · Cover, Dangerous when rushing or charging, Steep, See in and out · 0–4"');
    expect(terrainCardOf(tableOf(ruin), 'playing', 'p1', catalog, words)!.volumes[0]!.declarations).toEqual(['Cover', 'Unstable', 'Steep', 'Dense']);
  });

  it('scale and elevation raise the heights and each volume gets its own line', () => {
    const p = piece({ assetId: '0198-abcd' }, [v(0, 3, 'impassable'), v(3, 5, 'none')], 1, 0.5);
    expect(terrainLabelOf(p, catalog)).toBe('Terrain · Impassable · 1–2.5"\nTerrain · Open · 2.5–3.5"');
  });
});

describe('the terrain hover panel', () => {
  const forest = piece({ builtin: 'forest' }, [v(0, 6, 'blocks_line_of_sight, see_in_and_out, cover, difficult')]);

  it('names the hovered piece, its height and one chip per effect in the label\'s order, never for the selected piece', () => {
    const hover = terrainHoverOf(tableOf(forest), catalog, 'p1', null, { blocks_line_of_sight: 'Sight blocking' })!;
    expect(hover).toMatchObject({ name: 'Forest', height: '0–6"' });
    expect(hover.effects.map((e) => e.word)).toEqual(['Cover', 'Difficult', 'Sight blocking', 'See in and out']);
    expect(hover.effects[0]!.colour).toBe(effectOf('cover').colour);
    expect(terrainHoverOf(tableOf(forest), catalog, 'p1', 'p1')).toBeNull();
    expect(terrainHoverOf(tableOf(forest), catalog, null, null)).toBeNull();
    expect(terrainHoverOf(tableOf(forest), catalog, 'gone', null)).toBeNull();
  });

  it('reads Open for a piece with no declaration', () => {
    const p = piece({ assetId: 'a' }, [v(0, 1, 'none')]);
    expect(terrainHoverOf(tableOf(p), catalog, 'p1', null)!.effects.map((e) => [e.word, e.icon])).toEqual([['Open', 'open-ground']]);
  });
});

describe('the play terrain card', () => {
  it('is read-only in play and names the piece with what it does in words', () => {
    const buildings = piece({ builtin: 'small-buildings' }, [v(0, 4, 'impassable, blocks_line_of_sight')]);
    const card = terrainCardOf(tableOf(buildings), 'playing', 'p1', catalog)!;
    expect(card).toMatchObject({ name: 'Group of Small Buildings', editable: false, height: '0–4"' });
    expect(card.volumes[0]!.declarations).toEqual(['Impassable', 'Blocks line of sight']);
  });

  it('leads with the piece\'s effects, each with the server\'s sentence, and tints each volume by its strongest effect', () => {
    const buildings = piece({ builtin: 'small-buildings' }, [v(0, 4, 'impassable, blocks_line_of_sight'), v(0, 1, 'none')]);
    const card = terrainCardOf(tableOf(buildings), 'playing', 'p1', catalog, null, { impassable: 'Units may not move through it.' })!;
    expect(card.effects.map((e) => [e.word, e.rule])).toEqual([['Impassable', 'Units may not move through it.'], ['Blocks line of sight', null]]);
    expect(card.volumes.map((x) => x.colour)).toEqual([effectOf('impassable').colour, '#8B93A1']);
  });
});
