// viewer/src/lib/play/terrainHover.ts
// client/src/Table/HoverLabels.cs DrawTerrain on the 2D board: in play the hovered terrain piece's panel beside the pointer
// (TerrainLabel.HoverOf: its name, height and one chip per effect), except for the selected piece, whose details are the terrain card's.

import type { TableState } from '../protocol/wire.generated';
import type { Catalog } from '../setup/catalog';
import { inches, type TerrainWords } from '../setup/terrainCard';
import { effectRows, pieceDeclarations, type TerrainEffectRow } from './terrainEffects';
import { nameOf } from './terrainLabel';

export interface TerrainHover { readonly name: string; readonly height: string; readonly effects: readonly TerrainEffectRow[] }

/** The panel for the hovered piece; null when nothing is hovered, the piece is selected or is not on the table. */
export function terrainHoverOf(table: TableState, catalog: Catalog, hovered: string | null, selected: string | null, words?: TerrainWords | null): TerrainHover | null {
  if (hovered === null || hovered === selected) return null;
  const piece = table.terrain.find((t) => t.id === hovered);
  if (piece === undefined || piece.volumes.length === 0) return null;
  const bottom = Math.min(...piece.volumes.map((v) => v.volume.bottom)), top = Math.max(...piece.volumes.map((v) => v.volume.top));
  const height = `${inches(piece.position.z + bottom * piece.scale)}–${inches(piece.position.z + top * piece.scale)}"`;
  return { name: nameOf(piece, catalog), height, effects: effectRows(pieceDeclarations(piece), words) };
}
