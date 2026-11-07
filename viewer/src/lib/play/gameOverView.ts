// viewer/src/lib/play/gameOverView.ts
// client/src/Core/GameOverView.cs: what the Game over popup shows. Choose and waiting: the game_over Ask's prompt, with its choices while
// my team has not answered. Ended: the result and reason.

import type { Projection } from '../live/apply';
import type { AnswerAsk, AskOption, GameInfo } from '../protocol/wire.generated';
import { outcome } from './headerView';
import { Names } from './names';

export type GameOverStage = 'none' | 'choose' | 'waiting' | 'ended';

export interface GameOverModel {
  readonly stage: GameOverStage; readonly askId: string | null; readonly result: string | null; readonly reason: string;
  readonly choices: readonly AskOption[]; readonly note: string | null;
  /** The rounds played. */
  readonly rounds: number;
  /** The teams yet to answer while the Ask is open, each drawn with its band. */
  readonly waitingFor: readonly string[];
}

export const WAITING_NOTE = 'Waiting for the other player';
/** GameOverView.WaitingText: `Waiting for Team 2`, the teams named; WAITING_NOTE when none is named. */
export const waitingText = (teams: readonly string[], n: Names): string => (teams.length === 0 ? WAITING_NOTE : `Waiting for ${teams.map((t) => n.team(t)).join(', ')}`);
/** GameOverModel.Headline: `Team 1 wins · 4 rounds`; the result alone before a round is played; null before the end. */
export const headline = (m: GameOverModel): string | null =>
  m.result === null ? null : m.rounds > 0 ? `${m.result} · ${m.rounds} round${m.rounds === 1 ? '' : 's'}` : m.result;

/** GameOverView.Answer: the chosen option for the team the popup answers as; teamId omitted when none. */
export const answerOf = (model: GameOverModel, optionId: string, team: string | null): AnswerAsk =>
  ({ kind: 'answer_ask', askId: model.askId!, answer: { kind: 'chosen', optionId }, ...(team === null ? {} : { teamId: team }) });

export function gameOverOf(p: Projection, game: GameInfo, myTeam: string | null): GameOverModel {
  const rounds = p.scheduler.round;
  if (p.ended !== undefined) return { stage: 'ended', askId: null, result: outcome(p.ended, new Names(game, p)), reason: p.ended.reason, choices: [], note: null, rounds, waitingFor: [] };
  const open = [...p.openAsks.values()].find((a) => a.ask.question === 'game_over');
  if (open === undefined) return { stage: 'none', askId: null, result: null, reason: '', choices: [], note: null, rounds, waitingFor: [] };
  const mine = myTeam !== null && open.ask.teams.includes(myTeam) && !open.answers.has(myTeam);
  const legal = open.ask.legal;
  const waitingFor = open.ask.teams.filter((t) => !open.answers.has(t));
  return mine
    ? { stage: 'choose', askId: open.ask.id, result: null, reason: open.ask.prompt, choices: legal.kind === 'options' ? legal.choices : [], note: null, rounds, waitingFor }
    : { stage: 'waiting', askId: open.ask.id, result: null, reason: open.ask.prompt, choices: [], note: waitingText(waitingFor, new Names(game, p)), rounds, waitingFor };
}
