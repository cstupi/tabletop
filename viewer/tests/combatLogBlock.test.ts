import { describe, expect, it } from 'vitest';
import { latest, pip, filter, type ActivationBlock } from '../src/lib/play/combatLog';
import { Names } from '../src/lib/play/names';
import { collapsed, type LineEntry, type LogEntry, type RollEntry } from '../src/lib/play/rollEntry';
import type { EventPayload, RollRecord } from '../src/lib/protocol/wire.generated';
import {
  Enemy, Frame, GameSample, Model, Rifle, Round1, Token, Unit, buildSample, ctx, D, deploy, finished, grant, hit, move, rollOf, started, step, undone,
  TEAM1,
} from './logSamples';
import { askOf, newId } from './play';

// CombatLogBlockTests: headers, nested casts, consequences, roll-offs, change subjects and the lines outside actions.
const Foe = Names.short(Enemy);
const lines = (entries: readonly LogEntry[]) => entries.filter((e): e is LineEntry => e.type === 'line');
const rolls = (entries: readonly LogEntry[]) => entries.filter((e): e is RollEntry => e.type === 'roll');
const blocks = (entries: readonly LogEntry[]) => entries.filter((e): e is ActivationBlock => e.type === 'block');
const only = <T,>(xs: readonly T[]): T => { expect(xs).toHaveLength(1); return xs[0]!; };
const words = (r: RollEntry) => collapsed(r, pip);

const hold = (frame: string): EventPayload => started(frame, 'hold', ctx(Unit, [], [], 'Hold'));
/** As the server sends it: `shooting` with the Hold frame's own context, the targets on its `attack-sequence` children. */
const shooting = (frame: string): EventPayload => started(frame, 'shooting', ctx(Unit, [], [], 'Hold'));
const attack = (frame: string): EventPayload => started(frame, 'attack-sequence', ctx(Unit, [Enemy], [Rifle], 'Hold'));
const one = (roll: string, threshold: number, face: number, success: boolean): RollRecord => rollOf(roll, threshold, [D(face, face, success)]);

describe('CombatLogBlock', () => {
  it('a spawn placing a reserve does not make it a shooting target', () => {
    const place = newId(), shoot = newId(), atk = newId();
    const { entries } = buildSample(Round1, hold(Frame),
      started(place, 'place-reserve', ctx(Unit, [newId()], [], 'Spawn')), finished(place),
      shooting(shoot), attack(atk), finished(atk), finished(shoot), finished(Frame));
    const b = only(blocks(entries));
    expect(b.header).toBe(`R1 · Team 1 · Battle Brothers · Hold → shoots ${Foe}`);
    expect(b.targets).toEqual([Enemy]);
  });

  it('a hold that shoots names its target with the verb', () => {
    const shoot = newId(), atk = newId();
    const { entries } = buildSample(Round1, hold(Frame), shooting(shoot), attack(atk),
      step(atk, 'roll-to-hit', [hit()]), finished(atk), finished(shoot), finished(Frame));
    const b = only(blocks(entries));
    expect(b.header).toBe(`R1 · Team 1 · Battle Brothers · Hold → shoots ${Foe}`);
    expect(b.targets).toEqual([Enemy]);
  });

  it('an answer on the action frame\'s own step names the target', () => {
    const { entries } = buildSample(Round1, started(Frame, 'charge', ctx(Unit, [], [], 'Charge')),
      step(Frame, 'charge-target', [], [], { kind: 'selected_pieces', pieceIds: [Enemy] }), finished(Frame));
    const b = only(blocks(entries));
    expect(b.header).toBe(`R1 · Team 1 · Battle Brothers · Charge → ${Foe}`);
    expect(b.targets).toEqual([Enemy]);
  });

  it('a nested frame of another actor adds no target', () => {
    const back = newId();
    const { entries } = buildSample(Round1, hold(Frame), started(back, 'attack-sequence', ctx(Enemy, [Unit])), finished(back), finished(Frame));
    const b = only(blocks(entries));
    expect(b.targets).toEqual([]);
    expect(b.header).toBe('R1 · Team 1 · Battle Brothers · Hold');
  });

  it('a cast inside an action is a nested header over its casting roll', () => {
    const cast = newId();
    const casting = one('casting', 4, 5, true);
    const ask = askOf({ kind: 'options', choices: [{ id: 'hex|m', label: 'Hex (3)', pieces: [] }, { id: 'bolt|m', label: 'Bolt', pieces: [] }] },
      { question: 'cast', prompt: 'Cast which spell?', allowPass: false, frameId: cast });
    const chosen = { kind: 'chosen' as const, optionId: 'hex|m' };
    const { entries } = buildSample(Round1, hold(Frame),
      started(cast, 'casting', ctx(Unit, [], [], 'Hold')),
      { kind: 'ask_opened', ask }, { kind: 'ask_answered', askId: ask.id, teamId: TEAM1, answer: chosen, outsideLegalSet: false },
      { kind: 'ask_closed', askId: ask.id, answers: [{ teamId: TEAM1, answer: chosen }] },
      step(cast, 'pick-spell', [], [], chosen), step(cast, 'roll', [casting]),
      step(cast, 'targets', [], [], { kind: 'selected_pieces', pieceIds: [Enemy] }),
      finished(cast), finished(Frame));
    const b = only(blocks(entries));
    expect(b.header).toBe('R1 · Team 1 · Battle Brothers · Hold');
    const nested = only(b.entries) as ActivationBlock;
    expect(nested.nested).toBe(true);
    expect(nested.header).toBe(`Cast Hex (3) → ${Foe}`);
    expect([nested.spell, nested.spellCost]).toEqual(['hex', 3]);
    expect(words(only(nested.entries) as RollEntry)).toBe('Casting · 4+ · ⚄ · cast');
    expect(b.chainParts.join(' → ')).toBe('Casting cast');
    expect(latest(entries)!.record.id).toBe(casting.id);
    const onlyRolls = filter(entries, 'rolls', () => true, null);
    const inner = only((only(onlyRolls) as ActivationBlock).entries) as ActivationBlock;
    expect(only(inner.entries).type).toBe('roll');
  });

  it('a single die test reads its consequence and the change draws no line', () => {
    const morale = newId();
    const { entries } = buildSample(Round1, hold(Frame), started(morale, 'morale-test', ctx(Unit)),
      step(morale, 'test', [one('morale', 4, 3, false)]),
      step(morale, 'consequence', [], [{ kind: 'condition_set', subject: { kind: 'unit', id: Unit }, condition: 'shaken', present: true }]),
      finished(morale), finished(Frame));
    const b = only(blocks(entries));
    expect(words(only(b.entries) as RollEntry)).toBe('Morale · 4+ · ⚂ · failed → Shaken');
  });

  it('a condition on another unit names it and the actors own reads bare', () => {
    const theirs = newId();
    const { entries } = buildSample(Round1,
      started(theirs, 'hold', ctx(Enemy, [], [], 'Hold')),          // a strike-back's fatigue lands on the charger
      step(theirs, 'x', [], [{ kind: 'condition_set', subject: { kind: 'unit', id: Unit }, condition: 'fatigued', present: true },
        { kind: 'condition_set', subject: { kind: 'model', id: Model }, condition: 'shaken', present: true }]),
      finished(theirs),
      hold(Frame),
      step(Frame, 'x', [], [{ kind: 'condition_set', subject: { kind: 'model', id: Model }, condition: 'fatigued', present: true }]),
      finished(Frame));
    expect(blocks(entries).map((b) => lines(b.entries).map((l) => l.text))).toEqual([['Battle Brothers · Fatigued', 'Battle Brothers · Shaken'], ['Fatigued']]);
    const outside = newId();                                // a step outside any block: always names the unit
    const loose = buildSample(Round1, started(outside, 'x', ctx(undefined)),
      step(outside, 'y', [], [{ kind: 'condition_set', subject: { kind: 'unit', id: Unit }, condition: 'shaken', present: false }]));
    expect(lines(loose.entries).map((l) => l.text)).toEqual(['Round 1', 'Battle Brothers · Shaken cleared']);
  });

  it('a grant names the unit that gets it', () => {
    const { entries } = buildSample(Round1, hold(Frame), step(Frame, 'pick-rule', [], [{ kind: 'grant_added', unitId: Unit, grant }]), finished(Frame));
    expect(lines(only(blocks(entries)).entries).map((l) => l.text)).toEqual(['Battle Brothers gets Regeneration']);
  });

  it('a second undo to the same seq names the second action', () => {
    const { entries } = buildSample(Round1,
      started(Frame, 'advance', ctx(Unit, [], [], 'Advance')), finished(Frame),
      undone(1),
      started(newId(), 'charge', ctx(Unit, [], [], 'Charge')),
      undone(1));
    expect(lines(entries).map((l) => l.text)).toEqual(['Round 1', 'Undone to: R1 · Team 1 · Battle Brothers · Advance', 'Undone to: R1 · Team 1 · Battle Brothers · Charge']);
  });

  it('an undo before a block strikes it and names its whole header', () => {
    const shoot = newId(), atk = newId();
    const { entries } = buildSample(Round1, hold(Frame), shooting(shoot), attack(atk),
      step(atk, 'roll-to-hit', [hit()]), finished(atk), finished(shoot), finished(Frame), undone(1));
    const b = only(blocks(entries));
    expect(b.undone).toBe(true);
    for (const e of b.entries) expect(e.undone).toBe(true);
    expect(lines(entries).at(-1)!.text).toBe(`Undone to: R1 · Team 1 · Battle Brothers · Hold → shoots ${Foe}`);
  });

  it('outside an action a players deploy names the unit and a round ends changes read one each', () => {
    const { entries } = buildSample(move(deploy), { kind: 'round_ended', round: 1, changes: [{ kind: 'objective_control_set', markerId: Token.id, teamId: TEAM1 }] });
    expect(lines(entries).map((l) => l.text)).toEqual(['Team 1 · Battle Brothers deployed', `Team 1 took ${new Names(GameSample, null).marker(Token.id)}`]);
  });

  it('a roll-off names each teams die and the winner after the tie breaks', () => {
    const first = newId(), second = newId();
    const off = (frame: string, ...r: RollRecord[]) => step(frame, 'roll-off', r);
    const order = (frame: string, winner: number) => step(frame, 'order', [], [], undefined, { winner_ordinal: winner });
    const dice = (a: number, b: number) => rollOf('roll-off', undefined, [D(a, a, false), D(b, b, false)]);
    const { entries } = buildSample(
      started(first, 'roll-off', ctx(undefined)), off(first, dice(4, 1)), order(first, 1), finished(first),
      started(second, 'roll-off', ctx(undefined)), off(second, dice(3, 3), dice(1, 4)), order(second, 2), finished(second));
    expect(rolls(entries).map(words)).toEqual(['Roll-off · Team 1 ⚃ · Team 2 ⚀ · Team 1 wins', 'Roll-off · Team 1 ⚂ · Team 2 ⚂', 'Roll-off · Team 1 ⚀ · Team 2 ⚃ · Team 2 wins']);
  });

  it('a roll-off tie edited away names the winner on the edited record', () => {
    const frame = newId();
    const dice = (a: number, b: number) => rollOf('roll-off', undefined, [D(a, a, false), D(b, b, false)]);
    const tied = dice(3, 3), breaker = dice(1, 4);
    const { entries } = buildSample(
      started(frame, 'roll-off', ctx(undefined)), step(frame, 'roll-off', [tied, breaker]),
      { kind: 'dice_edited', frameId: frame, rollId: tied.id, replaced: [{ index: 0, old: D(3, 3, false), new: D(5, 5, false) }] },
      step(frame, 'order', [], [], undefined, { winner_ordinal: 1 }), finished(frame));
    expect(rolls(entries).map(words)).toEqual(['Roll-off · Team 1 ⚄ · Team 2 ⚂ · Team 1 wins · set by hand ⚂ → ⚄', 'Roll-off · 2 dice: ⚀ ⚃']);
  });
});
