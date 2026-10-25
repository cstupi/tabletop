// viewer/src/lib/setup/terrainGroups.ts
// The setup sidebar's terrain list: the catalog searched by name and grouped by what a piece does on the table, the order a table is
// balanced in (sight blockers, then impassable, dangerous, cover, difficult, then the rest). Also the words for textures and zone styles.

import { declarationsOf } from '../live/project';
import type { BuiltinTerrain, Catalog } from './catalog';
import { DECLARATION_WORDS } from './terrainCard';

export interface TerrainGroup { readonly title: string; readonly entries: readonly BuiltinTerrain[] }

/** A piece's group: the first of these declarations it carries, else Open ground. */
const GROUPS: readonly (readonly [string, readonly string[]])[] = [
  ['Blocks line of sight', ['blocks_line_of_sight']],
  ['Impassable', ['impassable']],
  ['Dangerous', ['dangerous', 'dangerous_on_rush_charge']],
  ['Cover', ['cover']],
  ['Difficult', ['difficult']],
];
export const OPEN_GROUP = 'Open ground';

const wordsOf = (entry: BuiltinTerrain): string[] => {
  const on = new Set(declarationsOf(entry.volume.declarations));
  return DECLARATION_WORDS.filter(([name]) => on.has(name)).map(([, word]) => word);
};

/** One line under a terrain row: its declarations in words, "Open" with none. */
export const terrainSummary = (entry: BuiltinTerrain): string => {
  const words = wordsOf(entry);
  return words.length === 0 ? 'Open' : words.join(', ');
};

/** True when every word of the query starts a word of the piece's name or its declarations. */
export function matches(entry: BuiltinTerrain, query: string): boolean {
  const wanted = query.toLowerCase().split(/\s+/).filter((w) => w !== '');
  const words = `${entry.label} ${wordsOf(entry).join(' ')}`.toLowerCase().split(/[^a-z0-9]+/).filter((w) => w !== '');
  return wanted.every((q) => words.some((w) => w.startsWith(q)));
}

/** The catalog's terrain matching the query, grouped and sorted by name; empty groups are left out. */
export function terrainGroups(catalog: Catalog, query = ''): TerrainGroup[] {
  const groups = new Map<string, BuiltinTerrain[]>([...GROUPS.map(([title]) => [title, []] as [string, BuiltinTerrain[]]), [OPEN_GROUP, []]]);
  for (const entry of catalog.terrain.values()) {
    if (!matches(entry, query)) continue;
    const on = new Set(declarationsOf(entry.volume.declarations));
    const title = GROUPS.find(([, names]) => names.some((n) => on.has(n)))?.[0] ?? OPEN_GROUP;
    groups.get(title)!.push(entry);
  }
  return [...groups].filter(([, list]) => list.length > 0)
    .map(([title, list]) => ({ title, entries: list.sort((a, b) => a.label.localeCompare(b.label)) }));
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
