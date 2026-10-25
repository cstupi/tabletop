// viewer/src/lib/play/askView.ts
// client/src/Core/AskViewModel.cs: what the decision box renders. Highlights are the pieces the legal set names.

import type { Projection } from '../live/apply';
import { effectiveLog } from '../live/apply';
import { byId } from '../lobby/seats';
import type { Ask, GameEvent, GameInfo, LegalSet, MoveRegion, TokenSpend } from '../protocol/wire.generated';
import { Names } from './names';

export type AskMode = 'choose' | 'pickPieces' | 'allocate' | 'assign' | 'move' | 'tokenSpend' | 'confirm';

export interface AskView {
  readonly ask: Ask; readonly mode: AskMode; readonly highlights: readonly string[];
  readonly answeredTeams: readonly string[]; readonly autoPass: boolean;
}

export function modeOf(legal: LegalSet): AskMode {
  switch (legal.kind) {
    case 'options': return 'choose';
    case 'pieces': return 'pickPieces';
    case 'allocation': return 'allocate';
    case 'assignment': return 'assign';
    case 'move_region': return 'move';
    case 'token_spend': return 'tokenSpend';
    case 'confirmation': return 'confirm';
    default: throw new RangeError(`Unknown legal set ${(legal as { kind: string }).kind}`);
  }
}

/**
 * The spend map narrowed to one team's own models. p.13's window addresses both teams, so the map names models this player may not
 * spend. A model whose unit or participant this projection cannot resolve is not mine.
 */
export function spendableBy(p: Projection, game: GameInfo, team: string, legal: TokenSpend): Map<string, number> {
  const out = new Map<string, number>();
  for (const [id, max] of Object.entries(legal.maxByModel)) if (teamOfModel(p, game, id) === team) out.set(id, max);
  return out;
}

function teamOfModel(p: Projection, game: GameInfo, modelId: string): string | null {
  const m = p.models.get(modelId);
  const unit = m === undefined ? undefined : p.units.get(m.unitId);
  return unit === undefined ? null : game.participants.find((x) => x.id === unit.participantId)?.teamId ?? null;
}

const unique = (ids: readonly string[]): string[] => [...new Set(ids)];

/**
 * The first open Ask addressed to my team that my team has not answered; any player of the team answers it, for any unit of the team
 * (rulebook p.50 Team Games). Null for a watcher: a participant with no team is never Asked.
 */
export function mine(p: Projection, game: GameInfo, myTeam: string | null, muted: ReadonlySet<string>): AskView | null {
  if (myTeam === null) return null;
  const open = [...p.openAsks.values()].sort((a, b) => byId(a.ask.id, b.ask.id)).find((a) => a.ask.teams.includes(myTeam) && !a.answers.has(myTeam));
  if (open === undefined) return null;
  const ask = open.ask;
  const names = new Names(game, p);
  const l = ask.legal;
  let highlights: readonly string[];
  switch (l.kind) {
    case 'options': highlights = unique(l.choices.flatMap((c) => c.pieces)); break;
    case 'pieces': highlights = l.eligible; break;
    case 'allocation': case 'assignment': highlights = l.targets; break;
    case 'move_region': highlights = moveHighlights(l); break;
    case 'token_spend': highlights = [...spendableBy(p, game, myTeam, l).keys()]; break;   // my own spenders only (p.13)
    default: highlights = [];
  }
  const autoPass = ask.windowKind !== undefined && muted.has(ask.windowKind) && ask.allowPass;
  return { ask, mode: modeOf(ask.legal), highlights, answeredTeams: [...open.answers.keys()].map((t) => names.team(t)), autoPass };
}

/** The pieces a move Ask names: its models, plus the containers containment offers (an enterable one, and the one a unit is exiting). */
function moveHighlights(m: MoveRegion): string[] {
  const ids = [...m.modelIds];
  if (m.containment !== undefined) {
    ids.push(...m.containment.enterable);
    if (m.containment.exitingId !== undefined) ids.push(m.containment.exitingId);
  }
  return unique(ids);
}

/** The waiting indicator: which teams the other open Asks are addressed to. */
export function waitingFor(p: Projection, game: GameInfo, myTeam: string | null): string | null {
  const names = new Names(game, p);
  const teams = unique([...p.openAsks.values()].flatMap((a) => a.ask.teams.filter((t) => !a.answers.has(t) && t !== myTeam))).map((t) => names.team(t));
  return teams.length === 0 ? null : teams.join(', ');
}

/** What the one button of a Confirm Ask says: a step gate says Continue; the round boundary's end_round Ask says what it does. */
export function confirmLabel(ask: Ask): string { return ask.question === 'end_round' ? 'End the round' : 'Continue'; }

/** The teammate who answered this Ask for my team: the live ask_answered event's actor. Null for an answer an undo took back, another team's answer, my own, or a watcher. */
export function answeredBy(log: readonly GameEvent[], askId: string, myTeam: string | null, me: string | null, n: Names): string | null {
  if (myTeam === null) return null;
  const live = effectiveLog(log);
  for (let i = live.length - 1; i >= 0; i--) {
    const e = live[i]!;
    if (e.payload.kind === 'ask_answered' && e.payload.askId === askId && e.payload.teamId === myTeam) return e.actor !== me ? n.participant(e.actor) : null;
  }
  return null;
}
