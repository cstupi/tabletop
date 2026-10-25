// viewer/src/lib/play/handMove.ts
// client/src/Table/TableInput.cs: MayDrag and OnLeftUp's free moves. A hand move is a drag outside an Ask: judged by the server under the
// game's enforcement (refused under strict), so a model drag starts under strict only when every model is proposing.

import type { Geometry, Placement, Point3, Yaw } from '../geometry';
import type { Projection } from '../live/apply';
import type { Enforcement, GameStatus, ModelRelocation, MoveMarker, MoveModels } from '../protocol/wire.generated';
import { allWords, placement as contactsOfPlacement } from './contactCheck';
import type { MarkerLocks } from './markerLocks';
import { terrainMayDrag } from './markerLocks';
import type { Names } from './names';

export type DragTarget =
  | { readonly kind: 'models'; readonly ids: readonly string[] }
  | { readonly kind: 'marker'; readonly id: string }
  | { readonly kind: 'terrain'; readonly id: string };

/** A watcher drags nothing; models need every one proposing or a game that is not strict; a locked objective never drags; terrain only in setup. */
export function mayDrag(
  target: DragTarget, watching: boolean, proposing: ReadonlySet<string>, enforcement: Enforcement,
  status: GameStatus | null, p: Projection | null, locks: MarkerLocks,
): boolean {
  if (watching) return false;
  switch (target.kind) {
    case 'models': return target.ids.every((id) => proposing.has(id)) || enforcement !== 'strict';
    case 'marker': return locks.mayDrag(status, p, target.id);
    case 'terrain': return terrainMayDrag(status);
  }
}

/** A hand move's relocations: from each grab to its landing and yaw (OnLeftUp). */
export function handMoves(grabs: ReadonlyMap<string, Placement>, landings: ReadonlyMap<string, Placement>): ModelRelocation[] {
  return [...landings].map(([modelId, to]) => ({
    modelId, from: { kind: 'table', placement: grabs.get(modelId)! }, to: { kind: 'table', placement: to },
  }));
}

/** What a hand drop sends (HandMoves.Models). */
export const handMoveCommand = (grabs: ReadonlyMap<string, Placement>, landings: ReadonlyMap<string, Placement>): MoveModels =>
  ({ kind: 'move_models', moves: handMoves(grabs, landings) });

/** While in hand: ContactCheck.Placement's words for the held models (cleared when the drag ends; the server judges the drop). */
export function handMarks(g: Geometry, p: Projection, held: ReadonlyMap<string, Placement>, n: Names): Map<string, string> {
  return allWords(contactsOfPlacement(g, p, held), undefined, (id) => n.unit(id));
}

export function markerMove(id: string, at: Point3, yaw: Yaw): MoveMarker {
  return { kind: 'move_marker', markerId: id, position: at, yaw };
}
