// viewer/src/lib/play/workedLines.ts
// client/src/Core/WorkedLines.cs: the detailed log's words (detailed-log spec → What it shows): a roll as a worked resolution in monospace
// columns. Composed from the record's names and numbers; no rule is named here.

import type { RollModifier, SequenceContext, Weapon } from '../protocol/wire.generated';
import type { Names } from './names';
import { type DieView, type RollEntry, RollWords, judged, outcome, signed } from './rollEntry';

const LABEL_WIDTH = 16;
const DICE_PER_ROW = 10;
const PASS = '✓';
const MISS = '·';

/** `  {label}{value}   {note}`: the label padded to 16, the value right-aligned in 3. */
export function row(label: string, value: string, note = ''): string {
  const head = `  ${label.length >= LABEL_WIDTH ? label + ' ' : label.padEnd(LABEL_WIDTH)}${value.padStart(3)}`;
  return note.length === 0 ? head.trimEnd() : `${head}   ${note}`;
}

/** A modifier's label as the normal log writes it, less its amount: `AP(1)`, `Rending AP(+4)`, `Stealth`. */
function modifierLabel(m: RollModifier): string {
  if (m.source === 'AP') return m.via !== undefined ? `${m.via} AP(${signed(-m.amount)})` : `AP(${-m.amount})`;
  return `${m.via !== undefined ? m.via + ' ' : ''}${m.source}`;
}

/** RollEntry.Worked. */
export function worked(e: RollEntry): string[] {
  const r = e.record;
  const lines: string[] = [];
  if (e.attack !== null) lines.push(...e.attack);
  const perWound = RollWords.perWound(r.roll, e.sequence);
  lines.push(RollWords.label(r.roll) + (perWound ? ' · one die per wound' : ''));
  const t = r.threshold;
  const b = r.basis;
  if (b !== undefined) {
    lines.push(row(b.stat, `${b.printed}+`, 'printed'));
    let running = b.printed;
    for (const c of b.changes)
      if (c.setTo !== undefined) { running = c.setTo; lines.push(row(c.source, `${c.setTo}+`, 'set')); }
      else { running += c.amount; lines.push(row(c.source, signed(c.amount), `→ ${running}+`)); }
    for (const x of b.ignored ?? []) lines.push(row(x.source, x.setTo !== undefined ? `${x.setTo}+` : signed(x.amount), `ignored: ${x.by}`));
  }
  const v = r.cover;
  if (v !== undefined) {
    const count = `${v.models}/${v.of} models in cover`;
    if (v.ignoredBy !== undefined) lines.push(row('cover', '+1', `ignored: ${v.ignoredBy} (${count})`));
    else if (!v.inCover) lines.push(row('cover', '--', `no cover: ${count}`));
  }
  for (const m of r.modifiers)
    lines.push(row(modifierLabel(m), signed(m.amount), m.source === 'cover' && v !== undefined ? `${v.models}/${v.of} models in cover` : ''));
  for (const x of r.ignored ?? []) lines.push(row(x.source, signed(x.amount), `ignored: ${x.by}`));
  const net = r.modifiers.reduce((s, m) => s + m.amount, 0);
  if (r.requiredRaw !== undefined) lines.push(r.requiredRaw <= 1 ? '  target: every face' : `  target unmodified ${r.requiredRaw}+`);
  else if (r.roll === 'dangerous-terrain' && t === 2 && b === undefined && r.modifiers.length === 0) lines.push('  target: a 1 fails');
  else if (t !== undefined) lines.push(`  target ${t}+` + (net !== 0 ? `, roll ${signed(net)}` : ''));
  lines.push(...dice(e, net));
  if (t !== undefined && r.requiredRaw === undefined && e.dice.some((d) => (d.raw === 6 && d.success && d.modified < t) || (d.raw === 1 && !d.success && d.modified >= t)))
    lines.push('  a 6 always succeeds, a 1 always fails (p.5)');
  for (const a of e.amendments.filter((x) => !x.undone))
    for (let i = 0; i < a.replaced.length; i++) {
      const which = a.indexes !== null && i < a.indexes.length ? `die ${a.indexes[i]! + 1}: ` : '';
      const to = a.with !== null && i < a.with.length ? ` → ${a.with[i]}` : '';
      const rule = a.by !== null && i < a.by.length ? a.by[i] : null;
      lines.push(`  ${a.kind} ${which}${a.replaced[i]}${to}${rule !== null && rule !== undefined ? ` (${rule})` : ''}`);
    }
  for (const x of e.stepIgnored ?? []) lines.push(row(x.source, '', `ignored: ${x.by}`));
  if (e.applied.length > 0) lines.push(`  rules: ${e.applied.join(', ')}`);
  const out = outcome(e);
  if (out.length > 0) lines.push(`  = ${out}`);
  return lines;
}

function dice(e: RollEntry, net: number): string[] {
  const labels = e.dieLabels;
  if (labels !== null && labels.length > 0 && labels.length === e.dice.length) return e.dice.map((d, i) => row(labels[i]!, d.raw.toString()));
  const r = e.record;
  const word = r.roll === 'hit' ? 'hits' : r.roll === 'defense' ? 'blocks' : RollWords.perWound(r.roll, e.sequence) ? 'ignored' : 'passes';
  const final = r.requiredRaw === undefined && r.threshold !== undefined && net !== 0;
  const out: string[] = [];
  for (let start = 0; start < e.dice.length; start += DICE_PER_ROW) {
    const chunk = e.dice.slice(start, start + DICE_PER_ROW);
    const line = (label: string, cell: (d: DieView) => string) => `  ${label.padEnd(7)}${chunk.map((d) => cell(d).padStart(3)).join('')}`;
    out.push(line('rolled', (d) => d.raw.toString()));
    if (final) out.push(line('final', (d) => d.modified.toString()));
    if (judged(e)) out.push(line(word, (d) => (d.success ? PASS : MISS)));
  }
  return out;
}

/** WorkedLines.Attack: `Attack: {unit} ({n} models) → {targets}`, then one line per weapon name: `{name} ×{count} ({stats}, {rules})`. */
export function attackLines(c: SequenceContext, n: Names): string[] {
  const weapons = n.weaponsOf(c.weaponIds);
  const models = new Set(weapons.map((w) => w.modelId)).size;
  const who = c.actorUnitId !== undefined ? n.unit(c.actorUnitId) : 'Attack';
  const head = `Attack: ${who}${models > 0 ? ` (${RollWords.plural(models, 'model')})` : ''}`
    + (c.targetUnitIds.length > 0 ? ` → ${c.targetUnitIds.map((u) => n.unit(u)).join(', ')}` : '');
  const groups = new Map<string, Weapon[]>();
  for (const w of weapons) { const g = groups.get(w.weapon.name) ?? []; g.push(w.weapon); groups.set(w.weapon.name, g); }
  return [head, ...[...groups].map(([name, ws]) => `  ${name} ×${ws.length}${profile(ws[0]!)}`)];
}

function profile(w: Weapon): string {
  const parts = [
    ...Object.entries(w.stats).map(([k, v]) => (k === 'Range' ? v : k === 'Attacks' ? `A${v}` : `${k} ${v}`)),
    ...w.properties.map((p) => (p.parameter !== undefined && p.parameter !== null ? `${p.name}(${p.parameter})` : p.name)),
  ];
  return parts.length === 0 ? '' : ` (${parts.join(', ')})`;
}
