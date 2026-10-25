// viewer/src/lib/setup/catalog.ts
// BuiltinCatalog's terrain entries and Place (client/src/Core/BuiltinCatalog.cs, libs/Tabletop.Content/BuiltinTerrain.cs), ported. A
// placement of a built-in names the terrain id as its mesh (Godot's VisualRef.OfBuiltin(name)) and copies the volume, so a later
// catalog never changes a placed piece.

import packedCatalog from '@builtin-catalog';
import type { Api } from '../api/client';
import { DECLARATIONS } from '../board/palette';
import type { Point3, TerrainPlacement, TerrainVolume, Volume, Yaw } from '../protocol/wire.generated';

export interface BuiltinTerrain { readonly id: string; readonly label: string; readonly volume: Volume }
export interface Catalog { readonly terrain: ReadonlyMap<string, BuiltinTerrain>; readonly textures: readonly string[] }

const NAME = /^[a-z0-9][a-z0-9-]{0,63}$/;

interface Raw {
  meshes?: unknown; textures?: unknown; terrain?: unknown;
}
interface RawTerrain {
  id?: unknown; label?: unknown; mesh?: unknown; kind?: unknown; footprint?: { kind?: unknown; a?: unknown; b?: unknown };
  top?: unknown; declarations?: unknown; notes?: unknown;
}

const names = (value: unknown, what: string): string[] => {
  const list = Array.isArray(value) ? value : [];
  for (const n of list) if (typeof n !== 'string' || !NAME.test(n)) throw new Error(`The ${what} name '${String(n)}' is not a name.`);
  if (new Set(list).size !== list.length) throw new Error(`A ${what} is listed twice.`);
  return list as string[];
};
const inRange = (n: unknown, max: number): n is number => typeof n === 'number' && n > 0 && n <= max;

/** BuiltinCatalog.Parse's terrain and texture checks; throws Error naming the first fault (Godot's messages). */
export function parseCatalog(json: unknown): Catalog {
  if (typeof json !== 'object' || json === null) throw new Error('The catalog is empty.');
  const raw = json as Raw;
  const meshes = names(raw.meshes, 'mesh');
  const textures = names(raw.textures, 'texture');
  if (meshes.length === 0) throw new Error('The catalog names no mesh.');
  if (textures.length === 0) throw new Error('The catalog names no texture.');
  if (!Array.isArray(raw.terrain)) throw new Error('The catalog has no terrain.');
  const terrain = new Map<string, BuiltinTerrain>();
  for (const t of raw.terrain as RawTerrain[]) {
    const id = typeof t.id === 'string' && NAME.test(t.id) ? t.id : fail(`Terrain id '${String(t.id)}' is not a name.`);
    if (terrain.has(id)) throw new Error(`Terrain '${id}' is listed twice.`);
    if (typeof t.label !== 'string' || t.label.trim() === '' || t.label.length > 64) throw new Error(`Terrain '${id}' has no label, or one over 64 characters.`);
    if (typeof t.notes === 'string' && t.notes.length > 500) throw new Error(`Terrain '${id}' has notes over 500 characters.`);
    if (typeof t.mesh !== 'string' || !meshes.includes(t.mesh)) throw new Error(`Terrain '${id}' names mesh '${String(t.mesh)}', which the catalog does not list.`);
    if (t.kind !== 'solid' && t.kind !== 'area') throw new Error(`Terrain '${id}' has kind '${String(t.kind)}', not solid or area.`);
    const f = t.footprint;
    const footprint = f?.kind === 'circle' && inRange(f.a, 144) ? { kind: 'circle' as const, a: f.a, b: f.a }
      : f?.kind === 'rect' && inRange(f.a, 144) && inRange(f.b, 144) ? { kind: 'rect' as const, a: f.a, b: f.b }
      : fail(`Terrain '${id}' has no circle or rect footprint with positive sizes up to 144".`);
    if (!inRange(t.top, 144)) throw new Error(`Terrain '${id}' has top ${String(t.top)}, not above 0 and up to 144".`);
    const declared = Array.isArray(t.declarations) ? (t.declarations as unknown[]) : [];
    for (const d of declared) {
      if (!DECLARATIONS.includes(d as (typeof DECLARATIONS)[number])) throw new Error(`Terrain '${id}' declares '${String(d)}', which is not a terrain declaration.`);
    }
    terrain.set(id, {
      id, label: t.label,
      volume: {
        kind: t.kind, footprint, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top: t.top,
        declarations: declarationsWire(declared as string[]), ...(typeof t.notes === 'string' ? { notes: t.notes } : {}),
      },
    });
  }
  if (terrain.size === 0) throw new Error('The catalog has no terrain.');
  return { terrain, textures };
}

function fail(message: string): never { throw new Error(message); }

/** GET /v1/builtin, else (404) the packed file: the catalog in use, as Godot's BuiltinContent. */
export async function loadCatalog(api: Api, packed: unknown = packedCatalog): Promise<Catalog> {
  return parseCatalog((await api.builtinCatalog()) ?? packed);
}

/** The wire's [Flags] string: canonical order (palette DECLARATIONS), ", "-joined, "none" when empty. */
export function declarationsWire(declared: readonly string[]): string {
  const present = DECLARATIONS.filter((d) => declared.includes(d));
  return present.length === 0 ? 'none' : present.join(', ');
}

/** BuiltinCatalog.Place: a new placement of the built-in with one volume copied from the catalog, new ids. */
export function placeBuiltin(entry: BuiltinTerrain, position: Point3, yaw: Yaw, scale = 1): TerrainPlacement {
  return {
    id: crypto.randomUUID(), mesh: { builtin: entry.id }, position, yaw, scale,
    volumes: [{ id: crypto.randomUUID(), volume: entry.volume }],
  };
}

/** The built-in's volume list as a fresh set of ids (Reset volumes). */
export const volumesOfBuiltin = (entry: BuiltinTerrain): TerrainVolume[] => [{ id: crypto.randomUUID(), volume: entry.volume }];

/** AddLibraryTerrain: the asset's volumes with new ids, scale 1 (the asset's own scale is already in its mesh and volumes). */
export function placeAsset(assetId: string, volumes: readonly TerrainVolume[], position: Point3): TerrainPlacement {
  return {
    id: crypto.randomUUID(), mesh: { assetId }, position, yaw: { degrees: 0 }, scale: 1,
    volumes: volumes.map((v) => ({ id: crypto.randomUUID(), volume: v.volume })),
  };
}

/** TerrainLabel.NameOf: the built-in's label, else "Terrain". */
export function nameOf(piece: TerrainPlacement, catalog: Catalog): string {
  return (piece.mesh.builtin === undefined ? undefined : catalog.terrain.get(piece.mesh.builtin)?.label) ?? 'Terrain';
}
