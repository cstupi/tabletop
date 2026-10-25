// viewer/src/lib/play/spellText.ts
// client/src/Core/SpellText.cs: a spell's hover text. The pick-spell Ask offers `{spell}|{caster model}` ids labelled `{spell} ({cost})`.

import type { RuleTexts } from './ruleTexts';

const LABELLED = /^(?<name>[\s\S]*?)(?: \((?<cost>\d+)\))?$/;

/** The spell name of a pick-spell option id: everything before the last '|' (a spell name may hold one). An id with no '|' is the name. */
export function nameOfOption(optionId: string): string {
  const cut = optionId.lastIndexOf('|');
  return cut > 0 ? optionId.slice(0, cut) : optionId;
}

/** A label `Hex (3)` as its name and casting value; a label with no trailing number has no value. */
export function split(label: string): { name: string; cost: number | null } {
  const g = LABELLED.exec(label)!.groups!;
  return { name: g.name!, cost: g.cost === undefined ? null : Number.parseInt(g.cost, 10) };
}

/** `Name`, `Casting value N` when known, then the catalog text. Null when the name is empty. */
export function tooltip(rules: RuleTexts, name: string, cost: number | null = null): string | null {
  if (name.length === 0) return null;
  const lines = [name];
  if (cost !== null) lines.push(`Casting value ${cost}`);
  const text = rules.textOf(name);
  if (text !== null && text.length > 0) lines.push(text);
  return lines.join('\n');
}

/** The tooltip of a pick-spell option, from its id and label. */
export function ofOption(rules: RuleTexts, optionId: string, label: string): string | null {
  return tooltip(rules, nameOfOption(optionId), split(label).cost);
}
