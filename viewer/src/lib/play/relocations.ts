// viewer/src/lib/play/relocations.ts
// client/src/Core/AnimationQueue.cs's Relocations: every model an event relocates, in order. A hand move's ModelsMoved is its own event; an Ask
// answer's move arrives as a ModelsMoved among a step's changes.

import type { GameEvent, ModelRelocation } from '../protocol/wire.generated';

export function relocations(e: GameEvent): readonly ModelRelocation[] {
  const p = e.payload;
  if (p.kind === 'models_moved') return p.moves;
  if (p.kind === 'step_completed') return p.changes.flatMap((c) => (c.kind === 'models_moved' ? c.moves : []));
  return [];
}
