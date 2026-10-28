// viewer/src/lib/live/apply.ts
// A port of Tabletop.Protocol.EventApplier (contracts plan Task 14), branch for branch.
// It copies. It never decides legality, never derives a location, never clears a store on its own.
import type {
  Board, DieReplacement, GameEvent, GameEnded, GameInfo, Grant, Location, Marker, ModelSlot, OutlineStep, RollRecord,
  SequenceContext, StateChange, Unit,
} from '../protocol/wire.generated';
import type { Ask, AskAnswer } from '../protocol/wire.generated';

export type UnitInPlay = {
  readonly id: string; readonly participantId: string; readonly definition: Unit;
  readonly actions: Set<string>; readonly conditions: Set<string>;
  grants: Grant[]; woundsThisActivation: number;
};
export type ModelInPlay = {
  readonly id: string; readonly unitId: string; readonly slot: ModelSlot;
  location: Location; wounds: number; readonly conditions: Set<string>; spellTokens: number;
};
export type SchedulerPosition = {
  round: number; order: string[]; activeTeamId: string | undefined;
  phase: string | undefined; readonly passed: Set<string>;
};
/** EventApplier's CompletedStep: one finished step of a sequence's frame. `rolls` is a copy, so a replaced die never edits the event. */
export type CompletedStep = {
  readonly step: string; readonly answer: AskAnswer | undefined; rolls: RollRecord[];
  readonly changes: readonly StateChange[]; readonly values: Readonly<Record<string, number>> | undefined;
  readonly applied: readonly string[] | undefined;
};
/** EventApplier's Frame: an open sequence. */
export type Frame = {
  readonly id: string; readonly sequence: string; readonly context: SequenceContext;
  readonly outline: readonly OutlineStep[]; readonly steps: CompletedStep[];
};
export type OpenAsk = { readonly ask: Ask; readonly answers: Map<string, AskAnswer | undefined> };

/**
 * The port of `Tabletop.Protocol.ProjectionDesyncException`: an event named
 * an id this projection does not hold, so this copy of the game has diverged from the server's. Thrown at
 * exactly the sites where the C# applier indexes a dictionary, and carrying the same two facts.
 */
export class ProjectionDesyncError extends Error {
  constructor(readonly kind: string, readonly unknownId: string) {
    super(`${kind} names ${unknownId}, which this projection does not hold`);
    this.name = 'ProjectionDesyncError';
  }
}

export type Projection = {
  readonly board: Board;
  readonly units: Map<string, UnitInPlay>;
  readonly models: Map<string, ModelInPlay>;
  readonly markers: Map<string, Marker>;
  readonly scores: Map<string, number>;
  readonly control: Map<string, string>;
  readonly scheduler: SchedulerPosition;
  readonly openAsks: Map<string, OpenAsk>;
  readonly stack: Frame[];
  lastSeq: number;
  ended: GameEnded | undefined;
};

export function initialProjection(board: Board, game: GameInfo): Projection {
  const p: Projection = {
    board,
    units: new Map(), models: new Map(), markers: new Map(), scores: new Map(), control: new Map(),
    scheduler: { round: 0, order: [], activeTeamId: undefined, phase: undefined, passed: new Set() },
    openAsks: new Map(), stack: [], lastSeq: 0, ended: undefined,
  };
  for (const team of game.teams) p.scores.set(team.id, 0);
  // Insertion order matters: it is the order every frame lists models and units in, and C#'s
  // Projection.Initial walks armies, then units, then model slots in exactly this order.
  for (const army of board.armies)
    for (const unit of army.snapshot.units) {
      p.units.set(unit.id, {
        id: unit.id, participantId: army.participantId, definition: unit,
        actions: new Set(), conditions: new Set(), grants: [], woundsThisActivation: 0,
      });
      for (const slot of unit.models)
        p.models.set(slot.id, {
          id: slot.id, unitId: unit.id, slot, location: { kind: 'shelf' },
          wounds: 0, conditions: new Set(), spellTokens: 0,
        });
    }
  return p;
}

/** The four lookups that can miss. Each is a place the C# applier indexes a dictionary. */
const unitIn = (p: Projection, kind: string, id: string): UnitInPlay => {
  const unit = p.units.get(id);
  if (unit === undefined) throw new ProjectionDesyncError(kind, id);
  return unit;
};
const modelIn = (p: Projection, kind: string, id: string): ModelInPlay => {
  const model = p.models.get(id);
  if (model === undefined) throw new ProjectionDesyncError(kind, id);
  return model;
};
const markerIn = (p: Projection, kind: string, id: string): Marker => {
  const marker = p.markers.get(id);
  if (marker === undefined) throw new ProjectionDesyncError(kind, id);
  return marker;
};
const askIn = (p: Projection, kind: string, id: string): OpenAsk => {
  const ask = p.openAsks.get(id);
  if (ask === undefined) throw new ProjectionDesyncError(kind, id);
  return ask;
};

const frameOf = (p: Projection, kind: string, frameId: string): Frame => {
  const frame = p.stack.find((f) => f.id === frameId);
  if (frame === undefined) throw new ProjectionDesyncError(kind, frameId);
  return frame;
};

const pop = (p: Projection, kind: string, frameId: string): void => {
  const index = p.stack.findIndex((f) => f.id === frameId);
  if (index < 0) throw new ProjectionDesyncError(kind, frameId);
  p.stack.splice(index);
};

const replaceDice = (p: Projection, kind: string, frameId: string, rollId: string, replaced: readonly DieReplacement[]): void => {
  const step = frameOf(p, kind, frameId).steps.find((st) => st.rolls.some((r) => r.id === rollId));
  if (step === undefined) throw new ProjectionDesyncError(kind, rollId);
  const i = step.rolls.findIndex((r) => r.id === rollId);
  const dice = [...step.rolls[i]!.dice];
  for (const r of replaced) {
    if (r.index < 0 || r.index >= dice.length) throw new ProjectionDesyncError(kind, rollId);
    dice[r.index] = r.new;
  }
  step.rolls[i] = { ...step.rolls[i]!, dice };
};

export function applyChange(p: Projection, change: StateChange): void {
  switch (change.kind) {
    case 'models_moved': for (const move of change.moves) modelIn(p, change.kind, move.modelId).location = move.to; return;
    case 'wounds_set': modelIn(p, change.kind, change.modelId).wounds = change.wounds; return;
    case 'condition_set': {
      const set = change.subject.kind === 'unit'
        ? unitIn(p, change.kind, change.subject.id).conditions
        : modelIn(p, change.kind, change.subject.id).conditions;
      if (change.present) set.add(change.condition); else set.delete(change.condition);
      return;
    }
    case 'action_recorded': unitIn(p, change.kind, change.unitId).actions.add(change.action); return;
    case 'action_record_cleared': unitIn(p, change.kind, change.unitId).actions.clear(); return;
    case 'grant_added': unitIn(p, change.kind, change.unitId).grants.push(change.grant); return;
    case 'grant_removed': {
      const unit = unitIn(p, change.kind, change.unitId);
      unit.grants = unit.grants.filter((g) => g.id !== change.grantId);
      return;
    }
    case 'spell_tokens_set': modelIn(p, change.kind, change.modelId).spellTokens = change.tokens; return;
    case 'wounds_this_activation_set': unitIn(p, change.kind, change.unitId).woundsThisActivation = change.wounds; return;
    // A store that C# writes with an indexer rather than reading through one: score_set cannot desync, and neither side faults on it.
    case 'score_set': p.scores.set(change.teamId, change.score); return;
    case 'marker_moved': {
      // A setup marker's first move copies its board row into the play markers, which win on the shared id from then on.
      const marker = p.markers.get(change.markerId) ?? p.board.table.markers.find((m) => m.id === change.markerId)
        ?? markerIn(p, change.kind, change.markerId);
      p.markers.set(change.markerId, { ...marker, position: change.position, yaw: change.yaw });
      return;
    }
    case 'objective_control_set':
      if (change.teamId === undefined) p.control.delete(change.markerId); else p.control.set(change.markerId, change.teamId);
      return;
    default: {
      // A kind added to the generated union fails `npm run check` here.
      const unreachable: never = change;
      throw new Error(`Unknown state change ${(unreachable as { kind: string }).kind}`);
    }
  }
}

// A Record, not a list: a kind added to the generated StateChange union is a missing key and fails the check.
const STATE_CHANGE_KINDS: Record<StateChange['kind'], true> = {
  models_moved: true, wounds_set: true, condition_set: true, action_recorded: true, action_record_cleared: true,
  grant_added: true, grant_removed: true, spell_tokens_set: true, wounds_this_activation_set: true,
  score_set: true, marker_moved: true, objective_control_set: true,
};
const isStateChange = (payload: { kind: string }): payload is StateChange =>
  Object.hasOwn(STATE_CHANGE_KINDS, payload.kind);

export function applyTo(p: Projection, event: GameEvent): void {
  const payload = event.payload;
  if (isStateChange(payload)) applyChange(p, payload);
  else switch (payload.kind) {
    case 'round_started':
      p.ended = undefined;
      p.scheduler.round = payload.round;
      p.scheduler.order = [...payload.order];
      p.scheduler.activeTeamId = undefined;
      p.scheduler.passed.clear();
      for (const change of payload.changes) applyChange(p, change);
      break;
    case 'round_ended': for (const change of payload.changes) applyChange(p, change); break;
    case 'game_ended': p.ended = payload; break;
    case 'turn_started': p.scheduler.activeTeamId = payload.teamId; break;
    case 'turn_ended':
      if (payload.passed) p.scheduler.passed.add(payload.teamId);
      if (p.scheduler.activeTeamId === payload.teamId) p.scheduler.activeTeamId = undefined;
      for (const change of payload.changes) applyChange(p, change);
      break;
    case 'phase_started': p.scheduler.phase = payload.phase; break;
    case 'sequence_started':
      p.stack.push({ id: payload.frameId, sequence: payload.sequence, context: payload.context, outline: payload.outline ?? [], steps: [] });
      break;
    case 'step_completed': {
      const frame = frameOf(p, payload.kind, payload.frameId);
      frame.steps.push({
        step: payload.step, answer: payload.answer, rolls: [...payload.rolls], changes: payload.changes,
        values: payload.values, applied: payload.applied,
      });
      for (const change of payload.changes) applyChange(p, change);
      break;
    }
    case 'sequence_finished': case 'sequence_abandoned': pop(p, payload.kind, payload.frameId); break;
    case 'dice_rerolled': case 'dice_edited': replaceDice(p, payload.kind, payload.frameId, payload.rollId, payload.replaced); break;
    case 'ask_opened': p.openAsks.set(payload.ask.id, { ask: payload.ask, answers: new Map() }); break;
    case 'ask_answered': askIn(p, payload.kind, payload.askId).answers.set(payload.teamId, payload.answer); break;
    case 'ask_closed': p.openAsks.delete(payload.askId); break;
    case 'undone': throw new Error('undone is applied by rebuilding; use Applier.apply');
    default: {
      const unreachable: never = payload;
      throw new Error(`Unknown payload ${(unreachable as { kind: string }).kind}`);
    }
  }
  p.lastSeq = event.seq;
}

/** The log with every undone range removed. Undone markers never appear in the result. */
export function effectiveLog(log: readonly GameEvent[]): readonly GameEvent[] {
  const live: GameEvent[] = [];
  for (const event of log) {
    if (event.payload.kind === 'undone') {
      const to = event.payload.toSeq;
      for (let i = live.length - 1; i >= 0; i--) if (live[i]!.seq > to) live.splice(i, 1);
    } else live.push(event);
  }
  return live;
}

function rebuild(board: Board, game: GameInfo, live: readonly GameEvent[], lastSeq: number): Projection {
  const p = initialProjection(board, game);
  for (const e of live) applyTo(p, e);
  p.lastSeq = lastSeq;
  return p;
}

/** EventApplier.ProjectTo: the effective log's events with seq <= `seq`, rebuilt on the board; what the client shows while an Undo travels. */
export function projectTo(board: Board, game: GameInfo, log: readonly GameEvent[], seq: number): Projection {
  return rebuild(board, game, effectiveLog(log).filter((e) => e.seq <= seq), seq);
}

export class Applier {
  readonly #board: Board;
  readonly #game: GameInfo;
  readonly #log: GameEvent[] = [];
  #projection: Projection;

  constructor(board: Board, game: GameInfo) {
    this.#board = board;
    this.#game = game;
    this.#projection = initialProjection(board, game);
  }

  get log(): readonly GameEvent[] { return this.#log; }
  get projection(): Projection { return this.#projection; }

  apply(event: GameEvent): void {
    this.#log.push(event);
    if (event.payload.kind === 'undone') {
      this.#projection = rebuild(this.#board, this.#game, effectiveLog(this.#log), event.seq);
    } else applyTo(this.#projection, event);
  }

  projectTo(seq: number): Projection { return projectTo(this.#board, this.#game, this.#log, seq); }

  applyAll(events: Iterable<GameEvent>): void { for (const event of events) this.apply(event); }
}
