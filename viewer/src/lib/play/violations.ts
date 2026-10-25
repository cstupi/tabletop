// viewer/src/lib/play/violations.ts
// client/src/Core/Violations.cs: the models of a drag or a placement in breach and why: the client's own measure, and the server's reason
// for a commit it refused, which stands on the models it sent until one of them moves again. Under strict a mark holds the commit.

import type { Enforcement } from '../protocol/wire.generated';

export class Violations {
  readonly #measured = new Map<string, string>();
  readonly #refused = new Set<string>();
  readonly #listeners = new Set<() => void>();
  #refusal: string | null = null;
  #marked: ReadonlyMap<string, string> = new Map();

  /** Each marked model's reason: the client's own, else the server's refusal. */
  get marked(): ReadonlyMap<string, string> { return this.#marked; }

  /** Changed. */
  subscribe(listener: () => void): () => void {
    this.#listeners.add(listener);
    return () => { this.#listeners.delete(listener); };
  }

  /** A measure over these models: their marks are replaced by what it found; other models' are kept. */
  measure(over: Iterable<string>, found: ReadonlyMap<string, string>): void {
    for (const id of over) this.#measured.delete(id);
    for (const [id, reason] of found) this.#measured.set(id, reason);
    this.#update();
  }

  /** The server refused the commit that sent these models: they stay marked with its reason. */
  refused(models: Iterable<string>, reason: string): void {
    this.#refused.clear();
    for (const id of models) this.#refused.add(id);
    this.#refusal = reason;
    this.#update();
  }

  /** The player moved these models: a refusal naming one of them lapses. */
  moved(models: Iterable<string>): void {
    if (![...models].some((id) => this.#refused.has(id))) return;
    this.#refused.clear(); this.#refusal = null;
    this.#update();
  }

  clear(): void { this.#measured.clear(); this.#refused.clear(); this.#refusal = null; this.#update(); }

  /** Under strict a marked model holds the commit; under warn and silent it goes and the server judges. */
  blocks(enforcement: Enforcement): boolean { return enforcement === 'strict' && this.#marked.size > 0; }

  /** One label per reason, over the first model in this order that has it. */
  labels(order: Iterable<string>): { modelId: string; reason: string }[] {
    const seen = new Set<string>();
    const out: { modelId: string; reason: string }[] = [];
    for (const id of order) {
      const reason = this.#marked.get(id);
      if (reason === undefined || seen.has(reason)) continue;
      seen.add(reason); out.push({ modelId: id, reason });
    }
    return out;
  }

  #update(): void {
    const next = new Map(this.#measured);
    if (this.#refusal !== null) for (const id of this.#refused) if (!next.has(id)) next.set(id, this.#refusal);
    if (next.size === this.#marked.size && [...next].every(([k, v]) => this.#marked.get(k) === v)) return;
    this.#marked = next;
    for (const l of [...this.#listeners]) l();
  }
}
