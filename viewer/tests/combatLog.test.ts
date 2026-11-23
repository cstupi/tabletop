import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import { build as buildFrom, filter, flatten, latest, pip, type ActivationBlock } from '../src/lib/play/combatLog';
import { Names } from '../src/lib/play/names';
import { collapsed, type LineEntry, type LogEntry, type RollEntry } from '../src/lib/play/rollEntry';
import type { RollRecord } from '../src/lib/protocol/wire.generated';
import {
  BoardSample, D, Enemy, Frame, GameSample, Model, P1, Rifle, Roll, Round1, SlotSample, TEAM1, TEAM2, Unit, UnitSample, abandoned, buildOn, buildSample,
  ctx, deploy, edit, finished, hit, hitWith, move, reroll, rollOf, started, step, undone,
} from './logSamples';
import { askOf, at, boardOf, ev, newId } from './play';

const lines = (entries: readonly LogEntry[]) => entries.filter((e): e is LineEntry => e.type === 'line');
const rolls = (entries: readonly LogEntry[]) => entries.filter((e): e is RollEntry => e.type === 'roll');
const blocks = (entries: readonly LogEntry[]) => entries.filter((e): e is ActivationBlock => e.type === 'block');
const block = (e: LogEntry | undefined): ActivationBlock => { expect(e?.type).toBe('block'); return e as ActivationBlock; };
const words = (r: RollEntry) => collapsed(r, pip);

const defense5 = { stat: 'Defense', printed: 5, changes: [] };
const quality3 = { stat: 'Quality', printed: 3, changes: [] };
const quality4 = { stat: 'Quality', printed: 4, changes: [] };

/** The one roll a single step of a shooting frame records, as the log builds it. */
function line(roll: RollRecord): RollEntry {
  const { entries } = buildSample(Round1, started(Frame, 'shooting', ctx(Unit, [Enemy], [Rifle], 'Hold')), step(Frame, 'roll', [roll]));
  return rolls(block(entries[1]).entries)[0]!;
}

const roll = (name: string, threshold: number, modifiers: RollRecord['modifiers'], dice: RollRecord['dice'], basis?: RollRecord['basis'], cover?: RollRecord['cover']) =>
  rollOf(name, threshold, [...dice], { modifiers, basis, cover });

describe('CombatLog', () => {
  it('a block in cover folds cover and AP into what it needs and shows the raw faces', () => {
    const r = line(roll('defense', 5, [{ source: 'cover', amount: 1 }, { source: 'AP', amount: -1 }], [D(6, 6, true), D(2, 2, false), D(1, 1, false)], defense5, { inCover: true, models: 4, of: 6 }));
    expect(words(r)).toBe('Block · Defense 5+ (cover +1, AP(1)) → needs 5 · 3 dice: ⚅ ⚁ ⚀ · 1 blocked, 2 wounds');
    expect(r.details).toContain('In cover: 4 of 6 models');
  });

  it('a block out of cover says nothing of cover', () => {
    const r = line(roll('defense', 5, [{ source: 'AP', amount: -1 }], [D(1, 0, false)], defense5, { inCover: false, models: 2, of: 6 }));
    expect(words(r)).toBe('Block · Defense 5+ (AP(1)) → needs 6 · ⚀ · 0 blocked, 1 wound');
  });

  it('a block whose cover a rule ignored names the rule', () => {
    const r = line(roll('defense', 5, [], [D(4, 4, false)], defense5, { inCover: true, models: 1, of: 1, ignoredBy: 'Blast' }));
    expect(words(r)).toBe('Block · Defense 5+ (cover ignored (Blast)) · ⚃ · 0 blocked, 1 wound');
  });

  it('a granted modifier carries the rule that granted it and past six needs six', () => {
    const r = line(roll('defense', 5, [{ source: 'AP', amount: -1 }, { source: 'AP', amount: -4, via: 'Rending' }], [D(6, 1, true)], defense5, { inCover: false, models: 0, of: 3 }));
    expect(words(r)).toBe('Block · Defense 5+ (AP(1), Rending AP(+4)) → needs 6 · ⚅ · 1 blocked, 0 wounds');
    expect(r.details).toEqual(['Heavy Rifle', 'Defense 5+', 'AP(1)', 'Rending AP(+4)', 'Not in cover: 0 of 3 models', 'Target 5+']);
  });

  it('an old record without basis or verdict folds its modifiers into its threshold', () => {
    expect(words(line(roll('defense', 5, [{ source: 'AP', amount: -1 }], [D(1, 0, false)])))).toBe('Block · 5+ (AP(1)) → needs 6 · ⚀ · 0 blocked, 1 wound');
  });

  it('a hit counts the dice that set off a rule by the rule', () => {
    const r = line(roll('hit', 3, [], [D(2, 2, false), D(6, 6, true, 'Rending'), D(1, 1, false), D(6, 6, true, 'Rending'), D(6, 6, true, 'Rending'), D(1, 1, false), D(5, 5, true), D(1, 1, false)], quality3));
    expect(words(r)).toBe('Hit · Quality 3+ · 8 dice: ⚁ ⚅ ⚀ ⚅ ⚅ ⚀ ⚄ ⚀ · 4 hits (3 sixes: Rending)');
  });

  it("a hit's extra hits add to its total", () => {
    const r = line(roll('hit', 4, [], [D(6, 6, true, 'Furious +1 hit'), D(6, 6, true, 'Furious +1 hit')], quality4));
    expect(words(r)).toBe('Hit · Quality 4+ · 2 dice: ⚅ ⚅ · 4 hits (2 sixes: Furious +2 hits)');
  });

  it('a threshold basis change is named and below two needs two', () => {
    const basis = { stat: 'Quality', printed: 5, changes: [{ source: 'Reliable', setTo: 2, amount: 0 }] };
    const r = line(roll('hit', 2, [{ source: 'Spell', amount: 1 }], [D(1, 2, false)], basis));
    expect(words(r)).toBe('Hit · Quality 5+ (Reliable 2+, Spell +1) → needs 2 · ⚀ · 0 hits');
    expect(r.details).toEqual(['Heavy Rifle', 'Quality 5+', 'Reliable: 2+', 'Spell +1', 'Target 2+']);
  });

  it('a single die test reads passed or failed and a raw face roll says unmodified', () => {
    expect(words(line(roll('morale', 4, [], [D(1, 1, false)], quality4)))).toBe('Morale · Quality 4+ · ⚀ · failed');
    const fatigued = rollOf('hit', 6, [D(6, 6, true)], { requiredRaw: 6 });
    expect(words(line(fatigued))).toBe('Hit · unmodified 6+ · ⚅ · 1 hit');
  });

  it("rerolls and edits stay on the roll's line", () => {
    const other = hitWith(newId());
    const { entries } = buildSample(started(Frame, 'x', ctx(Unit)), step(Frame, 'roll-to-hit', [hit(), other]),
      edit(0, D(6, 6, true), D(1, 1, false)), reroll(1, D(2, 2, false), D(5, 5, true)));
    const r = rolls(entries);
    expect(words(r[0]!)).toBe('Hit · 4+ (AP(1)) → needs 5 · 2 dice: ⚀ ⚄ · 1 hit · set by hand ⚅ → ⚀ · rerolled ⚁ → ⚄');
    expect(r[1]!.amendments).toEqual([]);                                  // the other pool of the same step
    expect(r[1]!.dice.map((d) => d.raw)).toEqual([6, 2]);
    expect(lines(entries)).toEqual([]);
  });

  it('a rerolled die counts the triggers its new face set off', () => {
    const h: RollRecord = { id: Roll, roll: 'hit', threshold: 4, modifiers: [], dice: [D(2, 2, false), D(6, 6, true, 'Rending')] };
    const { entries } = buildSample(started(Frame, 'x', ctx(Unit)), step(Frame, 'roll-to-hit', [h]), reroll(0, D(2, 2, false), D(6, 6, true, 'Rending')));
    const r = rolls(entries)[0]!;
    expect(words(r)).toBe('Hit · 4+ · 2 dice: ⚅ ⚅ · 2 hits (2 sixes: Rending) · rerolled ⚁ → ⚅');
    expect(r.dice[0]!.triggered).toEqual(['Rending']);
  });

  it('a roll with no target number shows its faces without a result', () => {
    const r0: RollRecord = { id: Roll, roll: 'scatter', modifiers: [{ source: 'bonus', amount: 1 }], dice: [D(4, 5, false), D(2, 2, false)] };
    const { entries } = buildSample(Round1, started(Frame, 'x', ctx(Unit, [], [], 'Hold')), step(Frame, 'roll', [r0]), finished(Frame));
    const b = block(entries[1]);
    const r = rolls(b.entries)[0]!;
    expect(r.dice.map((d) => `${d.raw}${d.modified === d.raw ? '' : ` → ${d.modified}`}`)).toEqual(['4 → 5', '2']);
    expect(words(r)).toBe('Scatter · 2 dice: ⚃ ⚁');
    expect(b.chainParts.join(' → ')).toBe('Scatter');
  });

  it('questions answers closes empty steps and frame bookkeeping are not drawn', () => {
    const nested = newId();
    const ask = askOf({ kind: 'confirmation' }, { question: 'q', prompt: 'Pick', allowPass: false, frameId: Frame });
    const { entries } = buildSample(Round1, { kind: 'turn_started', teamId: TEAM1 },
      started(Frame, 'charge', ctx(Unit, [Enemy], [], 'Charge')),
      { kind: 'ask_opened', ask }, { kind: 'ask_answered', askId: ask.id, teamId: TEAM1, answer: { kind: 'confirmed' }, outsideLegalSet: false },
      { kind: 'ask_closed', askId: ask.id, answers: [] },
      step(Frame, 'charge-target'),
      started(nested, 'melee', ctx(Unit, [Enemy])), finished(nested), abandoned(Frame), { kind: 'turn_ended', teamId: TEAM1, passed: false, changes: [] });
    expect(lines(entries).map((l) => l.text)).toEqual(['Round 1']);
    expect(block(entries[1]).entries).toEqual([]);
    expect(entries).toHaveLength(2);
  });

  it('a header names the round team unit action targets and the farthest move', () => {
    const to = { kind: 'table' as const, placement: at(12, 19.4) };
    const { entries } = buildSample(Round1, move(deploy), { kind: 'round_started', round: 2, order: [TEAM1, TEAM2], changes: [] },
      started(Frame, 'charge', ctx(Unit, [Enemy], [], 'Charge')),
      step(Frame, 'move', [], [{ kind: 'models_moved', moves: [{ modelId: Model, from: deploy.to, to }] }]), finished(Frame));
    const b = block(blocks(entries)[0]);
    expect(b.header).toBe(`R2 · Team 1 · Battle Brothers · Charge → ${Names.short(Enemy)} · moved 7.5"`);
    expect(b.entries).toEqual([]);                                        // the move is in the header
  });

  it('a unit sent to the shelf unwounded after a failed morale test fled', () => {
    const morale = roll('morale', 4, [], [D(1, 1, false)], quality4);
    const shelf = step(Frame, 'consequence', [], [{ kind: 'models_moved', moves: [{ modelId: Model, from: deploy.to, to: { kind: 'shelf' } }] }]);
    const { entries } = buildSample(Round1, move(deploy), started(Frame, 'charge', ctx(Unit, [Enemy], [], 'Charge')),
      step(Frame, 'test', [morale]), shelf, finished(Frame));
    const b = block(blocks(entries)[0]);
    // The test's consequence goes on its line; the rout draws no line of its own.
    expect(b.entries.map((e) => (e.type === 'roll' ? words(e) : (e as LineEntry).text))).toEqual(['Morale · Quality 4+ · ⚀ · failed → Battle Brothers Fled!']);
    expect(b.chainParts.join(' → ')).toBe('Morale failed → Battle Brothers Fled!');

    // A rout no single-die test precedes in its frame reads as its own line.
    const loose = buildSample(Round1, move(deploy), started(Frame, 'charge', ctx(Unit, [Enemy], [], 'Charge')), shelf, finished(Frame));
    expect(lines(blocks(loose.entries)[0]!.entries).map((l) => l.text)).toEqual(['Battle Brothers Fled!']);
  });

  it('an undo reads undone to the header it took back and a menu edit reads what changed', () => {
    const { entries } = buildSample(Round1, started(Frame, 'advance', ctx(Unit, [], [], 'Advance')), step(Frame, 'roll-to-hit', [hit()]), finished(Frame),
      undone(1), { kind: 'wounds_set', modelId: Model, wounds: 1 });
    const ls = lines(entries).map((l) => l.text);
    expect(ls).toContain('Undone to: R1 · Team 1 · Battle Brothers · Advance');
    expect(ls.some((l) => l.startsWith('Battle Brothers (') && l.endsWith(' set to 1 wound · by Clay'))).toBe(true);
  });

  it('a hand edit reads what changed and names only another player', () => {
    const set = (present: boolean) => ({ kind: 'condition_set' as const, subject: { kind: 'unit' as const, id: Unit }, condition: 'set-aside', present });
    const { applier } = buildOn(BoardSample, GameSample, [set(true), set(false)]);
    const of = (game: typeof GameSample, me: string) => lines(flatten(buildFrom(applier.log, new Names(game, applier.projection, me)))).map((l) => l.text);
    expect(of(GameSample, P1)).toEqual(['Battle Brothers set aside', 'Battle Brothers returned to deployment']);
    const ann = { id: newId(), accountId: newId(), name: 'Ann', teamId: TEAM2 };
    expect(of({ ...GameSample, participants: [...GameSample.participants, ann] }, ann.id)[0]).toBe('Battle Brothers set aside · by Clay');
  });

  it('a second roll-to-hit in the frame (extra attacks taken on an Ask) reads the attack once, on the pool', () => {
    const nested = newId();
    const { entries } = buildSample(Round1, started(Frame, 'hold', ctx(Unit, [Enemy], [Rifle], 'Hold')), started(nested, 'attack-sequence', ctx(Unit, [Enemy], [Rifle], 'Hold')),
      step(nested, 'roll-to-hit', [hit()]), step(nested, 'roll-to-hit', [hitWith(newId())]), finished(nested), finished(Frame));
    const rs = rolls(flatten(entries));
    expect(rs).toHaveLength(2);
    expect(rs[0]!.attack?.[0]).toMatch(/^Attack: /);
    expect(rs[1]!.attack).toBeNull();
  });

  it('an action frame groups its entries under one header with a chain summary', () => {
    const nested = newId();
    const { entries } = buildSample({ kind: 'round_started', round: 2, order: [TEAM1, TEAM2], changes: [] },
      started(Frame, 'advance', ctx(Unit, [Enemy], [Rifle], 'Advance')), started(nested, 'attack-sequence', ctx(Unit, [Enemy], [Rifle], 'Advance')),
      step(nested, 'roll-to-hit', [hit()]),
      step(nested, 'remove', [], [{ kind: 'wounds_set', modelId: Model, wounds: 1 }, { kind: 'condition_set', subject: { kind: 'unit', id: Unit }, condition: 'shaken', present: true }]),
      finished(nested), finished(Frame), { kind: 'turn_ended', teamId: TEAM1, passed: false, changes: [] });
    expect(entries).toHaveLength(2);
    const b = block(entries[1]);
    expect(b.header).toBe(`R2 · Team 1 · Battle Brothers · Advance → ${Names.short(Enemy)}`);
    expect(b.chainParts.join(' → ')).toBe('Hit 1 → 1 model wounded → Battle Brothers · Shaken');
  });

  it('an undone activation closes struck through and an undo inside one keeps it open', () => {
    const second = newId();
    const { entries } = buildSample(Round1,
      started(Frame, 'advance', ctx(Unit, [], [], 'Advance')), step(Frame, 'roll-to-hit', [hit()]),
      undone(1),                                                                              // before the block: closes it
      started(second, 'hold', ctx(Unit, [], [], 'Hold')), step(second, 'roll-to-hit', [hitWith(newId())]),
      undone(5),                                                                              // inside the block: stays open
      finished(second));
    const bs = blocks(entries);
    expect(bs.map((b) => b.undone)).toEqual([true, false]);
    for (const e of bs[0]!.entries) expect(e.undone).toBe(true);
    expect([bs[0]!.chainParts.join(' → '), bs[1]!.chainParts.join(' → ')]).toEqual(['', '']);   // an undone roll is not in a chain
    expect(rolls(bs[1]!.entries)[0]!.undone).toBe(true);
    expect(bs[1]!.entries.some((e) => e.type === 'line' && e.event.payload.kind === 'undone')).toBe(true);
    expect(entries.some((e) => e.type === 'line' && e.event.payload.kind === 'undone')).toBe(true);
    expect(latest(entries)).toBeNull();
  });

  it('filters keep rolls my units or the carded unit', () => {
    const mine = newId(), theirs = newId();
    const { entries, names } = buildSample(Round1,
      started(mine, 'hold', ctx(Unit, [], [], 'Hold')), finished(mine),
      started(theirs, 'advance', ctx(Enemy, [], [], 'Advance')), step(theirs, 'roll-to-hit', [hit()]), finished(theirs));
    const isMine = (u: string) => names.teamOfUnit(u) === TEAM1;
    const actorOf = (e: LogEntry) => (e as ActivationBlock).actor;
    expect(filter(entries, 'mine', isMine, null).map(actorOf)).toEqual([Unit]);
    const onlyRolls = filter(entries, 'rolls', isMine, null);
    expect(onlyRolls).toHaveLength(1);
    for (const e of block(onlyRolls[0]).entries) expect(e.type).toBe('roll');
    expect(filter(entries, 'unit', isMine, Enemy).map(actorOf)).toEqual([Enemy]);
    expect(filter(entries, 'unit', isMine, null)).toEqual([]);
    expect(filter(entries, 'all', isMine, null)).toBe(entries);
  });

  it('a model wounded then removed in one step reads removed not wounded', () => {
    const second = { ...SlotSample, id: newId(), ordinal: 2 };
    const board = boardOf([{ participantId: P1, units: [{ ...UnitSample, models: [SlotSample, second] }] }]);
    const shelved = { modelId: Model, from: deploy.to, to: { kind: 'shelf' as const } };
    const { entries } = buildOn(board, GameSample, [Round1, move(deploy), started(Frame, 'advance', ctx(Unit, [Enemy], [], 'Advance')),
      step(Frame, 'remove', [], [{ kind: 'wounds_set', modelId: Model, wounds: 3 }, { kind: 'models_moved', moves: [shelved] }, { kind: 'wounds_set', modelId: second.id, wounds: 1 }]),
      finished(Frame)]);
    expect(blocks(entries)[0]!.chainParts.join(' → ')).toBe('1 model wounded → 1 model removed');
  });

  it('an undo into a finished activation reopens it until its live finish', () => {
    const { entries } = buildSample(Round1,
      started(Frame, 'advance', ctx(Unit, [], [], 'Advance')), step(Frame, 'roll-to-hit', [hit()]), finished(Frame),
      undone(3),                                                                              // into the block: its finish dies
      step(Frame, 'roll-to-hit', [hitWith(newId())]), finished(Frame));
    expect(entries).toHaveLength(2);
    const b = block(entries[1]);
    expect(b.undone).toBe(false);
    expect(b.entries.map((e) => e.seq)).toEqual([3, 5, 6]);
    expect(b.chainParts.join(' → ')).toBe('Hit 1 → Hit 1');
  });

  it('a block is found by the targets of its nested frames', () => {
    const nested = newId();
    const { entries } = buildSample(Round1,
      started(Frame, 'shooting', ctx(Unit, [], [], 'Hold')), started(nested, 'attack-sequence', ctx(Unit, [Enemy], [Rifle])),
      step(nested, 'roll-to-hit', [hit()]), finished(nested), finished(Frame));
    const found = filter(entries, 'unit', () => false, Enemy);
    expect(found).toHaveLength(1);
    expect(block(found[0]).targets).toEqual([Enemy]);
    expect(filter(entries, 'mine', (u) => u === Enemy, null)).toHaveLength(1);               // the attacked unit's side
  });

  it('an undone block closes at its own finish or abandonment', () => {
    const second = newId(), third = newId();
    const { entries } = buildSample(Round1,
      started(Frame, 'advance', ctx(Unit, [], [], 'Advance')), step(Frame, 'roll-to-hit', [hit()]), finished(Frame),
      started(second, 'hold', ctx(Enemy, [], [], 'Hold')), step(second, 'roll-to-hit', [hitWith(newId())]), abandoned(second),
      started(third, 'hold', ctx(Unit, [], [], 'Hold')), finished(third),
      undone(1));
    const bs = blocks(entries);
    expect(bs.map((b) => b.seq)).toEqual([2, 5, 8]);
    for (const b of bs) expect(b.undone).toBe(true);
    expect(bs.map((b) => b.actor)).toEqual([Unit, Enemy, Unit]);
    expect(rolls(bs[0]!.entries)).toHaveLength(1);
    expect(rolls(bs[1]!.entries)).toHaveLength(1);
  });

  it("the engine's action record draws no line and a hand record does", () => {
    const a = new Applier(BoardSample, GameSample);
    a.applyAll([
      ev(1, Round1, P1), ev(2, { kind: 'action_recorded', unitId: Unit, action: 'Hold' }),
      ev(3, started(Frame, 'activation', ctx(Unit, [], [], 'Hold'))), ev(4, finished(Frame)),
      ev(5, { kind: 'action_recorded', unitId: Unit, action: 'Advance' }, P1)]);
    const n = new Names(GameSample, a.projection);
    const ls = lines(flatten(buildFrom(a.log, n))).map((l) => l.text);
    expect(ls.some((l) => l.includes('recorded Hold'))).toBe(false);
    expect(ls).toContain('Battle Brothers recorded as Advance · by Clay');
  });
});
