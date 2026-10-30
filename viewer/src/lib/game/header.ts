// viewer/src/lib/game/header.ts
// The game page's header strip: the game's name and status, the connection when it is not connected, Leave, and the one primary
// action in setup (the creator's End setup) or what everyone else waits for.

import type { ConnectionState } from '../connection/gameConnection';
import type { Edit, GameStatus } from '../protocol/wire.generated';
import { ruleSetText } from '../play/headerText';
import { STATUS_LABEL } from '../games/present';
import { endSetupText } from '../lobby/lobbyView';
import { waitingFor } from '../lobby/lobbyPage';
import { endSetup } from '../lobby/lobbyEdits';

export const WATCHER_ID = '00000000-0000-0000-0000-000000000000';

export interface HeaderStrip {
  readonly title: string;
  readonly status: GameStatus | null;
  readonly statusLabel: string;
  /** The rule set's name beside the title; null before the welcome. */
  readonly ruleSet: string | null;
  /** "Connecting…", "Reconnecting…", "Disconnected"; null while connected. */
  readonly connection: string | null;
  readonly primary: { readonly label: string; readonly edit: Edit } | null;
  readonly waiting: string | null;
  /** A participant may leave in the lobby and setup; a watcher has no seat to leave. */
  readonly canLeave: boolean;
}

const CONNECTION: Record<ConnectionState['phase'], string | null> = {
  connecting: 'Connecting…', connected: null, reconnecting: 'Reconnecting…', closed: 'Disconnected',
};

/** The blocking wait before a game draws: no welcome yet, a reconnect, or welcomed with the board or log not yet applied. */
export function waitText(snap: ConnectionState | null): string {
  if (snap === null) return 'Connecting…';
  if (snap.phase === 'reconnecting') return 'Reconnecting…';
  return snap.phase === 'connected' && snap.game !== null ? 'Loading game…' : 'Connecting…';
}

export function headerOf(snap: ConnectionState | null, creator: boolean): HeaderStrip {
  const game = snap?.game ?? null;
  const status = game?.status ?? null;
  const watcher = snap === null || snap.participantId === null || snap.participantId === WATCHER_ID;
  const early = status === 'lobby' || status === 'setup';
  // The lobby's Start setup sits at the foot of its Game panel; setup's End setup is the strip's.
  const primary = creator && status === 'setup' ? { label: endSetupText(snap!.draft), edit: endSetup() as Edit } : null;
  return {
    title: game?.name ?? 'Game',
    status,
    statusLabel: status === null ? '' : STATUS_LABEL[status],
    ruleSet: game === null ? null : ruleSetText(game),
    connection: snap === null ? CONNECTION.connecting : CONNECTION[snap.phase],
    primary,
    waiting: game === null || watcher || status !== 'setup' ? null : waitingFor(game, creator),
    canLeave: early && !watcher,
  };
}
