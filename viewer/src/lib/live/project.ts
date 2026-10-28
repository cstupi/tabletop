// viewer/src/lib/live/project.ts
// A port of Tabletop.Sim.Recording.FrameProjector (simulation plan Task 5): applies the events the socket
// delivered and writes down what the table looked like afterwards, in the recording's own shape.
import { DECLARATIONS } from '../board/palette';
import type {
  Ask, Board, GameEvent, GameInfo, ModelSlot, RollRecord, TerrainPlacement, VisualRef, Volume,
} from '../protocol/wire.generated';
import type {
  RecordedAsk, RecordedAskOption, RecordedBoard, RecordedFrame, RecordedModel, RecordedModelState,
  RecordedRoll, RecordedTerrain, RecordedUnitState, RecordedVolume,
} from '../protocol/recording.generated';
import { Applier, type ModelInPlay, type OpenAsk, type Projection, type UnitInPlay } from './apply';
import { lineOf } from './logLines';

/** The [Flags] wire form ("none", or "cover, impassable") in the canonical declaration order C# emits. */
export function declarationsOf(wire: string): readonly string[] {
  const present = new Set(wire.split(',').map((s) => s.trim()).filter((s) => s.length > 0 && s !== 'none'));
  return DECLARATIONS.filter((d) => present.has(d));
}

const visualName = (visual: VisualRef): string => visual.builtin ?? visual.assetId!;

function volumeOf(id: string, v: Volume): RecordedVolume {
  return {
    id, kind: v.kind, footprint: v.footprint.kind, a: v.footprint.a, b: v.footprint.b,
    offsetX: v.offset.x, offsetY: v.offset.y, yaw: v.yaw.degrees, bottom: v.bottom, top: v.top,
    declarations: declarationsOf(v.declarations),
    ...(v.notes === undefined ? {} : { notes: v.notes }),
  };
}

function terrainOf(piece: TerrainPlacement): RecordedTerrain {
  return {
    id: piece.id, mesh: visualName(piece.mesh), x: piece.position.x, y: piece.position.y, z: piece.position.z,
    yaw: piece.yaw.degrees, scale: piece.scale, volumes: piece.volumes.map((v) => volumeOf(v.id, v.volume)),
  };
}

export function recordedBoardOf(board: Board, game: GameInfo): RecordedBoard {
  const table = board.table;
  const teamOf = new Map(game.participants.filter((p) => p.teamId !== undefined).map((p) => [p.id, p.teamId!]));
  const ordinalOf = new Map(game.teams.map((t) => [t.id, t.ordinal]));
  const models: RecordedModel[] = board.armies.flatMap((army) =>
    army.snapshot.units.flatMap((unit) =>
      unit.models.map((slot: ModelSlot) => {
        const teamId = teamOf.get(army.participantId)!;
        return {
          id: slot.id, unitId: unit.id, unitName: unit.name, name: slot.name, ordinal: slot.ordinal,
          teamId, teamOrdinal: ordinalOf.get(teamId)!,
          baseShape: slot.base.shape, baseA: slot.base.a, baseB: slot.base.b, height: slot.height,
        };
      }),
    ),
  );
  return {
    width: table.size.width, length: table.size.length, texture: visualName(table.texture),
    terrain: table.terrain.map(terrainOf),
    markers: table.markers.map((m) => ({
      id: m.id, kind: m.kind, diameter: m.diameter, ...(m.label === undefined ? {} : { label: m.label }),
    })),
    // The live feed resolves no zone: ZoneStyles is C# and is not ported (Task 4). An empty ring list
    // is what an unresolvable shape id produces C#-side too, and draws no outline rather than a wrong
    // one. Task 10's normaliseBoard drops `polygons` before comparing, and says why.
    zones: table.zones.map((z) => ({ id: z.id, shape: z.shape, teamOrdinal: z.teamOrdinal, polygons: [] })),
    models,
  };
}

const modelState = (m: ModelInPlay): RecordedModelState => {
  const common = { id: m.id, wounds: m.wounds, spellTokens: m.spellTokens, conditions: [...m.conditions].sort() };
  if (m.location.kind === 'table') {
    const { position, yaw } = m.location.placement;
    return { ...common, where: 'table', x: position.x, y: position.y, z: position.z, yaw: yaw.degrees };
  }
  if (m.location.kind === 'inside')
    return { ...common, where: 'inside', containerId: m.location.containerId };
  return { ...common, where: 'shelf' };
};

const unitState = (u: UnitInPlay): RecordedUnitState => ({
  id: u.id, actions: [...u.actions].sort(), conditions: [...u.conditions].sort(),
  grants: u.grants.map((g) => g.entryName), woundsThisActivation: u.woundsThisActivation,
});

const optionOf = (o: { id: string; label: string; pieces: readonly string[] }): RecordedAskOption =>
  ({ id: o.id, label: o.label, pieces: o.pieces });

function askOf(open: OpenAsk): RecordedAsk {
  const ask: Ask = open.ask;
  const legal = ask.legal;
  let kind = legal.kind as string;
  let options: readonly RecordedAskOption[] = [];
  let pieces: readonly string[] = [];
  let allowance: number | undefined;
  switch (legal.kind) {
    case 'options': options = legal.choices.map(optionOf); break;
    case 'pieces': pieces = legal.eligible; break;
    case 'allocation': pieces = legal.targets; break;
    case 'assignment': options = legal.items.map(optionOf); pieces = legal.targets; break;
    case 'move_region': pieces = legal.modelIds; allowance = legal.allowance; break;
    // One maximum per eligible model (p.13): the models go in `pieces` and their sum in `allowance`, which is what
    // `FrameProjector.AskOf` records on the C# side. The per-model figures are not carried; each frame already has
    // every model's `spellTokens`.
    case 'token_spend':
      pieces = Object.keys(legal.maxByModel);
      allowance = Object.values(legal.maxByModel).reduce((a, b) => a + b, 0);
      break;
    case 'confirmation': break;
    default: { const unreachable: never = legal; kind = (unreachable as { kind: string }).kind; }
  }
  return {
    id: ask.id, question: ask.question, prompt: ask.prompt, teams: ask.teams,
    allowPass: ask.allowPass, sealed: ask.sealed,
    ...(ask.windowKind === undefined ? {} : { windowKind: ask.windowKind }),
    ...(ask.frameId === undefined ? {} : { frameId: ask.frameId }),
    legalKind: kind, answered: [...open.answers.keys()], options, pieces,
    ...(allowance === undefined ? {} : { allowance }),
  };
}

const rollOf = (r: RollRecord): RecordedRoll => ({
  id: r.id, roll: r.roll, ...(r.threshold === undefined ? {} : { threshold: r.threshold }),
  net: r.dice.length === 0 ? 0 : r.dice[0]!.modified - r.dice[0]!.raw,
  raw: r.dice.map((d) => d.raw), modified: r.dice.map((d) => d.modified), success: r.dice.map((d) => d.success),
  modifiers: r.modifiers.map((m) => ({ source: m.source, amount: m.amount })),
});

export class FrameProjector {
  readonly #applier: Applier;
  readonly #ordinals: ReadonlyMap<string, number>;
  readonly #frames: RecordedFrame[] = [];
  readonly board: RecordedBoard;

  constructor(board: Board, game: GameInfo) {
    this.#applier = new Applier(board, game);
    this.#ordinals = new Map(game.teams.map((t) => [t.id, t.ordinal]));
    this.board = recordedBoardOf(board, game);
  }

  get frames(): readonly RecordedFrame[] { return this.#frames; }
  get projection(): Projection { return this.#applier.projection; }

  add(event: GameEvent): RecordedFrame {
    this.#applier.apply(event);
    const p = this.#applier.projection;
    const frame = {
      index: this.#frames.length, seq: event.seq, kind: event.payload.kind,
      ...(event.actor === undefined ? {} : { actor: event.actor }),
      ...(event.warning === undefined ? {} : { warning: event.warning }),
      round: p.scheduler.round,
      ...(p.scheduler.phase === undefined ? {} : { phase: p.scheduler.phase }),
      ...(p.scheduler.activeTeamId === undefined ? {} : { activeTeamId: p.scheduler.activeTeamId }),
      line: lineOf(event, p, this.#ordinals),
      models: [...p.models.values()].map(modelState),
      units: [...p.units.values()].map(unitState),
      // Setup markers not moved in play, then the moved ones' projection entries.
      markers: [...p.board.table.markers.filter((m) => !p.markers.has(m.id)), ...p.markers.values()].map((m) => ({
        id: m.id, kind: m.kind, diameter: m.diameter, ...(m.label === undefined ? {} : { label: m.label }),
        x: m.position.x, y: m.position.y, z: m.position.z, yaw: m.yaw.degrees,
        ...(p.control.has(m.id) ? { controlledBy: p.control.get(m.id)! } : {}),
      })),
      openAsks: [...p.openAsks.values()].map(askOf),
      rolls: event.payload.kind === 'step_completed' ? event.payload.rolls.map(rollOf) : [],
      // Copied, not aliased: every frame is kept for the whole run and the applier keeps mutating both.
      scores: Object.fromEntries(p.scores),
      passed: [...p.scheduler.passed],
    };
    this.#frames.push(frame);
    return frame;
  }
}
