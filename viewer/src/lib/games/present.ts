// viewer/src/lib/games/present.ts
// How the games list words a game: its status, the one main action a row offers, and when it was made.

import type { GameSummary } from '../live/games';

export const STATUS_LABEL: Record<GameSummary['status'], string> = { lobby: 'Lobby', setup: 'Setup', playing: 'Playing', ended: 'Ended' };

/** The row's main action: back into the game where it stands, or its replay once it has ended. */
export function mainActionOf(game: GameSummary): { readonly label: string; readonly replay: boolean } {
  switch (game.status) {
    case 'lobby': return { label: 'Open lobby', replay: false };
    case 'setup': return { label: 'Continue setup', replay: false };
    case 'playing': return { label: 'Continue', replay: false };
    default: return { label: 'Replay', replay: true };
  }
}

/** "just now", "5 minutes ago", "3 hours ago", "yesterday", "4 days ago", then the date. */
export function ago(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const minutes = Math.round((now.getTime() - then.getTime()) / 60_000);
  if (Number.isNaN(minutes)) return '';
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`;
  const days = Math.round(hours / 24);
  if (days === 1) return 'yesterday';
  if (days < 14) return `${days} days ago`;
  return then.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

/** "Ann, Ben and 2 more"; "No players yet". */
export function playersLine(names: readonly string[]): string {
  if (names.length === 0) return 'No players yet';
  if (names.length <= 3) return names.length === 1 ? names[0]! : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
  return `${names.slice(0, 2).join(', ')} and ${names.length - 2} more`;
}
