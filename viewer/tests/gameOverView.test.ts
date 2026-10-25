import { describe, expect, it } from 'vitest';
import { WAITING_NOTE, gameOverOf } from '../src/lib/play/gameOverView';
import type { EventPayload } from '../src/lib/protocol/wire.generated';
import { TEAM1, TEAM2, askOf, option, projectionOf, sample } from './play';

const s = sample();
const asked = askOf({ kind: 'options', choices: [option('end', 'End'), option('continue', 'Continue')] },
  { question: 'game_over', prompt: '4 rounds have been played', teams: [TEAM1, TEAM2], allowPass: false, sealed: true });
const after = (...p: EventPayload[]) => projectionOf(s.game, s.board, p).projection;

describe('GameOverView', () => {
  it('nothing shows while the game runs and no game over ask is open', () => {
    expect(gameOverOf(after({ kind: 'round_started', round: 4, order: [], changes: [] }), s.game, TEAM1).stage).toBe('none');
    expect(gameOverOf(after({ kind: 'ask_opened', ask: askOf({ kind: 'options', choices: [] }) }), s.game, TEAM1).stage).toBe('none');
  });

  it('the game over ask offers its choices until my team answers then waits', () => {
    const m = gameOverOf(after({ kind: 'ask_opened', ask: asked }), s.game, TEAM1);
    expect([m.stage, m.askId, m.reason, m.note]).toEqual(['choose', asked.id, '4 rounds have been played', null]);
    expect(m.choices.map((c) => c.label)).toEqual(['End', 'Continue']);
    const answered = gameOverOf(after({ kind: 'ask_opened', ask: asked },
      { kind: 'ask_answered', askId: asked.id, teamId: TEAM1, answer: { kind: 'chosen', optionId: 'continue' }, outsideLegalSet: false }), s.game, TEAM1);
    expect([answered.stage, answered.note]).toEqual(['waiting', WAITING_NOTE]);
    expect(answered.choices).toEqual([]);
    expect(gameOverOf(after({ kind: 'ask_opened', ask: asked }), s.game, null).stage).toBe('waiting');
  });

  it('once ended it shows the result and reason', () => {
    const m = gameOverOf(after({ kind: 'ask_opened', ask: asked }, { kind: 'ask_closed', askId: asked.id, answers: [] },
      { kind: 'game_ended', reason: '4 rounds have been played. 2 to 1', winners: [TEAM1] }), s.game, TEAM1);
    expect([m.stage, m.askId, m.reason, m.result, m.choices]).toEqual(['ended', null, '4 rounds have been played. 2 to 1', 'Team 1 wins', []]);
  });
});
