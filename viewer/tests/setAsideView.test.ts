// client/src/Core/SetAsideView.cs's tests (SetAsideViewTests, SheetFocusDeployTests), through the port.
import { describe, expect, it } from 'vitest';
import { forRosterUnit } from '../src/lib/play/menuModel';
import { Names } from '../src/lib/play/names';
import { rosterOf } from '../src/lib/play/rosterView';
import { drawnKey, HOW, HOW_BACK, PlacingFocus, SET_ASIDE, setAsideChipOf, setAsideCommand } from '../src/lib/play/setAsideView';
import { sheetFocusOf } from '../src/lib/play/sheetFocus';
import type { EventPayload, SetAsideRule } from '../src/lib/protocol/wire.generated';
import { activating, blue, blue2, focusGame, red, TEAM_A } from './focusSamples';
import { P1, P2, TEAM1, boardOf, gameOf, projectionOf, unitOf } from './play';

function rig(...payloads: EventPayload[]) {
  const squad = unitOf('Squad'), reserve = unitOf('Reserve'), orcs = unitOf('Orcs');
  const game = gameOf();
  const board = boardOf([{ participantId: P1, units: [squad, reserve] }, { participantId: P2, units: [orcs] }]);
  const held = (on: boolean): EventPayload => ({ kind: 'condition_set', subject: { kind: 'unit', id: reserve.id }, condition: SET_ASIDE, present: on });
  const p = (more: EventPayload[] = []) => projectionOf(game, board, [...payloads, ...more]).projection;
  const rules = new Map<string, SetAsideRule>([[reserve.id, { name: 'Ambush', text: 'May be set aside before deployment. At the start of any round after the first, may be deployed anywhere over 9" away from enemy units.' }]]);
  return { squad, reserve, game, held, p, rules, may: new Set([reserve.id]) };
}

describe('SetAsideView', () => {
  it('a unit the server names gets a chip with the rule’s name, its text and how to set it aside', () => {
    const r = rig();
    expect(setAsideChipOf(r.p(), r.reserve.id, r.rules)).toEqual({ name: 'Ambush', tip: `May be set aside before deployment.\n${HOW}` });
    expect(setAsideChipOf(r.p(), r.squad.id, r.rules)).toBeNull();
    expect(setAsideChipOf(r.p(), r.reserve.id, new Map())).toBeNull();                       // round one: the server names none
  });

  it('a held unit’s chip says how to return it; no text gives the how alone', () => {
    const r = rig();
    expect(setAsideChipOf(r.p([r.held(true)]), r.reserve.id, r.rules)?.tip).toBe(`May be set aside before deployment.\n${HOW_BACK}`);
    expect(setAsideChipOf(r.p(), r.reserve.id, new Map([[r.reserve.id, { name: 'Ambush', text: '' }]]))?.tip).toBe(HOW);
  });

  it('the deploy pick’s Set aside sends what the roster menu sends', () => {
    const r = rig();
    const p = r.p();
    const menu = forRosterUnit(r.game, p, r.reserve.id, TEAM1, undefined, r.may).find((i) => i.label === 'Set aside')!;
    expect(setAsideCommand(p, r.reserve.id, r.may)).toEqual(menu.build(null));
    expect(setAsideCommand(p, r.squad.id, r.may)).toBeNull();
    expect(setAsideCommand(r.p([r.held(true)]), r.reserve.id, r.may)).toBeNull();
  });

  it('the roster row carries the chip', () => {
    const r = rig();
    const p = r.p();
    const rows = rosterOf(p, r.game, new Names(r.game, p), TEAM1, new Map(), undefined, r.rules)[0]!.rows;
    expect(rows.find((x) => x.unitId === r.reserve.id)?.setAside?.name).toBe('Ambush');
    expect(rows.find((x) => x.unitId === r.squad.id)?.setAside).toBeNull();
  });
});

describe('drawnKey', () => {
  it('changes with the units and their rules, not with order or new objects', () => {
    const rules = new Map<string, SetAsideRule>([['a', { name: 'Ambush', text: 'x' }], ['b', { name: 'Scout', text: 'y' }]]);
    expect(drawnKey(new Set(['a', 'b']), rules)).toBe(drawnKey(new Set(['b', 'a']), new Map(rules)));
    expect(drawnKey(new Set(['a']), rules)).not.toBe(drawnKey(new Set(['a', 'b']), rules));
    expect(drawnKey(new Set(['a']), new Map())).not.toBe(drawnKey(new Set(['a']), rules));
    expect(drawnKey(new Set(), new Map())).toBe('');
  });
});

describe('SheetFocus while deploying', () => {
  const p = activating(null);
  it('a deploy pick tile under the pointer takes the card, then the unit being placed, then the selection', () => {
    expect(sheetFocusOf(p, focusGame, TEAM_A, red.id, blue2.id, blue.id)).toBe(blue2.id);
    expect(sheetFocusOf(p, focusGame, TEAM_A, red.id, null, blue.id)).toBe(blue.id);
    expect(sheetFocusOf(p, focusGame, TEAM_A, null, null, blue.id)).toBe(blue.id);
    expect(sheetFocusOf(p, focusGame, TEAM_A, red.id)).toBe(red.id);
    expect(sheetFocusOf(p, focusGame, TEAM_A, red.id, 'not-a-unit')).toBe(red.id);
  });

  it('the unit picked holds the card until a new selection, and a new pick takes it again', () => {
    const f = new PlacingFocus();
    expect(f.placing(blue.id, red.id)).toBe(blue.id);
    expect(f.placing(blue.id, red.id)).toBe(blue.id);
    expect(f.placing(blue.id, blue2.id)).toBeNull();
    expect(f.placing(blue.id, null)).toBe(blue.id);
    expect(f.placing(blue2.id, null)).toBe(blue2.id);
    expect(f.placing(null, null)).toBeNull();
  });
});
