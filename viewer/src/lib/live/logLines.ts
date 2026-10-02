// viewer/src/lib/live/logLines.ts
// A port of Tabletop.Sim.Recording.LogLines (simulation plan Task 5), case for case. Rules-free by
// construction: every name it prints is a string the event already carries.
import type { AskAnswer, GameEvent, RollRecord, Subject } from '../protocol/wire.generated';
import type { Projection } from './apply';

export function shortId(id: string): string {
  return id.replace(/-/g, '').slice(0, 8);
}

const team = (ordinals: ReadonlyMap<string, number>, id: string): string =>
  ordinals.has(id) ? `Team ${ordinals.get(id)}` : shortId(id);
const unit = (p: Projection, id: string): string => p.units.get(id)?.definition.name ?? shortId(id);
const model = (p: Projection, id: string): string => p.models.get(id)?.slot.name ?? shortId(id);
const subject = (p: Projection, s: Subject): string => (s.kind === 'unit' ? unit(p, s.id) : model(p, s.id));
const actor = (p: Projection, id: string | undefined): string => (id === undefined ? '' : ` for ${unit(p, id)}`);
/** C#'s "+#;-#;0" custom numeric format. */
const signed = (n: number): string => (n > 0 ? `+${n}` : n < 0 ? `${n}` : '0');

// Every roll on the step, with its threshold: a ruleset splits one roll wherever a step's dice do not share one, and OPR's
// roll-to-hit takes one pool per Quality value the attacking models bring (p.4, and a joined hero brings its own). The
// threshold is the only thing that tells two pools of the same roll apart.
const dice = (rolls: readonly RollRecord[]): string =>
  rolls.length === 0
    ? ''
    : `: ${rolls
        .map(
          (r) =>
            `${r.roll}${r.threshold === undefined ? '' : ` ${r.threshold}+`} ` +
            `${r.dice.map((d) => (d.success ? `${d.modified}!` : `${d.modified}`)).join(',')}`,
        )
        .join('; ')}`;

function answerOf(answer: AskAnswer | undefined): string {
  if (answer === undefined) return 'nothing';
  switch (answer.kind) {
    case 'chosen': return answer.optionId;
    case 'selected_pieces': return `${answer.pieceIds.length} piece(s)`;
    case 'allocated': return `${Object.values(answer.counts).reduce((a, b) => a + b, 0)} allocated`;
    case 'assigned': return `${Object.keys(answer.targets).length} assignment(s)`;
    case 'destinations': return `${answer.moves.length} destination(s)`;
    case 'tokens_spent': return `${Object.values(answer.counts).reduce((a, b) => a + b, 0)} token(s) spent`;
    case 'confirmed': return 'yes';
    case 'passed': return 'a pass';
    default: return (answer as { kind: string }).kind;
  }
}

export function lineOf(event: GameEvent, after: Projection, ordinals: ReadonlyMap<string, number>): string {
  const e = event.payload;
  switch (e.kind) {
    case 'round_started': return `Round ${e.round} started`;
    case 'turn_started': return `${team(ordinals, e.teamId)}'s turn`;
    case 'turn_ended': return e.passed ? `${team(ordinals, e.teamId)} passed` : `${team(ordinals, e.teamId)}'s turn ended`;
    case 'phase_started': return `Phase: ${e.phase}`;
    case 'sequence_started': return `${e.sequence} started${actor(after, e.context.actorUnitId)}`;
    case 'step_completed': return `${e.step}${dice(e.rolls)}`;
    case 'sequence_finished': return 'sequence finished';
    case 'sequence_abandoned': return 'sequence abandoned';
    case 'ask_opened': return `Asked ${e.ask.teams.map((t) => team(ordinals, t)).join(', ')}: ${e.ask.prompt}`;
    case 'ask_answered':
      return `${team(ordinals, e.teamId)} answered ${answerOf(e.answer)}${e.outsideLegalSet ? ' (outside the legal set)' : ''}`;
    case 'ask_closed': return 'ask closed';
    case 'dice_rerolled': return `rerolled ${e.replaced.length} dice`;
    case 'dice_edited': return `edited ${e.replaced.length} dice`;
    case 'modifier_added': return `${e.modifier.source} gives ${signed(e.modifier.amount)} to ${e.roll}`;
    case 'undone': return `undone to ${e.toSeq}`;
    case 'models_moved': return `${e.moves.length} model(s) moved`;
    case 'wounds_set': return `${model(after, e.modelId)} at ${e.wounds} wound(s)`;
    case 'condition_set': return `${subject(after, e.subject)} ${e.present ? 'gains' : 'loses'} ${e.condition}`;
    case 'action_recorded': return `${unit(after, e.unitId)} did ${e.action}`;
    case 'action_record_cleared': return `${unit(after, e.unitId)}'s action record cleared`;
    case 'grant_added': return `${unit(after, e.unitId)} gains ${e.grant.entryName}`;
    case 'grant_removed': return 'a grant was removed';
    case 'spell_tokens_set': return `${model(after, e.modelId)} holds ${e.tokens} token(s)`;
    case 'wounds_this_activation_set': return `${unit(after, e.unitId)} has taken ${e.wounds} wound(s) this activation`;
    case 'score_set': return `${team(ordinals, e.teamId)} scores ${e.score}`;
    case 'marker_placed': return `marker placed${e.marker.label === undefined ? '' : `: ${e.marker.label}`}`;
    case 'marker_moved': return 'marker moved';
    case 'marker_removed': return 'marker removed';
    default: return (e as { kind: string }).kind;
  }
}
