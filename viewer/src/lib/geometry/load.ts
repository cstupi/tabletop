import type { Contact, Geometry, Nearest, PieceEnd, Segment, XY, HeldEnd } from './types';
import type { Point3, Yaw } from './types';

interface Exports { [method: string]: (json: string) => string }

const loads = new Map<string, Promise<Geometry>>();

/** Fetches the .NET runtime and the facade from `base` (default `${import.meta.env.BASE_URL}geometry/_framework/`) on the
 *  first call, once per base; a failed load is forgotten so the next call retries. */
export function loadGeometry(base: string = `${import.meta.env.BASE_URL}geometry/_framework/`): Promise<Geometry> {
  let load = loads.get(base);
  if (!load) {
    load = fetchRuntime(base);
    loads.set(base, load);
    load.catch(() => { if (loads.get(base) === load) loads.delete(base); });
  }
  return load;
}

async function fetchRuntime(base: string): Promise<Geometry> {
  const { dotnet } = await import(/* @vite-ignore */ `${base}dotnet.js`);
  const runtime = await dotnet.create();
  const exports = (await runtime.getAssemblyExports('Tabletop.Geometry.Wasm')).Tabletop.Geometry.Wasm.Exports as Exports;
  return wrap(exports);
}

function wrap(exports: Exports): Geometry {
  const call = (method: string, json: string): string => {
    const f = exports[method];
    if (!f) throw new Error(`Geometry has no method ${method}`);
    return f(json);
  };
  const ask = <T>(method: string, request: unknown): T => JSON.parse(call(method, JSON.stringify(request))) as T;
  // The facade reads a terrain piece's id, position, yaw, scale and volumes; the mesh stays behind.
  const pieces = (terrain: Parameters<Geometry['landPiece']>[0]) =>
    terrain.map((t) => ({ id: t.id, position: t.position, yaw: t.yaw, scale: t.scale, volumes: t.volumes }));
  return {
    landModels: (held, stationary, walls, terrain) => ask<HeldEnd[]>('LandModels', { held, stationary, walls, terrain: pieces(terrain) }),
    landPiece: (terrain, pieceId, at, to) => ask<PieceEnd>('LandPiece', { terrain: pieces(terrain), pieceId, at, to }),
    turnPiece: (terrain, pieceId, yaw) => ask<Point3>('TurnPiece', { terrain: pieces(terrain), pieceId, yaw }),
    modelSurfaces: (model, terrain) => ask<number[]>('ModelSurfaces', { model, terrain: pieces(terrain) }),
    restModel: (model, terrain, level) => ask<number>('RestModel', { model, terrain: pieces(terrain), level }),
    pieceSurfaces: (terrain, pieceId, at) => ask<number[]>('PieceSurfaces', { terrain: pieces(terrain), pieceId, at }),
    restPiece: (terrain, pieceId, at, level) => ask<number>('RestPiece', { terrain: pieces(terrain), pieceId, at, level }),
    contacts: (models, proposed, terrain, terms) => new Map(
      ask<Contact[]>('Contacts', { models, proposed: Object.fromEntries(proposed), terrain: pieces(terrain), terms }).map((c) => [c.modelId, c])),
    overlapBases: (a, b) => ask<boolean>('OverlapBases', { a, b }),
    edgeToEdge: (a, b) => ask<number>('EdgeToEdge', { a, b }),
    bodyToBody: (a, b) => ask<number>('BodyToBody', { a, b }),
    edgeToPoint: (model, point) => ask<number>('EdgeToPoint', { model, point }),
    edgeToMarker: (model, centre, diameter) => ask<number>('EdgeToMarker', { model, centre, diameter }),
    nearestPair: (from, to) => from.length === 0 || to.length === 0 ? null : ask<Nearest>('NearestPair', { from, to }),
    rangeOutline: (models, range) => ask<Segment[]>('RangeOutline', { models, range }),
    zonePolygons: (shape, size) => ask<XY[][]>('ZonePolygons', { shape, size }),
    zoneContains: (shape, size, point) => ask<boolean>('ZoneContains', { shape, size, point }),
    zoneHoldsBase: (shape, size, model) => ask<boolean>('ZoneHoldsBase', { shape, size, model }),
    tableHoldsBase: (size, model) => ask<boolean>('TableHoldsBase', { size, model }),
    exitYaw: (body, container, within, at, terrain, impassable, size) =>
      ask<Yaw | null>('ExitYaw', { body, container, within, at, impassable, terrain: pieces(terrain), size }),
    call,
  };
}
