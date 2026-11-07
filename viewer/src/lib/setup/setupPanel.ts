// viewer/src/lib/setup/setupPanel.ts
// Ports of Godot's ZoneDiagram and ObjectiveRows (client/src/Core/ZoneDiagram.cs, SetupPanel.cs): the Zones section's drawing and the
// Objectives section's rows. Zone polygons come in resolved (Geometry.zonePolygons), so this file needs none of the WebAssembly.

import type { DeploymentZone, TableSize, TableState } from '../protocol/wire.generated';
import { zoneStyleLabel } from './terrainGroups';

export interface XY { readonly x: number; readonly y: number }
export interface DiagramZone { readonly ordinal: number; readonly polygons: readonly (readonly XY[])[] }
export interface ZoneDiagramView { readonly width: number; readonly height: number; readonly zones: readonly DiagramZone[]; readonly style: string }

/** The style of a draft's zones from their shape ids ("frontline/1" → "frontline"), null with none. */
export const styleOf = (zones: readonly DeploymentZone[]): string | null => zones[0]?.shape.split('/')[0] ?? null;

/** The table outline at the width (at most maxHeight tall) and each zone, the table's Y up the drawing as the setup camera shows it. */
export function zoneDiagram(size: TableSize, zones: readonly DeploymentZone[], polygonsOf: (shape: string) => readonly (readonly XY[])[],
  width: number, maxHeight: number): ZoneDiagramView {
  const scale = Math.min(width / size.width, maxHeight / size.length);
  const w = size.width * scale, h = size.length * scale;
  const drawn: DiagramZone[] = [];
  for (const zone of [...zones].sort((a, b) => a.teamOrdinal - b.teamOrdinal)) {
    let polygons: readonly (readonly XY[])[];
    try { polygons = polygonsOf(zone.shape); } catch { continue; }   // a shape this build does not know
    drawn.push({ ordinal: zone.teamOrdinal, polygons: polygons.map((p) => p.map((v) => ({ x: v.x * scale, y: h - v.y * scale }))) });
  }
  const style = styleOf(zones);
  return { width: w, height: h, zones: drawn, style: style === null ? 'No zones' : zoneStyleLabel(style) };
}

export interface ObjectiveRow { readonly id: string; readonly label: string; readonly position: string }

const inches = (n: number): string => String(Math.round(n * 10) / 10);

/** The draft's objectives in order; one without a label is "Objective n" by its place in the list. */
export const objectiveRows = (draft: TableState | null): ObjectiveRow[] => draft === null ? [] : draft.markers.filter((m) => m.kind === 'objective')
  .map((m, i) => ({ id: m.id, label: (m.label ?? '').trim() === '' ? `Objective ${i + 1}` : m.label!, position: `(${inches(m.position.x)}, ${inches(m.position.y)})` }));

/** The HTML drag type a terrain tile carries from the sidebar to the board: the catalog id. */
export const TILE_TYPE = 'application/x-tabletop-terrain';
