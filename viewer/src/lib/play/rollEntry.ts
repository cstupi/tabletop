// viewer/src/lib/play/rollEntry.ts
// client/src/Core/RollEntry.cs: LogEntry, LineEntry, DieView, RollAmendment, RollEntry and RollWords. The words come from the record's
// names and numbers; no rule is named here. C# records with computed members are plain objects plus functions over them.

import type {
  Die, DieReplacement, GameEvent, RollModifier, RollRecord, SequenceContext, StepCompleted,
} from '../protocol/wire.generated';
import type { ActivationBlock } from './combatLog';
import { Names } from './names';

/** A combat log entry. Undone: struck through, an undone event or a block whose start was undone. */
export type LogEntry = LineEntry | RollEntry | ActivationBlock;
export interface LineEntry { readonly type: 'line'; readonly seq: number; readonly event: GameEvent; readonly undone: boolean; readonly text: string }

/** A die as the log shows it. rawJudged: the roll judged the raw face alone (requiredRaw). judged: the roll has a target number or a
 * required face; a roll with neither shows its faces with no pass or fail. */
export interface DieView {
  readonly raw: number; readonly modified: number; readonly success: boolean; readonly rawJudged: boolean; readonly judged: boolean;
  readonly triggered: readonly string[] | null;
}

export const dieText = (d: DieView): string =>
  `${d.raw}${d.modified === d.raw ? '' : ` → ${d.modified}`}${d.judged ? ` ${d.success ? 'pass' : 'fail'}` : ''}${d.rawJudged ? ' (raw face)' : ''}`
  + (d.triggered !== null && d.triggered.length > 0 ? ` (${d.triggered.join(', ')})` : '');

/** A reroll or hand edit of one roll: the faces it replaced and the faces it put in their place. */
export interface RollAmendment {
  readonly seq: number; readonly kind: string; readonly replaced: readonly number[]; readonly undone: boolean; readonly with: readonly number[] | null;
}

/** `rerolled ⚀ → ⚄`, one pair per die. */
export const amendmentLine = (a: RollAmendment, face: (n: number) => string): string =>
  `${a.kind} ${a.replaced.map((old, i) => (a.with !== null && i < a.with.length ? `${face(old)} → ${face(a.with[i]!)}` : face(old))).join(', ')}`;

/** One RollRecord of a step_completed as one line (combat-log spec → The log). */
export interface RollEntry {
  readonly type: 'roll'; readonly seq: number; readonly event: GameEvent; readonly undone: boolean;
  readonly frameId: string; readonly record: RollRecord; readonly context: SequenceContext; readonly label: string;
  readonly target: string; readonly dice: readonly DieView[]; readonly amendments: readonly RollAmendment[];
  readonly applied: readonly string[]; readonly details: readonly string[]; readonly sequence: string | null;
  /** What the roll led to, set by the log build: a single-die test's changes, or a roll-off's winner. */
  readonly consequence: string | null;
  /** Set on a roll-off: one word per die read before its face; null when the dice and the teams differ in number. */
  readonly dieLabels: readonly string[] | null;
}

/** The record has a target number or a required face (a roll-off or a property's own unjudged roll has neither). */
export const judged = (r: RollEntry): boolean => r.record.threshold !== undefined || r.record.requiredRaw !== undefined;
export const successes = (r: RollEntry): number => r.dice.filter((d) => d.success).length;
/** The label and the target: the line before its dice, and what the dice tray titles the roll with. */
export const title = (r: RollEntry): string => (r.target.length === 0 ? r.label : `${r.label} · ${r.target}`);

/** One judged die read passed or failed (not a hit, a block or a roll per wound): what a consequence follows. */
export function isTest(r: RollRecord, sequence: string | null): boolean {
  return (r.threshold !== undefined || r.requiredRaw !== undefined) && r.dice.length === 1 && r.roll !== 'hit' && r.roll !== 'defense'
    && !RollWords.perWound(r.roll, sequence);
}

export function result(r: RollEntry): string {
  if (!judged(r)) return '';
  return RollWords.result(r.record.roll, r.dice, r.sequence) + (r.consequence !== null && r.dice.length === 1 ? ` → ${r.consequence}` : '');
}

export const chain = (r: RollEntry): string => (!judged(r) ? r.label : `${r.label} ${r.dice.length === 1 ? result(r) : successes(r).toString()}`);

/** What follows the faces: the result, or an unjudged roll's consequence (a roll-off's winner). */
export const outcome = (r: RollEntry): string => (judged(r) ? result(r) : r.consequence ?? '');

/** The whole line: title, faces, outcome, and each live amendment (`rerolled ⚀ → ⚄`). */
export const collapsed = (r: RollEntry, face: (n: number) => string): string =>
  [title(r), faces(r, face), outcome(r), ...r.amendments.filter((a) => !a.undone).map((a) => amendmentLine(a, face))]
    .filter((s) => s.length > 0).join(' · ');

/** `3 dice: ⚅ ⚁ ⚀` (`3 wounds:` for a roll per wound), one die bare; a labelled roll-off `Team 1 ⚃ · Team 2 ⚀`. */
export function faces(r: RollEntry, face: (n: number) => string): string {
  const labels = r.dieLabels;
  if (labels !== null && labels.length > 0 && labels.length === r.dice.length) return r.dice.map((d, i) => `${labels[i]} ${face(d.raw)}`).join(' · ');
  return (r.dice.length > 1 ? `${RollWords.plural(r.dice.length, RollWords.perWound(r.record.roll, r.sequence) ? 'wound' : 'die')}: ` : '')
    + r.dice.map((d) => face(d.raw)).join(' ');
}

export const capital = (s: string): string => (s.length === 0 ? s : s[0]!.toUpperCase() + s.slice(1));

/** CombatLog.Pip: a face as its die glyph (⚀–⚅), else the number. */
export const pip = (face: number): string => (face >= 1 && face <= 6 ? String.fromCodePoint(0x2680 + face - 1) : face.toString());

export function rollEntryOf(
  e: GameEvent, undone: boolean, step: StepCompleted, r: RollRecord, c: SequenceContext, sequence: string | null,
  amendments: readonly { event: GameEvent; undone: boolean }[], n: Names,
): RollEntry {
  const dice: Die[] = [...r.dice];
  const amended: RollAmendment[] = [];
  for (const { event: a, undone: dead } of amendments) {
    const p = a.payload;
    const [kind, replaced]: [string, readonly DieReplacement[]] = p.kind === 'dice_rerolled' ? ['rerolled', p.replaced] : ['set by hand', (p as { replaced: readonly DieReplacement[] }).replaced];
    amended.push({ seq: a.seq, kind, replaced: replaced.map((d) => d.old.raw), undone: dead, with: replaced.map((d) => d.new.raw) });
    if (!dead) for (const d of replaced.filter((x) => x.index >= 0 && x.index < dice.length)) dice[d.index] = d.new;
  }
  const isJudged = r.threshold !== undefined || r.requiredRaw !== undefined;
  const views = dice.map((d): DieView => ({
    raw: d.raw, modified: d.modified, success: d.success, rawJudged: r.requiredRaw !== undefined, judged: isJudged, triggered: d.triggered ?? null,
  }));
  const details: string[] = [];
  if (c.weaponIds.length > 0) details.push([...new Set(c.weaponIds.map((w) => n.weapon(w)))].join(', '));
  details.push(...RollWords.details(r));
  return {
    type: 'roll', seq: e.seq, event: e, undone, frameId: step.frameId, record: { ...r, dice }, context: c, label: RollWords.label(r.roll),
    target: RollWords.target(r), dice: views, amendments: amended, applied: step.applied ?? [], details, sequence, consequence: null, dieLabels: null,
  };
}

const signed = (n: number): string => (n > 0 ? `+${n}` : n < 0 ? `-${n * -1}` : '0');
const FACES = ['', 'one', 'two', 'three', 'four', 'five', 'six'];

interface TriggerGroup { readonly text: string; readonly extra: number; readonly word: string }

/** `Rule +N things` or `Rule`; the thing singular. */
function parseTrigger(trigger: string): { rule: string; amount: number; word: string } {
  const plus = trigger.lastIndexOf(' +');
  if (plus < 0) return { rule: trigger, amount: 0, word: '' };
  const rest = trigger.slice(plus + 2);
  const sp = rest.indexOf(' ');
  const head = sp < 0 ? rest : rest.slice(0, sp);
  if (sp >= 0 && /^[+-]?\d+$/.test(head)) return { rule: trigger.slice(0, plus), amount: Number.parseInt(head, 10), word: rest.slice(sp + 1).replace(/s+$/, '') };
  return { rule: trigger, amount: 0, word: '' };
}

const faceWord = (face: number, count: number): string =>
  face >= 1 && face <= 6 ? `${count} ${FACES[face]}${count === 1 ? '' : face === 6 ? 'es' : 's'}` : RollWords.plural(count, 'die');

/** The dice that set something off, by what they set off: `3 sixes: Rending`, `2 sixes: Furious +2 hits`. */
function triggersOf(dice: readonly DieView[]): TriggerGroup[] {
  const groups = new Map<string, { rule: string; word: string; items: { die: DieView; amount: number }[] }>();
  for (const d of dice)
    for (const t of d.triggered ?? []) {
      const p = parseTrigger(t);
      const key = `${p.rule}\u0000${p.word}`;
      let g = groups.get(key);
      if (g === undefined) groups.set(key, g = { rule: p.rule, word: p.word, items: [] });
      g.items.push({ die: d, amount: p.amount });
    }
  return [...groups.values()].map((g) => {
    const faceList = [...new Set(g.items.map((x) => x.die.raw))];
    const count = g.items.length;
    const who = faceList.length === 1 ? faceWord(faceList[0]!, count) : RollWords.plural(count, 'die');
    const extra = g.items.reduce((s, x) => s + x.amount, 0);
    const what = g.word.length === 0 ? g.rule : `${g.rule} ${signed(extra)} ${g.word}${extra === 1 ? '' : 's'}`;
    return { text: `${who}: ${what}`, extra, word: g.word };
  });
}

/** The log's words for a roll, composed from its record. */
export const RollWords = {
  label(roll: string): string {
    return roll === 'hit' ? 'Hit' : roll === 'defense' ? 'Block' : roll === 'roll-off' ? 'Roll-off' : capital(roll.replaceAll('-', ' '));
  },

  /** A roll made once per wound being dealt: one in a deal-wounds frame. */
  perWound(roll: string, sequence: string | null): boolean { return sequence === 'deal-wounds' && roll !== 'hit' && roll !== 'defense'; },

  /** The printed value, the changes in parentheses, `→ needs N` when anything changed it. */
  target(r: RollRecord): string {
    if (r.requiredRaw !== undefined) return r.requiredRaw <= 1 ? '' : `unmodified ${r.requiredRaw}+`;
    if (r.threshold === undefined) return '';
    const t = r.threshold;
    if (r.roll === 'dangerous-terrain' && t === 2 && r.basis === undefined && r.modifiers.length === 0) return '1 fails';
    const printed = r.basis !== undefined ? `${r.basis.stat} ${r.basis.printed}+` : `${t}+`;
    const parts: string[] = [];
    const c = r.cover;
    if (c !== undefined && !(c.inCover && c.ignoredBy === undefined))
      parts.push(c.ignoredBy !== undefined ? `cover ignored (${c.ignoredBy})` : `no cover: ${c.models}/${c.of} models`);
    if (r.basis !== undefined) parts.push(...r.basis.changes.map((x) => (x.setTo !== undefined ? `${x.source} ${x.setTo}+` : `${x.source} ${signed(x.amount)}`)));
    parts.push(...r.modifiers.map(RollWords.modifier));
    const changed = r.modifiers.length > 0 || (r.basis !== undefined && r.basis.changes.length > 0);
    const net = r.modifiers.reduce((s, m) => s + m.amount, 0);
    return printed + (parts.length === 0 ? '' : ` (${parts.join(', ')})`) + (changed ? ` → needs ${Math.min(Math.max(t - net, 2), 6)}` : '');
  },

  /** `cover +1`, a spell's `-1`; AP as p.13 writes it, `AP(1)` for the weapon's own and `Rending AP(+4)` for a granted one. */
  modifier(m: RollModifier): string {
    if (m.source === 'AP') return m.via !== undefined ? `${m.via} AP(${signed(-m.amount)})` : `AP(${-m.amount})`;
    return `${m.via !== undefined ? m.via + ' ' : ''}${m.source} ${signed(m.amount)}`;
  },

  /** The expanded lines: the printed value and each change, each modifier, the cover verdict's count. */
  details(r: RollRecord): string[] {
    const lines: string[] = [];
    if (r.basis !== undefined)
      lines.push(`${r.basis.stat} ${r.basis.printed}+`, ...r.basis.changes.map((c) => (c.setTo !== undefined ? `${c.source}: ${c.setTo}+` : `${c.source}: ${signed(c.amount)}`)));
    lines.push(...r.modifiers.map(RollWords.modifier));
    const v = r.cover;
    if (v !== undefined)
      lines.push(v.ignoredBy !== undefined ? `In cover (${v.models} of ${v.of} models), ignored by ${v.ignoredBy}`
        : `${v.inCover ? 'In cover' : 'Not in cover'}: ${v.models} of ${v.of} models`);
    if (r.threshold !== undefined) lines.push(`Target ${r.threshold}+`);
    return lines;
  },

  /** `4 hits (3 sixes: Rending)`, `1 blocked, 2 wounds`, `1 ignored, 2 wounds stand`, `passed` (`cast`), `2 of 3 passed`. */
  result(roll: string, dice: readonly DieView[], sequence: string | null = null): string {
    const ok = dice.filter((d) => d.success).length;
    const failures = dice.length - ok;
    const triggers = triggersOf(dice);
    const extra = (word: string) => triggers.filter((g) => g.word === word).reduce((s, g) => s + g.extra, 0);
    let text: string;
    if (roll === 'hit') text = RollWords.plural(ok + extra('hit'), 'hit');
    else if (roll === 'defense') text = `${ok} blocked, ${RollWords.plural(failures + extra('wound'), 'wound')}`;
    else if (RollWords.perWound(roll, sequence)) text = `${ok} ignored, ${RollWords.plural(failures, 'wound')} ${failures === 1 ? 'stands' : 'stand'}`;
    else if (dice.length === 1) text = ok === 1 ? (roll === 'casting' ? 'cast' : 'passed') : 'failed';
    else if (roll === 'dangerous-terrain') text = RollWords.plural(failures, 'wound');
    else text = `${ok} of ${dice.length} passed`;
    return triggers.length === 0 ? text : `${text} (${triggers.map((g) => g.text).join('; ')})`;
  },

  /** `1 hit`, `2 hits`, `1 die`, `3 dice`. */
  plural(n: number, word: string): string {
    return word === 'die' ? (n === 1 ? '1 die' : `${n} dice`) : `${n} ${word}${n === 1 ? '' : 's'}`;
  },
};
