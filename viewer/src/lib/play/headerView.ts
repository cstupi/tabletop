// viewer/src/lib/play/headerView.ts
// client/src/Core/HeaderView.cs: the header's three lines (round, turn and scores; the active unit, its action and the step strip; the
// waiting line). The bar itself is D4. `result` is set once the log has ended the game.

import type { Frame, Projection } from '../live/apply';
import { byId } from '../lobby/seats';
import type { GameEnded, GameInfo, Location } from '../protocol/wire.generated';
import { Names } from './names';
import type { TeamWaiting } from './waiting';

export type OutlineState = 'done' | 'current' | 'next';
export interface OutlineItem { readonly label: string; readonly state: OutlineState }

export interface HeaderModel {
  readonly round: number; readonly turn: string; readonly scores: readonly (readonly [string, number])[];
  readonly activeUnit: string | null; readonly action: string | null; readonly outline: readonly OutlineItem[];
  readonly waiting: string | null; readonly result: string | null;
  /** The first line: "Round 4 · Team 1's turn", or "Round 4" when no team has the turn. */
  readonly topLine: string;
}

export function headerOf(p: Projection, game: GameInfo, n: Names): HeaderModel {
  const s = p.scheduler;
  const turn = [s.round === 0 ? s.phase : undefined, s.activeTeamId !== undefined ? `${n.team(s.activeTeamId)}'s turn` : undefined]
    .filter((x): x is string => x !== undefined).join(' · ');
  const scores = [...game.teams].sort((a, b) => a.ordinal - b.ordinal).map((t): readonly [string, number] => [n.team(t.id), p.scores.get(t.id) ?? 0]);
  const frame = actionFrame(p);
  const unit = frame?.context.actorUnitId;
  return {
    round: s.round, turn, scores, activeUnit: unit !== undefined ? n.unit(unit) : null, action: frame?.context.action ?? null,
    outline: frame === null ? [] : outline(frame), waiting: waiting(p, n), result: p.ended === undefined ? null : `Game over · ${outcome(p.ended, n)}`,
    topLine: turn.length === 0 ? `Round ${s.round}` : `Round ${s.round} · ${turn}`,
  };
}

/** The activation's top frame: the outermost one whose context names an action. Nested frames run under it. */
export function actionFrame(p: Projection): Frame | null { return p.stack.find((f) => f.context.action !== undefined) ?? null; }

/** Per team: units in play (a model on the table, or inside a container on the table: p.15 "units may enter/exit by using any move
 * action") with no action recorded this round. Who may activate is the scheduler's, not this count's. */
export function notActivated(p: Projection, game: GameInfo): Map<string, number> {
  const out = new Map<string, number>();
  for (const t of game.teams)
    out.set(t.id, [...p.units.values()].filter((u) => u.actions.size === 0
      && game.participants.some((x) => x.id === u.participantId && x.teamId === t.id)
      && [...p.models.values()].some((m) => m.unitId === u.id && inPlay(p, m.location))).length);
  return out;
}

function inPlay(p: Projection, at: Location): boolean {
  return at.kind === 'table' || (at.kind === 'inside' && p.models.get(at.containerId)?.location.kind === 'table');
}

/** Objective markers in setup's order, with their controller (Projection.control); null is neutral. */
export function objectives(p: Projection): { markerId: string; teamId: string | null }[] {
  return p.board.table.markers.map((m) => ({ markerId: m.id, teamId: p.control.get(m.id) ?? null }));
}

/** "Orcs · Charge"; led by "Team 2 activating: " when the actor is not my team's (a watcher has none). */
export function activationLine(p: Projection, game: GameInfo, n: Names, myTeam: string | null, fit: HeaderFit = 'full'): string | null {
  const c = actionFrame(p)?.context;
  const unit = c?.actorUnitId;
  if (c === undefined || unit === undefined) return null;
  const line = c.action !== undefined ? `${n.unit(unit)} · ${c.action}` : n.unit(unit);
  const u = p.units.get(unit);
  const team = u === undefined ? undefined : game.participants.find((x) => x.id === u.participantId)?.teamId;
  return fit === 'full' && team !== undefined && team !== myTeam ? `${n.team(team)} activating: ${line}` : line;
}

/**
 * Every outline step up to the last one the frame has completed is done, the next is current, the rest are next. A frame pushed by a
 * step completes nothing on this one, so the step it runs under stays current. No outline, no strip.
 */
export function outline(frame: Frame): OutlineItem[] {
  const completed = new Set(frame.steps.map((x) => x.step));
  let last = -1;
  frame.outline.forEach((o, i) => { if (completed.has(o.step)) last = i; });
  return frame.outline.map((o, i): OutlineItem => ({ label: o.label, state: i <= last ? 'done' : i === last + 1 ? 'current' : 'next' }));
}

/**
 * Who the game waits on and for what: one entry per team that has not answered an open Ask, in Ask order. With `mine`, my teams' entries
 * come first and read "Your move" ("Your move (Team 2)" when I hold several): any player of a team answers its Asks (rulebook p.50).
 */
export function waiting(p: Projection, n: Names, mine: readonly string[] = []): string | null {
  const who = (t: string, myMove: boolean) => (myMove ? (mine.length > 1 ? `Your move (${n.team(t)})` : 'Your move') : n.team(t));
  const entries: { mine: boolean; text: string }[] = [];
  for (const a of [...p.openAsks.values()].sort((x, y) => byId(x.ask.id, y.ask.id)))
    for (const t of a.ask.teams.filter((team) => !a.answers.has(team))) {
      const myMove = mine.includes(t);
      const text = `${who(t, myMove)}: ${a.ask.question.replaceAll('_', ' ')}`;
      if (!entries.some((e) => e.mine === myMove && e.text === text)) entries.push({ mine: myMove, text });
    }
  const ordered = [...entries.filter((e) => e.mine), ...entries.filter((e) => !e.mine)];
  return ordered.length === 0 ? null : ordered.map((e) => e.text).join(' · ');
}

/** A team's tile on the header (HeaderView.TeamTile): its score, its units in play and how many have acted this round (a dot each, filled once
 * acted; past twelve the count), the turn mark on the active team, the player icon on mine. */
export interface TeamTile {
  readonly teamId: string; readonly ordinal: number; readonly name: string; readonly score: number;
  readonly acted: number; readonly inPlay: number; readonly acting: boolean; readonly mine: boolean;
}
export const MAX_DOTS = 12;
export const tileDots = (t: TeamTile): boolean => t.inPlay <= MAX_DOTS;
export const tileCount = (t: TeamTile): string => `${t.acted}/${t.inPlay}`;

/** One tile per team in ordinal order (HeaderView.Tiles). */
export function tiles(p: Projection, game: GameInfo, n: Names, mine: readonly string[]): TeamTile[] {
  return [...game.teams].sort((a, b) => a.ordinal - b.ordinal).map((t) => {
    const units = [...p.units.values()].filter((u) => game.participants.some((x) => x.id === u.participantId && x.teamId === t.id)
      && [...p.models.values()].some((m) => m.unitId === u.id && inPlay(p, m.location)));
    return { teamId: t.id, ordinal: t.ordinal, name: n.team(t.id), score: p.scores.get(t.id) ?? 0, acted: units.filter((u) => u.actions.size > 0).length,
      inPlay: units.length, acting: p.scheduler.activeTeamId === t.id, mine: mine.includes(t.id) };
  });
}

/** HeaderFit: how much the header's lines draw to fit; compact draws the step strip's marks without their words but for the current step's,
 *  tight also the round as its number. */
export type HeaderFit = 'full' | 'compact' | 'tight';

/** The round on the header (HeaderView.RoundText): `Round 2`; before round 1 the phase (`Deployment`), else `Before round 1`. */
export function roundText(round: number, phase: string | null | undefined, fit: HeaderFit = 'full'): string {
  if (round > 0) return fit === 'tight' ? `${round}` : `Round ${round}`;
  return phase ? phase.charAt(0).toUpperCase() + phase.slice(1).replaceAll('-', ' ') : 'Before round 1';
}

/** HeaderView.StepText: a step's mark (✓ done, ● current, ○ to come) and label; past full only the current step keeps its label. */
export function stepText(item: OutlineItem, fit: HeaderFit): string {
  const mark = item.state === 'done' ? '✓' : item.state === 'current' ? '●' : '○';
  return fit === 'full' || item.state === 'current' ? `${mark} ${item.label}` : mark;
}

const ACTION_ICONS = ['hold', 'advance', 'rush', 'charge'] as const;
export type ActionIcon = (typeof ACTION_ICONS)[number] | 'activation';
/** The icon for an activation's action word (HeaderView.ActionIcon): hold, advance, rush, charge; any other word the activation icon. */
export function actionIcon(action: string | null | undefined): ActionIcon {
  const word = action?.trim().toLowerCase();
  return ACTION_ICONS.find((i) => i === word) ?? 'activation';
}

/** The header's waiting line (HeaderView.WaitLine): mine reads `Your move` in my team's colour; else the first waiting team's band and what
 * each team waits for. */
export interface WaitLine { readonly mine: boolean; readonly ordinal: number; readonly text: string }
export function waitLineOf(p: Projection, game: GameInfo, n: Names, mine: readonly string[]): WaitLine | null {
  const waits: { team: string; what: string }[] = [];
  for (const a of [...p.openAsks.values()].sort((x, y) => byId(x.ask.id, y.ask.id)))
    for (const t of a.ask.teams.filter((team) => !a.answers.has(team))) {
      const what = a.ask.question.replaceAll('_', ' ');
      if (!waits.some((w) => w.team === t && w.what === what)) waits.push({ team: t, what });
    }
  if (waits.length === 0) return null;
  const ordinal = (t: string) => game.teams.find((x) => x.id === t)?.ordinal ?? 0;
  const my = waits.find((w) => mine.includes(w.team));
  if (my !== undefined) return { mine: true, ordinal: ordinal(my.team), text: mine.length > 1 ? `Your move · ${n.team(my.team)}` : 'Your move' };
  return { mine: false, ordinal: ordinal(waits[0]!.team), text: waits.map((w) => `${n.team(w.team)} · ${w.what}`).join(', ') };
}

/** The header's line while deployment waits for players (WaitingForPlayers), one clause per team it waits on. */
export function waitingForPlayers(waiting: readonly TeamWaiting[]): string | null {
  return waiting.length === 0 ? null : `Deployment waits: ${waiting.map((w) => w.text).join(' · ')}`;
}

/** The header's line for a client the server answers as a watcher (Session.IsWatching). */
export const WATCHING = 'Watching';
/** The header's line in a replay (Session.Replaying). */
export const REPLAYING = 'Replay: the table at this point in the game';

/** One winner wins, several draw, none is no result. */
export function outcome(ended: GameEnded, n: Names): string {
  if (ended.winners.length === 0) return 'No result';
  return ended.winners.length === 1 ? `${n.team(ended.winners[0]!)} wins` : 'Draw';
}
