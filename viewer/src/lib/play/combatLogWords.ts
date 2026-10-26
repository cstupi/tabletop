// viewer/src/lib/play/combatLogWords.ts
// client/src/Core/CombatLog.Words.cs: the combat log's headers and change words (combat-log spec → The log).

import type {
  AskAnswer, GameEvent, ModelsMoved, SequenceStarted, StateChange, StepCompleted, Team,
} from '../protocol/wire.generated';
import type { ActivationBlock } from './combatLog';
import { describe } from './logText';
import { Names } from './names';
import { type LogEntry, RollWords, capital, isTest } from './rollEntry';
import { shownCondition } from './rosterReserve';
import { nameOfOption, split } from './spellText';

/**
 * What a step did to the table: models wounded, models removed (wounded to the shelf), a unit that left the table unwounded fled,
 * each condition set or cleared (a unit other than actor, or a model's unit, named first), each grant (`{unit} gets {entry}`).
 * Outside an action a deployment and an objective's control also read, in LogText's words.
 */
export function changeLines(changes: readonly StateChange[], n: Names, outside: boolean, actor: string | null): string[] {
  const out: string[] = [];
  const moves = changes.filter((c): c is ModelsMoved => c.kind === 'models_moved').flatMap((m) => m.moves);
  const shelved = new Set(moves.filter((m) => m.to.kind === 'shelf').map((m) => m.modelId));
  const woundedIds = new Set(changes.flatMap((c) => (c.kind === 'wounds_set' ? [c.modelId] : [])));
  const wounded = [...woundedIds].filter((m) => !shelved.has(m)).length;
  const removed = [...shelved].filter((m) => woundedIds.has(m)).length;
  if (wounded > 0) out.push(`${RollWords.plural(wounded, 'model')} wounded`);
  if (removed > 0) out.push(`${RollWords.plural(removed, 'model')} removed`);
  const fled = [...shelved].filter((m) => !woundedIds.has(m)).map((m) => n.unitOfModel(m)).filter((u): u is string => u !== null);
  for (const unit of new Set(fled)) out.push(`${n.unit(unit)} Fled!`);
  for (const c of changes) {
    if (c.kind !== 'condition_set' || !shownCondition(c.condition)) continue;   // not the bookkeeping ones: the header's "moved 2\"" already says it
    const unit = c.subject.kind === 'unit' ? c.subject.id : n.unitOfModel(c.subject.id) ?? c.subject.id;
    out.push((unit === actor ? '' : `${n.unit(unit)} · `) + capital(c.condition) + (c.present ? '' : ' cleared'));
  }
  for (const c of changes) if (c.kind === 'grant_added') out.push(`${n.unit(c.unitId)} gets ${c.grant.entryName}`);
  if (!outside) return out;
  for (const c of changes)
    if ((c.kind === 'models_moved' && c.moves.length > 0 && c.moves[0]!.from.kind === 'shelf' && c.moves[0]!.to.kind === 'table') || c.kind === 'objective_control_set')
      out.push(change(c, n));
  return out;
}

/** LogText's words for a step's change, without the actor: the engine applied it. */
function change(c: StateChange, n: Names): string {
  const text = describe({ seq: 0, payload: c } as GameEvent, n);
  const engine = `${n.participant(null)} `;
  return text.startsWith(engine) ? text.slice(engine.length) : text;
}

/** One line per unit a player placed from the shelf: `{Team} · {unit} deployed`. */
export function deployed(m: ModelsMoved, n: Names): string[] {
  const units = [...new Set(m.moves.map((x) => n.unitOfModel(x.modelId) ?? x.modelId))];
  return units.map((u) => {
    const t = n.teamOfUnit(u);
    return [t !== null ? n.team(t) : null, n.unit(u)].filter((x): x is string => x !== null).join(' · ') + ' deployed';
  });
}

type Dead = { event: GameEvent; dead: boolean };

/**
 * What later steps settle about a roll, read ahead of the build. A judged single die (isTest) takes the change words of its frame's
 * later steps up to the frame's finish or the next step with a roll (those steps draw no change line: absorbed). A roll-off record's
 * dice belong, in ordinal order, to the teams whose faces so far (live amendments applied) equal another team's, every team for the
 * first record; the record after which no team is tied (else the step's last) takes winner_ordinal.
 */
export class Aftermath {
  readonly #words = new Map<string, string[]>();
  readonly labels = new Map<string, readonly string[]>();
  readonly absorbed = new Set<number>();

  consequence(roll: string): string | null {
    const w = this.#words.get(roll);
    return w !== undefined && w.length > 0 ? w.join(', ') : null;
  }

  static of(
    log: readonly GameEvent[], frames: ReadonlyMap<string, SequenceStarted>, isDead: (e: GameEvent) => boolean, n: Names,
    amendments: ReadonlyMap<string, readonly Dead[]>,
  ): Aftermath {
    const a = new Aftermath();
    const test = new Map<string, { roll: string; dead: boolean }>();
    const rollOff = new Map<string, { roll: string; dead: boolean }>();
    const teams = n.teams;
    for (const e of log) {
      const dead = isDead(e);
      const p = e.payload;
      switch (p.kind) {
        case 'sequence_started': test.delete(p.frameId); rollOff.delete(p.frameId); break;
        case 'sequence_finished': case 'sequence_abandoned': {
          const t = test.get(p.frameId);
          if (t !== undefined && t.dead === dead) test.delete(p.frameId);
          break;
        }
        case 'step_completed': {
          const started = frames.get(p.frameId);
          const pending = test.get(p.frameId);
          if (p.rolls.length > 0) test.delete(p.frameId);   // a later roll's changes are its own
          else if (pending !== undefined && pending.dead === dead) {
            const words = changeLines(p.changes, n, false, started?.context.actorUnitId ?? null);
            if (words.length > 0) { a.#words.get(pending.roll)!.push(...words); a.absorbed.add(e.seq); }
          }
          for (const r of p.rolls.filter((x) => isTest(x, started?.sequence ?? null))) { test.set(p.frameId, { roll: r.id, dead }); a.#words.set(r.id, []); }
          const offs = p.rolls.filter((r) => r.roll === 'roll-off');
          if (offs.length > 0) rollOff.set(p.frameId, { roll: a.#rollOff(offs, teams, n, amendments), dead });
          const k = p.values?.['winner_ordinal'];
          const won = rollOff.get(p.frameId);
          const winner = k === undefined ? undefined : teams.find((x) => x.ordinal === k);
          if (k !== undefined && won !== undefined && won.dead === dead && winner !== undefined) a.#words.set(won.roll, [`${n.team(winner.id)} wins`]);
          break;
        }
      }
    }
    return a;
  }

  /** Labels one roll-off step's records by the teams tied before each; the record the winner goes on. */
  #rollOff(records: readonly StepCompleted['rolls'][number][], teams: readonly Team[], n: Names, amendments: ReadonlyMap<string, readonly Dead[]>): string {
    const keys = new Map<string, number[]>(teams.map((t) => [t.id, []]));
    const same = (x: number[], y: number[]) => x.length === y.length && x.every((v, i) => v === y[i]);
    const tiedNow = (): Team[] => teams.filter((t) => teams.some((o) => o.id !== t.id && same(keys.get(o.id)!, keys.get(t.id)!)));
    let settled: string | null = null;
    for (const r of records) {
      const tied = tiedNow();
      const faces = r.dice.map((d) => d.raw);
      for (const x of (amendments.get(r.id) ?? []).filter((y) => !y.dead)) {
        const p = x.event.payload;
        if (p.kind !== 'dice_rerolled' && p.kind !== 'dice_edited') continue;
        for (const d of p.replaced) if (d.index >= 0 && d.index < faces.length) faces[d.index] = d.new.raw;
      }
      this.labels.set(r.id, faces.length === tied.length ? tied.map((t) => n.team(t.id)) : []);
      for (let i = 0; i < Math.min(tied.length, faces.length); i++) keys.get(tied[i]!.id)!.push(faces[i]!);
      if (settled === null && tied.length > 0 && tiedNow().length === 0) settled = r.id;
    }
    return settled ?? records[records.length - 1]!.id;
  }
}

/** The inner sequences whose targets are the action's: a placement's (Spawn, Split) is the unit it places, not a target. */
const ATTACK_SEQUENCES = new Set(['shooting', 'attack-sequence', 'melee', 'charge']);

/** An open block: its entries and chain so far, and what its header gathers from the events seen while it is open. A live block reads
 * only live events; an undone one reads all of its own. */
export class Draft {
  readonly #s: SequenceStarted;
  readonly #startDead: boolean;
  readonly #nested: boolean;
  readonly #n: Names;
  readonly #round: number;
  readonly #word: string;
  readonly #targets: string[] = [];
  readonly #labels = new Map<string, string>();
  readonly #moved = new Map<string, number>();
  #verb: string | null = null;
  #chosen: string | null = null;

  readonly start: GameEvent;
  readonly entries: LogEntry[] = [];
  readonly chain: string[] = [];

  /** word: the action, or `Cast` for a nested casting frame, whose own targets are not its header's. */
  constructor(start: GameEvent, s: SequenceStarted, startDead: boolean, n: Names, round: number, word: string, nested = false) {
    this.start = start; this.#s = s; this.#startDead = startDead; this.#n = n; this.#round = round; this.#word = word; this.#nested = nested;
    if (!nested) this.#target(s.context.targetUnitIds);
  }

  get actor(): string | null { return this.#s.context.actorUnitId ?? null; }

  /** `R{round} · {Team} · {Unit} · {Action}`: the header before its targets and move, what an undo into it names. */
  get baseHeader(): string {
    const a = this.actor;
    const t = a !== null ? this.#n.teamOfUnit(a) : null;
    return [`R${this.#round}`, t !== null ? this.#n.team(t) : null, a !== null ? this.#n.unit(a) : null, this.#word]
      .filter((x): x is string => x !== null).join(' · ');
  }

  see(e: GameEvent, dead: boolean): void {
    if (dead && !this.#startDead) return;
    const p = e.payload;
    switch (p.kind) {
      case 'sequence_started':
        if (p.frameId !== this.#s.frameId && (this.#nested || ((p.context.actorUnitId ?? null) === this.actor && ATTACK_SEQUENCES.has(p.sequence)))) {
          this.#target(p.context.targetUnitIds);
          if (!this.#nested && p.sequence === 'shooting') this.#verb ??= 'shoots';   // the server pushes `shooting` with no targets; its children carry them
        }
        break;
      case 'step_completed': {
        const answer: AskAnswer | undefined = p.answer;
        if (p.frameId === this.#s.frameId && answer?.kind === 'selected_pieces') this.#target(answer.pieceIds);
        if (this.#nested && p.frameId === this.#s.frameId && answer?.kind === 'chosen') this.#chosen = answer.optionId;
        if (!this.#nested) this.#moves(p.changes);
        break;
      }
      case 'ask_opened':
        if (this.#nested && p.ask.legal.kind === 'options' && p.ask.frameId === this.#s.frameId)
          for (const choice of p.ask.legal.choices) this.#labels.set(choice.id, choice.label);
        break;
    }
  }

  #target(ids: readonly string[]): void {
    for (const id of ids) if (!this.#targets.includes(id)) this.#targets.push(id);
  }

  /** Each model's table-to-table distance, summed over the block's moves. */
  #moves(changes: readonly StateChange[]): void {
    for (const c of changes) {
      if (c.kind !== 'models_moved') continue;
      for (const m of c.moves)
        if (m.from.kind === 'table' && m.to.kind === 'table') {
          const a = m.from.placement.position, b = m.to.placement.position;
          this.#moved.set(m.modelId, (this.#moved.get(m.modelId) ?? 0) + Math.hypot(b.x - a.x, b.y - a.y));
        }
    }
  }

  /** The header with its targets and the farthest any of the actor's models moved, to 0.5". */
  #header(): string {
    const n = this.#n;
    const targets = this.#targets.length === 0 ? '' : ` → ${this.#verb !== null ? this.#verb + ' ' : ''}${this.#targets.map((t) => n.piece(t)).join(', ')}`;
    if (this.#nested) {
      const label = this.#chosen === null ? undefined : this.#labels.get(this.#chosen);
      return `${this.#word}${label !== undefined ? ' ' + label : ''}${targets}`;
    }
    const actor = this.actor;
    let farthest = 0;
    for (const [model, d] of this.#moved) if (actor === null || n.unitOfModel(model) === actor) farthest = Math.max(farthest, d);
    const moved = Math.round(farthest * 2) / 2;
    return this.baseHeader + targets + (moved > 0 ? ` · moved ${moved}"` : '');
  }

  close(undone: boolean): ActivationBlock {
    const label = this.#nested && this.#chosen !== null ? this.#labels.get(this.#chosen) : undefined;
    return {
      type: 'block', seq: this.start.seq, event: this.start, undone, header: this.#header(), actor: this.actor, targets: [...this.#targets],
      entries: this.entries, chainParts: this.chain, nested: this.#nested,
      spell: this.#nested && this.#chosen !== null ? nameOfOption(this.#chosen) : null,
      spellCost: label !== undefined ? split(label).cost : null,
    };
  }
}
