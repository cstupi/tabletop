import { describe, expect, it } from 'vitest';
import type { ConnectionState } from '../src/lib/connection/gameConnection';
import { Applier } from '../src/lib/live/apply';
import {
  WAITING_TITLE, isWatching, linesChanged, waitHolds, waitingLines, waitingTeams,
} from '../src/lib/play/waiting';
import { waitingForPlayers } from '../src/lib/play/headerView';
import type { GameInfo } from '../src/lib/protocol/wire.generated';
import { P1, P2, TEAM1, TEAM2, boardOf, ev, gameOf, newId, sample, unitOf } from './play';

const EMPTY = '00000000-0000-0000-0000-000000000000';
const SECOND = newId();

describe('WaitingForPlayers and WaitingView', () => {
  it('while deployment waits everyone reads why and a participant may join the empty team', () => {
    const s = sample();
    const game = gameOf({}, [{ id: P1, accountId: newId(), name: 'Clay', teamId: TEAM1, armyId: newId() }]);
    const waiting = waitingTeams(game, s.board);
    const lines = waitingLines(game, waiting, P1);
    expect(lines).toHaveLength(1);
    expect(lines[0]!.text).toBe('Waiting for a player on Team 2');
    expect(lines[0]!.join!.edit).toEqual({ kind: 'pick_team', teamId: TEAM2 });
    expect(lines[0]!.army).toBeNull();
    expect(waitingLines(game, waiting, EMPTY)[0]!.join).toBeNull();
    expect(waitingForPlayers(waiting)).toBe('Deployment waits: Waiting for a player on Team 2');

    const account = game.participants[0]!.accountId;
    const mine: GameInfo = { ...game, participants: [...game.participants, { id: SECOND, accountId: account, name: 'Clay', teamId: TEAM2 }] };
    const army = waitingLines(mine, waitingTeams(mine, s.board), P1)[0]!.army!;
    expect(army.teamId).toBe(TEAM2);
    expect(army.label).toBe('Army for Team 2');
  });

  it('only a team with no army is offered the army picker', () => {
    const cid = newId();
    const s = sample();
    const base = gameOf();
    const game = { ...base, participants: [...base.participants, { id: cid, accountId: newId(), name: 'Cid', teamId: TEAM1 }] };
    const bobs = { participantId: P2, units: [unitOf('Bob')] };
    const armed = boardOf([{ participantId: P1, units: s.board.armies[0]!.snapshot.units as never }, bobs]);
    expect(waitingLines(game, waitingTeams(game, armed), cid)).toEqual([]);
    const unarmed = boardOf([bobs]);
    const lines = waitingLines(game, waitingTeams(game, unarmed), cid);
    expect(lines).toHaveLength(1);
    expect(lines[0]!.text).toBe('Team 1 has no army');
    expect(lines[0]!.army).not.toBeNull();
  });

  it('a team with a seat and an army is no wait', () => {
    const s = sample();
    expect(waitingTeams(s.game, s.board)).toEqual([]);
  });

  it('the waiting panel rebuilds only when its lines change', () => {
    const s = sample();
    const game = gameOf();
    const empty = boardOf([]);
    const shown = waitingLines(game, waitingTeams(game, empty), P1);
    expect(shown.map((l) => l.text)).toEqual(['Team 1 has no army', 'Team 2 has no army']);
    expect(linesChanged(null, shown)).toBe(true);
    expect(linesChanged(shown, waitingLines(game, waitingTeams(game, empty), P1))).toBe(false);
    const picked = { ...game, participants: [{ ...game.participants[0]!, armyId: newId() }, game.participants[1]!] };
    expect(linesChanged(shown, waitingLines(picked, waitingTeams(picked, empty), P1))).toBe(false);
    const board = boardOf([{ participantId: P2, units: [unitOf('Bob')] }]);
    const now = waitingLines(picked, waitingTeams(picked, board), P1);
    expect(now.map((l) => l.text)).toEqual(['Team 1 has no army']);
    expect(linesChanged(shown, now)).toBe(true);
    expect(WAITING_TITLE).toBe('Deployment starts once every team has a player and an army.');
    expect(s.game.teams).toHaveLength(2);
  });

  it('a Join line becomes an army line once the board holds my seat, and an unchanged frame keeps the lines', () => {
    const empty = boardOf([]);
    const account = newId();
    const alone = gameOf({}, [{ id: P1, accountId: account, name: 'Clay', teamId: TEAM1 }]);
    const before = waitingLines(alone, waitingTeams(alone, empty), P1);
    expect(before.map((l) => [l.text, l.join !== null, l.army !== null])).toEqual([['Team 1 has no army', false, true], ['Waiting for a player on Team 2', true, false]]);
    const again: GameInfo = { ...alone, participants: alone.participants.map((x) => ({ ...x })) };       // a new GameInfo object, the same lines
    expect(linesChanged(before, waitingLines(again, waitingTeams(again, empty), P1))).toBe(false);
    const joined: GameInfo = { ...alone, participants: [...alone.participants, { id: SECOND, accountId: account, name: 'Clay', teamId: TEAM2 }] };
    const after = waitingLines(joined, waitingTeams(joined, empty), P1);
    expect(after.map((l) => [l.text, l.join !== null, l.army !== null])).toEqual([['Team 1 has no army', false, true], ['Team 2 has no army', false, true]]);
    expect(linesChanged(before, after)).toBe(true);
  });

  it('the wait holds only on an empty log', () => {
    const game = gameOf();
    const empty = boardOf([]);
    expect(waitHolds(game, empty, 0)).toBe(true);
    expect(waitHolds(game, empty, 3)).toBe(false);
    expect(waitHolds(game, null, 0)).toBe(false);
    expect(waitHolds({ ...game, status: 'setup' }, empty, 0)).toBe(false);
    expect(waitHolds(game, sample().board, 0)).toBe(false);
  });

  it('an admin watcher and a teamless participant watch, but not while the wait holds', () => {
    const s = sample();
    const state = (game: GameInfo, participantId: string, board = s.board, seq = 0): ConnectionState => {
      const ap = new Applier(board, game);
      if (seq > 0) ap.apply(ev(seq, { kind: 'score_set', teamId: TEAM1, score: 1 }));
      return { phase: 'connected', game, participantId, board, projection: ap.projection, log: ap.log } as unknown as ConnectionState;
    };
    expect(isWatching(state(s.game, EMPTY))).toBe(true);
    const teamless = gameOf({}, [{ id: P1, accountId: newId(), name: 'Clay' }, { id: P2, accountId: newId(), name: 'Bo', teamId: TEAM2 }]);
    expect(isWatching(state(teamless, P1, s.board, 1))).toBe(true);
    expect(isWatching(state(teamless, P1))).toBe(false);          // team 1 has no player: the wait holds
    expect(isWatching(state(s.game, P1))).toBe(false);
    expect(isWatching(state(teamless, P1, boardOf([]), 0))).toBe(false);
    expect(isWatching({ phase: 'connecting', game: null } as unknown as ConnectionState)).toBe(false);
    expect(isWatching(state({ ...teamless, status: 'setup' }, P1))).toBe(false);
  });
});
