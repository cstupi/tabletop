import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import { Names } from '../src/lib/play/names';
import { describeSelection, selectedModels } from '../src/lib/play/selectionView';
import type { EventPayload, TerrainPlacement } from '../src/lib/protocol/wire.generated';
import { BoardSample, GameSample, Model, Unit } from './logSamples';
import { P1, at, ev, newId } from './play';

// SelectionViewTests
function projectionOf(...history: EventPayload[]) {
  const a = new Applier(BoardSample, GameSample);
  history.forEach((p, i) => a.apply(ev(i + 1, p, P1)));
  return a.projection;
}
const placed: EventPayload = { kind: 'models_moved', moves: [{ modelId: Model, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } }] };

describe('SelectionView', () => {
  it('a selected unit names itself and counts its models on the table', () => {
    const shelved = projectionOf(), onTable = projectionOf(placed);
    const sel = { pieceId: Unit, modelIds: [] };
    expect(describeSelection(new Names(GameSample, shelved), shelved, sel)).toBe('Battle Brothers: 0 of 1 models on the table');
    expect(describeSelection(new Names(GameSample, onTable), onTable, sel)).toBe('Battle Brothers: 1 of 1 models on the table');
  });

  it('a shift selected model names the model and nothing selected says nothing', () => {
    const p = projectionOf(placed), names = new Names(GameSample, p);
    expect(describeSelection(names, p, { pieceId: Unit, modelIds: [Model] })).toBe('Battle Brothers (Battle Brother)');
    expect(describeSelection(names, p, null)).toBeNull();
  });

  it('a selected terrain piece has no line: the terrain card names it', () => {
    const piece = {
      id: newId(), mesh: { builtin: 'forest' }, position: { x: 10, y: 10, z: 0 }, yaw: { degrees: 0 }, scale: 1, volumes: [],
    } as unknown as TerrainPlacement;
    const p = new Applier({ ...BoardSample, table: { ...BoardSample.table, terrain: [piece] } }, GameSample).projection;
    expect(describeSelection(new Names(GameSample, p), p, { pieceId: piece.id, modelIds: [] })).toBeNull();
  });

  it('a selection lights the listed models or else every model of the unit', () => {
    const p = projectionOf();
    expect(selectedModels(p, { pieceId: Unit, modelIds: [] })).toEqual([Model]);
    expect(selectedModels(p, { pieceId: Unit, modelIds: [Model] })).toEqual([Model]);
    expect(selectedModels(p, { pieceId: newId(), modelIds: [] })).toEqual([]);      // terrain or a marker: no models
    expect(selectedModels(p, null)).toEqual([]);
  });
});
