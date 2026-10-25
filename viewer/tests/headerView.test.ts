import { describe, expect, it } from 'vitest';
import {
  REPLAYING, WATCHING, actionFrame, activationLine, headerOf, notActivated, objectives, waiting, waitingForPlayers,
} from '../src/lib/play/headerView';
import { Names } from '../src/lib/play/names';
import type { EventPayload, OutlineStep } from '../src/lib/protocol/wire.generated';
import { FRAME, TEAM1, TEAM2, askOf, at, boardOf, emptyTable, gameOf, markerOf, newId, projectionOf, sample, slotOf, unitOf, P1 } from './play';

const NESTED = '0198b7ac-4444-7000-8000-000000000004';
const OUTLINE: OutlineStep[] = [{ step: 'move', label: 'Move' }, { step: 'shoot', label: 'Shoot' }, { step: 'end', label: 'End' }];

const s = sample();
const after = (...p: EventPayload[]) => projectionOf(s.game, s.board, p).projection;
const of = (p: ReturnType<typeof after>) => headerOf(p, s.game, new Names(s.game, p));
const round = (r: number, order = [TEAM1, TEAM2]): EventPayload => ({ kind: 'round_started', round: r, order, changes: [] });
const started = (frameId: string, action: string | undefined, outline?: OutlineStep[]): EventPayload => ({
  kind: 'sequence_started', frameId, sequence: 'advance', context: { actorUnitId: s.squad.id, targetUnitIds: [], weaponIds: [], action }, ...(outline ? { outline } : {}),
});
const step = (frameId: string, name: string): EventPayload => ({ kind: 'step_completed', frameId, step: name, rolls: [], changes: [] });

describe('HeaderView', () => {
  it('round, turn and every team\'s score', () => {
    const h = of(after(round(2), { kind: 'turn_started', teamId: TEAM1 }, { kind: 'score_set', teamId: TEAM1, score: 3 }));
    expect([h.round, h.turn]).toEqual([2, "Team 1's turn"]);
    expect(h.scores).toEqual([['Team 1', 3], ['Team 2', 0]]);
    expect([h.activeUnit, h.action, h.outline, h.waiting, h.result]).toEqual([null, null, [], null, null]);
  });

  it('before the first round the phase leads the turn', () => {
    expect(of(after(round(0), { kind: 'phase_started', phase: 'deployment' }, { kind: 'turn_started', teamId: TEAM2 })).turn).toBe("deployment · Team 2's turn");
    expect(of(after(round(0, [TEAM1]), { kind: 'phase_started', phase: 'deployment' })).turn).toBe('deployment');
  });

  it('the outline ticks completed steps and a nested frame lights the step it runs under', () => {
    const p = after(round(1), { ...started(FRAME, 'Advance', OUTLINE) }, step(FRAME, 'move'), {
      kind: 'sequence_started', frameId: NESTED, sequence: 'shooting', context: { actorUnitId: s.squad.id, targetUnitIds: [], weaponIds: [] } });
    const h = of(p);
    expect([h.activeUnit, h.action]).toEqual(['Battle Brothers', 'Advance']);
    expect(h.outline.map((o) => [o.label, o.state])).toEqual([['Move', 'done'], ['Shoot', 'current'], ['End', 'next']]);
    expect(actionFrame(p)).toBe(p.stack[0]);
  });

  it('a step outside the outline moves nothing and the last step done leaves no current', () => {
    const internal = of(after(started(FRAME, 'Advance', OUTLINE), step(FRAME, 'bookkeeping')));
    expect(internal.outline.map((o) => o.state)).toEqual(['current', 'next', 'next']);
    const allDone = of(after(started(FRAME, 'Advance', OUTLINE), step(FRAME, 'end')));
    expect(allDone.outline.every((o) => o.state === 'done')).toBe(true);
  });

  it('no outline gives no strip', () => {
    const h = of(after(started(FRAME, 'Advance'), step(FRAME, 'move')));
    expect([h.activeUnit, h.action]).toEqual(['Battle Brothers', 'Advance']);
    expect(h.outline).toEqual([]);
  });

  it('a frame with no action is not the action frame', () => {
    const h = of(after({ kind: 'sequence_started', frameId: FRAME, sequence: 'roll-off', context: { targetUnitIds: [], weaponIds: [] } }));
    expect([h.activeUnit, h.action, h.outline]).toEqual([null, null, []]);
  });

  it('the waiting line names each team not yet answered and what for', () => {
    const casualties = askOf({ kind: 'allocation', targets: [s.model.id], amount: 2 }, { id: '0198b7ac-5555-7000-8000-000000000005', question: 'remove_casualties', teams: [TEAM2], allowPass: false });
    const endRound = askOf({ kind: 'confirmation' }, { id: '0198b7ac-6666-7000-8000-000000000006', question: 'end_round', teams: [TEAM1, TEAM2], allowPass: false });
    expect(of(after({ kind: 'ask_opened', ask: casualties })).waiting).toBe('Team 2: remove casualties');
    const both = after({ kind: 'ask_opened', ask: casualties }, { kind: 'ask_opened', ask: endRound },
      { kind: 'ask_answered', askId: endRound.id, teamId: TEAM1, answer: { kind: 'confirmed' }, outsideLegalSet: false });
    expect(of(both).waiting).toBe('Team 2: remove casualties · Team 2: end round');
  });

  it('the waiting line says you for my team and puts me first', () => {
    const casualties = askOf({ kind: 'allocation', targets: [s.model.id], amount: 2 }, { id: '0198b7ac-5555-7000-8000-000000000005', question: 'remove_casualties', teams: [TEAM1], allowPass: false });
    const endRound = askOf({ kind: 'confirmation' }, { id: '0198b7ac-4444-7000-8000-000000000004', question: 'end_round', teams: [TEAM2], allowPass: false });
    const p = after({ kind: 'ask_opened', ask: endRound }, { kind: 'ask_opened', ask: casualties });
    const n = new Names(s.game, p);
    expect(waiting(p, n, [TEAM1])).toBe('Your move: remove casualties · Team 2: end round');
    expect(waiting(p, n)).toBe('Team 2: end round · Team 1: remove casualties');
  });

  it('a watcher is told they watch and a replay that it changes nothing', () => {
    expect(WATCHING).toBe('Watching: you have no team in this game, so you see everything and change nothing.');
    expect(REPLAYING).toBe('Replay: the table as it stood at this point in the game. Nothing here changes the game.');
  });

  it('game over carries the result', () => {
    expect(of(after({ kind: 'game_ended', reason: '4 rounds have been played', winners: [TEAM1] })).result).toBe('Game over · Team 1 wins');
    expect(of(after({ kind: 'game_ended', reason: 'x', winners: [TEAM1, TEAM2] })).result).toBe('Game over · Draw');
    expect(of(after({ kind: 'game_ended', reason: 'abandoned', winners: [] })).result).toBe('Game over · No result');
  });

  it('the first line drops the separator when there is no turn', () => {
    expect(of(after(round(2), { kind: 'turn_started', teamId: TEAM1 })).topLine).toBe("Round 2 · Team 1's turn");
    expect(of(after(round(4))).topLine).toBe('Round 4');
  });

  it('not activated counts units on the table with no action and objectives carry their controller', () => {
    const objective = { ...markerOf('A', 'objective') };
    const board = boardOf([{ participantId: P1, units: [s.squad] }, { participantId: s.game.participants[1]!.id, units: [s.rivals] }], { ...emptyTable(), markers: [objective] });
    const deploy: EventPayload = { kind: 'models_moved', moves: [{ modelId: s.model.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } }] };
    const p = projectionOf(s.game, board, [deploy, { kind: 'action_recorded', unitId: s.squad.id, action: 'Advance' }]).projection;
    const counts = notActivated(p, s.game);
    expect([counts.get(TEAM1), counts.get(TEAM2)]).toEqual([0, 0]);
    expect(notActivated(projectionOf(s.game, board, [deploy]).projection, s.game).get(TEAM1)).toBe(1);
    expect(objectives(p)).toEqual([{ markerId: objective.id, teamId: null }]);
    const taken = projectionOf(s.game, board, [{ kind: 'objective_control_set', markerId: objective.id, teamId: TEAM2 }]).projection;
    expect(objectives(taken)).toEqual([{ markerId: objective.id, teamId: TEAM2 }]);
  });

  it('not activated counts a unit inside a transport on the table, not one inside a shelved transport', () => {
    const passengers = unitOf('Passengers', [slotOf({ name: 'Passenger' })]);
    const board = boardOf([{ participantId: P1, units: [s.squad, passengers] }, { participantId: s.game.participants[1]!.id, units: [s.rivals] }]);
    const aboard: EventPayload = { kind: 'models_moved', moves: [{ modelId: passengers.models[0]!.id, from: { kind: 'shelf' }, to: { kind: 'inside', containerId: s.model.id } }] };
    const deploy: EventPayload = { kind: 'models_moved', moves: [{ modelId: s.model.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } }] };
    expect(notActivated(projectionOf(s.game, board, [aboard, deploy]).projection, s.game).get(TEAM1)).toBe(2);
    expect(notActivated(projectionOf(s.game, board, [aboard]).projection, s.game).get(TEAM1)).toBe(0);
  });

  it('another team\'s activation is named on the action line', () => {
    const p = after({ kind: 'sequence_started', frameId: newId(), sequence: 'charge', context: { actorUnitId: s.rivals.id, targetUnitIds: [], weaponIds: [], action: 'Charge' } });
    const n = new Names(s.game, p);
    expect(activationLine(p, s.game, n, TEAM1)).toBe('Team 2 activating: Rivals · Charge');
    expect(activationLine(p, s.game, n, TEAM2)).toBe('Rivals · Charge');
    expect(activationLine(after(), s.game, n, TEAM1)).toBeNull();
    expect(gameOf().teams).toHaveLength(2);
  });

  it('deployment waits lines one clause per team', () => {
    expect(waitingForPlayers([{ teamId: TEAM2, ordinal: 2, hasPlayer: false, text: 'Waiting for a player on Team 2' }])).toBe('Deployment waits: Waiting for a player on Team 2');
    expect(waitingForPlayers([])).toBeNull();
  });
});
