// viewer/src/lib/play/logSheet.ts
// client/src/Core/LogSheet.cs and LogSheet.Detail.cs: the log as plain text, what the panel draws and what a copy holds, one entry per
// paragraph. The summary gives each entry one line (an attack `{unit} → {target}  H: {hits}  W: {wounds}`, then `  with cover` when cover applied); the detailed log
// gives an attack 1–6 tables of its faces, each block's modifiers over its table and `H: X  W: Y`. Words come from the log's entries.

import type { RollModifier, RollRecord } from '../protocol/wire.generated';
import {
  type AttackTag, type DieView, type LogEntry, type RollEntry, RollWords, amendmentLine, capital, isTest, outcome, parseTrigger, signed, successes,
} from './rollEntry';

/** One piece of the drawn log: an entry, or an attack's rolls (entries), as lines at a depth (two spaces each). entry is what a click picks. */
export interface LogParagraph { readonly entry: LogEntry; readonly entries: readonly LogEntry[]; readonly depth: number; readonly lines: readonly string[] }

/** The lines indented two spaces per depth; an empty line stays empty. */
export const indented = (p: LogParagraph): string[] => p.lines.map((l) => (l.length === 0 ? l : ' '.repeat(p.depth * 2) + l));

export function sheetOf(entries: readonly LogEntry[], detailed: boolean): LogParagraph[] {
  const out: LogParagraph[] = [];
  add(out, entries, 0, null, detailed);
  return out;
}

/** The paragraphs as one text, a line each: what Copy puts on the clipboard. */
export const sheetText = (paragraphs: readonly LogParagraph[]): string => paragraphs.flatMap(indented).join('\n');

function add(into: LogParagraph[], entries: readonly LogEntry[], depth: number, actor: string | null, detailed: boolean): void {
  const para = (e: LogEntry, group: readonly LogEntry[], lines: readonly string[]): void => {
    into.push({ entry: e, entries: group, depth, lines: detailed && lines.length > 1 ? [...lines, ''] : lines });
  };
  for (let i = 0; i < entries.length; i++) {
    const e = entries[i]!;
    if (e.type === 'block') {
      para(e, [e], [e.header]);
      add(into, e.entries, depth + 1, e.actor ?? actor, detailed);
    } else if (e.type === 'roll') {
      const group: RollEntry[] = [e];
      const tag = e.inAttack;
      while (tag !== null && i + 1 < entries.length) {
        const next = entries[i + 1]!;
        if (next.type !== 'roll' || next.inAttack?.frame !== tag.frame || next.undone !== e.undone) break;
        group.push(next); i++;
      }
      if (group.some((x) => x.record.roll === 'hit' || x.record.roll === 'defense')) para(e, group, detailed ? attackDetail(group) : attackSummary(group));
      else for (const x of group) para(x, [x], detailed ? rollDetail(x, actor) : [rollLine(x, actor)]);
    } else para(e, [e], detailed && e.worked !== null ? e.worked : [e.text]);
  }
}

const perWound = (r: RollEntry): boolean => RollWords.perWound(r.record.roll, r.sequence);
const isDefense = (r: RollEntry): boolean => r.record.roll === 'defense';
const sum = (xs: readonly number[]): number => xs.reduce((s, x) => s + x, 0);

/** `{unit} → {targets}`, with ` (Impact)` for hits a rule dealt and ` (melee)` for a melee weapon's. */
const who = (t: AttackTag): string =>
  [t.actor, t.targets].filter((s) => s.length > 0).join(' → ') + (t.source !== null ? ` (${t.source})` : t.melee ? ' (melee)' : '');

/** The extra things (`hit`, `wound`) the faces set off. */
const extra = (dice: readonly DieView[], word: string): number =>
  sum(dice.flatMap((d) => d.triggered ?? []).map(parseTrigger).filter((t) => t.word === word).map((t) => t.amount));

interface Totals { rolled: number; bonus: number; hits: number; wounds: number; cover: boolean }

/** LogSheet.Sum: hits are the dice the blocks rolled, else the hits scored with their extra hits, multiplied; wounds the blocks' failures and
 * extra wounds less those a roll per wound ignored; cover a block's verdict applied. */
function totals(group: readonly RollEntry[]): Totals {
  const hitRolls = group.filter((r) => !isDefense(r) && !perWound(r));
  const blocks = group.filter(isDefense);
  const rolled = sum(hitRolls.map(successes));
  const multiplier = Math.max(1, ...hitRolls.map((r) => r.hitMultiplier));
  const hits = blocks.length > 0 ? sum(blocks.map((b) => b.dice.length)) : (rolled + sum(hitRolls.map((r) => extra(r.dice, 'hit')))) * multiplier;
  const wounds = sum(blocks.map((b) => b.dice.filter((d) => !d.success).length + extra(b.dice, 'wound'))) * group[0]!.inAttack!.woundMultiplier
    - sum(group.filter(perWound).map(successes));
  const cover = blocks.some((b) => b.record.cover !== undefined && b.record.cover.inCover && b.record.cover.ignoredBy === undefined);
  return { rolled, bonus: Math.max(0, hits - rolled), hits, wounds: Math.max(0, wounds), cover };
}

function attackSummary(group: readonly RollEntry[]): string[] {
  const t = totals(group);
  return [`${who(group[0]!.inAttack!)}  H: ${t.hits}  W: ${t.wounds}${t.cover ? '  with cover' : ''}`, ...group.filter(perWound).map((r) => rollLine(r, null))];
}

/** The unit a test is for, ahead of its line, when it is not the block's actor. */
const prefix = (r: RollEntry, actor: string | null): string =>
  isTest(r.record, r.sequence) && r.unit !== null && (r.context.actorUnitId ?? null) !== actor ? `${r.unit} · ` : '';

const labelled = (r: RollEntry): readonly string[] | null =>
  r.dieLabels !== null && r.dieLabels.length > 0 && r.dieLabels.length === r.dice.length ? r.dieLabels : null;

/** LogSheet.RollLine: `{Label} {target}: rolled {faces}, {result}` and each live reroll or edit. */
export function rollLine(r: RollEntry, actor: string | null): string {
  const head = r.target.length === 0 ? r.label : `${r.label} ${r.target}`;
  const labels = labelled(r);
  const faces = labels !== null ? r.dice.map((d, i) => `${labels[i]} rolled ${d.raw}`).join(' · ') : `rolled ${r.dice.map((d) => d.raw).join(' ')}`;
  const o = outcome(r);
  return `${prefix(r, actor)}${head}: ${faces}${o.length === 0 ? '' : `, ${o}`}` + r.amendments.filter((a) => !a.undone).map((a) => ` · ${amendmentLine(a, String)}`).join('');
}

/** A header row of the faces 1–6 over a row of how many dice show each, columns two wide (wider for a count of 100), a space apart. */
export function table(faces: Iterable<number>): string[] {
  const counts = [0, 0, 0, 0, 0, 0, 0];
  for (const f of faces) if (f >= 1 && f <= 6) counts[f]!++;
  const width = Math.max(2, String(Math.max(...counts)).length);
  const row = (cell: (f: number) => number): string => [1, 2, 3, 4, 5, 6].map((f) => String(cell(f)).padStart(width)).join(' ');
  return [row((f) => f), row((f) => counts[f]!)];
}

/** LogSheet.Modifier: `AP(1) -1`, `Rending AP(4) -4`, `Cover +1`: the modifier's name and the amount the roll adds. */
export function modifierText(m: RollModifier): string {
  const via = m.via !== undefined ? `${m.via} ` : '';
  return `${via}${m.source === 'AP' ? `AP(${-m.amount})` : capital(m.source)} ${signed(m.amount)}`;
}

/** LogSheet.Threshold: what a roll is judged against, ` · ` between the parts. */
export function threshold(r: RollRecord): string {
  if (r.requiredRaw !== undefined) return r.requiredRaw <= 1 ? '' : `unmodified ${r.requiredRaw}+`;
  if (r.threshold === undefined) return '';
  const t = r.threshold;
  if (r.roll === 'dangerous-terrain' && t === 2 && r.basis === undefined && r.modifiers.length === 0) return '1 fails';
  const parts = [r.basis !== undefined ? `${r.basis.stat} ${r.basis.printed}+` : `${t}+`];
  if (r.basis !== undefined) {
    parts.push(...r.basis.changes.map((c) => (c.setTo !== undefined ? `${c.source} ${c.setTo}+` : `${c.source} ${signed(c.amount)}`)));
    parts.push(...(r.basis.ignored ?? []).map((x) => `${x.source} ${x.setTo !== undefined ? `${x.setTo}+` : signed(x.amount ?? 0)} ignored (${x.by})`));
  }
  if (r.cover?.ignoredBy !== undefined) parts.push(`Cover ignored (${r.cover.ignoredBy})`);
  parts.push(...r.modifiers.map(modifierText));
  parts.push(...(r.ignored ?? []).map((x) => `${x.source} ${signed(x.amount ?? 0)} ignored (${x.by})`));
  if (r.modifiers.length > 0 || (r.basis !== undefined && r.basis.changes.length > 0))
    parts.push(`needs ${Math.min(Math.max(t - sum(r.modifiers.map((m) => m.amount)), 2), 6)}+`);
  return parts.join(' · ');
}

/** The faces as rolled, before any live reroll or edit. */
function rolledFaces(r: RollEntry): number[] {
  const faces = r.dice.map((d) => d.raw);
  for (const a of [...r.amendments].filter((x) => !x.undone).reverse())
    a.replaced.forEach((old, i) => { const ix = a.indexes?.[i]; if (ix !== undefined && ix >= 0 && ix < faces.length) faces[ix] = old; });
  return faces;
}

/** The p.5 note, when it decided a die. */
function always(r: RollEntry): string | null {
  const t = r.record.threshold;
  if (t === undefined || r.record.requiredRaw !== undefined) return null;
  return r.dice.some((d) => (d.raw === 6 && d.success && d.modified < t) || (d.raw === 1 && !d.success && d.modified >= t)) ? 'a 6 always succeeds, a 1 always fails' : null;
}

function amended(r: RollEntry): string[] {
  const out: string[] = [];
  for (const a of r.amendments.filter((x) => !x.undone)) {
    const by = [...new Set((a.by ?? []).filter((b): b is string => b !== null))].join(', ');
    const label = capital(a.kind) + (by.length > 0 ? ` (${by})` : '');
    const w = a.with ?? [];
    if (a.replaced.length === 1) { out.push(`${label}: ${a.replaced[0]}${w.length > 0 ? ` → ${w[0]}` : ''}`); continue; }
    out.push(`${label} (${RollWords.plural(w.length, 'die')})`, ...table(w));
  }
  return out;
}

/** LogSheet.Dice: the table as rolled, each reroll or edit, each rule the faces set off as one line (`+2 hits (Relentless)`). */
function diceLines(r: RollEntry): string[] {
  const out = [...table(rolledFaces(r)), ...amended(r)];
  // A rule a face set off rolls no dice of its own (die.triggered on these dice): one line, its total, no table.
  const groups = new Map<string, { rule: string; word: string; total: number; faces: number[] }>();
  for (const d of r.dice)
    for (const t of d.triggered ?? []) {
      const p = parseTrigger(t);
      const key = `${p.rule}\u0000${p.word}`;
      let g = groups.get(key);
      if (g === undefined) groups.set(key, g = { rule: p.rule, word: p.word, total: 0, faces: [] });
      g.total += p.amount; g.faces.push(d.raw);
    }
  for (const g of groups.values())
    out.push(g.word.length > 0 ? `${signed(g.total)} ${g.word}${Math.abs(g.total) === 1 ? '' : 's'} (${g.rule})`
      : `${g.rule} (${[...new Set(g.faces)].sort((a, b) => a - b).map((f) => `${f}s`).join(', ')}: ${RollWords.plural(g.faces.length, 'die')})`);
  const note = always(r);
  if (note !== null) out.push(note);
  return out;
}

function rules(rolls: readonly RollEntry[]): string[] {
  const applied = [...new Set(rolls.flatMap((r) => r.applied))];
  return [...(applied.length > 0 ? [`Rules: ${applied.join(', ')}`] : []), ...rolls.flatMap((r) => r.stepIgnored ?? []).map((x) => `${x.source} ignored (${x.by})`)];
}

const count = (r: RollEntry): string => `(${RollWords.plural(r.dice.length, perWound(r) ? 'wound' : 'die')})`;

function attackDetail(group: readonly RollEntry[]): string[] {
  const tag = group[0]!.inAttack!;
  const t = totals(group);
  const lines = [who(tag) + (tag.weapons.length > 0 && tag.source === null ? ` · ${tag.weapons}` : '')];
  const attack = group.find((r) => r.attack !== null && r.attack.length > 1)?.attack;
  if (attack !== undefined && attack !== null) lines.push(...attack.slice(1));
  const hitRolls = group.filter((r) => !isDefense(r) && !perWound(r));
  for (const r of hitRolls) {
    const th = threshold(r.record);
    lines.push(`${r.label} roll${th.length > 0 ? ` · ${th}` : ''} ${count(r)}`, ...diceLines(r));
  }
  const multiplier = Math.max(1, ...hitRolls.map((r) => r.hitMultiplier));
  if (multiplier > 1) lines.push(`Hits ×${multiplier}`);
  lines.push(hitRolls.length === 0 ? `${tag.source !== null ? `${tag.source}: ` : ''}${RollWords.plural(t.hits, 'hit')}`
    : RollWords.plural(t.rolled, 'hit') + (t.bonus > 0 ? ` · ${RollWords.plural(t.bonus, 'bonus hit')}` : ''));
  const after = group.filter((r) => isDefense(r) || perWound(r));
  if (after.length > 0) lines.push('');
  for (const r of after) {
    const th = threshold(r.record);
    lines.push(isDefense(r) ? `${r.record.basis === undefined ? 'Defense ' : ''}${th} ${count(r)}` : `${r.label}${th.length > 0 ? ` · ${th}` : ''} ${count(r)}`);
    lines.push(...diceLines(r));
    if (perWound(r)) lines.push(`${successes(r)} ignored`);
  }
  if (tag.woundMultiplier > 1) lines.push(`Wounds ×${tag.woundMultiplier}`);
  lines.push(`H: ${t.hits}  W: ${t.wounds}`, ...rules(group));
  return lines;
}

function rollDetail(r: RollEntry, actor: string | null): string[] {
  const th = threshold(r.record);
  const lines = [`${prefix(r, actor)}${r.label}${th.length > 0 ? ` · ${th}` : ''}${r.dice.length > 1 ? ` ${count(r)}` : ''}`];
  const labels = labelled(r);
  if (labels !== null) lines.push(...r.dice.map((d, i) => `${labels[i]} rolled ${d.raw}`), ...amended(r));
  else if (r.dice.length === 1) {
    lines.push(`rolled ${rolledFaces(r)[0]}`, ...amended(r));
    const note = always(r);
    if (note !== null) lines.push(note);
  } else lines.push(...diceLines(r));
  const o = outcome(r);
  if (o.length > 0) lines.push(o);
  lines.push(...rules([r]));
  return lines;
}
