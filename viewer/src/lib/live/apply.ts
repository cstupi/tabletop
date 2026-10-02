// viewer/src/lib/live/apply.ts
// A port of Tabletop.Protocol.EventApplier (contracts plan Task 14), branch for branch.
// It copies. It never decides legality, never derives a location, never clears a store on its own.
import type {
  Board, GameEvent, GameInfo, Grant, Location, Marker, ModelSlot, StateChange, Unit,
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
  readonly scheduler: SchedulerPosition;
  readonly openAsks: Map<string, OpenAsk>;
  lastSeq: number;
};

export function initialProjection(board: Board, game: GameInfo): Projection {
  const p: Projection = {
    board,
    units: new Map(), models: new Map(), markers: new Map(), scores: new Map(),
    scheduler: { round: 0, order: [], activeTeamId: undefined, phase: undefined, passed: new Set() },
    openAsks: new Map(), lastSeq: 0,
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
    // A store that C# writes with an indexer or Remove rather than reading through one: score_set,
    // marker_placed and marker_removed cannot desync, and neither side faults on them.
    case 'score_set': p.scores.set(change.teamId, change.score); return;
    case 'marker_placed': p.markers.set(change.marker.id, change.marker); return;
    case 'marker_moved': {
      const marker = markerIn(p, change.kind, change.markerId);
      p.markers.set(change.markerId, { ...marker, position: change.position, yaw: change.yaw });
      return;
    }
    case 'marker_removed': p.markers.delete(change.markerId); return;
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
  score_set: true, marker_placed: true, marker_moved: true, marker_removed: true,
};
const isStateChange = (payload: { kind: string }): payload is StateChange =>
  Object.hasOwn(STATE_CHANGE_KINDS, payload.kind);

export function applyTo(p: Projection, event: GameEvent): void {
  const payload = event.payload;
  if (isStateChange(payload)) applyChange(p, payload);
  else switch (payload.kind) {
    case 'round_started':
      p.scheduler.round = payload.round;
      p.scheduler.order = [...payload.order];
      p.scheduler.activeTeamId = undefined;
      p.scheduler.passed.clear();
      for (const change of payload.changes) applyChange(p, change);
      break;
    case 'turn_started': p.scheduler.activeTeamId = payload.teamId; break;
    case 'turn_ended':
      if (payload.passed) p.scheduler.passed.add(payload.teamId);
      if (p.scheduler.activeTeamId === payload.teamId) p.scheduler.activeTeamId = undefined;
      for (const change of payload.changes) applyChange(p, change);
      break;
    case 'phase_started': p.scheduler.phase = payload.phase; break;
    case 'step_completed': for (const change of payload.changes) applyChange(p, change); break;
    case 'ask_opened': p.openAsks.set(payload.ask.id, { ask: payload.ask, answers: new Map() }); break;
    case 'ask_answered': askIn(p, payload.kind, payload.askId).answers.set(payload.teamId, payload.answer); break;
    case 'ask_closed': p.openAsks.delete(payload.askId); break;
    // The frame stack is not ported: RecordedFrame carries no stack, and its rolls come from the
    // step_completed payload, so these six change nothing any field of a frame records.
    // Unported stack-site faults: Pop on sequence_finished/sequence_abandoned, FrameOf,
    // ReplaceDice's roll and die-index lookups.
    case 'sequence_started': case 'sequence_finished': case 'sequence_abandoned':
    case 'dice_rerolled': case 'dice_edited': case 'modifier_added': break;
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
      const rebuilt = initialProjection(this.#board, this.#game);
      for (const live of effectiveLog(this.#log)) applyTo(rebuilt, live);
      rebuilt.lastSeq = event.seq;
      this.#projection = rebuilt;
    } else applyTo(this.#projection, event);
  }

  applyAll(events: Iterable<GameEvent>): void { for (const event of events) this.apply(event); }
}
