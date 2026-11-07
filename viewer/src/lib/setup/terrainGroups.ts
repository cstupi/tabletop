// viewer/src/lib/setup/terrainGroups.ts
// The setup sidebar's terrain list: the catalog searched by name and grouped by what a piece does on the table, the order a table is
// balanced in (sight blockers, then impassable, dangerous, cover, difficult, then the rest). Also the words for textures and zone styles.

import { declarationsOf } from '../live/project';
import type { BuiltinTerrain, Catalog } from './catalog';
import { declarationWords, type TerrainWords } from './terrainCard';

export interface TerrainGroup { readonly title: string; readonly entries: readonly BuiltinTerrain[] }

/** A piece's group: the first of these declarations it carries, else Open ground. A group of one declaration is titled in the server's word. */
const GROUPS: readonly (readonly [string, readonly string[]])[] = [
  ['Blocks line of sight', ['blocks_line_of_sight']],
  ['Impassable', ['impassable']],
  ['Dangerous', ['dangerous', 'dangerous_on_rush_charge']],
  ['Cover', ['cover']],
  ['Difficult', ['difficult', 'steep']],
];
export const OPEN_GROUP = 'Open ground';

const titleOf = (title: string, given?: TerrainWords | null): string => {
  const names = GROUPS.find(([t]) => t === title)?.[1];
  return names?.length === 1 ? given?.[names[0]!] ?? title : title;
};

const wordsOf = (entry: BuiltinTerrain, given?: TerrainWords | null): string[] => {
  const on = new Set(declarationsOf(entry.volume.declarations));
  return declarationWords(given).filter(([name]) => on.has(name)).map(([, word]) => word);
};

/** One line under a terrain row: its declarations in words (the server's where given), "Open" with none. */
export const terrainSummary = (entry: BuiltinTerrain, given?: TerrainWords | null): string => {
  const words = wordsOf(entry, given);
  return words.length === 0 ? 'Open' : words.join(', ');
};

/** True when every word of the query starts a word of the piece's name or its declarations. */
export function matches(entry: BuiltinTerrain, query: string, given?: TerrainWords | null): boolean {
  const wanted = query.toLowerCase().split(/\s+/).filter((w) => w !== '');
  const words = `${entry.label} ${wordsOf(entry, given).join(' ')}`.toLowerCase().split(/[^a-z0-9]+/).filter((w) => w !== '');
  return wanted.every((q) => words.some((w) => w.startsWith(q)));
}

/** The catalog's terrain matching the query, grouped and sorted by name; empty groups are left out. */
export function terrainGroups(catalog: Catalog, query = '', given?: TerrainWords | null): TerrainGroup[] {
  const groups = new Map<string, BuiltinTerrain[]>([...GROUPS.map(([title]) => [title, []] as [string, BuiltinTerrain[]]), [OPEN_GROUP, []]]);
  for (const entry of catalog.terrain.values()) {
    if (!matches(entry, query, given)) continue;
    const on = new Set(declarationsOf(entry.volume.declarations));
    const title = GROUPS.find(([, names]) => names.some((n) => on.has(n)))?.[0] ?? OPEN_GROUP;
    groups.get(title)!.push(entry);
  }
  return [...groups].filter(([, list]) => list.length > 0)
    .map(([title, list]) => ({ title: titleOf(title, given), entries: list.sort((a, b) => a.label.localeCompare(b.label)) }));
}

/** A terrain group as a tile and legend dot (TerrainTiles.cs): its icon key, legend word and colour. */
export interface TerrainKind { readonly key: string; readonly legend: string; readonly colour: string }
export interface TerrainTile { readonly entry: BuiltinTerrain; readonly kind: TerrainKind; readonly summary: string }

/** The six groups in the order a table is balanced; each a group of GROUPS above, then Open ground. */
export const TERRAIN_KINDS: readonly TerrainKind[] = [
  { key: 'blocks-sight', legend: 'Blocks sight', colour: '#C98CFF' },
  { key: 'impassable', legend: 'Impassable', colour: '#FF6B5C' },
  { key: 'dangerous', legend: 'Dangerous', colour: '#FF9D4E' },
  { key: 'cover', legend: 'Cover', colour: '#6DDF7C' },
  { key: 'difficult', legend: 'Difficult', colour: '#FFC44E' },
  { key: 'open-ground', legend: 'Open', colour: '#8B93A1' },
];

/** A piece's group: the first of GROUPS it carries a declaration of, else Open. */
export function kindOf(entry: BuiltinTerrain): TerrainKind {
  const on = new Set(declarationsOf(entry.volume.declarations));
  const at = GROUPS.findIndex(([, names]) => names.some((n) => on.has(n)));
  return TERRAIN_KINDS[at < 0 ? TERRAIN_KINDS.length - 1 : at]!;
}

/** The catalog as tiles: matching the query, the hidden groups left out, by group then name. */
export function terrainTiles(catalog: Catalog, query = '', hidden: ReadonlySet<string> = new Set(), given?: TerrainWords | null): TerrainTile[] {
  return [...catalog.terrain.values()].filter((e) => matches(e, query, given))
    .map((entry) => ({ entry, kind: kindOf(entry), summary: terrainSummary(entry, given) }))
    .filter((t) => !hidden.has(t.kind.key))
    .sort((a, b) => TERRAIN_KINDS.indexOf(a.kind) - TERRAIN_KINDS.indexOf(b.kind) || a.entry.label.localeCompare(b.entry.label));
}

/** The legend's toggle: a dot hides its group, again shows it. */
export function toggleKind(hidden: ReadonlySet<string>, key: string): ReadonlySet<string> {
  const next = new Set(hidden);
  if (!next.delete(key)) next.add(key);
  return next;
}

/** A catalog name as words: "large-forest" → "Large forest". */
export const wordsOfName = (name: string): string => {
  const spaced = name.replaceAll('-', ' ').replaceAll('_', ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
};

/** The table textures by name: "grass" → "Grass". */
export const textureLabel = (name: string): string => wordsOfName(name);

/** Grimdark Future's deployment styles (p.22, p.50) by their rulebook names. */
const ZONE_STYLE_LABELS: Record<string, string> = {
  frontline: 'Frontline', 'ground-war': 'Ground War', 'side-battle': 'Side Battle', disordered: 'Disordered', spearhead: 'Spearhead',
  'opposing-forces': 'Opposing Forces', 'free-for-all': 'Free-for-all',
};
export const zoneStyleLabel = (style: string): string => ZONE_STYLE_LABELS[style] ?? wordsOfName(style);
