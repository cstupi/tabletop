import { describe, expect, it } from 'vitest';
import { barOf, doing, groupsOf, headOf, keyCap, quietOf, unitPips, word } from '../src/lib/play/decisionCard';
import { decisionOf } from '../src/lib/play/decisionView';
import { read } from '../src/lib/play/moveRuler';
import { Names } from '../src/lib/play/names';
import type { AskOption, MoveRegion } from '../src/lib/protocol/wire.generated';
import { FRAME, TEAM1, TEAM2, askOf, option, projectionOf, sample, unitOf } from './play';

const s = sample();
const region: MoveRegion = { kind: 'move_region', modelIds: [s.model.id], allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false, ignoresTerrain: false };
const at = (d: number) => read({ x: 0, y: 0 }, { x: d, y: 0 }, 12);

describe('DecisionCard', () => {
  it('a unit past twelve wounds reads left over max, so its pips leave the name room', () => {
    const p = projectionOf(s.game, s.board).projection;
    expect(unitPips(p, s.squad.id, new Map([[s.model.id, 3]]))).toBe('●●●');
    expect(unitPips(p, s.squad.id, new Map([[s.model.id, 20]]))).toBe('20/20');
  });

  it('activation options group by unit with the word after the colon and keys continuing', () => {
    const scouts = unitOf('Scouts');
    const board = { ...s.board, armies: [{ ...s.board.armies[0]!, snapshot: { ...s.board.armies[0]!.snapshot, units: [s.squad, scouts] } }, s.board.armies[1]!] };
    const choices: AskOption[] = [
      option('h1', 'Battle Brothers: Hold', [s.squad.id]), option('a1', 'Battle Brothers: Advance', [s.squad.id]),
      option('h2', 'Scouts: Hold', [scouts.id], 'Shaken: may only Hold'), option('x', 'Scouts: Dance', [scouts.id], 'Shaken: may only Hold'),
    ];
    const ask = askOf({ kind: 'options', choices }, { question: 'activate' });
    const p = projectionOf(s.game, board, [{ kind: 'ask_opened', ask }]).projection;
    const n = new Names(s.game, p);
    const numbered = decisionOf(p, s.game, n, TEAM1).numbered;
    const groups = groupsOf(ask, choices, numbered, p, s.game, n, new Map([[s.model.id, 3]]));
    expect(groups.length).toBe(2);
    expect([groups[0]!.unit, groups[0]!.teamOrdinal, groups[0]!.pips, groups[0]!.note]).toEqual(['Battle Brothers', 1, '●●●', null]);
    expect(groups[0]!.tiles.map((t) => [t.key, t.word, t.icon])).toEqual([[1, 'Hold', 'hold'], [2, 'Advance', 'advance']]);
    expect([groups[1]!.unit, groups[1]!.note]).toEqual(['Scouts', 'Shaken: may only Hold']);
    expect(groups[1]!.tiles.map((t) => [t.key, t.word, t.icon])).toEqual([[3, 'Hold', 'hold'], [4, 'Dance', 'activation']]);
  });

  it('other options are one group of whole labels', () => {
    const choices = [option('strike', 'Strike back: now', [s.squad.id]), option('pass', "Don't")];
    const ask = askOf({ kind: 'options', choices }, { question: 'strike_back', prompt: 'Strike back?' });
    const p = projectionOf(s.game, s.board, [{ kind: 'ask_opened', ask }]).projection;
    const g = groupsOf(ask, choices, [{ key: 1, label: '', optionId: 'strike' }, { key: 2, label: '', optionId: 'pass' }], p, s.game, new Names(s.game, p), new Map());
    expect(g.length).toBe(1);
    expect(g[0]!.unit).toBeNull();
    expect(g[0]!.tiles.map((t) => [t.word, t.icon])).toEqual([['Strike back: now', null], ["Don't", null]]);
  });

  it('the head names the ask\'s icon and a short title', () => {
    const p = projectionOf(s.game, s.board).projection;
    const n = new Names(s.game, p);
    const head = (question: string, prompt: string, legal = { kind: 'options', choices: [] } as never) => headOf(askOf(legal, { question, prompt }), p, n);
    expect(head('activate', 'Activate a unit')).toEqual({ icon: 'activation', title: 'Activate a unit', subject: null, prompt: null });
    expect(head('deploy', 'Deploy one unit').icon).toBe('army');
    expect(head('charge_target', 'Charge which unit?').icon).toBe('charge');
    const long = 'Spend tokens for or against the casting roll of the spell';
    expect(head('spend_tokens', long)).toEqual({ icon: 'cast', title: 'Spend tokens', subject: null, prompt: long });
    expect(head('game_over', '4 rounds')).toEqual({ icon: 'ready', title: 'End or continue?', subject: null, prompt: '4 rounds' });
  });

  it('a move\'s head is the activation\'s action and its unit', () => {
    const p = projectionOf(s.game, s.board, [
      { kind: 'round_started', round: 1, order: [TEAM1, TEAM2], changes: [] },
      { kind: 'sequence_started', frameId: FRAME, sequence: 'advance', context: { actorUnitId: s.squad.id, targetUnitIds: [], weaponIds: [], action: 'Advance' } },
    ]).projection;
    const n = new Names(s.game, p);
    const move = askOf(region, { question: 'move' });
    expect(headOf(move, p, n)).toEqual({ icon: 'advance', title: 'Advance', subject: 'Battle Brothers', prompt: null });
    expect(headOf(askOf({ kind: 'assignment', items: [], targets: [], maxDistinctTargets: 2 }, { question: 'declare_targets' }), p, n).title).toBe('Shoot');
    const bare = projectionOf(s.game, s.board).projection;
    expect(headOf(move, bare, new Names(s.game, bare))).toEqual({ icon: 'advance', title: 'Move', subject: 'Battle Brothers', prompt: null });
  });

  it('the bar reads the farthest model over the allowance', () => {
    const bar = barOf([at(3), at(7.5)], 12, null);
    expect([bar.text, bar.fraction, bar.over]).toEqual(['7.5" of 12"', 0.625, false]);
    const over = barOf([at(13)], 12, 9);
    expect([over.over, over.fraction, over.minimumFraction]).toEqual([true, 1, 0.75]);
    expect(barOf([], 6, null).text).toBe('0" of 6"');
  });

  it('an ask not mine is a quiet band per waiting team', () => {
    const theirs = askOf({ kind: 'allocation', targets: [], amount: 1 }, { question: 'remove_casualties', teams: [TEAM2], id: '00000000-0000-0000-0000-000000000001' });
    const mine = askOf({ kind: 'options', choices: [] }, { question: 'activate', id: '00000000-0000-0000-0000-000000000002' });
    const p = projectionOf(s.game, s.board, [{ kind: 'ask_opened', ask: theirs }, { kind: 'ask_opened', ask: mine }]).projection;
    expect(quietOf(p, s.game, new Names(s.game, p), [TEAM1])).toEqual([
      { teamId: TEAM1, ordinal: 1, team: 'Team 1', doing: 'activating', mine: true },
      { teamId: TEAM2, ordinal: 2, team: 'Team 2', doing: 'removing casualties', mine: false },
    ]);
    expect(doing('charge_target')).toBe('charge target');
  });

  it('key caps and words', () => {
    expect([keyCap(3), keyCap(0)]).toEqual(['3', '']);
    expect([word('Red unit 1: Hold'), word('Red unit 1')]).toEqual(['Hold', 'Red unit 1']);
  });
});
