// Builders the play tests share, after tests/Tabletop.Client.Tests/TableUx.cs and tests/Tabletop.Protocol.Tests/Samples.cs.
import { Applier } from '../src/lib/live/apply';
import type {
  Ask, AskOption, Board, EventPayload, GameEvent, GameInfo, LegalSet, Marker, ModelSlot, Participant, Placement, TableState, Unit, Weapon,
} from '../src/lib/protocol/wire.generated';

let counter = 0;
/** A fresh UUID-shaped id; ids sort in creation order, as UUIDv7 does. */
export function newId(): string {
  counter++;
  return `0198b7ac-${counter.toString(16).padStart(4, '0')}-7000-8000-${counter.toString(16).padStart(12, '0')}`;
}

export const TEAM1 = newId();
export const TEAM2 = newId();
export const P1 = newId();
export const P2 = newId();
export const FRAME = newId();
export const ASK_ID = newId();
export const CONTAINER = newId();

export const at = (x: number, y: number): Placement => ({ position: { x, y, z: 0 }, yaw: { degrees: 0 } });

export function weaponOf(name: string): Weapon { return { id: newId(), name, stats: {}, properties: [] }; }

export function slotOf(over: Partial<ModelSlot> & { name: string }): ModelSlot {
  return {
    id: newId(), ordinal: 1, stats: {}, properties: [], base: { shape: 'circle', a: 1, b: 1 }, height: 1, weapons: [], ...over,
  };
}

export function unitOf(name: string, models: ModelSlot[] = [slotOf({ name: 'Battle Brother' })]): Unit {
  return { id: newId(), ordinal: 1, name, stats: {}, properties: [], models };
}

/** Two participants on teams 1 and 2 unless told; the first is Clay. */
export function gameOf(over: Partial<GameInfo> = {}, participants?: Participant[]): GameInfo {
  return {
    id: newId(), name: 'Friday', status: 'playing', createdBy: P1, gameRules: 'grimdark-future', propertiesHash: 'sha256:abc',
    schedulerShape: 'alternating-activation', enforcement: 'warn', waitsBetweenSteps: false,
    teams: [{ id: TEAM1, ordinal: 1 }, { id: TEAM2, ordinal: 2 }],
    participants: participants ?? [
      { id: P1, accountId: newId(), name: 'Clay', teamId: TEAM1, armyId: newId(), armyName: 'Vanguard' },
      { id: P2, accountId: newId(), name: 'Bo', teamId: TEAM2, armyId: newId(), armyName: 'Rivals' },
    ],
    optionalRules: {}, ...over,
  };
}

export const emptyTable = (): TableState => ({
  id: newId(), name: 'Table', size: { width: 72, length: 48 }, texture: { builtin: 'grass' }, terrain: [], markers: [], zones: [],
} as unknown as TableState);

export function boardOf(armies: ReadonlyArray<{ participantId: string; units: Unit[] }>, table: TableState = emptyTable()): Board {
  return {
    table,
    armies: armies.map((a) => ({ participantId: a.participantId, snapshot: { id: newId(), name: 'Army', gameSystem: 'grimdark-future', units: a.units } })),
  };
}

export const ev = (seq: number, payload: EventPayload, actor?: string): GameEvent => ({ seq, actor, payload } as GameEvent);

/** An Applier over the board with the payloads applied as events 1..n, actor P1. */
export function projectionOf(game: GameInfo, board: Board, payloads: readonly EventPayload[] = []): Applier {
  const applier = new Applier(board, game);
  payloads.forEach((p, i) => applier.apply(ev(i + 1, p, P1)));
  return applier;
}

export function askOf(
  legal: LegalSet,
  over: { question?: string; teams?: string[]; allowPass?: boolean; windowKind?: string; frameId?: string; sealed?: boolean; prompt?: string; id?: string } = {},
): Ask {
  return {
    id: over.id ?? newId(), question: over.question ?? 'activate', prompt: over.prompt ?? 'Activate a unit', teams: over.teams ?? [TEAM1], legal,
    allowPass: over.allowPass ?? true, sealed: over.sealed ?? false, windowKind: over.windowKind, frameId: over.frameId,
  };
}

export const option = (id: string, label: string, pieces: string[] = [], note?: string): AskOption => ({ id, label, pieces, note });

export const markerOf = (label: string | undefined, kind: Marker['kind'] = 'token'): Marker => ({
  id: newId(), kind, position: { x: 1, y: 1, z: 0 }, yaw: { degrees: 0 }, diameter: 1, label, visual: { builtin: 'chip' },
});

/** The standard sample: Clay (team 1) has Battle Brothers with one model; Bo (team 2) has Rivals with one. */
export function sample() {
  const squad = unitOf('Battle Brothers');
  const rivals = unitOf('Rivals', [slotOf({ name: 'Rival' })]);
  const game = gameOf();
  const board = boardOf([{ participantId: P1, units: [squad] }, { participantId: P2, units: [rivals] }]);
  return { game, board, squad, model: squad.models[0]!, rivals, rival: rivals.models[0]! };
}
