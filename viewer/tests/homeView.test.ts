import { describe, expect, it } from 'vitest';
import { cardOf, editionOf, homeListsOf } from '../src/lib/games/homeView';
import type { GameSummary } from '../src/lib/live/games';

// The same cases as tests/Tabletop.Client.Tests/HomeViewTests.cs.
const me = 'me', other = 'other';
const now = new Date('2026-11-07T12:00:00Z');
const twoHoursAgo = '2026-11-07T10:00:00Z';

const g = (over: Partial<GameSummary> = {}): GameSummary => ({
  id: crypto.randomUUID(), name: 'g', status: 'lobby', createdBy: me, createdAt: twoHoursAgo, participants: 1, participantNames: ['Clay'],
  joined: true, ruleSet: 'Grimdark Future v3.6.0', teams: 0, teamsReady: 0, ...over,
});
const card = (over: Partial<GameSummary>) => cardOf(g(over), me, now);

describe('homeListsOf', () => {
  it('puts joined games in yours, the rest not ended in open, and folds ended ones until asked', () => {
    const lobby = g(), open = g({ status: 'setup', joined: false }), ended = g({ status: 'ended' }), notMine = g({ status: 'ended', joined: false });
    const lists = homeListsOf([lobby, open, ended, notMine], me, '', false, now);
    expect(lists.yours.map((c) => c.game.id)).toEqual([lobby.id]);
    expect(lists.open.map((c) => c.game.id)).toEqual([open.id]);
    expect(lists.ended).toEqual([]);
    expect(homeListsOf([lobby, open, ended, notMine], me, '', true, now).ended.map((c) => c.game.id)).toEqual([ended.id]);
  });

  it('lists newest first and filters every section by name', () => {
    const older = g({ name: 'Friday night', createdAt: '2026-11-04T12:00:00Z' }), newer = g({ name: 'Ruins run', status: 'playing', createdAt: '2026-11-06T12:00:00Z' });
    expect(homeListsOf([older, newer], me, '', false, now).yours.map((c) => c.game.id)).toEqual([newer.id, older.id]);
    expect(homeListsOf([older, newer], me, 'fri', false, now).yours.map((c) => c.game.id)).toEqual([older.id]);
  });
});

describe('cardOf', () => {
  it('takes the primary from the status, and Join on a game not mine', () => {
    expect(card({}).primary).toBe('open');
    expect(card({ status: 'setup' }).primary).toBe('continue');
    expect(card({ status: 'playing' }).primary).toBe('play');
    expect(card({ status: 'ended' }).primary).toBe('replay');
    expect(card({ joined: false }).primary).toBe('join');
    expect(card({ status: 'playing', joined: false }).primary).toBe('join');
  });

  it('holds replay on a playing game, end and delete for the creator, and leave in the menu', () => {
    expect(card({ status: 'playing' }).menu).toEqual(['replay', 'end', 'delete', 'leave']);
    expect(card({ createdBy: other }).menu).toEqual(['leave']);
    expect(card({ status: 'ended' }).menu).toEqual(['delete']);
    expect(card({ status: 'ended', createdBy: other }).menu).toEqual([]);
    expect(card({ joined: false }).menu).toEqual([]);
  });

  it('shows the edition and three players then a count', () => {
    const c = card({ participantNames: ['Clay', 'Ann', 'Ben', 'Cid', 'Dee'] });
    expect(c.edition).toBe('3.6.0');
    expect(c.players).toEqual(['Clay', 'Ann', 'Ben']);
    expect(c.morePlayers).toBe(2);
    expect(editionOf('Grimdark Future core')).toBe('Grimdark Future core');
    expect(editionOf(undefined)).toBe('');
  });

  it('counts ready teams in the lobby and lays the table in setup', () => {
    expect([card({ teams: 2, teamsReady: 1 }).state, card({ teams: 2, teamsReady: 1 }).stateWarns]).toEqual(['1 of 2 teams ready', false]);
    expect(card({}).state).toBe('2 hours ago');
    expect(card({ status: 'setup' }).state).toBe('Laying the table');
  });

  it('says your move when an Ask waits on me, else the round and the acting team', () => {
    const mine = card({ status: 'playing', progress: { round: 2, actingTeam: 1, waitingOn: [me] } });
    expect([mine.state, mine.stateWarns]).toEqual(['Your move', true]);
    expect(card({ status: 'playing', progress: { round: 3, actingTeam: 2, waitingOn: [other] } }).state).toBe("Round 3 · Team 2's turn");
    expect(card({ status: 'playing', progress: { round: 0, actingTeam: 1, waitingOn: [] } }).state).toBe("Deployment · Team 1's turn");
    expect(card({ status: 'playing', progress: { round: 4, waitingOn: [] } }).state).toBe('Round 4');
    expect(card({ status: 'playing' }).state).toBe('2 hours ago');
  });

  it('names an ended game by its outcome', () => {
    expect(card({ status: 'ended', progress: { round: 4, waitingOn: [], outcome: [1] } }).state).toBe('Team 1 wins');
    expect(card({ status: 'ended', progress: { round: 4, waitingOn: [], outcome: [1, 2] } }).state).toBe('Draw');
    expect(card({ status: 'ended', progress: { round: 4, waitingOn: [], outcome: [] } }).state).toBe('No result');
    expect(card({ status: 'ended' }).state).toBe('2 hours ago');
  });
});
