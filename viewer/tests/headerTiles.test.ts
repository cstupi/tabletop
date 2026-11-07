import { describe, expect, it } from 'vitest';
import { actionIcon, roundText, stepText, tileCount, tileDots, tiles, waitLineOf, type OutlineItem, type TeamTile } from '../src/lib/play/headerView';
import { Names } from '../src/lib/play/names';
import type { EventPayload } from '../src/lib/protocol/wire.generated';
import { P1, TEAM1, TEAM2, askOf, at, boardOf, projectionOf, sample } from './play';

const s = sample();
const board = boardOf([{ participantId: P1, units: [s.squad] }, { participantId: s.game.participants[1]!.id, units: [s.rivals] }]);
const deploy: EventPayload = { kind: 'models_moved', moves: [{ modelId: s.model.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } }] };
const after = (...p: EventPayload[]) => projectionOf(s.game, board, [deploy, ...p]).projection;

describe('HeaderView tiles (HeaderView.Tiles)', () => {
  it('a tile per team with its score, units in play, acted, the turn and mine', () => {
    const p = after({ kind: 'action_recorded', unitId: s.squad.id, action: 'Advance' }, { kind: 'score_set', teamId: TEAM2, score: 3 }, { kind: 'turn_started', teamId: TEAM2 });
    const t = tiles(p, s.game, new Names(s.game, p), [TEAM2]);
    expect(t.map((x) => [x.ordinal, x.name, x.score, x.acted, x.inPlay, x.acting, x.mine])).toEqual([
      [1, 'Team 1', 0, 1, 1, false, false], [2, 'Team 2', 3, 0, 0, true, true]]);
  });

  it('past twelve units a tile draws its count', () => {
    const t: TeamTile = { teamId: TEAM1, ordinal: 1, name: 'Team 1', score: 0, acted: 4, inPlay: 13, acting: false, mine: false };
    expect([tileDots(t), tileCount(t)]).toEqual([false, '4/13']);
  });

  it('the action word names its icon and the round its line', () => {
    expect(['Hold', 'Advance', 'rush', 'Charge', 'Shoot', null].map(actionIcon)).toEqual(['hold', 'advance', 'rush', 'charge', 'activation', 'activation']);
    expect([roundText(2, null), roundText(0, 'deployment'), roundText(0, null)]).toEqual(['Round 2', 'Deployment', 'Before round 1']);
  });

  it('a tight header reads the round as its number and a compact step strip only the mark but for the current step', () => {
    expect([roundText(4, null, 'tight'), roundText(4, null, 'compact'), roundText(0, 'deployment', 'tight')]).toEqual(['4', 'Round 4', 'Deployment']);
    const items: OutlineItem[] = [{ label: 'Pick target', state: 'done' }, { label: 'Target moves', state: 'current' }, { label: 'Melee', state: 'next' }];
    expect(items.map((i) => stepText(i, 'full'))).toEqual(['✓ Pick target', '● Target moves', '○ Melee']);
    expect(items.map((i) => stepText(i, 'compact'))).toEqual(['✓', '● Target moves', '○']);
  });

  it('the waiting line is my move in my colour, else the waiting team with what it does', () => {
    const ask = (teams: string[]) => askOf({ kind: 'confirmation' }, { id: '0198b7ac-5555-7000-8000-000000000005', question: 'remove_casualties', teams, allowPass: false });
    const both = after({ kind: 'ask_opened', ask: ask([TEAM1, TEAM2]) });
    const n = new Names(s.game, both);
    expect(waitLineOf(both, s.game, n, [TEAM2])).toEqual({ mine: true, ordinal: 2, text: 'Your move' });
    expect(waitLineOf(both, s.game, n, [TEAM1, TEAM2])).toEqual({ mine: true, ordinal: 1, text: 'Your move · Team 1' });
    const theirs = after({ kind: 'ask_opened', ask: ask([TEAM2]) });
    expect(waitLineOf(theirs, s.game, n, [TEAM1])).toEqual({ mine: false, ordinal: 2, text: 'Team 2 · remove casualties' });
    expect(waitLineOf(after(), s.game, n, [TEAM1])).toBeNull();
  });
});
