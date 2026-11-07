import { describe, expect, it } from 'vitest';
import { choicesOf, clickAnswers, decisionOf, noteOf, number, rows, stopLabel, unitNamed } from '../src/lib/play/decisionView';
import { Names } from '../src/lib/play/names';
import type { Ask, LegalSet, MoveRegion } from '../src/lib/protocol/wire.generated';
import { TEAM1, TEAM2, askOf, newId, option, projectionOf, sample } from './play';

const s = sample();
const region = (over: Partial<MoveRegion> = {}): MoveRegion => ({
  kind: 'move_region', modelIds: [s.model.id], allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false,
  passesModels: false, ignoresTerrain: false, ...over,
});
const none = new Set<string>();
const of = (ask: Ask, me: string | null, muted: ReadonlySet<string> = none) => {
  const a = projectionOf(s.game, s.board, [{ kind: 'ask_opened', ask }]);
  return decisionOf(a.projection, s.game, new Names(s.game, a.projection), me, muted);
};
const ask = (question: string, legal: LegalSet, prompt = 'prompt', over: Parameters<typeof askOf>[1] = {}) => askOf(legal, { question, prompt, allowPass: false, ...over });

describe('DecisionView', () => {
  it('options are numbered and the hint names the keys', () => {
    const a = ask('activate', { kind: 'options', choices: [option('a', 'Advance', [s.squad.id]), option('h', 'Hold', [s.squad.id])] }, 'Activate a unit');
    const d = of(a, TEAM1);
    expect([d.ask!.id, d.mode, d.title]).toEqual([a.id, 'choose', 'Activate a unit']);
    expect(d.numbered).toEqual([{ key: 1, label: 'Advance', optionId: 'a' }, { key: 2, label: 'Hold', optionId: 'h' }]);
    expect(d.hint).toBe('1–2: pick · click a glowing unit');
    expect(of(ask('shoot_or_not', { kind: 'options', choices: [option('s', 'Shoot')] }), TEAM1).hint).toBe('1: pick');
  });

  it('past nine an entry has no key', () => {
    const many: LegalSet = { kind: 'options', choices: Array.from({ length: 11 }, (_, i) => option(`o${i + 1}`, `Option ${i + 1}`)) };
    const d = of(ask('activate', many), TEAM1);
    expect(d.numbered.map((x) => x.key)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 0]);
    expect(d.hint).toBe('1–9: pick');
  });

  it('a deploy ask numbers the shelved units and a target pick asks for a click', () => {
    const deploy = of(ask('deploy', { kind: 'pieces', eligible: [s.squad.id], min: 1, max: 1 }), TEAM1);
    expect(deploy.numbered).toEqual([{ key: 1, label: 'Battle Brothers', optionId: s.squad.id }]);
    expect(deploy.hint).toBe('1: pick a unit to deploy');
    const charge = of(ask('charge_target', { kind: 'pieces', eligible: [s.squad.id], min: 1, max: 1 }), TEAM1);
    expect(charge.numbered).toEqual([]);
    expect(charge.hint).toBe('Click a glowing unit');
    expect(of(ask('takedown_target', { kind: 'pieces', eligible: [s.model.id], min: 1, max: 1 }), TEAM1).hint).toBe('Click a glowing model');
    expect(of(ask('spell_targets', { kind: 'pieces', eligible: [s.squad.id], min: 1, max: 2 }), TEAM1).hint).toBe('Click a glowing unit · Enter: done');
  });

  it('a stoppable move offers cast when a moving model holds spell tokens', () => {
    const stoppable = region({ stoppable: true });
    const a = projectionOf(s.game, s.board);
    expect(stopLabel(a.projection, stoppable)).toBe('Stop here to use a rule');
    expect(stopLabel(a.projection, region())).toBeNull();
    const withTokens = projectionOf(s.game, s.board, [{ kind: 'spell_tokens_set', modelId: s.model.id, tokens: 2 }, { kind: 'ask_opened', ask: ask('move', stoppable) }]);
    expect(stopLabel(withTokens.projection, stoppable)).toBe('Cast a spell');
    expect(decisionOf(withTokens.projection, s.game, new Names(s.game, withTokens.projection), TEAM1, none).hint).toContain('C: cast a spell · ');
  });

  it('each mode has its hint and casualties title the count', () => {
    expect(of(ask('move', region()), TEAM1).hint).toBe('Drag to move · Shift: pick models · 1–5 while holding: rows · Q/E: turn · Enter: commit · Esc: reset');
    expect(of(ask('move', region({ stoppable: true })), TEAM1).hint)
      .toBe('Drag to move · Shift: pick models · 1–5 while holding: rows · Q/E: turn · Enter: commit · C: stop here to use a rule · Esc: reset');
    expect(of(ask('declare_targets', { kind: 'assignment', items: [option('Rifles', 'Rifles')], targets: [s.squad.id], maxDistinctTargets: 2 }), TEAM1).hint)
      .toBe('Click a target · Click a chip: that group alone · Enter: fire · Esc: clear');
    const casualties = of(ask('remove_casualties', { kind: 'allocation', targets: [s.model.id], amount: 3 }, 'Assign 3 wound(s)'), TEAM1);
    expect([casualties.title, casualties.hint]).toEqual(['Remove 3', 'Click a model: +1 · Right-click: −1 · Auto: fill the rest · Enter: confirm']);
    expect(of(ask('spend_tokens', { kind: 'token_spend', maxByModel: { [s.model.id]: 1 } }), TEAM1).hint).toBe('Set the tokens per caster · Enter: spend');
    expect(of(ask('confirm', { kind: 'confirmation' }), TEAM1).hint).toBe('Enter: Continue');
    expect(of(ask('end_round', { kind: 'confirmation' }), TEAM1).hint).toBe('Enter: End the round');
  });

  it('nothing addressed to me gives no ask', () => {
    const a = ask('remove_casualties', { kind: 'allocation', targets: [s.model.id], amount: 2 }, 'p', { teams: [TEAM2] });
    for (const me of [TEAM1, null]) {
      const d = of(a, me);
      expect([d.ask, d.mode, d.title, d.hint, d.numbered]).toEqual([null, null, '', '', []]);
    }
  });

  it('a muted window is an auto pass', () => {
    const a = ask('react', { kind: 'options', choices: [option('r', 'React')] }, 'p', { allowPass: true, windowKind: 'reaction' });
    expect(of(a, TEAM1, new Set(['reaction'])).autoPass).toBe(true);
    expect(of(a, TEAM1).autoPass).toBe(false);
  });

  it('only a piece one choice names answers a click', () => {
    const other = newId();
    const options = { kind: 'options', choices: [option('a', 'Advance', [s.squad.id]), option('h', 'Hold', [s.squad.id]), option('s', 'Strike', [other])] } as const;
    expect([...clickAnswers(options)]).toEqual([other]);
  });

  it('a unit narrows the box to its choices numbered from one', () => {
    const a = projectionOf(s.game, s.board);
    const other = newId();
    const options = { kind: 'options', choices: [option('x', 'Other: Hold', [other]), option('a', 'Advance', [s.squad.id]), option('m', 'Charge', [s.model.id])] } as const;
    const narrowed = choicesOf(options, a.projection, s.squad.id);
    expect(narrowed.map((c) => c.id)).toEqual(['a', 'm']);
    expect(number(narrowed)).toEqual([{ key: 1, label: 'Advance', optionId: 'a' }, { key: 2, label: 'Charge', optionId: 'm' }]);
    expect(choicesOf(options, a.projection, newId())).toEqual([]);
  });

  it('an option names one unit by its id or its models\' ids', () => {
    const p = projectionOf(s.game, s.board).projection;
    expect(unitNamed([s.squad.id], p)).toBe(s.squad.id);
    expect(unitNamed([s.model.id], p)).toBe(s.squad.id);
    expect(unitNamed([s.squad.id, s.model.id], p)).toBe(s.squad.id);
    expect(unitNamed([], p)).toBeNull();
    expect(unitNamed([s.squad.id, s.rivals.id], p)).toBeNull();
    expect(unitNamed([s.squad.id, newId()], p)).toBeNull();
  });

  const SHAKEN = 'Shaken: must spend this activation being idle';

  it('a unit\'s note shows once after its buttons and an unnoted unit shows none', () => {
    const other = newId();
    const list = rows([option('i', 'Squad: Idle', [s.squad.id], SHAKEN), option('h', 'Other: Hold', [other]), option('a', 'Other: Advance', [other])]);
    expect(list.map((r) => [r.option?.id ?? null, r.note])).toEqual([['i', null], [null, SHAKEN], ['h', null], ['a', null]]);
    const two = rows([option('h', 'Gun: Hold', [s.squad.id], 'Artillery'), option('x', 'Gun: Advance', [s.squad.id], 'Artillery')]);
    expect(two.map((r) => [r.option?.id ?? null, r.note])).toEqual([['h', null], ['x', null], [null, 'Artillery']]);
  });

  it('the selected unit\'s note is read off my open choice', () => {
    const a = ask('activate', { kind: 'options', choices: [option('i', 'Squad: Idle', [s.squad.id], SHAKEN), option('o', 'Other: Hold', [newId()])] });
    const p = projectionOf(s.game, s.board, [{ kind: 'ask_opened', ask: a }]).projection;
    expect(noteOf(p, s.game, TEAM1, none, s.squad.id)).toBe(SHAKEN);
    expect(noteOf(p, s.game, TEAM2, none, s.squad.id)).toBeNull();
    expect(noteOf(p, s.game, TEAM1, none, newId())).toBeNull();
  });
});
