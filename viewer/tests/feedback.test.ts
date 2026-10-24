import { describe, expect, it } from 'vitest';
import { closedText, refusalText, rejectedText } from '../src/lib/connection/feedback';
import type { Refused, RefusalReason } from '../src/lib/protocol/wire.generated';

const refused = (reason: RefusalReason, detail: string, messageId = 'm1'): Refused => ({ kind: 'refused', messageId, reason, detail });

describe('refusalText', () => {
  it('names the kind and keeps the server detail and its stop', () => {
    expect(refusalText(refused('outside_legal_set', 'no such option'))).toBe('Not one of the choices offered: no such option.');
    expect(refusalText(refused('illegal', 'Too far!'))).toBe('Against the rules: Too far!');
    expect(refusalText(refused('wrong_status', 'setup is over'))).toBe('Not at this point of the game: setup is over.');
    expect(refusalText(refused('not_permitted', ''))).toBe('Not allowed.');
    expect(refusalText(refused('unknown', 'boom'))).toBe('Refused: boom.');
  });
  it('a watcher is told so, except for its id-less presence', () => {
    expect(refusalText(refused('watcher', 'x'))).toBe('You are watching this game: a watcher cannot change it.');
    expect(refusalText(refused('watcher', 'x', '00000000-0000-0000-0000-000000000000'))).toBeNull();
  });
});

describe('closedText', () => {
  it('reads the close code', () => {
    expect(closedText(1000, 'too many windows', false)).toBe('Your account has too many game windows open; the oldest closed.');
    expect(closedText(1000, 'replaced', false)).toBe('This game was opened in another window, which took over.');
    expect(closedText(1000, null, false)).toBe('Disconnected (code 1000).');
    expect(closedText(1001, null, true)).toBe('The game server is restarting; reconnecting…');
    expect(closedText(1009, null, false)).toBe('A message was larger than the server accepts.');
    expect(closedText(1011, null, false)).toBe('The game server failed.');
    expect(closedText(1011, 'oops', false)).toBe('The game server failed (oops).');
    expect(closedText(1013, null, true)).toBe('The connection fell too far behind the game; reconnecting…');
  });
  it('a 1008 differs with and without a reconnect', () => {
    expect(closedText(1008, null, true)).toBe('Your sign-in on the game server expired; reconnecting with a fresh one…');
    expect(closedText(1008, null, false)).toBe('Your sign-in on the game server expired; sign in again.');
  });
  it('no code: unreachable, dropped, other', () => {
    expect(closedText(null, null, true, false, true)).toBe('Cannot reach the game server; reconnecting…');
    expect(closedText(null, 'refused', true, false, true)).toBe('Cannot reach the game server (refused); reconnecting…');
    expect(closedText(null, null, true)).toBe('The connection dropped; reconnecting…');
    expect(closedText(4000, 'bye', false)).toBe('Disconnected: bye.');
    expect(closedText(4000, null, false)).toBe('Disconnected (code 4000).');
  });
  it('a desync rebuilds, or gives up with the reason', () => {
    expect(closedText(null, 'x', true, true)).toBe('The table fell out of step with the server; rebuilding it from the log…');
    expect(closedText(null, 'unit u9 missing', false, true)).toBe("This game's log cannot be replayed: unit u9 missing");
  });
});

describe('rejectedText', () => {
  it('has a line per rejection', () => {
    expect(rejectedText('upgrade_required')).toBe('This client is older than the game server accepts.');
    expect(rejectedText('invalid_session')).toBe('The game server did not accept your sign-in. Sign out and in again if it happens again.');
    expect(rejectedText('not_a_participant')).toBe('You are not in that game: join it from the list.');
  });
});
