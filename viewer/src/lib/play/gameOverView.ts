// viewer/src/lib/play/gameOverView.ts
// client/src/Core/GameOverView.cs: what the Game over popup shows. Choose and waiting: the game_over Ask's prompt, with its choices while
// my team has not answered. Ended: the result and reason.

import type { Projection } from '../live/apply';
import type { AskOption, GameInfo } from '../protocol/wire.generated';
import { outcome } from './headerView';
import { Names } from './names';

export type GameOverStage = 'none' | 'choose' | 'waiting' | 'ended';

export interface GameOverModel {
  readonly stage: GameOverStage; readonly askId: string | null; readonly result: string | null; readonly reason: string;
  readonly choices: readonly AskOption[]; readonly note: string | null;
}

export const WAITING_NOTE = 'Waiting for the other player';

export function gameOverOf(p: Projection, game: GameInfo, myTeam: string | null): GameOverModel {
  if (p.ended !== undefined) return { stage: 'ended', askId: null, result: outcome(p.ended, new Names(game, p)), reason: p.ended.reason, choices: [], note: null };
  const open = [...p.openAsks.values()].find((a) => a.ask.question === 'game_over');
  if (open === undefined) return { stage: 'none', askId: null, result: null, reason: '', choices: [], note: null };
  const mine = myTeam !== null && open.ask.teams.includes(myTeam) && !open.answers.has(myTeam);
  const legal = open.ask.legal;
  return mine
    ? { stage: 'choose', askId: open.ask.id, result: null, reason: open.ask.prompt, choices: legal.kind === 'options' ? legal.choices : [], note: null }
    : { stage: 'waiting', askId: open.ask.id, result: null, reason: open.ask.prompt, choices: [], note: WAITING_NOTE };
}
