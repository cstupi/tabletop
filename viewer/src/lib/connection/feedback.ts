// viewer/src/lib/connection/feedback.ts
// Feedback.Refusal, Closed and Rejected (client/src/Core/Feedback.cs), word for word: what the player reads when the server says no
// or the connection changes. Every message keeps the server's own detail.

import type { RejectReason, Refused } from '../protocol/wire.generated';

const EMPTY_ID = '00000000-0000-0000-0000-000000000000';

/** "Kind: detail." keeping the detail's own stop; "Kind." without one. */
function withDetail(kind: string, detail: string | null | undefined): string {
  const text = detail?.trim() ?? '';
  if (text === '') return `${kind}.`;
  return /[.!?]$/.test(text) ? `${kind}: ${text}` : `${kind}: ${text}.`;
}

/** The toast for a refusal. Null for a watcher's refused presence or ping (message id empty). */
export function refusalText(r: Refused): string | null {
  if (r.reason === 'watcher') return r.messageId === EMPTY_ID ? null : 'You are watching this game.';
  const kind = {
    outside_legal_set: 'Not one of the choices offered',
    illegal: 'Against the rules',
    wrong_status: 'Not at this point of the game',
    not_permitted: 'Not allowed',
    unknown: 'Refused',
    watcher: 'Refused',
  }[r.reason] ?? 'Refused';
  return withDetail(kind, r.detail);
}

/**
 * The line for a closed socket or a resync, by close code (the game server's codes: 1000 a replaced socket, 1001 shutting down,
 * 1008 the JWT's expiry, 1009 a frame too large, 1011 a server failure, 1013 a client too far behind). `code` is null when the
 * browser gave none (an abnormal close); `error` says the socket reported an error, so the server was not reached. A close with no
 * code reads "Reconnecting…": its reason goes to the console, not the player.
 */
export function closedText(code: number | null, reason: string | null, willReconnect: boolean, desync = false, error = false): string {
  if (desync) {
    return willReconnect
      ? 'Rebuilding the table from the log…'
      : `This game's log cannot be replayed: ${reason ?? ''}`;
  }
  const why = reason === null || reason.trim() === '' ? null : reason;
  const has = (word: string) => why?.toLowerCase().includes(word) === true;
  let what: string;
  if (code === 1000 && has('too many')) what = 'Your account has too many game windows open; the oldest closed';
  else if (code === 1000 && has('replaced')) what = 'This game was opened in another window, which took over';
  else if (code === 1001) what = 'The game server is restarting';
  else if (code === 1008) what = willReconnect ? 'Your sign-in on the game server expired' : 'Your sign-in on the game server expired; sign in again';
  else if (code === 1009) what = 'A message was larger than the server accepts';
  else if (code === 1011) what = why === null ? 'The game server failed' : `The game server failed (${why})`;
  else if (code === 1013) what = 'The connection fell too far behind the game';
  else if (code === null && willReconnect) return 'Reconnecting…';
  else if (code === null && error) what = 'Cannot reach the game server';
  else if (code === null) what = 'The connection dropped';
  else what = why === null ? `Disconnected (code ${code})` : `Disconnected: ${why}`;
  if (!willReconnect) return `${what}.`;
  return code === 1008 ? `${what}; reconnecting with a fresh one…` : `${what}; reconnecting…`;
}

/** The line for a connect the server rejected. */
export function rejectedText(reason: RejectReason): string {
  switch (reason) {
    case 'upgrade_required': return 'This client is older than the game server accepts.';
    case 'invalid_session': return 'The game server did not accept your sign-in. Sign out and in again if it happens again.';
    case 'not_a_participant': return 'You are not in that game: join it from the list.';
    default: return String(reason);
  }
}
