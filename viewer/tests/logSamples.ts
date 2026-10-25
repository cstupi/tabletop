// Tabletop.Protocol.Tests.Samples for the combat log tests: Battle Brothers with one Battle Brother carrying a Heavy Rifle, on team 1.
import { Applier } from '../src/lib/live/apply';
import { build } from '../src/lib/play/combatLog';
import { Names } from '../src/lib/play/names';
import type {
  AskAnswer, Board, Die, EventPayload, GameInfo, Grant, ModelRelocation, RollRecord, SequenceContext, StateChange, StepCompleted,
} from '../src/lib/protocol/wire.generated';
import { P1, TEAM1, TEAM2, at, boardOf, ev, gameOf, markerOf, newId, slotOf, unitOf, weaponOf } from './play';

export { P1, TEAM1, TEAM2 };

const rifleW = weaponOf('Heavy Rifle');
const slot = slotOf({ name: 'Battle Brother', weapons: [rifleW] });
const unit = unitOf('Battle Brothers', [slot]);

export const Enemy = '0198b7ac-2222-7000-8000-000000000002';
export const Unit = unit.id;
export const Model = slot.id;
export const Rifle = rifleW.id;
export const Frame = newId();
export const Roll = newId();
export const Token = markerOf('smoke');
export const GameSample: GameInfo = gameOf();
export const BoardSample: Board = boardOf([{ participantId: P1, units: [unit] }]);
export const UnitSample = unit;
export const SlotSample = slot;
export const grant: Grant = { id: newId(), effectId: newId(), entryName: 'Regeneration', casterUnitId: Unit, remainingUses: 1 };

export const deploy: ModelRelocation = { modelId: Model, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(12, 12) } };

export const Round1: EventPayload = { kind: 'round_started', round: 1, order: [TEAM1, TEAM2], changes: [] };

export const D = (raw: number, modified: number, success: boolean, ...triggered: string[]): Die =>
  ({ raw, modified, success, triggered: triggered.length === 0 ? undefined : triggered });

export function rollOf(roll: string, threshold: number | undefined, dice: Die[], over: Partial<RollRecord> = {}): RollRecord {
  return { id: newId(), roll, threshold, modifiers: [], dice, ...over };
}

/** Samples.Hit: a hit at 4+ with AP(1), a six and a two. */
export const hit = (): RollRecord => ({ id: Roll, roll: 'hit', threshold: 4, modifiers: [{ source: 'AP', amount: -1 }], dice: [D(6, 6, true), D(2, 2, false)] });
export const hitWith = (id: string): RollRecord => ({ ...hit(), id });

export const ctx = (actor: string | undefined, targets: string[] = [], weapons: string[] = [], action?: string): SequenceContext =>
  ({ actorUnitId: actor, targetUnitIds: targets, weaponIds: weapons, action });
export const started = (frameId: string, sequence: string, context: SequenceContext): EventPayload => ({ kind: 'sequence_started', frameId, sequence, context });
export const finished = (frameId: string): EventPayload => ({ kind: 'sequence_finished', frameId });
export const abandoned = (frameId: string): EventPayload => ({ kind: 'sequence_abandoned', frameId });
export const step = (
  frameId: string, name: string, rolls: RollRecord[] = [], changes: StateChange[] = [], answer?: AskAnswer, values?: Record<string, number>,
): StepCompleted => ({ kind: 'step_completed', frameId, step: name, answer, rolls, changes, values });
export const undone = (toSeq: number): EventPayload => ({ kind: 'undone', toSeq });
export const move = (...moves: ModelRelocation[]): EventPayload => ({ kind: 'models_moved', moves });
export const reroll = (index: number, from: Die, to: Die, frameId = Frame, rollId = Roll): EventPayload =>
  ({ kind: 'dice_rerolled', frameId, rollId, replaced: [{ index, old: from, new: to }] });
export const edit = (index: number, from: Die, to: Die, frameId = Frame, rollId = Roll): EventPayload =>
  ({ kind: 'dice_edited', frameId, rollId, replaced: [{ index, old: from, new: to }] });

export function buildOn(board: Board, game: GameInfo, payloads: readonly EventPayload[]) {
  const applier = new Applier(board, game);
  payloads.forEach((p, i) => applier.apply(ev(i + 1, p, P1)));
  const names = new Names(game, applier.projection);
  return { entries: build(applier.log, names), log: applier.log, names, applier };
}

export const buildSample = (...payloads: EventPayload[]) => buildOn(BoardSample, GameSample, payloads);
