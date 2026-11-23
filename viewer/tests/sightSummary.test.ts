import { describe, expect, it } from 'vitest';
import { dimmed, summaryText } from '../src/lib/play/sightSummary';
import type { ShotRange, SightAnswer, SightLine } from '../src/lib/protocol/wire.generated';
import { P1, boardOf, gameOf, newId, option, projectionOf, slotOf, unitOf, weaponOf } from './play';

const R1 = weaponOf('Rifle'), R2 = weaponOf('Rifle'), PG = weaponOf('Plasma Gun');
const shooters = unitOf('Blue Warriors', [slotOf({ name: 'T', ordinal: 1, weapons: [R1] }), slotOf({ name: 'T', ordinal: 2, weapons: [R2] }), slotOf({ name: 'T', ordinal: 3, weapons: [PG] })]);
const p = projectionOf(gameOf(), boardOf([{ participantId: P1, units: [shooters] }])).projection;
const M = (i: number) => shooters.models[i]!.id;
const T1 = newId(), T2 = newId();
const rifles = option('Rifle', 'Rifle', [R1.id, R2.id]), plasma = option('Plasma Gun', 'Plasma Gun', [PG.id]);
const line = (from: string, clear: boolean, screened: boolean): SightLine => ({ fromModelId: from, toModelId: newId(), clear, screened, blockedBy: clear ? [] : [newId()] });
const answer = (lines: SightLine[], canShoot: string[]): SightAnswer => ({ kind: 'sight_answer', queryId: newId(), lines, canShoot });

describe('SightSummary', () => {
  it('the summary counts seeing models and screened clear lines', () => {
    const a = answer([line(M(0), true, true), line(M(0), true, false), line(M(1), true, true), line(M(2), false, false)], [M(0)]);
    expect(summaryText(a, 'Red Warriors', false)).toBe('2 of 3 see Red Warriors · 2 screened');
    expect(summaryText(a, 'Red Warriors', true)).toBe('2 of 3 see Red Warriors · 2 screened · 1 can shoot');
    expect(summaryText(answer([line(M(0), true, false), line(M(1), false, false)], []), 'Red Warriors', false))
      .toBe('1 of 2 see Red Warriors · 0 screened');
    expect(summaryText(answer([line(M(0), false, false)], []), 'Red Warriors', false)).toBe('0 of 1 see Red Warriors');
  });

  it('under a target pick the summary names the ranges rules changed against the target', () => {
    const range = (weapon: string, printed: number, inches: number, by: string[]): ShotRange => ({ weapon, printed, inches, by });
    const a: SightAnswer = { ...answer([line(M(0), true, false), line(M(1), true, false), line(M(2), true, false)], [M(2)]),
      ranges: [range('Carbine', 18, 14, ['Shadowborn']), range('Plasma Rifle', 24, 20, ['Shadowborn']), range('Pistol', 12, 12, [])] };
    expect(summaryText(a, 'Reapers', true))
      .toBe('3 of 3 see Reapers · 0 screened · 1 can shoot · Carbine 18" → 14", Plasma Rifle 24" → 20" (Shadowborn)');
    expect(summaryText(a, 'Reapers', false)).toBe('3 of 3 see Reapers · 0 screened');
    expect(summaryText({ ...a, ranges: [range('Carbine', 18, 13.5, ['Shadowborn', 'Long Shot'])] }, 'Reapers', true))
      .toMatch(/ · 1 can shoot · Carbine 18" → 13\.5" \(Shadowborn, Long Shot\)$/);
    expect(summaryText({ ...a, ranges: undefined }, 'Reapers', true)).toMatch(/ · 1 can shoot$/);
  });

  it('dimmed are the models that cannot shoot the hovered target or their groups\' assigned one', () => {
    const hovered = answer([line(M(0), true, false), line(M(1), true, false), line(M(2), true, false)], [M(0), M(2)]);
    expect(dimmed(p, hovered, [rifles, plasma], new Map(), new Map())).toEqual(new Set([M(1)]));
    const assigned = new Map([['Rifle', T1], ['Plasma Gun', T2]]);
    const answers = new Map([[T1, answer([], [M(0), M(2)])], [T2, answer([], [M(0)])]]);
    expect(dimmed(p, null, [rifles, plasma], assigned, answers)).toEqual(new Set([M(1), M(2)]));
    expect(dimmed(p, null, [rifles, plasma], assigned, new Map())).toEqual(new Set());
  });
});
