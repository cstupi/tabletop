// viewer/src/lib/setup/edits.ts
// The setup edits Godot's toolbar, card, menu and drops send (SetupToolbar, TerrainCard, MenuModel). Geometry comes in as a
// parameter, so this file needs none of the WebAssembly to load.

import { declarationsOf } from '../live/project';
import type { Geometry } from '../geometry';
import type {
  AddSetupMarker, AddTerrain, Edit, Marker, MoveTerrain, OverrideTerrainVolumes, RemoveSetupMarker, RemoveTerrain, ReplaceDraft, ScaleTerrain,
  SetTableSize, SetTexture, SetZoneStyle, SwapTerrainMesh, SwapZones, TableSize, TableState, TerrainPlacement, Volume,
} from '../protocol/wire.generated';
import { type BuiltinTerrain, declarationsWire, placeAsset, placeBuiltin } from './catalog';
import { NOTES, beside, freeSpot } from './limits';

export const TERRAIN_TURN = 15, SCALE_MIN = 0.25, SCALE_MAX = 4, SCALE_STEP = 0.25;

/** ZoneStyles.All, in order. */
export const ZONE_STYLES: readonly string[] = [
  'frontline', 'ground-war', 'side-battle', 'disordered', 'spearhead', 'opposing-forces', 'free-for-all',
];

/** ZoneStyles.ShapesOf: 'style/1'…, four for free-for-all. */
export const zoneShapes = (style: string): string[] =>
  Array.from({ length: style === 'free-for-all' ? 4 : 2 }, (_, i) => `${style}/${i + 1}`);

export const objective = (size: TableSize, markers: readonly Marker[]): AddSetupMarker => ({
  kind: 'add_setup_marker',
  marker: { id: crypto.randomUUID(), kind: 'objective', position: freeSpot(size, markers, 3), yaw: { degrees: 0 }, diameter: 3, visual: { builtin: 'objective-ring' } },
});

/** MenuModel.TurnInDraft over Geometry.turnPiece: facing kept in 0–360; null for no such piece or a non-finite angle. */
export function turnInDraft(g: Geometry, d: TableState, pieceId: string, degrees: number): MoveTerrain | null {
  if (!d.terrain.some((t) => t.id === pieceId) || !Number.isFinite(degrees)) return null;
  const yaw = { degrees: ((degrees % 360) + 360) % 360 };
  return { kind: 'move_terrain', placementId: pieceId, position: g.turnPiece(d.terrain, pieceId, yaw), yaw };
}

export function removeFromDraft(d: TableState, id: string): RemoveTerrain | RemoveSetupMarker | null {
  if (d.terrain.some((t) => t.id === id)) return { kind: 'remove_terrain', placementId: id };
  if (d.markers.some((m) => m.id === id)) return { kind: 'remove_setup_marker', markerId: id };
  return null;
}

export interface MenuItem { readonly label: string; readonly input: 'none' | 'integer'; readonly edit: (text: string) => Edit | null }

/** MenuModel.ForSetupPiece: terrain Turn left 15°, Turn right 15°, Set facing…, then Remove; a marker Remove; else []. */
export function setupMenu(g: Geometry, d: TableState, id: string): MenuItem[] {
  const remove = removeFromDraft(d, id);
  if (remove === null) return [];
  const items: MenuItem[] = [];
  const piece = d.terrain.find((t) => t.id === id);
  if (piece !== undefined) {
    items.push({ label: 'Turn left 15°', input: 'none', edit: () => turnInDraft(g, d, id, piece.yaw.degrees + TERRAIN_TURN) });
    items.push({ label: 'Turn right 15°', input: 'none', edit: () => turnInDraft(g, d, id, piece.yaw.degrees - TERRAIN_TURN) });
    items.push({
      label: 'Set facing…', input: 'integer',
      edit: (text) => /^\s*-?\d+\s*$/.test(text) ? turnInDraft(g, d, id, Number.parseInt(text, 10)) : null,
    });
  }
  items.push({ label: 'Remove', input: 'none', edit: () => remove });
  return items;
}

/** TerrainCard.RenderVolumeEdits: the whole volume list with one volume changed. */
export function overrideVolume(piece: TerrainPlacement, volumeId: string, change: (v: Volume) => Volume): OverrideTerrainVolumes {
  return {
    kind: 'override_terrain_volumes', placementId: piece.id,
    volumes: piece.volumes.map((v) => v.id !== volumeId ? v : { ...v, volume: change(v.volume) }),
  };
}

/** Notes clipped to 500; '' clears. */
export const withNotes = (text: string) => (v: Volume): Volume => {
  const { notes: _ignored, ...rest } = v;
  void _ignored;
  const clipped = text.slice(0, NOTES);
  return clipped === '' ? rest : { ...rest, notes: clipped };
};

export const withDeclaration = (name: string, on: boolean) => (v: Volume): Volume => {
  const set = new Set(declarationsOf(v.declarations));
  if (on) set.add(name); else set.delete(name);
  return { ...v, declarations: declarationsWire([...set]) };
};

/** A circle sets a and b (b mirrors the radius); a rect sets the one field. */
export const withFootprint = (field: 'a' | 'b', value: number) => (v: Volume): Volume => ({
  ...v,
  footprint: v.footprint.kind === 'circle' ? { kind: 'circle', a: value, b: value } : { ...v.footprint, [field]: value },
});

export const withKind = (kind: Volume['kind']) => (v: Volume): Volume => ({ ...v, kind });
export const withBottom = (bottom: number) => (v: Volume): Volume => ({ ...v, bottom });
export const withTop = (top: number) => (v: Volume): Volume => ({ ...v, top });

export const clearTerrain = (d: TableState): ReplaceDraft => ({ kind: 'replace_draft', draft: { ...d, terrain: [] } });

// One function per toolbar and card control (SetupEdits.cs).
export const tableSize = (width: number, length: number): SetTableSize => ({ kind: 'set_table_size', size: { width, length } });
export const texture = (builtin: string): SetTexture => ({ kind: 'set_texture', texture: { builtin } });
export const addBuiltin = (entry: BuiltinTerrain, size: TableSize, random?: () => number): AddTerrain =>
  ({ kind: 'add_terrain', placement: placeBuiltin(entry, beside(size, random), { degrees: 0 }) });
/** A tile dropped on the board (SetupEdits.DropBuiltin): the piece where a lifted setup drag lands it, clear of Impassable pieces. */
export const dropBuiltin = (geometry: Geometry, entry: BuiltinTerrain, terrain: readonly TerrainPlacement[], at: { x: number; y: number }): AddTerrain => {
  const placement = placeBuiltin(entry, { x: at.x, y: at.y, z: 0 }, { degrees: 0 });
  const end = geometry.landPiece([...terrain, placement], placement.id, { position: placement.position, yaw: placement.yaw }, at);
  return { kind: 'add_terrain', placement: { ...placement, position: end.lands } };
};
/** The asset's volumes under new ids, scale 1 (the asset's own scale is already in its mesh and volumes). */
export const addAsset = (assetId: string, volumes: readonly Volume[], size: TableSize, random?: () => number): AddTerrain =>
  ({ kind: 'add_terrain', placement: placeAsset(assetId, volumes.map((volume) => ({ id: crypto.randomUUID(), volume })), beside(size, random)) });
export const zones = (style: string): SetZoneStyle => ({ kind: 'set_zone_style', style, shapes: zoneShapes(style) });
export const swapZones = (a: number, b: number): SwapZones => ({ kind: 'swap_zones', teamOrdinalA: a, teamOrdinalB: b });
export const scale = (placementId: string, value: number): ScaleTerrain => ({ kind: 'scale_terrain', placementId, scale: value });
export const swapBuiltin = (placementId: string, builtin: string): SwapTerrainMesh => ({ kind: 'swap_terrain_mesh', placementId, mesh: { builtin } });
export const swapAsset = (placementId: string, assetId: string): SwapTerrainMesh => ({ kind: 'swap_terrain_mesh', placementId, mesh: { assetId } });
export const removeTerrain = (placementId: string): RemoveTerrain => ({ kind: 'remove_terrain', placementId });
export const removeMarker = (markerId: string): RemoveSetupMarker => ({ kind: 'remove_setup_marker', markerId });
/** Reset volumes: the given volumes under new ids. */
export const resetVolumes = (placementId: string, volumes: readonly Volume[]): OverrideTerrainVolumes =>
  ({ kind: 'override_terrain_volumes', placementId, volumes: volumes.map((volume) => ({ id: crypto.randomUUID(), volume })) });
