// viewer/src/lib/play/ruleTexts.ts
// client/src/Core/RuleTexts.cs and GrantChoices.cs, and Session.FetchRuleTexts.

import type { Api } from '../api/client';
import type { GrantableEffectDto, RuleTextDto } from '../api/types';

export type { GrantableEffectDto, RuleTextDto };

/**
 * A pinned set's catalog by rule name: the hover text of every rule, and whether the engine plays it. A rule with no entry, or a
 * text-only entry (played false), is not automated. Empty is the catalog not loaded yet, or a failed fetch: it knows no text and tags
 * nothing, since an unknown catalog says nothing about what is automated.
 */
export class RuleTexts {
  static readonly empty = new RuleTexts(new Map(), false);
  readonly #byName: ReadonlyMap<string, { text: string; played: boolean }>;
  readonly loaded: boolean;

  private constructor(byName: ReadonlyMap<string, { text: string; played: boolean }>, loaded: boolean) {
    this.#byName = byName;
    this.loaded = loaded;
  }

  static from(rows: readonly RuleTextDto[]): RuleTexts {
    const byName = new Map<string, { text: string; played: boolean }>();
    for (const row of rows) if (!byName.has(row.name)) byName.set(row.name, { text: row.ruleText, played: row.played ?? true });
    return new RuleTexts(byName, true);
  }

  textOf(name: string): string | null { return this.#byName.get(name)?.text ?? null; }
  isAutomated(name: string): boolean { return this.#byName.get(name)?.played === true; }
  /** The *not automated* tag: the catalog is loaded, and has no entry for the name or a text-only one. */
  notAutomated(name: string): boolean { return this.loaded && !this.isAutomated(name); }
}

/** One effect the menu's Add a grant offers: its label, and the entry and effect the AddGrant names. */
export interface GrantChoice { readonly label: string; readonly entryName: string; readonly effectId: string }

/** The pinned set's grantable effects as menu choices, in the server's order: the entry's name, numbered when the entry has several. */
export function grantChoices(effects: readonly GrantableEffectDto[]): GrantChoice[] {
  const groups = new Map<string, GrantableEffectDto[]>();
  for (const e of effects) {
    const list = groups.get(e.entryName);
    if (list === undefined) groups.set(e.entryName, [e]); else list.push(e);
  }
  return [...groups.values()].flatMap((list) => list.map((e, i) => ({
    label: list.length === 1 ? e.entryName : `${e.entryName} (${i + 1} of ${list.length})`, entryName: e.entryName, effectId: e.effectId,
  })));
}

/** Session.FetchRuleTexts: rules, then grants; a failure yields null (nothing cached, the next game frame tries again). */
export async function fetchRuleTexts(api: Api, hash: string): Promise<{ rules: RuleTexts; grants: GrantChoice[] } | null> {
  let rules: RuleTexts;
  try { rules = RuleTexts.from(await api.ruleTexts(hash)); } catch { return null; }
  let grants: GrantChoice[] = [];
  try { grants = grantChoices(await api.grantableEffects(hash)); } catch { /* the menu's Add a grant stays out until a refetch */ }
  return { rules, grants };
}
