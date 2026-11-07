import { describe, expect, it } from 'vitest';
import packed from '@builtin-catalog';
import { parseCatalog } from '../src/lib/setup/catalog';
import { TERRAIN_KINDS, terrainGroups, terrainTiles, toggleKind } from '../src/lib/setup/terrainGroups';
import { effectOf, OPEN_GROUND } from '../src/lib/play/terrainEffects';
import { objectiveRows, styleOf, zoneDiagram } from '../src/lib/setup/setupPanel';
import { columnLine, type TeamColumn } from '../src/lib/lobby/teamColumns';
import type { Marker, TableState } from '../src/lib/protocol/wire.generated';

const catalog = parseCatalog(packed);

describe('the terrain tiles (TerrainTiles.cs)', () => {
  it('are every piece once by group then name, each group as the terrain list groups it', () => {
    const tiles = terrainTiles(catalog);
    expect(tiles.length).toBe(catalog.terrain.size);
    expect([...new Set(tiles.map((t) => t.kind.key))]).toEqual(['blocks-sight', 'impassable', 'dangerous', 'cover', 'difficult', 'open-ground']);
    const keyOf = (id: string) => tiles.find((t) => t.entry.id === id)!.kind.key;
    expect([keyOf('large-forest'), keyOf('large-lake'), keyOf('quicksand'), keyOf('hill'), keyOf('shallow-river'), keyOf('hill-small')])
      .toEqual(['blocks-sight', 'impassable', 'dangerous', 'cover', 'difficult', 'open-ground']);
    for (const g of terrainGroups(catalog)) expect(new Set(g.entries.map((e) => tiles.find((t) => t.entry === e)!.kind.key)).size).toBe(1);
  });

  it('draws each group in its lead effect\'s icon and colour (terrainEffects.ts)', () => {
    const lead = ['blocks_line_of_sight', 'impassable', 'dangerous', 'cover', 'difficult'].map(effectOf).concat(OPEN_GROUND);
    expect(TERRAIN_KINDS.map((k) => [k.key, k.colour])).toEqual(lead.map((e) => [e.icon, e.colour]));
  });

  it('a legend dot hides its group; the search filters by name', () => {
    expect(TERRAIN_KINDS.map((k) => k.legend)).toEqual(['Blocks sight', 'Impassable', 'Dangerous', 'Cover', 'Difficult', 'Open']);
    const hidden = toggleKind(new Set(), 'cover');
    expect(terrainTiles(catalog, '', hidden).some((t) => t.kind.key === 'cover')).toBe(false);
    expect(terrainTiles(catalog, '', toggleKind(hidden, 'cover')).some((t) => t.kind.key === 'cover')).toBe(true);
    expect(terrainTiles(catalog, 'forest').map((t) => t.entry.label)).toEqual(['Forest', 'Large Forest']);
  });
});

describe('the zone diagram (ZoneDiagram.cs)', () => {
  it('fits the table to the width with the table Y up the drawing', () => {
    const zones = [{ id: 'a', shape: 'frontline/1', teamOrdinal: 1 }, { id: 'b', shape: 'frontline/2', teamOrdinal: 2 }];
    const view = zoneDiagram({ width: 72, length: 48 }, zones, () => [[{ x: 0, y: 0 }, { x: 72, y: 0 }, { x: 72, y: 12 }]], 288, 400);
    expect([view.width, view.height, view.style]).toEqual([288, 192, 'Frontline']);
    expect(view.zones[0]!.polygons[0]).toEqual([{ x: 0, y: 192 }, { x: 288, y: 192 }, { x: 288, y: 144 }]);
    expect(styleOf([])).toBeNull();
    expect(zoneDiagram({ width: 72, length: 48 }, [], () => [], 288, 400).style).toBe('No zones');
  });
});

describe('the setup rows', () => {
  it('objectives are their label and position', () => {
    const m = (id: string, x: number, y: number, label?: string): Marker =>
      ({ id, kind: 'objective', position: { x, y, z: 0 }, yaw: { degrees: 0 }, diameter: 3, label, visual: { builtin: 'objective-ring' } }) as Marker;
    const draft = { markers: [m('a', 12, 30.25), m('b', 60, 8, 'Relay')] } as unknown as TableState;
    expect(objectiveRows(draft).map((r) => [r.label, r.position])).toEqual([['Objective 1', '(12, 30.3)'], ['Relay', '(60, 8)']]);
  });

  it('a team folds to one line of names and armies', () => {
    const member = (name: string, army: string | null, noArmyWarns: boolean) =>
      ({ participantId: name, name, army, noArmyWarns, you: false, myArmy: null, moves: [] });
    const col = (members: ReturnType<typeof member>[]) => ({ members } as unknown as TeamColumn);
    expect(columnLine(col([member('Clay', 'Orcs · 3.6', false), member('Ben', null, false)]))).toBe('Clay · Orcs · 3.6, Ben');
    expect(columnLine(col([member('Sam', null, true)]))).toBe('Sam · no army yet');
    expect(columnLine(col([]))).toBe('No players yet');
  });
});
