// viewer/src/lib/play/casualtyPick.ts
// client/src/Core/CasualtyPick.cs: the remove-casualties pick (in-game UX spec → Casualties). A click adds one wound to an offered model up
// to its wounds left, and on a model that can take no more puts it back; a right-click takes one away; Clear empties the pick; Auto fills
// the rest in the Ask's order. Required is the lesser of the amount and what the models it may use can take, so a pick whose wounds
// outnumber the models still completes. Under the Ask's maxTargets of one, a click on another model moves the pick there. The answer is
// Allocated, summing to the amount, any surplus on the last model counted for the server to carry over (p.8).

import type { Allocated, Allocation, Ask } from '../protocol/wire.generated';
import type { Projection } from '../live/apply';

export class CasualtyPick {
  readonly #legal: Allocation;
  readonly #woundsLeft: ReadonlyMap<string, number>;
  readonly #counts = new Map<string, number>();
  readonly #total: number | null;

  /** `total`: the wounds the Ask's frame started with (`totalOf`), when the Ask asks for what an earlier segment left of them. */
  constructor(legal: Allocation, woundsLeft: ReadonlyMap<string, number>, total: number | null = null) {
    this.#legal = legal;
    this.#woundsLeft = woundsLeft;
    this.#total = total;
  }

  /** The wounds the Ask's deal-wounds frame started with (`wounds_total`, which the server writes on each segment it completed); null on its
   *  first segment, which asks for them all. */
  static totalOf(p: Projection, ask: Ask): number | null {
    const frame = p.stack.find((f) => f.id === ask.frameId);
    const step = frame === undefined ? undefined : [...frame.steps].reverse().find((s) => s.values?.['wounds_total'] !== undefined);
    return step?.values?.['wounds_total'] ?? null;
  }

  /** `1 of 2 wounds assigned`: the frame's wounds an earlier segment already placed, so the card agrees with the block's count; null when none. */
  get assigned(): string | null {
    const t = this.#total;
    return t !== null && t > this.#legal.amount ? `${t - this.#legal.amount} of ${t} wounds assigned` : null;
  }

  get counts(): ReadonlyMap<string, number> { return this.#counts; }
  get chosen(): number { let n = 0; for (const v of this.#counts.values()) n += v; return n; }
  get complete(): boolean { return this.chosen === this.required; }

  /** The models it may still use: all of them, or under maxTargets the ones counted once that many are, else the roomiest. */
  get required(): number {
    let targets = [...new Set(this.#legal.targets)];
    const most = this.#legal.maxTargets;
    if (most !== undefined && most !== null && most < targets.length)
      targets = this.#counts.size >= most ? [...this.#counts.keys()] : [...targets].sort((a, b) => this.#capacity(b) - this.#capacity(a)).slice(0, most);
    return Math.min(this.#legal.amount, targets.reduce((n, m) => n + this.#capacity(m), 0));
  }

  /** Every offered model takes one wound: the pick is which models are removed. */
  get oneEach(): boolean { return this.#legal.targets.every((m) => this.#capacity(m) <= 1); }

  get title(): string {
    if (this.oneEach) return `Remove ${this.required} ${this.required === 1 ? 'model' : 'models'}`;
    return `Assign ${this.#legal.amount} ${this.#legal.amount === 1 ? 'wound' : 'wounds'}${this.#legal.maxTargets === 1 ? ' to one model' : ''}`;
  }

  get status(): string { return `${this.chosen} of ${this.required} chosen`; }

  /** Wounds left; a model the facts do not name takes one. */
  #capacity(modelId: string): number { return Math.max(0, this.#woundsLeft.get(modelId) ?? 1); }

  add(modelId: string): void {
    if (!this.#legal.targets.includes(modelId)) return;
    const most = this.#legal.maxTargets;
    if (!this.#counts.has(modelId) && most !== undefined && most !== null && this.#counts.size >= most) {
      if (most !== 1) return;
      this.#counts.clear();                                                // one model at a time: the click moves the pick
    }
    if (this.chosen >= this.required || (this.#counts.get(modelId) ?? 0) >= this.#capacity(modelId)) return;
    this.#counts.set(modelId, (this.#counts.get(modelId) ?? 0) + 1);
  }

  /** A left click: a picked model that can take no more (its wounds left, or the pick full) is put back, anything else is add. */
  click(modelId: string): void {
    if (this.#counts.has(modelId) && (this.chosen >= this.required || this.#counts.get(modelId)! >= this.#capacity(modelId))) this.#counts.delete(modelId);
    else this.add(modelId);
  }

  remove(modelId: string): void {
    const n = this.#counts.get(modelId);
    if (n === undefined) return;
    if (n <= 1) this.#counts.delete(modelId); else this.#counts.set(modelId, n - 1);
  }

  clear(): void { this.#counts.clear(); }

  auto(): void {
    const most = this.#legal.maxTargets;
    for (const m of this.#legal.targets) {
      if (!this.#counts.has(m) && most !== undefined && most !== null && this.#counts.size >= most) continue;
      while (this.chosen < this.required && (this.#counts.get(m) ?? 0) < this.#capacity(m)) this.add(m);
    }
  }

  answer(): Allocated | null {
    if (!this.complete) return null;
    const counts = new Map(this.#counts);
    const keys = [...this.#counts.keys()];
    const last = keys.length > 0 ? keys[keys.length - 1]! : this.#legal.targets[this.#legal.targets.length - 1];
    if (this.#legal.amount > this.chosen && last !== undefined) counts.set(last, (counts.get(last) ?? 0) + this.#legal.amount - this.chosen);
    return { kind: 'allocated', counts: Object.fromEntries(counts) };
  }
}
