// viewer/src/lib/play/terrainLabel.ts
// client/src/Core/TerrainLabel.cs: a terrain piece hovered in play, one line per volume: the piece's name, the volume's declarations
// (Open with none) and its height range on the table. A built-in is named by its catalog label; an asset piece is "Terrain". The words
// are the terrain card's (setup/terrainCard.ts DECLARATION_WORDS), spelled out. The selected piece's details are the terrain card's.

import { declarationsOf } from '../live/project';
import type { TerrainPlacement, Volume } from '../protocol/wire.generated';
import { nameOf as catalogName, type Catalog } from '../setup/catalog';
import { DECLARATION_WORDS, inches } from '../setup/terrainCard';

export function terrainLabelOf(t: TerrainPlacement, catalog: Catalog): string {
  const name = nameOf(t, catalog);
  return t.volumes.map((tv) => {
    const flags = flagsOf(tv.volume);
    return `${name} · ${flags.length === 0 ? 'Open' : flags.join(', ')} · ${heightsOf(t, tv.volume)}`;
  }).join('\n');
}

/** The built-in's label, else "Terrain". */
export const nameOf = (t: TerrainPlacement, catalog: Catalog): string => catalogName(t, catalog);

/** The volume's declarations in the label's order; empty for an open volume. */
export function flagsOf(v: Volume): string[] {
  const on = new Set(declarationsOf(v.declarations));
  return DECLARATION_WORDS.filter(([name]) => on.has(name)).map(([, word]) => word);
}

/** The volume's height range on the table, scale and elevation applied: `0–4"`. */
export const heightsOf = (t: TerrainPlacement, v: Volume): string =>
  `${inches(t.position.z + v.bottom * t.scale)}–${inches(t.position.z + v.top * t.scale)}"`;
