// viewer/src/lib/game/send.ts
// One send for every panel: the edit goes through GameConnection.sendEdit and what went wrong comes back as the line the panel's
// alert shows (Godot's Guarded.Send: a refusal's Feedback text, or the send's own failure).

import { refusalText } from '../connection/feedback';
import type { GameConnection } from '../connection/gameConnection';
import type { Edit } from '../protocol/wire.generated';

/** Null when the server took the edit; else the text to show. */
export async function sendEdit(connection: GameConnection, edit: Edit): Promise<string | null> {
  try {
    const outcome = await connection.sendEdit(edit);
    return outcome.refused === null ? null : refusalText(outcome.refused) ?? 'Refused.';
  } catch (e) {
    return (e as Error).message;
  }
}
