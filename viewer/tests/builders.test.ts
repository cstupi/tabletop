// Task 10 builders beside their siblings' tests: one assertion per function, the exact object the component sent before.
import { describe, expect, it } from 'vitest';
import { handMoveCommand, handMoves } from '../src/lib/play/handMove';
import { answerOf, type GameOverModel } from '../src/lib/play/gameOverView';
import type { Placement, TableState, Volume } from '../src/lib/protocol/wire.generated';
import { dropEdit } from '../src/lib/setup/drag';
import {
  addAsset, addBuiltin, removeMarker, removeTerrain, resetVolumes, scale, swapAsset, swapBuiltin, swapZones, tableSize, texture, zones,
} from '../src/lib/setup/edits';

const ID = '019a0000-0000-7000-8000-000000000001';
const volume: Volume = { kind: 'solid', footprint: { kind: 'rect', a: 6, b: 6 }, offset: { x: 0, y: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 3, declarations: 'cover', } as unknown as Volume;
const ruin = { id: 'ruin', label: 'Ruin', volume };
const at = (x: number): Placement => ({ position: { x, y: 0, z: 0 }, yaw: { degrees: 0 } });

describe('setup edit builders', () => {
  it('a built-in piece lands beside the table at yaw 0', () => {
    const draws = [0.5, 0.25];
    const e = addBuiltin(ruin, { width: 72, length: 48 }, () => draws.shift()!);
    expect(e.placement.position).toEqual({ x: 84, y: 12, z: 0 });
    expect(e.placement.yaw).toEqual({ degrees: 0 });
    expect(e.placement.mesh).toEqual({ builtin: 'ruin' });
  });
  it('an asset piece takes its volumes under new ids at scale 1', () => {
    const e = addAsset(ID, [volume, volume], { width: 72, length: 48 }, () => 0);
    expect(e.placement.mesh).toEqual({ assetId: ID });
    expect(e.placement.scale).toBe(1);
    expect(e.placement.position).toEqual({ x: 76, y: 4, z: 0 });
    expect(new Set(e.placement.volumes.map((v) => v.id)).size).toBe(2);
  });
  it('the toolbar and card controls', () => {
    expect(tableSize(60, 44)).toEqual({ kind: 'set_table_size', size: { width: 60, length: 44 } });
    expect(texture('desert')).toEqual({ kind: 'set_texture', texture: { builtin: 'desert' } });
    expect(zones('spearhead')).toEqual({ kind: 'set_zone_style', style: 'spearhead', shapes: ['spearhead/1', 'spearhead/2'] });
    expect(swapZones(1, 2)).toEqual({ kind: 'swap_zones', teamOrdinalA: 1, teamOrdinalB: 2 });
    expect(scale(ID, 1.5)).toEqual({ kind: 'scale_terrain', placementId: ID, scale: 1.5 });
    expect(swapBuiltin(ID, 'ruin')).toEqual({ kind: 'swap_terrain_mesh', placementId: ID, mesh: { builtin: 'ruin' } });
    expect(swapAsset(ID, 'a')).toEqual({ kind: 'swap_terrain_mesh', placementId: ID, mesh: { assetId: 'a' } });
    expect(removeTerrain(ID)).toEqual({ kind: 'remove_terrain', placementId: ID });
    expect(removeMarker(ID)).toEqual({ kind: 'remove_setup_marker', markerId: ID });
  });
  it('reset volumes takes each volume under a new id, in order', () => {
    const e = resetVolumes(ID, [volume, { ...volume, top: 5 }]);
    expect(e.volumes.map((v) => v.volume.top)).toEqual([3, 5]);
    expect(e.volumes[0]!.id).not.toBe(e.volumes[1]!.id);
  });
});

describe('drop and answer builders', () => {
  it('a hand move goes from each grab to its landing', () => {
    const grabs = new Map([['m1', at(1)]]), landings = new Map([['m1', at(2)]]);
    expect(handMoveCommand(grabs, landings)).toEqual({ kind: 'move_models', moves: handMoves(grabs, landings) });
  });
  it('a dropped draft piece is move_terrain, a marker move_setup_marker, anything else null', () => {
    const draft = { terrain: [{ id: 'T' }], markers: [{ id: 'K' }] } as unknown as TableState;
    const p = { x: 1, y: 2, z: 0 }, yaw = { degrees: 30 };
    expect(dropEdit(draft, 'T', p, yaw)).toEqual({ kind: 'move_terrain', placementId: 'T', position: p, yaw });
    expect(dropEdit(draft, 'K', p, yaw)).toEqual({ kind: 'move_setup_marker', markerId: 'K', position: p, yaw });
    expect(dropEdit(draft, 'X', p, yaw)).toBeNull();
  });
  it('a game over answer names its team, or none', () => {
    const model = { stage: 'choose', askId: ID, result: null, reason: '', choices: [], note: null } as GameOverModel;
    expect(answerOf(model, 'end', 'T1')).toEqual({ kind: 'answer_ask', askId: ID, answer: { kind: 'chosen', optionId: 'end' }, teamId: 'T1' });
    expect(answerOf(model, 'end', null)).toEqual({ kind: 'answer_ask', askId: ID, answer: { kind: 'chosen', optionId: 'end' } });
  });
});
