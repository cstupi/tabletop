import { describe, expect, it } from 'vitest';
import {
  armySeats, endSetupText, members, pickArmy, place, players, seatChoices, seatLine, teamChoices, waitingForCreator,
} from '../src/lib/lobby/lobbyView';
import { isCreator } from '../src/lib/lobby/seats';
import type { GameInfo, Participant, TableState } from '../src/lib/protocol/wire.generated';

const T1 = 't1', T2 = 't2';
const row = (id: string, account: string, name: string, extra: Partial<Participant> = {}): Participant => ({ id, accountId: account, name, ...extra });
const game = (participants: Participant[], status: GameInfo['status'] = 'lobby'): GameInfo => ({
  id: 'g', name: 'G', status, createdBy: 'a1', gameRules: 'r', propertiesHash: 'h', schedulerShape: 'alternating-activation',
  enforcement: 'warn', waitsBetweenSteps: false, teams: [{ id: T1, ordinal: 1 }, { id: T2, ordinal: 2 }], participants, optionalRules: {},
});

describe('LobbyView', () => {
  it('the seat line names my teams and asks for an army', () => {
    const g = game([row('p1', 'a1', 'Ann', { teamId: T1 })]);
    expect(seatLine(g, 'p1')).toBe('You are on Team 1. Pick your army below.');
    const both = game([row('p1', 'a1', 'Ann', { teamId: T1, armyId: 'x' }), row('p2', 'a1', 'Ann', { teamId: T2, armyId: 'y' })]);
    expect(seatLine(both, 'p1')).toBe('You are on Team 1 and Team 2. You play every side you hold.');
  });

  it("a watcher's seat line", () => {
    expect(seatLine(game([row('p1', 'a1', 'Ann')]), '00000000-0000-0000-0000-000000000000')).toBe('You are watching this game.');
    expect(seatLine(game([row('p1', 'a1', 'Ann')]), 'p1')).toMatch(/^You have no team/);
  });

  it('an empty team is joined, my team is left, a held team asks the creator', () => {
    const g = game([row('p1', 'a1', 'Ann', { teamId: T1 }), row('p2', 'a2', 'Bo', { teamId: T2 })]);
    const [one, two] = teamChoices(g, 'p1', false);
    expect(one).toMatchObject({ text: 'Leave team 1', enabled: true, edit: { kind: 'leave_team', teamId: T1 } });
    expect(two).toMatchObject({ text: 'Team 2: Bo · ask the creator to add you', enabled: false });
    const free = teamChoices(game([row('p2', 'a2', 'Bo')]), 'p2', false);
    expect(free[0]).toMatchObject({ text: 'Join team 1', enabled: true, edit: { kind: 'pick_team', teamId: T1 } });
  });

  it('the creator may join a held team', () => {
    const g = game([row('p1', 'a1', 'Ann'), row('p2', 'a2', 'Bo', { teamId: T2 })]);
    expect(teamChoices(g, 'p1', true)[1]).toMatchObject({ text: 'Join team 2', enabled: true, why: 'Held by Bo; as the creator you may join it.' });
    expect(isCreator(g, 'p1')).toBe(true);
    expect(isCreator(g, 'p2')).toBe(false);
  });

  it('one army picker per team I hold, labelled per team when several', () => {
    const one = game([row('p1', 'a1', 'Ann', { teamId: T1, armyId: 'x' })]);
    expect(armySeats(one, 'p1')).toEqual([{ teamId: T1, label: 'Your army', armyId: 'x' }]);
    const two = game([row('p1', 'a1', 'Ann', { teamId: T1 }), row('p2', 'a1', 'Ann', { teamId: T2 })]);
    expect(armySeats(two, 'p1').map((s) => s.label)).toEqual(['Your army for Team 1', 'Your army for Team 2']);
    expect(armySeats(game([row('p1', 'a1', 'Ann')]), 'p1')).toEqual([{ teamId: null, label: 'Your army', armyId: null }]);
    expect(pickArmy({ teamId: T1, label: '', armyId: null }, 'z')).toEqual({ kind: 'pick_army', armyId: 'z', teamId: T1 });
    expect(pickArmy({ teamId: null, label: '', armyId: 'x' }, null)).toEqual({ kind: 'pick_army' });
  });

  it('players list teamless rows with No team', () => {
    const g = game([row('p1', 'a1', 'Ann'), row('p2', 'a2', 'Bo', { teamId: T2, armyName: 'Iron' })]);
    expect(players(g)).toEqual([
      { participantId: 'p1', name: 'Ann', seat: 'No team', selected: 0, army: null },
      { participantId: 'p2', name: 'Bo', seat: 'Team 2', selected: 2, army: 'Iron' },
    ]);
    expect(seatChoices(g).map((c) => c.label)).toEqual(['No team', 'Team 1', 'Team 2']);
  });

  it('members name each army or no army yet', () => {
    const g = game([row('p1', 'a1', 'Ann', { teamId: T1, armyName: 'Iron' }), row('p2', 'a2', 'Bo', { teamId: T1 })]);
    expect(members(g, T1)).toBe('Ann ⚔ Iron\nBo · no army yet');
    expect(members(g, T2)).toBe('No players yet');
  });

  it('place builds PickTeam with the participant', () => {
    const g = game([row('p1', 'a1', 'Ann')]);
    expect(place(g, 'p1', 2)).toEqual({ kind: 'pick_team', teamId: T2, participantId: 'p1' });
    expect(place(g, 'p1', 0)).toEqual({ kind: 'pick_team', participantId: 'p1' });
  });

  it('waiting lines by status, none for the creator', () => {
    expect(waitingForCreator(game([]), false)).toBe('Waiting for the creator to start setup.');
    expect(waitingForCreator(game([], 'setup'), false)).toBe('Anyone may edit the table. Waiting for the creator to end setup.');
    expect(waitingForCreator(game([], 'playing'), false)).toBeNull();
    expect(waitingForCreator(game([]), true)).toBeNull();
  });

  it('endSetupText counts pieces whose centre is off the slab', () => {
    const piece = (x: number, y: number) => ({ id: `${x}`, position: { x, y, z: 0 } });
    const draft = { size: { width: 72, length: 48 }, terrain: [piece(76, 4), piece(72, 48), piece(10, 10)] } as unknown as TableState;
    expect(endSetupText(draft)).toBe('End setup (1 piece off the table will be removed)');
    const three = { ...draft, terrain: [piece(76, 4), piece(-1, 4), piece(10, 49)] } as unknown as TableState;
    expect(endSetupText(three)).toBe('End setup (3 pieces off the table will be removed)');
    expect(endSetupText({ ...draft, terrain: [piece(10, 10)] } as unknown as TableState)).toBe('End setup');
    expect(endSetupText(null)).toBe('End setup');
  });
});
