// viewer/src/lib/setup/terrainCard.ts
// TerrainCardContent, MarkerCardContent and TerrainLabel (client/src/Core), ported: what the card shows for the selected terrain piece
// or setup marker. Editable in setup only.

import { declarationsOf } from '../live/project';
import type { GameStatus, TableState, TerrainPlacement, Volume, Yaw } from '../protocol/wire.generated';
import { nameOf, type Catalog } from './catalog';

export interface CardVolume {
  readonly id: string; readonly kind: string; readonly footprint: string; readonly heights: string;
  readonly declarations: readonly string[]; readonly notes: string | null;
}
export interface TerrainCard {
  readonly pieceId: string; readonly name: string; readonly source: string; readonly facing: number; readonly scale: number;
  readonly height: string; readonly volumes: readonly CardVolume[]; readonly editable: boolean;
}
export interface MarkerCard { readonly pieceId: string; readonly name: string; readonly diameter: string; readonly label: string | null }

/** TerrainLabel's order: the seven declarations with their words. */
export const DECLARATION_WORDS: readonly (readonly [string, string])[] = [
  ['cover', 'Cover'], ['difficult', 'Difficult'], ['dangerous', 'Dangerous'], ['dangerous_on_rush_charge', 'Dangerous on Rush/Charge'],
  ['impassable', 'Impassable'], ['blocks_line_of_sight', 'Blocks LOS'], ['see_in_and_out', 'See in/out'],
];

/** TerrainLabel.Inches: "0.#". */
export const inches = (v: number): string => String(Number(v.toFixed(1)));

/** Degrees counter-clockwise from above, whole, 0–359. */
export const facingOf = (yaw: Yaw): number => (((Math.round(yaw.degrees) % 360) + 360) % 360);

const heightsOf = (t: TerrainPlacement, v: Volume): string => `${inches(t.position.z + v.bottom * t.scale)}–${inches(t.position.z + v.top * t.scale)}"`;
const footprintOf = (v: Volume, scale: number): string => v.footprint.kind === 'circle'
  ? `${inches(2 * v.footprint.a * scale)}" across`
  : `${inches(v.footprint.a * scale)} × ${inches(v.footprint.b * scale)}"`;
const kindWord = (kind: string): string => kind.charAt(0).toUpperCase() + kind.slice(1);
const inPlay = (status: GameStatus | null): boolean => status === 'setup' || status === 'playing' || status === 'ended';

/** The selected piece's card when it is a terrain piece on the table, in setup or play. */
export function terrainCardOf(t: TableState | null, status: GameStatus | null, selected: string | null, catalog: Catalog): TerrainCard | null {
  if (!inPlay(status) || selected === null) return null;
  const piece = t?.terrain.find((p) => p.id === selected);
  if (piece === undefined) return null;
  const source = piece.mesh.builtin !== undefined ? `Built-in ${piece.mesh.builtin}` : `Library asset ${(piece.mesh.assetId ?? '').replaceAll('-', '').slice(0, 8)}`;
  const height = piece.volumes.length === 0 ? ''
    : `${inches(piece.position.z + Math.min(...piece.volumes.map((v) => v.volume.bottom)) * piece.scale)}–${inches(piece.position.z + Math.max(...piece.volumes.map((v) => v.volume.top)) * piece.scale)}"`;
  const volumes = piece.volumes.map((tv): CardVolume => {
    const set = new Set(declarationsOf(tv.volume.declarations));
    const words = DECLARATION_WORDS.filter(([name]) => set.has(name)).map(([, word]) => word);
    return {
      id: tv.id, kind: kindWord(tv.volume.kind), footprint: footprintOf(tv.volume, piece.scale), heights: heightsOf(piece, tv.volume),
      declarations: words.length === 0 ? ['Open'] : words, notes: tv.volume.notes ?? null,
    };
  });
  return {
    pieceId: piece.id, name: nameOf(piece, catalog), source, facing: facingOf(piece.yaw), scale: piece.scale, height, volumes,
    editable: status === 'setup',
  };
}

/** A selected setup marker on the same card: kind, diameter and label. Setup only. */
export function markerCardOf(t: TableState | null, status: GameStatus | null, selected: string | null): MarkerCard | null {
  if (status !== 'setup' || selected === null) return null;
  const m = t?.markers.find((x) => x.id === selected);
  return m === undefined ? null
    : { pieceId: m.id, name: kindWord(m.kind), diameter: `${inches(m.diameter)}" across`, label: m.label ?? null };
}
