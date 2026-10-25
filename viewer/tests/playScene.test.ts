import { describe, expect, it } from 'vitest';
import { sceneOfPlay } from '../src/lib/play/scene';
import type { EventPayload } from '../src/lib/protocol/wire.generated';
import { P1, TEAM2, at, boardOf, emptyTable, markerOf, projectionOf, sample, slotOf, unitOf } from './play';

const zones = () => [];
const name = () => 'terrain';
const s = sample();
const moved = (modelId: string, to: unknown): EventPayload => ({ kind: 'models_moved', moves: [{ modelId, from: { kind: 'shelf' }, to }] } as EventPayload);
const onTable = (x: number, y: number, degrees = 0) => ({ kind: 'table', placement: { position: { x, y, z: 0 }, yaw: { degrees } } });

describe('sceneOfPlay', () => {
  it('models on the table draw at their placement, a ghost overrides it', () => {
    const a = projectionOf(s.game, s.board, [moved(s.model.id, onTable(10, 12, 90)), moved(s.rival.id, onTable(30, 20))]);
    const scene = sceneOfPlay(a.projection, s.game, zones, name);
    expect(scene.models.map((m) => [m.id, m.teamOrdinal, m.centre])).toEqual([
      [s.model.id, 1, { x: 10, y: 12 }], [s.rival.id, 2, { x: 30, y: 20 }]]);
    expect(scene.models[0]!.unitName).toBe('Battle Brothers');
    expect(scene.models[0]!.circle).toEqual({ kind: 'circle', centre: { x: 10, y: 12 }, radius: 0.5 });
    const ghosted = sceneOfPlay(a.projection, s.game, zones, name, new Map([[s.model.id, at(40, 5)]]));
    expect(ghosted.models.find((m) => m.id === s.model.id)!.centre).toEqual({ x: 40, y: 5 });
    expect(ghosted.models.find((m) => m.id === s.rival.id)!.centre).toEqual({ x: 30, y: 20 });
  });

  it('shelf and inside models are off the table', () => {
    const transport = unitOf('Transport', [slotOf({ name: 'Hull' })]);
    const board = boardOf([{ participantId: P1, units: [s.squad, transport] }]);
    const a = projectionOf(s.game, board, [moved(s.model.id, { kind: 'inside', containerId: transport.models[0]!.id })]);
    const scene = sceneOfPlay(a.projection, s.game, zones, name);
    expect(scene.models).toEqual([]);
    expect(scene.offTable).toEqual([
      { id: s.model.id, label: 'Battle Brother', teamOrdinal: 1, where: 'inside', containerLabel: 'Hull' },
      { id: transport.models[0]!.id, label: 'Hull', teamOrdinal: 1, where: 'shelf' }]);
    const ghost = sceneOfPlay(a.projection, s.game, zones, name, new Map([[transport.models[0]!.id, at(5, 5)]]));
    expect(ghost.models.map((m) => m.id)).toEqual([transport.models[0]!.id]);
  });

  it('a controlled objective takes its team\'s ordinal', () => {
    const objective = markerOf('A', 'objective');
    const board = boardOf([{ participantId: P1, units: [s.squad] }], { ...emptyTable(), markers: [objective] });
    const placed = markerOf('smoke');
    const a = projectionOf(s.game, board, [{ kind: 'marker_placed', marker: placed }, { kind: 'objective_control_set', markerId: objective.id, teamId: TEAM2 }]);
    const scene = sceneOfPlay(a.projection, s.game, zones, name);
    expect(scene.markers.map((m) => [m.id, m.label, m.teamOrdinal])).toEqual([[objective.id, 'A', 2], [placed.id, 'smoke', undefined]]);
    const neutral = sceneOfPlay(projectionOf(s.game, board).projection, s.game, zones, name);
    expect(neutral.markers[0]!.teamOrdinal).toBeUndefined();
  });

  it('a moved setup marker is drawn once, at its new spot', () => {
    const objective = markerOf('A', 'objective');
    const board = boardOf([{ participantId: P1, units: [] }], { ...emptyTable(), markers: [objective] });
    const a = projectionOf(s.game, board, [{ kind: 'marker_moved', markerId: objective.id, position: { x: 9, y: 8, z: 0 }, yaw: { degrees: 0 } }]);
    const scene = sceneOfPlay(a.projection, s.game, zones, name);
    expect(scene.markers).toHaveLength(1);
    expect(scene.markers[0]!.centre).toEqual({ x: 9, y: 8 });
  });
});
