import { describe, expect, it } from 'vitest';
import { columnsOf, creatorName, moveTo, moves, stillToDo } from '../src/lib/lobby/teamColumns';
import type { GameInfo, Participant } from '../src/lib/protocol/wire.generated';

// TeamColumnsTests.cs, the same game: Ann (the creator) and Ben on Team 1, Ann with an army; Cid on Team 2 with one; Team 3 empty; Dee teamless.
const T1 = 't1', T2 = 't2', T3 = 't3';
const row = (id: string, account: string, name: string, extra: Partial<Participant> = {}): Participant => ({ id, accountId: account, name, ...extra });
const base: Participant[] = [
  row('ann', 'a-ann', 'Ann', { teamId: T1, armyId: 'x', armyName: 'Iron Legion' }), row('ben', 'a-ben', 'Ben', { teamId: T1 }),
  row('cid', 'a-cid', 'Cid', { teamId: T2, armyId: 'y', armyName: 'Orc Marauders' }), row('dee', 'a-dee', 'Dee'),
];
const game = (participants: Participant[] = base, status: GameInfo['status'] = 'lobby', teams = [{ id: T2, ordinal: 2 }, { id: T1, ordinal: 1 }, { id: T3, ordinal: 3 }]): GameInfo => ({
  id: 'g', name: 'G', status, createdBy: 'a-ann', gameRules: 'grimdark-future-3.6', propertiesHash: 'h', schedulerShape: 'alternating-activation',
  enforcement: 'warn', waitsBetweenSteps: false, teams, participants, optionalRules: {},
});

describe('the team columns', () => {
  it('are the teams by ordinal then Watching, each with its state and members', () => {
    const columns = columnsOf(game(), 'ann', true);
    expect(columns.map((c) => c.title)).toEqual(['Team 1', 'Team 2', 'Team 3', 'Watching']);
    expect(columns.map((c) => c.stateText)).toEqual(['Ready', 'Ready', 'Needs a player', 'Watches once play starts']);
    expect(columns.map((c) => c.members.map((m) => m.name))).toEqual([['Ann', 'Ben'], ['Cid'], [], ['Dee']]);
    expect(columns[0]!.members[0]).toMatchObject({ army: 'Iron Legion · 3.6', you: true, myArmy: { teamId: T1, label: 'Your army', armyId: 'x' } });
    expect(columns[0]!.members[1]).toMatchObject({ army: null, noArmyWarns: false, myArmy: null });
  });

  it('a team whose players have no army needs one, and its rows warn', () => {
    const g = game(base.map((p) => (p.id === 'ann' ? row('ann', 'a-ann', 'Ann', { teamId: T1 }) : p)));
    const team1 = columnsOf(g, 'ann', true)[0]!;
    expect(team1.stateText).toBe('Needs an army');
    expect(team1.members.every((m) => m.noArmyWarns)).toBe(true);
  });

  // The owner, 2026-11-07: "you removed my single player ability in the client where a user could be on two teams". Join adds a seat.
  it('holding a team, Join adds a seat and never moves mine', () => {
    const columns = columnsOf(game(), 'cid', false);
    expect(columns[2]!.join).toEqual({ text: 'Join', edit: { kind: 'pick_team', teamId: T3 }, why: null });
    expect(columns[1]!.join).toBeNull();
    expect(columnsOf(game(), 'cid', false, true)[2]!.join!.edit).toEqual({ kind: 'pick_team', teamId: T3 });
  });

  it('one account joins a second team and picks an army on each row', () => {
    const teams = [{ id: T1, ordinal: 1 }, { id: T2, ordinal: 2 }];
    const solo = [row('ann', 'a-ann', 'Ann', { teamId: T1 })];
    expect(columnsOf(game(solo, 'lobby', teams), 'ann', true)[1]!.join).toEqual({ text: 'Join', edit: { kind: 'pick_team', teamId: T2 }, why: null });
    const columns = columnsOf(game([...solo, row('ann2', 'a-ann', 'Ann', { teamId: T2 })], 'lobby', teams), 'ann', true);
    expect(columns[0]!.members[0]!.myArmy).toEqual({ teamId: T1, label: 'Your army for Team 1', armyId: null });
    expect(columns[1]!.members[0]!.myArmy).toEqual({ teamId: T2, label: 'Your army for Team 2', armyId: null });
    expect([columns[0]!.join, columns[1]!.join]).toEqual([null, null]);
  });

  it('my row menu moves the seat, and leaves a team while I hold several', () => {
    expect(moves(game(), 'cid', false, 'cid').map((m) => m.text)).toEqual(['Move to Team 3', 'Watch']);
    const g = game([...base.filter((p) => p.id !== 'cid'), row('ann2', 'a-ann', 'Ann', { teamId: T2 })]);
    const mine = moves(g, 'ann', true, 'ann2');
    expect(mine.map((m) => m.text)).toEqual(['Move to Team 3', 'Leave Team 2']);
    expect(mine[0]!.edit).toEqual({ kind: 'pick_team', teamId: T3, participantId: 'ann2' });
    expect(mine[1]!.edit).toEqual({ kind: 'leave_team', teamId: T2 });
    expect(moveTo(g, 'ann', true, 'ann', null)).toEqual({ kind: 'leave_team', teamId: T1 });
    expect(moves(g, 'ann', true, 'ben').map((m) => m.text)).toEqual(['Move to Team 2', 'Move to Team 3', 'Watch']);
  });

  it('a team another account holds is joined only by the creator', () => {
    const team1 = columnsOf(game(), 'cid', false)[0]!;
    expect(team1.join).toEqual({ text: 'Join', edit: null, why: 'Ann, Ben hold this team: the creator adds players to it.' });
    expect(columnsOf(game(), 'ann', true)[1]!.join!.edit).toEqual({ kind: 'pick_team', teamId: T2 });
  });

  it('without a team Join takes it; Watching offers Watch to a player holding a team', () => {
    const columns = columnsOf(game(), 'dee', false);
    expect(columns[2]!.join).toEqual({ text: 'Join', edit: { kind: 'pick_team', teamId: T3 }, why: null });
    expect(columns[3]!.join).toBeNull();
    expect(columnsOf(game(), 'cid', false)[3]!.join).toEqual({ text: 'Watch', edit: { kind: 'pick_team' }, why: null });
  });

  it('the creator moves any row, a player only their own, never where the server refuses', () => {
    expect(moves(game(), 'ann', true, 'ben').map((m) => m.text)).toEqual(['Move to Team 2', 'Move to Team 3', 'Watch']);
    expect(moves(game(), 'ann', true, 'ben')[2]!.edit).toEqual({ kind: 'pick_team', participantId: 'ben' });
    expect(moves(game(), 'cid', false, 'ben')).toEqual([]);
    expect(moves(game(), 'ben', false, 'ben').map((m) => m.text)).toEqual(['Move to Team 3', 'Watch']);
    expect(moveTo(game(), 'ben', false, 'ben', T3)).toEqual({ kind: 'pick_team', teamId: T3, participantId: 'ben' });
    expect(moveTo(game(), 'ben', false, 'ben', T2)).toBeNull();
  });

  it('compact columns leave Watching out while no one watches', () => {
    const g = game(base.filter((p) => p.id !== 'dee'));
    expect(columnsOf(g, 'ann', true, true).some((c) => c.teamId === null)).toBe(false);
    expect(columnsOf(g, 'ann', true).some((c) => c.teamId === null)).toBe(true);
  });

  it('Still to do names each team that waits, then the phase step', () => {
    const g = game(base.map((p) => (p.id === 'cid' ? row('cid', 'a-cid', 'Cid', { teamId: T2 }) : p)));
    expect(stillToDo(g, true).map((l) => l.text)).toEqual(['Team 3 needs a player', 'Team 2 needs an army', 'You start setup']);
    expect(stillToDo(g, false).map((l) => l.text)).toEqual(['Team 3 needs a player', 'Team 2 needs an army', 'Ann starts setup']);
    expect(stillToDo({ ...g, status: 'setup' }, false).map((l) => l.text)).toEqual(['Team 3 needs a player', 'Team 2 needs an army', 'Anyone may edit the table', 'Ann ends setup']);
    expect(stillToDo(game(base, 'lobby', []), true).map((l) => l.text)).toEqual(['Add a team for each side', 'You start setup']);
    expect(stillToDo(g, true).map((l) => l.missing)).toEqual([true, true, false]);
    expect(stillToDo({ ...g, status: 'playing' }, true)).toEqual([]);
    expect(creatorName(game(base.filter((p) => p.id !== 'ann')))).toBe('the creator');
  });
});
