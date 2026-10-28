// viewer/src/lib/play/logText.ts
// client/src/Core/LogText.cs: every event in words, with its actor. Presence never appears here because presence is never an event.

import type { AskAnswer, AskAnswered, GameEvent, ModelRelocation, StateChange, StepCompleted } from '../protocol/wire.generated';
import { Names } from './names';

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

export function describe(e: GameEvent, n: Names): string {
  const who = n.participant(e.actor);
  const p = e.payload;
  let text: string;
  switch (p.kind) {
    case 'models_moved': text = moved(p.moves, n); break;
    case 'wounds_set': text = `set ${n.model(p.modelId)} to ${p.wounds} wound${p.wounds === 1 ? '' : 's'}`; break;
    case 'condition_set':
      text = `${p.present ? 'marked' : 'cleared'} ${p.subject.kind === 'unit' ? n.unit(p.subject.id) : n.model(p.subject.id)} ${p.condition}`; break;
    case 'action_recorded': text = `recorded ${p.action} for ${n.unit(p.unitId)}`; break;
    case 'action_record_cleared': text = `cleared ${n.unit(p.unitId)}'s action record`; break;
    case 'grant_added': text = `granted ${p.grant.entryName} to ${n.unit(p.unitId)}`; break;
    case 'grant_removed': text = `removed a grant from ${n.unit(p.unitId)}`; break;
    case 'spell_tokens_set': text = `set ${n.model(p.modelId)} to ${p.tokens} spell token${p.tokens === 1 ? '' : 's'}`; break;
    case 'wounds_this_activation_set': text = `set ${n.unit(p.unitId)}'s wounds this activation to ${p.wounds}`; break;
    case 'score_set': text = `set ${n.team(p.teamId)}'s score to ${p.score}`; break;
    case 'marker_moved': text = `moved marker ${n.piece(p.markerId)}`; break;
    case 'round_started': text = `started round ${p.round}`; break;
    case 'turn_started': text = `started ${n.team(p.teamId)}'s turn`; break;
    case 'turn_ended': text = p.passed ? `passed for ${n.team(p.teamId)}` : `ended ${n.team(p.teamId)}'s turn`; break;
    case 'phase_started': text = `began ${p.phase}`; break;
    case 'sequence_started':
      text = `began ${p.sequence}${p.context.actorUnitId === undefined ? '' : ` with ${n.unit(p.context.actorUnitId)}`}`; break;
    case 'step_completed': text = step(p); break;
    case 'sequence_finished': text = 'finished the sequence'; break;
    case 'sequence_abandoned': text = 'abandoned the sequence'; break;
    case 'ask_opened': text = `asked ${p.ask.teams.map((t) => n.team(t)).join(', ')}: ${p.ask.prompt}`; break;
    // "confirmed" is what a confirm gate and the round boundary's end_round Ask both are; the round_started line under it says which round began.
    case 'ask_answered': {
      const a = p.answer;
      if (a?.kind === 'passed') text = `passed for ${n.team(p.teamId)}`;
      else if (a?.kind === 'confirmed') text = `confirmed for ${n.team(p.teamId)}`;
      // An unsealed answer is in hand, so it is in words: a takedown's pick has to name the model it picked (p.13).
      else if (a !== undefined) text = `answered for ${n.team(p.teamId)}: ${answer(a, n)}${flag(p)}`;
      else text = `answered for ${n.team(p.teamId)}${flag(p)}`;   // withheld: a sealed Ask reveals at ask_closed
      break;
    }
    case 'ask_closed':
      text = p.answers.length === 0 ? 'closed the question'
        : `closed the question: ${p.answers.map((x) => `${n.team(x.teamId)} ${answer(x.answer, n)}`).join(', ')}`;   // a sealed Ask's reveal
      break;
    case 'dice_rerolled': text = `rerolled ${p.replaced.length} dice`; break;
    case 'dice_edited': text = `set ${p.replaced.length} dice by hand`; break;
    case 'undone': text = `undid back to #${p.toSeq}`; break;
    case 'round_ended':
      text = `ended round ${p.round}${p.changes.length === 0 ? '' : `: ${p.changes.map((c) => roundChange(c, n)).join(', ')}`}`; break;
    // a menu edit; inside round_ended the same change reads "took"
    case 'objective_control_set':
      text = p.teamId !== undefined ? `gave ${n.marker(p.markerId)} to ${n.team(p.teamId)}` : `made ${n.marker(p.markerId)} neutral`; break;
    case 'game_ended': text = `Game ended: ${endReason(p)}${result(p.winners, n)}`; break;
    default: text = (p as { kind: string }).kind;
  }
  const line = p.kind === 'game_ended' ? text : `${who} ${text}`;   // the end has no actor; the sim and viewer lines read "Game ended: …"
  return line + (e.warning === undefined ? '' : ` — ${e.warning}`);
}

/** The reason, its closing full stop dropped when a result follows it ("… Markers held: Team 1: 0, Team 2: 0; tied between …"). */
function endReason(g: { reason: string; winners: readonly string[] }): string {
  return g.winners.length === 0 ? g.reason : g.reason.replace(/\.+$/, '');
}

/** One winner, a tie, or nothing when the ruleset recorded no result. */
function result(winners: readonly string[], n: Names): string {
  if (winners.length === 0) return '';
  if (winners.length === 1) return `; ${n.team(winners[0]!)} won`;
  return `; tied between ${winners.map((w) => n.team(w)).join(', ')}`;
}

/** A round end's change in words. Only objective control is written there today; anything else reads by its kind. */
export function roundChange(c: StateChange, n: Names): string {
  if (c.kind === 'objective_control_set')
    return c.teamId !== undefined ? `${n.team(c.teamId)} took ${n.marker(c.markerId)}` : `${n.marker(c.markerId)} became neutral`;
  return c.kind;
}

function moved(moves: readonly ModelRelocation[], n: Names): string {
  const first = moves[0]!;
  const unitId = n.unitOfModel(first.modelId);
  const unit = unitId !== null ? n.unit(unitId) : Names.short(first.modelId);
  const { from, to } = first;
  if (from.kind === 'table' && to.kind === 'table') {
    const a = from.placement.position, b = to.placement.position;
    return `moved ${unit} ${Math.hypot(b.x - a.x, b.y - a.y).toFixed(1)}"`;
  }
  if (from.kind === 'shelf' && to.kind === 'table') return `deployed ${unit}`;
  if (from.kind === 'inside' && to.kind === 'table') return `took ${unit} out of ${n.piece(from.containerId)}`;
  if (to.kind === 'shelf') return `moved ${unit} to the shelf`;
  if (to.kind === 'inside') return `put ${unit} inside ${n.piece(to.containerId)}`;
  return `moved ${unit}`;
}

/** A sealed Ask's answers reach the client only inside ask_closed, which is the reveal. */
function flag(a: AskAnswered): string {
  return a.breach !== undefined ? ` (breaks ${a.breach})` : a.outsideLegalSet ? ' (outside the legal set)' : '';
}

const sum = (counts: { readonly [key: string]: number }) => Object.values(counts).reduce((x, y) => x + y, 0);

export function answer(a: AskAnswer, n: Names): string {
  switch (a.kind) {
    case 'chosen': return `chose ${a.optionId}`;
    case 'selected_pieces':
      return a.pieceIds.length === 1 ? `picked ${n.piece(a.pieceIds[0]!)}` : `picked ${plural(a.pieceIds.length, 'piece')}`;
    case 'allocated': return `allocated ${sum(a.counts)}`;
    case 'assigned': return 'assigned targets';
    case 'destinations': return `moved ${plural(a.moves.length, 'model')}`;
    case 'tokens_spent': return `spent ${plural(sum(a.counts), 'token')} (${parts(a.counts, n)})`;
    case 'confirmed': return 'confirmed';
    case 'passed': return 'passed';
    default: return 'answered';
  }
}

/** Which model gave what, ordered by the name so the words do not depend on a dictionary's order. */
function parts(counts: { readonly [key: string]: number }, n: Names): string {
  return Object.entries(counts).filter(([, v]) => v > 0).map(([k, v]) => `${n.piece(k)} ${v}`)
    .sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)).join(', ');
}

function step(s: StepCompleted): string {
  if (s.rolls.length === 0) return `completed ${s.step}`;
  const dice = s.rolls.flatMap((r) => r.dice);
  return `completed ${s.step}: ${dice.filter((d) => d.success).length} of ${dice.length} succeeded`;
}

const unique = (ids: readonly string[]): string[] => [...new Set(ids)];

/** What clicking the entry highlights on the table. */
export function touched(e: GameEvent): string[] {
  const p = e.payload;
  switch (p.kind) {
    case 'models_moved': return p.moves.map((x) => x.modelId);
    case 'wounds_set': case 'spell_tokens_set': return [p.modelId];
    case 'condition_set': return [p.subject.id];
    case 'action_recorded': case 'action_record_cleared': case 'grant_added': case 'grant_removed': case 'wounds_this_activation_set': return [p.unitId];
    case 'marker_moved': return [p.markerId];
    case 'sequence_started':
      return [...(p.context.actorUnitId === undefined ? [] : [p.context.actorUnitId]), ...p.context.targetUnitIds];
    case 'step_completed': return p.changes.flatMap((c) => touched({ seq: 0, payload: c }));
    case 'ask_opened': {
      const l = p.ask.legal;
      return l.kind === 'options' ? unique(l.choices.flatMap((c) => c.pieces)) : l.kind === 'pieces' ? [...l.eligible] : [];
    }
    // An answer names pieces too: the model a Takedown picked, and the models a team spent its tokens from (p.14).
    case 'ask_answered': return p.answer === undefined ? [] : answerPieces(p.answer);
    case 'ask_closed': return unique(p.answers.flatMap((x) => answerPieces(x.answer)));
    default: return [];
  }
}

function answerPieces(a: AskAnswer): string[] {
  switch (a.kind) {
    case 'selected_pieces': return [...a.pieceIds];
    case 'allocated': case 'tokens_spent': return Object.entries(a.counts).filter(([, v]) => v > 0).map(([k]) => k);
    case 'assigned': return unique(Object.values(a.targets));
    case 'destinations': return a.moves.map((m) => m.modelId);
    default: return [];
  }
}
