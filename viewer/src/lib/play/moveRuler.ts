// viewer/src/lib/play/moveRuler.ts
// client/src/Core/MoveRuler.cs, the parts the spec keeps: the displacement readout (GF p.7), the commit's gate, the distance used inside a
// frame, and the two region tests. No reach outline: the browser's ruler is straight and the allowance is the Ask's.

import type { XY } from '../geometry';
import type { Frame, Projection } from '../live/apply';
import type { Enforcement, MoveRegion } from '../protocol/wire.generated';

/** Distance is the base's displacement from where it started (GF p.7); remaining is what the allowance leaves; underMinimum is short of a
 *  minimum move the Ask set (MoveRegion.minDistance). */
export interface RulerReadout {
  readonly distance: number; readonly remaining: number | null; readonly overAllowance: boolean;
  readonly underMinimum: boolean; readonly outOfRange: boolean;
}

/** Whether a move's commit may go, and the line that says how many models are out of range. */
export interface MoveGate { readonly allowed: boolean; readonly reason: string | null }

const EPS = 1e-6;

export function read(start: XY, current: XY, allowance: number | null, minimum: number | null = null): RulerReadout {
  return readDistance(Math.hypot(current.x - start.x, current.y - start.y), allowance, minimum);
}

/** MoveRuler.Read over a distance already measured (the path the move takes). */
export function readDistance(distance: number, allowance: number | null, minimum: number | null = null): RulerReadout {
  const overAllowance = allowance !== null && distance > allowance + EPS;
  const underMinimum = minimum !== null && distance < minimum - EPS;
  return { distance, remaining: allowance === null ? null : Math.max(0, allowance - distance), overAllowance, underMinimum, outOfRange: overAllowance || underMinimum };
}

/** The commit against every held model's readout and its contact marks: the models out of range are counted, each contact reason named
 *  once with its count, and under strict any of them disables it; under warn and silent it goes and the server judges. `warnings` (movePaths'
 *  lines: a dangerous terrain test a model's path owes) are named after them and never hold it: the move is legal. */
export function gate(readouts: readonly RulerReadout[], enforcement: Enforcement, marked?: ReadonlyMap<string, string>, warnings: readonly string[] = []): MoveGate {
  const over = readouts.filter((r) => r.overAllowance).length, under = readouts.filter((r) => r.underMinimum).length;
  const contacts = new Map<string, number>();
  for (const reason of marked?.values() ?? []) contacts.set(reason, (contacts.get(reason) ?? 0) + 1);
  if (over + under + contacts.size === 0) return { allowed: true, reason: warnings.length === 0 ? null : `${warnings.join(' · ')}.` };
  const models = (n: number, what: string): string => (n === 1 ? `1 model ${what} its` : `${n} models ${what} their`);
  const parts: string[] = [];
  if (over > 0) parts.push(`${models(over, 'past')} allowance`);
  if (under > 0) parts.push(`${models(under, 'short of')} minimum`);
  for (const [reason, n] of contacts) parts.push(n === 1 ? reason : `${reason} (${n} models)`);
  parts.push(...warnings);
  return { allowed: enforcement !== 'strict', reason: `${parts.join(' · ')}.` };
}

/** Distance a model has already moved inside a frame: the sum of its logged table-to-table relocations in the frame's completed steps. */
export function usedInFrame(frame: Frame, modelId: string): number {
  let sum = 0;
  for (const step of frame.steps)
    for (const change of step.changes)
      if (change.kind === 'models_moved')
        for (const r of change.moves)
          if (r.modelId === modelId && r.from.kind === 'table' && r.to.kind === 'table')
            sum += Math.hypot(r.to.placement.position.x - r.from.placement.position.x, r.to.placement.position.y - r.from.placement.position.y);
  return sum;
}

/** The unit is inside a container, so this move is a disembark. */
export function isDisembark(legal: MoveRegion): boolean { return legal.containment?.exitingId !== undefined; }

/** The same anchored region over models on the shelf (a Spawn or Split reserve placed within 6" of its anchor model). */
export function isShelfPlacement(legal: MoveRegion, p: Projection): boolean {
  return isDisembark(legal) && legal.modelIds.length > 0 && p.models.get(legal.modelIds[0]!)?.location.kind === 'shelf';
}

/** RulerNode's label: `6.0"  (1.5" left)`, or `6.0"` without an allowance. */
export function readoutText(r: RulerReadout): string {
  const d = `${r.distance.toFixed(1)}"`;
  return r.remaining === null ? d : `${d}  (${r.remaining.toFixed(1)}" left)`;
}
