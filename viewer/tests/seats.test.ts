import { describe, expect, it } from 'vitest';
import { acting, playingAs, teamsOf } from '../src/lib/lobby/seats';
import { waiting as headerWaiting } from '../src/lib/play/headerView';
import { Names } from '../src/lib/play/names';
import type { EventPayload, GameInfo } from '../src/lib/protocol/wire.generated';
import { P1, TEAM1, TEAM2, askOf, ev, gameOf, newId, option, projectionOf, sample } from './play';
import { Applier } from '../src/lib/live/apply';

const ACCOUNT = newId();
const SECOND = newId();
const bothSides = (): GameInfo => gameOf({}, [
  { id: P1, accountId: ACCOUNT, name: 'Clay', teamId: TEAM1 }, { id: SECOND, accountId: ACCOUNT, name: 'Clay', teamId: TEAM2 },
]);

function withAsk(game: GameInfo, ...teams: Array<[string, boolean]>) {
  const s = sample();
  const ask = askOf({ kind: 'confirmation' }, { question: 'confirm', prompt: 'Continue', teams: teams.map((t) => t[0]), allowPass: false, sealed: true });
  const payloads: EventPayload[] = [{ kind: 'ask_opened', ask }];
  for (const [team, answered] of teams) if (answered) payloads.push({ kind: 'ask_answered', askId: ask.id, teamId: team, answer: { kind: 'confirmed' }, outsideLegalSet: false });
  return projectionOf(game, s.board, payloads).projection;
}

describe('Seats', () => {
  it('an account on both teams holds both and acts for the team an ask waits on', () => {
    const game = bothSides();
    expect(teamsOf(game, SECOND)).toEqual([TEAM1, TEAM2]);
    const both = [TEAM1, TEAM2];
    expect(acting(withAsk(game, [TEAM1, false], [TEAM2, false]), both)).toBe(TEAM1);
    expect(acting(withAsk(game, [TEAM1, true], [TEAM2, false]), both)).toBe(TEAM2);
    expect(acting(withAsk(game, [TEAM2, false]), both)).toBe(TEAM2);
    expect(playingAs(game, both, TEAM2)).toBe('Playing as Team 2');
    expect(playingAs(game, [TEAM1], TEAM1)).toBeNull();
    expect(acting(null, [])).toBeNull();
  });

  it('between turns an account on both teams stays with the team that last had the turn', () => {
    const s = sample();
    const a = projectionOf(bothSides(), s.board, [{ kind: 'turn_started', teamId: TEAM2 }, { kind: 'turn_ended', teamId: TEAM2, passed: false, changes: [] }]);
    expect(acting(a.projection, [TEAM1, TEAM2], a.log)).toBe(TEAM2);
    expect(acting(a.projection, [TEAM1, TEAM2])).toBe(TEAM1);
  });

  it('at a round boundary an account on both teams acts for the team that just answered', () => {
    const s = sample();
    const ask = askOf({ kind: 'pieces', eligible: [s.squad.id], min: 1, max: 1 }, { question: 'deploy_ambusher', teams: [TEAM2] });
    const a = projectionOf(bothSides(), s.board, [
      { kind: 'turn_started', teamId: TEAM1 }, { kind: 'turn_ended', teamId: TEAM1, passed: false, changes: [] }, { kind: 'ask_opened', ask },
      { kind: 'ask_closed', askId: ask.id, answers: [{ teamId: TEAM2, answer: { kind: 'selected_pieces', pieceIds: [s.squad.id] } }] },
    ]);
    expect(acting(a.projection, [TEAM1, TEAM2], a.log)).toBe(TEAM2);
  });

  it('the active team is acting when it is mine, else my first', () => {
    const s = sample();
    const game = bothSides();
    const a = projectionOf(game, s.board, [{ kind: 'turn_started', teamId: TEAM2 }]);
    expect(acting(a.projection, [TEAM1, TEAM2])).toBe(TEAM2);
    const b = projectionOf(game, s.board, [{ kind: 'turn_started', teamId: newId() }]);
    expect(acting(b.projection, [TEAM1, TEAM2])).toBe(TEAM1);
  });

  it('the header names each of my teams whose move it is', () => {
    const game = bothSides();
    const p = withAsk(game, [TEAM1, false], [TEAM2, false]);
    expect(headerWaiting(p, new Names(game, p), [TEAM1, TEAM2])).toBe('Your move (Team 1): confirm · Your move (Team 2): confirm');
  });

  it('asks are ordered by id as strings', () => {
    const s = sample();
    const first = askOf({ kind: 'options', choices: [option('a', 'A')] }, { id: '0198b7ac-0001-7000-8000-000000000001', teams: [TEAM2] });
    const second = askOf({ kind: 'options', choices: [option('a', 'A')] }, { id: '0198b7ac-0002-7000-8000-000000000001', teams: [TEAM1] });
    const game = bothSides();
    const ap = new Applier(s.board, game);
    ap.apply(ev(1, { kind: 'ask_opened', ask: second }));
    ap.apply(ev(2, { kind: 'ask_opened', ask: first }));
    expect(acting(ap.projection, [TEAM1, TEAM2])).toBe(TEAM2);
  });
});
