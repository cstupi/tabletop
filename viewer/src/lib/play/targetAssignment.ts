// viewer/src/lib/play/targetAssignment.ts
// client/src/Core/TargetAssignment.cs: the declare-targets pick (in-game UX spec → Shooting targets). p.8: each weapon group fires at one
// target it can shoot (the Ask's Assignment.TargetsOf), at most two targets in all. Every group starts picked; aiming points those picked
// groups that can shoot a unit at it, and the others keep their aim; a chip picks its group alone. The answer is Assigned, each aimed group
// to its target; a group left unaimed holds fire (p.8: models "may" shoot).

import type { AskOption, Assigned, Assignment } from '../protocol/wire.generated';

/** Assignment.TargetsOf: the targets one item may take; every target without a per-item map, none for an unlisted item. */
export function targetsOf(legal: Assignment, itemId: string): readonly string[] {
  if (legal.targetsByItem === undefined || legal.targetsByItem === null) return legal.targets;
  return Object.hasOwn(legal.targetsByItem, itemId) ? legal.targetsByItem[itemId]! : [];
}

export class TargetAssignment {
  readonly #legal: Assignment;
  readonly #picked: Set<string>;
  readonly #assigned = new Map<string, string>();

  constructor(legal: Assignment) {
    this.#legal = legal;
    this.#picked = new Set(legal.items.map((i) => i.id));
  }

  get groups(): readonly AskOption[] { return this.#legal.items; }
  get picked(): ReadonlySet<string> { return this.#picked; }
  get assigned(): ReadonlyMap<string, string> { return this.#assigned; }

  pickOnly(groupId: string): void {
    if (this.#legal.items.every((i) => i.id !== groupId)) return;
    this.#picked.clear();
    this.#picked.add(groupId);
  }

  /** The picked groups that can shoot the unit: the ones a click on it aims. */
  #shooters(unitId: string): string[] {
    return this.#legal.items.filter((i) => this.#picked.has(i.id) && targetsOf(this.#legal, i.id).includes(unitId)).map((i) => i.id);
  }

  /** A unit some picked group can shoot that, with those groups moved onto it, keeps the distinct targets within the Ask's maximum. */
  canTarget(unitId: string): boolean {
    const shooters = this.#shooters(unitId);
    if (shooters.length === 0) return false;
    const targets = new Set([...this.#assigned].filter(([g]) => !shooters.includes(g)).map(([, t]) => t));
    targets.add(unitId);
    return targets.size <= this.#legal.maxDistinctTargets;
  }

  /**
   * The units a click may aim at, which glow: with the Ask's maximum of distinct targets taken (p.8: up to two), only those targets;
   * otherwise every offered unit a picked group can shoot and move onto.
   */
  get candidates(): string[] {
    const taken = [...new Set(this.#assigned.values())];
    return this.#legal.targets.filter((u) => (taken.length < this.#legal.maxDistinctTargets || taken.includes(u)) && this.canTarget(u));
  }

  /** A group's chip: its label and how many weapons it holds (its pieces are the weapon ids); with a target its name, or "holds fire" for null. */
  static chip(group: AskOption, target?: string | null): string {
    const chip = `${group.label} ×${group.pieces.length}`;
    if (target === undefined) return chip;
    return target === null ? `${chip} · holds fire` : `${chip} → ${target}`;
  }

  aim(targetUnitId: string): boolean {
    if (!this.candidates.includes(targetUnitId)) return false;
    for (const g of this.#shooters(targetUnitId)) this.#assigned.set(g, targetUnitId);
    return true;
  }

  /** Some group has a target: Clear targets (Esc) has something to remove. */
  get canClear(): boolean { return this.#assigned.size > 0; }

  /** Clear targets: every group holds fire and is picked again. Local only; nothing is sent until Fire. */
  clear(): void {
    this.#assigned.clear();
    this.#picked.clear();
    for (const i of this.#legal.items) this.#picked.add(i.id);
  }

  /** Null until some group has a target; the groups without one hold fire. */
  answer(): Assigned | null {
    return this.#assigned.size > 0 ? { kind: 'assigned', targets: Object.fromEntries(this.#assigned) } : null;
  }
}
