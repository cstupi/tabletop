import { describe, expect, it } from 'vitest';
import { notActivatedText, outlineText, scoreText, waitingLine } from '../src/lib/play/headerText';
import { REPLAYING, WATCHING } from '../src/lib/play/headerView';
import { Names } from '../src/lib/play/names';
import { ASK_ID, TEAM1, askOf, projectionOf, sample } from './play';

const s = sample();
const open = projectionOf(s.game, s.board, [
  { kind: 'ask_opened', ask: askOf({ kind: 'options', choices: [] }, { id: ASK_ID, question: 'activate' }) },
]).projection;
const n = new Names(s.game, open);
const wait = [{ teamId: TEAM1, ordinal: 1, hasPlayer: false, text: 'Waiting for a player on Team 1' }];

describe('headerText', () => {
  it('the wait for players comes first, then the replay and watcher lines, then the open Asks', () => {
    expect(waitingLine(open, n, wait, true, [TEAM1])).toBe('Deployment waits: Waiting for a player on Team 1');
    expect(waitingLine(open, n, [], true, [TEAM1], true)).toBe(REPLAYING);
    expect(waitingLine(open, n, [], true, [TEAM1])).toBe(WATCHING);
    expect(waitingLine(open, n, [], false, [TEAM1])).toBe('Your move: activate');
    expect(waitingLine(open, n, [], false, [])).toBe('Team 1: activate');
  });

  it('a done step is ticked', () => {
    expect(outlineText({ label: 'Move', state: 'done' })).toBe('  ✓ Move');
    expect(outlineText({ label: 'Shoot', state: 'current' })).toBe('  Shoot');
    expect([scoreText('Team 1', 3), notActivatedText(2)]).toEqual(['  Team 1 3', ' · 2 not activated']);
  });
});
