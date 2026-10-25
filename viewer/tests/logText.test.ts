import { describe, expect, it } from 'vitest';
import { answer, describe as describeEvent, roundChange, touched } from '../src/lib/play/logText';
import { Names } from '../src/lib/play/names';
import type { Ask, EventPayload } from '../src/lib/protocol/wire.generated';
import {
  CONTAINER, P1, TEAM1, TEAM2, askOf, at, boardOf, emptyTable, ev, gameOf, markerOf, newId, option, projectionOf, sample,
} from './play';

const names = (payloads: EventPayload[]) => {
  const s = sample();
  const a = projectionOf(s.game, s.board, payloads);
  return { s, a, n: new Names(s.game, a.projection), lines: (n: Names) => a.log.map((e) => describeEvent(e, n)) };
};

describe('LogText', () => {
  it('log lines name actor and subject', () => {
    const s = sample();
    const endRound: Ask = askOf({ kind: 'confirmation' }, { question: 'end_round', prompt: 'End the round', teams: [TEAM1, TEAM2], allowPass: false });
    const move = (from: unknown, to: unknown) => ({ kind: 'models_moved', moves: [{ modelId: s.model.id, from, to }] }) as EventPayload;
    const shelf = { kind: 'shelf' };
    const table = (x: number, y: number) => ({ kind: 'table', placement: at(x, y) });
    const inside = { kind: 'inside', containerId: CONTAINER };
    const a = projectionOf(s.game, s.board, [
      move(table(0, 0), table(3, 4)), { kind: 'score_set', teamId: TEAM1, score: 4 }, { kind: 'turn_ended', teamId: TEAM1, passed: true, changes: [] },
      { kind: 'undone', toSeq: 1 },
      { kind: 'ask_opened', ask: endRound }, { kind: 'ask_answered', askId: endRound.id, teamId: TEAM1, answer: { kind: 'confirmed' }, outsideLegalSet: false },
      { kind: 'round_started', round: 3, order: [TEAM1, TEAM2], changes: [] },
      move(table(12, 12), inside), move(inside, table(14, 12)),
    ]);
    const n = new Names(s.game, a.projection);
    const lines = a.log.map((e) => describeEvent(e, n));
    expect(lines[0]).toBe('Clay moved Battle Brothers 5.0"');
    expect(lines[1]).toBe("Clay set Team 1's score to 4");
    expect(lines[2]).toBe('Clay passed for Team 1');
    expect(lines[3]).toBe('Clay undid back to #1');
    expect(lines[4]).toBe('Clay asked Team 1, Team 2: End the round');
    expect(lines[5]).toBe('Clay confirmed for Team 1');
    expect(lines[6]).toBe('Clay started round 3');
    expect(lines[7]).toBe(`Clay put Battle Brothers inside ${Names.short(CONTAINER)}`);
    expect(lines[8]).toBe(`Clay took Battle Brothers out of ${Names.short(CONTAINER)}`);
    expect(describeEvent(ev(99, move(table(12, 12), inside), P1), n)).toBe(lines[7]);
    expect(describeEvent(ev(9, move(shelf, table(1, 1)), P1), n)).toBe('Clay deployed Battle Brothers');
    expect(describeEvent(ev(9, move(table(1, 1), shelf), P1), n)).toBe('Clay moved Battle Brothers to the shelf');
    expect(touched(a.log[0]!)).toEqual([s.model.id]);
    expect(n.participant(null)).toBe('the game');
  });

  it('an answer names the pieces it carries', () => {
    const s = sample();
    const spender = '0198b7ac-1111-7000-8000-000000000001';
    const spend = askOf({ kind: 'token_spend', maxByModel: { [s.model.id]: 2, [spender]: 1 } }, { question: 'spend_tokens', teams: [TEAM1, TEAM2], sealed: true });
    const takedown = askOf({ kind: 'pieces', eligible: [s.model.id], min: 1, max: 1 }, { question: 'takedown_target' });
    const a = projectionOf(s.game, s.board, [
      { kind: 'ask_opened', ask: spend }, { kind: 'ask_opened', ask: takedown },
      { kind: 'ask_answered', askId: takedown.id, teamId: TEAM1, answer: { kind: 'selected_pieces', pieceIds: [s.model.id] }, outsideLegalSet: false },
      { kind: 'ask_closed', askId: spend.id, answers: [
        { teamId: TEAM1, answer: { kind: 'tokens_spent', counts: { [s.model.id]: 2 } } },
        { teamId: TEAM2, answer: { kind: 'tokens_spent', counts: { [spender]: 1 } } }] },
    ]);
    const n = new Names(s.game, a.projection);
    const lines = a.log.map((e) => describeEvent(e, n));
    expect(lines[2]).toBe('Clay answered for Team 1: picked Battle Brothers (Battle Brother)');
    expect(lines[3]).toBe('Clay closed the question: Team 1 spent 2 tokens (Battle Brothers (Battle Brother) 2), Team 2 spent 1 token (0198b7ac 1)');
    expect(touched(a.log[2]!)).toEqual([s.model.id]);
    expect(touched(a.log[3]!)).toEqual([s.model.id, spender]);
    expect(answer({ kind: 'selected_pieces', pieceIds: [s.model.id, spender] }, n)).toBe('picked 2 pieces');
  });

  it('game ended names the winner or the tie', () => {
    const { n } = names([]);
    const line = (...winners: string[]) => describeEvent(ev(9, { kind: 'game_ended', reason: '4 rounds have been played', winners }), n);
    expect(line(TEAM1)).toBe('Game ended: 4 rounds have been played; Team 1 won');
    expect(line(TEAM1, TEAM2)).toBe('Game ended: 4 rounds have been played; tied between Team 1, Team 2');
    expect(line()).toBe('Game ended: 4 rounds have been played');
  });

  it('an answer that broke a named rule is labelled with it', () => {
    const { n } = names([]);
    const line = (outside: boolean, breach?: string) =>
      describeEvent(ev(9, { kind: 'ask_answered', askId: newId(), teamId: TEAM1, outsideLegalSet: outside, breach }, P1), n);
    expect(line(false, 'difficult_cap').endsWith(' (breaks difficult_cap)')).toBe(true);
    expect(line(true).endsWith(' (outside the legal set)')).toBe(true);
  });

  it('control reads as taking a marker or making it neutral', () => {
    const token = markerOf('smoke');
    const setupMarker = { ...markerOf(undefined, 'objective') };
    const s = sample();
    const table = { ...emptyTable(), markers: [setupMarker] };
    const board = boardOf([{ participantId: P1, units: [] }], table);
    const a = projectionOf(s.game, board, [{ kind: 'marker_placed', marker: token }]);
    const n = new Names(s.game, a.projection);
    const line = (p: EventPayload) => describeEvent(ev(9, p), n);
    expect(line({ kind: 'round_ended', round: 4, changes: [{ kind: 'objective_control_set', markerId: token.id, teamId: TEAM1 }] }))
      .toBe('the game ended round 4: Team 1 took smoke');
    expect(line({ kind: 'round_ended', round: 4, changes: [] })).toBe('the game ended round 4');
    expect(line({ kind: 'objective_control_set', markerId: token.id })).toBe('the game made smoke neutral');
    expect(line({ kind: 'objective_control_set', markerId: setupMarker.id, teamId: TEAM2 })).toBe(`the game gave ${Names.short(setupMarker.id)} to Team 2`);
    expect(roundChange({ kind: 'objective_control_set', markerId: token.id }, n)).toBe('smoke became neutral');

    const labelled = { ...markerOf('centre', 'objective') };
    const b = projectionOf(s.game, boardOf([{ participantId: P1, units: [] }], { ...emptyTable(), markers: [labelled] }));
    expect(describeEvent(ev(9, { kind: 'objective_control_set', markerId: labelled.id, teamId: TEAM2 }), new Names(s.game, b.projection)))
      .toBe('the game gave centre to Team 2');
  });

  it('names fall back to the first eight characters of the id', () => {
    const s = sample();
    const n = new Names(s.game, null);
    const id = '0198b7ac-1234-7000-8000-000000000009';
    expect(n.unit(id)).toBe('0198b7ac');
    expect(n.model(id)).toBe('0198b7ac');
    expect(n.team(id)).toBe('0198b7ac');
    expect(n.participant(id)).toBe('0198b7ac');
    expect(n.piece(id)).toBe('0198b7ac');
    expect(Names.short(id)).toBe('0198b7ac');
    expect(describeEvent(ev(1, { kind: 'wounds_set', modelId: id, wounds: 1 }, P1), n)).toBe('Clay set 0198b7ac to 1 wound');
  });

  it('describes the rest of the kinds in the words Godot uses', () => {
    const s = sample();
    const a = projectionOf(s.game, s.board);
    const n = new Names(s.game, a.projection);
    const line = (p: EventPayload) => describeEvent(ev(9, p, P1), n);
    expect(line({ kind: 'sequence_started', frameId: newId(), sequence: 'attack-sequence', context: { actorUnitId: s.squad.id, targetUnitIds: [], weaponIds: [] } }))
      .toBe('Clay began attack-sequence with Battle Brothers');
    expect(line({ kind: 'step_completed', frameId: newId(), step: 'roll-to-hit', rolls: [], changes: [] })).toBe('Clay completed roll-to-hit');
    expect(line({ kind: 'step_completed', frameId: newId(), step: 'roll-to-hit', changes: [], rolls: [
      { id: newId(), roll: 'hit', modifiers: [], dice: [{ raw: 6, modified: 6, success: true }, { raw: 1, modified: 1, success: false }] }] }))
      .toBe('Clay completed roll-to-hit: 1 of 2 succeeded');
    expect(line({ kind: 'wounds_set', modelId: s.model.id, wounds: 2 })).toBe('Clay set Battle Brothers (Battle Brother) to 2 wounds');
    expect(describeEvent({ ...ev(9, { kind: 'score_set', teamId: TEAM1, score: 1 }, P1), warning: 'careful' }, n)).toBe("Clay set Team 1's score to 1 — careful");
    expect(answer({ kind: 'assigned', targets: {} }, n)).toBe('assigned targets');
    expect(answer({ kind: 'chosen', optionId: 'x' }, n)).toBe('chose x');
    const choose = askOf({ kind: 'options', choices: [option('a', 'A', [s.model.id, s.model.id])] });
    expect(touched(ev(1, { kind: 'ask_opened', ask: choose }))).toEqual([s.model.id]);
    expect(gameOf().teams).toHaveLength(2);
  });
});
