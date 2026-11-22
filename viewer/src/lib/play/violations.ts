// viewer/src/lib/play/violations.ts
// client/src/Core/Violations.cs: the models of a drag or a placement in breach and why: the client's own measure, and the server's reason
// for a commit it refused, which stands on the models it sent until one of them moves again. Under strict a mark holds the commit.

import type { Enforcement } from '../protocol/wire.generated';
import { sentence } from './contactCheck';

export class Violations {
  readonly #measured = new Map<string, string>();
  readonly #refused = new Set<string>();
  readonly #listeners = new Set<() => void>();
  readonly #subject: ((modelId: string) => string | null) | null;
  #refusal: string | null = null;
  #words: ReadonlyMap<string, string> = new Map();
  #marked: ReadonlyMap<string, string> = new Map();
  #warned: ReadonlyMap<string, string> = new Map();

  /** `subject` names a model for the decision box's sentences (contactCheck's `sentence`); without it a mark is its words. */
  constructor(subject: ((modelId: string) => string | null) | null = null) { this.#subject = subject; }

  /** Each marked model's reason for the decision box: the client's own as a sentence naming the first model with it, else the server's
   *  refusal. */
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

  /** A move's warnings over these models (movePaths' marks: a dangerous terrain test the path owes): drawn and labelled like a mark, never
   *  holding the commit; other models' are kept. */
  get warned(): ReadonlyMap<string, string> { return this.#warned; }

  warn(over: Iterable<string>, found: ReadonlyMap<string, string>): void {
    const next = new Map(this.#warned);
    for (const id of over) next.delete(id);
    for (const [id, words] of found) next.set(id, words);
    if (same(next, this.#warned)) return;
    this.#warned = next;
    for (const l of [...this.#listeners]) l();
  }

  clear(): void {
    const warned = this.#warned.size > 0;
    this.#warned = new Map();
    this.#measured.clear(); this.#refused.clear(); this.#refusal = null;
    if (!this.#update() && warned) for (const l of [...this.#listeners]) l();
  }

  /** Under strict a marked model holds the commit; under warn and silent it goes and the server judges. */
  blocks(enforcement: Enforcement): boolean { return enforcement === 'strict' && this.#marked.size > 0; }

  /** One label per reason, over the first model in this order that has it: the words, since the label stands over the model. */
  labels(order: Iterable<string>): { modelId: string; reason: string }[] {
    const seen = new Set<string>();
    const out: { modelId: string; reason: string }[] = [];
    for (const id of order)
      for (const reason of [this.#words.get(id), this.#warned.get(id)]) {
        if (reason === undefined || seen.has(reason)) continue;
        seen.add(reason); out.push({ modelId: id, reason });
      }
    return out;
  }

  /** Recomputes the marks; true when they changed (and the listeners heard it). */
  #update(): boolean {
    const words = new Map(this.#measured);
    if (this.#refusal !== null) for (const id of this.#refused) if (!words.has(id)) words.set(id, this.#refusal);
    const said = new Map<string, string>();                               // each measured reason's sentence, about its first model
    for (const [id, w] of this.#measured) {
      if (said.has(w)) continue;
      const name = this.#subject?.(id) ?? null;
      said.set(w, name === null ? w : sentence(w, name));
    }
    const marked = new Map([...words].map(([id, w]) => [id, this.#measured.has(id) ? said.get(w)! : w] as const));
    if (same(words, this.#words) && same(marked, this.#marked)) return false;
    this.#words = words; this.#marked = marked;
    for (const l of [...this.#listeners]) l();
    return true;
  }
}

const same = (a: ReadonlyMap<string, string>, b: ReadonlyMap<string, string>): boolean =>
  a.size === b.size && [...a].every(([k, v]) => b.get(k) === v);
