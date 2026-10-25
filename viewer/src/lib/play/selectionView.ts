// viewer/src/lib/play/selectionView.ts
// client/src/Core/SelectionView.cs: what a selection shows: the models to light and the line naming them. A plain click selects the unit
// (no model ids); shift selects one model.

import type { Projection } from '../live/apply';
import type { Names } from './names';

export interface Selection { readonly pieceId: string; readonly modelIds: readonly string[] }

export function selectedModels(p: Projection, s: Selection | null): string[] {
  if (s === null) return [];
  if (s.modelIds.length > 0) return s.modelIds.filter((id) => p.models.has(id));
  return [...p.models.values()].filter((m) => m.unitId === s.pieceId).map((m) => m.id);
}

export function describeSelection(n: Names, p: Projection, s: Selection | null): string | null {
  if (s === null) return null;
  if (s.modelIds.length > 0) return s.modelIds.map((id) => n.model(id)).join(', ');
  if (!p.units.has(s.pieceId)) return n.piece(s.pieceId);
  const models = [...p.models.values()].filter((m) => m.unitId === s.pieceId);
  return `${n.unit(s.pieceId)}: ${models.filter((m) => m.location.kind === 'table').length} of ${models.length} models on the table`;
}
