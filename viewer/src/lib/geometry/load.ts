import type { CoherencyGap, Contact, Geometry, MoveBands, Nearest, PieceEnd, RouteFound, Segment, XY, HeldEnd } from './types';
import type { Point3, Yaw } from './types';
import { DownloadProgress } from './progress';

interface Exports { [method: string]: (json: string) => string }

const loads = new Map<string, Promise<Geometry>>();
const downloads = new Map<string, DownloadProgress>();
/** Where the runtime is served: `${import.meta.env.BASE_URL}geometry/_framework/` (the move workers load it from there too). */
export const geometryBase = (): string => `${import.meta.env.BASE_URL}geometry/_framework/`;
const defaultBase = geometryBase;

/** The download of `base`'s runtime: counts while loadGeometry(base) fetches, finished once it has settled. */
export function geometryDownload(base: string = defaultBase()): DownloadProgress {
  let p = downloads.get(base);
  if (!p) { p = new DownloadProgress(); downloads.set(base, p); }
  return p;
}

/** Fetches the .NET runtime and the facade from `base` (default `${import.meta.env.BASE_URL}geometry/_framework/`) on the
 *  first call, once per base; a failed load is forgotten so the next call retries. */
export function loadGeometry(base: string = defaultBase()): Promise<Geometry> {
  let load = loads.get(base);
  if (!load) {
    const progress = geometryDownload(base);
    progress.restart();
    load = fetchRuntime(base, progress);
    loads.set(base, load);
    load.then(() => progress.finish(), () => { progress.finish(); if (loads.get(base) === load) loads.delete(base); });
  }
  return load;
}

async function fetchRuntime(base: string, progress: DownloadProgress): Promise<Geometry> {
  const { dotnet } = await import(/* @vite-ignore */ `${base}dotnet.js`);
  // dotnet.js counts each resource as it arrives against those requested so far.
  const runtime = await dotnet
    .withModuleConfig({ onDownloadResourceProgress: (done: number, total: number) => progress.report(done, total) })
    .create();
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
    landModels: (held, stationary, walls, terrain, abut) =>
      ask<HeldEnd[]>('LandModels', { held, stationary, walls, terrain: pieces(terrain), ...(abut === undefined ? {} : { abut }) }),
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
    rangeOutline: (models, range, ranges) => ask<Segment[]>('RangeOutline', ranges === undefined ? { models, range } : { models, range, ranges }),
    clearanceOutline: (from, over, lifted, liftedWithin) => from.length === 0 ? [] : ask<Segment[]>('ClearanceOutline', { from, over, lifted: lifted ?? [], liftedWithin: liftedWithin ?? 0 }),
    coherencyGaps: (models, chain, elevated, spread) => ask<CoherencyGap[]>('CoherencyGaps', { models, chain, elevated, spread }),
    clearanceBreaks: (placed, from, over, lifted, liftedWithin) => ask<number>('ClearanceBreaks', { from, over, lifted: lifted ?? [], liftedWithin: liftedWithin ?? 0, placed }),
    zonePolygons: (shape, size, legs) => ask<XY[][]>('ZonePolygons', { shape, size, legs }),
    zoneContains: (shape, size, point, legs) => ask<boolean>('ZoneContains', { shape, size, point, legs }),
    zoneHoldsBase: (shape, size, model, legs, reach) => ask<boolean>('ZoneHoldsBase', { shape, size, model, legs, reach }),
    zoneReachOutline: (shape, size, reach, legs) => ask<Segment[]>('ZoneReachOutline', { shape, size, reach, legs }),
    tableHoldsBase: (size, model) => ask<boolean>('TableHoldsBase', { size, model }),
    exitYaw: (body, container, within, at, terrain, impassable, size) =>
      ask<Yaw | null>('ExitYaw', { body, container, within, at, impassable, terrain: pieces(terrain), size }),
    moveBands: (request, terrain) => ask<MoveBands>('MoveBands', { ...request, terrain: pieces(terrain) }),
    moveRoutes: (request, terrain) => request.models.length === 0 ? [] : ask<RouteFound[]>('MoveRoutes', { ...request, terrain: pieces(terrain) }),
    call,
  };
}
