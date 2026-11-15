// viewer/src/lib/play/woundNumbers.ts
// client/src/Core/WoundNumbers.cs: each wound a live batch deals to a model still standing after it floats a "-1" over the model, rising
// and fading over SECONDS, a model's numbers STAGGER apart. A model taken off the table or at its max wounds shows nothing; healing shows
// nothing. Wounds on the wire are totals, so this keeps each model's last. A batch while catching up, an empty one or one with an undo
// floats nothing and re-reads the totals. WoundFloats draws them on the board's floating layer.

import type { FloatingLayer } from '../board/interaction';
import type { Point } from '../board/transform';
import type { Projection } from '../live/apply';
import type { GameEvent, Point3, StateChange } from '../protocol/wire.generated';

export const SECONDS = 2.5;
export const STAGGER = 0.35;
export const TEXT = '-1';
const HOLD = 0.6;

/** One "-1": `at` the model's placement, `radius` its base's in inches, `index` its place among the model's numbers in the batch. */
export interface WoundNumber { readonly modelId: string; readonly at: Point3; readonly radius: number; readonly index: number; readonly delay: number }

function changesOf(e: GameEvent): readonly StateChange[] {
  const p = e.payload;
  switch (p.kind) {
    case 'step_completed': case 'round_started': case 'round_ended': case 'turn_ended': return p.changes;
    case 'wounds_set': return [p];
    default: return [];
  }
}

export class WoundNumbers {
  readonly #wounds = new Map<string, number>();

  seed(p: Projection): void { this.#wounds.clear(); for (const m of p.models.values()) this.#wounds.set(m.id, m.wounds); }
  onBoard(): void { this.#wounds.clear(); }

  onBatch(batch: readonly GameEvent[], p: Projection, catchingUp: boolean, maxWounds: ReadonlyMap<string, number>): WoundNumber[] {
    if (catchingUp || batch.length === 0 || batch.some((e) => e.payload.kind === 'undone')) { this.seed(p); return []; }
    const shown: WoundNumber[] = []; const next = new Map<string, number>();
    for (const e of batch)
      for (const change of changesOf(e)) {
        if (change.kind !== 'wounds_set') continue;
        const taken = change.wounds - (this.#wounds.get(change.modelId) ?? 0);
        this.#wounds.set(change.modelId, change.wounds);
        const m = p.models.get(change.modelId);
        if (taken <= 0 || m === undefined || m.location.kind !== 'table') continue;
        const max = maxWounds.get(m.id);
        if (max !== undefined && m.wounds >= max) continue;
        const radius = Math.max(m.slot.base.a, m.slot.base.b) / 2;
        for (let i = 0; i < taken; i++) {
          const index = next.get(m.id) ?? 0; next.set(m.id, index + 1);
          shown.push({ modelId: m.id, at: m.location.placement.position, radius, index, delay: index * STAGGER });
        }
      }
    return shown;
  }
}

/** A number `age` seconds after it appeared: rise 0..1 of its climb (fast, then slowing), alpha opaque at first, then fading out. */
export function woundLook(age: number): { rise: number; alpha: number } {
  const t = Math.min(1, Math.max(0, age / SECONDS));
  return { rise: 1 - (1 - t) * (1 - t), alpha: t <= HOLD ? 1 : 1 - (t - HOLD) / (1 - HOLD) };
}

const RISE_PX = 70, SPREAD_PX = 10, LIFT_PX = 6;

/** The wound numbers in flight: `add` a batch's numbers at `now` (ms); `draw` is the board's floating layer. */
export class WoundFloats implements FloatingLayer {
  #live: Array<{ n: WoundNumber; start: number }> = [];
  readonly #listeners = new Set<() => void>();

  onStart(listener: () => void): () => void { this.#listeners.add(listener); return () => { this.#listeners.delete(listener); }; }

  add(numbers: readonly WoundNumber[], now: number): void {
    if (numbers.length === 0) return;
    for (const n of numbers) this.#live.push({ n, start: now + n.delay * 1000 });
    for (const listener of [...this.#listeners]) listener();
  }

  clear(): void { this.#live = []; }

  draw(ctx: CanvasRenderingContext2D, project: (p: Point) => Point, pixelsPerInch: number, now: number): boolean {
    this.#live = this.#live.filter((f) => now - f.start < SECONDS * 1000);
    if (this.#live.length === 0) return false;
    ctx.save();
    ctx.font = 'bold 30px system-ui, sans-serif'; ctx.textAlign = 'center'; ctx.textBaseline = 'bottom';
    ctx.lineJoin = 'round'; ctx.lineWidth = 6; ctx.strokeStyle = '#1a0504'; ctx.fillStyle = '#f0291e';
    for (const { n, start } of this.#live) {
      if (now < start) continue;
      const { rise, alpha } = woundLook((now - start) / 1000);
      const centre = project({ x: n.at.x, y: n.at.y });
      const x = centre.x + ((n.index + 1) % 3 - 1) * SPREAD_PX;
      const y = centre.y - n.radius * pixelsPerInch - LIFT_PX - RISE_PX * rise;
      ctx.globalAlpha = alpha;
      ctx.strokeText(TEXT, x, y); ctx.fillText(TEXT, x, y);
    }
    ctx.restore();
    return true;
  }
}
