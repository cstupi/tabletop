import { describe, expect, it } from 'vitest';
import type { Abutment, Geometry, HeldEnd, HeldModel, PlacedModel, Placement } from '../src/lib/geometry';
import { Applier } from '../src/lib/live/apply';
import { ModelDrag, abutmentOf, clampToReach, grabs, stationary } from '../src/lib/play/modelDrag';
import type { MoveRegion, TerrainPlacement } from '../src/lib/protocol/wire.generated';
import { P1, P2, boardOf, emptyTable, gameOf, slotOf, unitOf } from './play';

const at = (x: number, y: number, yaw = 0): Placement => ({ position: { x, y, z: 0 }, yaw: { degrees: yaw } });

interface Land { held: readonly HeldModel[]; stationary: readonly PlacedModel[]; walls: readonly string[]; abut?: Abutment }
function fake(lift = 0.5) {
  const lands: Land[] = [];
  const rests: PlacedModel[] = [];
  const g = {
    landModels: (held: readonly HeldModel[], stat: readonly PlacedModel[], walls: readonly string[], _terrain: unknown, abut?: Abutment): HeldEnd[] => {
      lands.push({ held, stationary: stat, walls, ...(abut === undefined ? {} : { abut }) });
      return held.map((h) => ({ id: h.id, drawn: { x: h.to.x, y: h.to.y, z: lift }, lands: { x: h.to.x, y: h.to.y, z: 0 } }));
    },
    restModel: (m: PlacedModel) => { rests.push(m); return 3; },
    edgeToEdge: () => 1,
  } as unknown as Geometry;
  return { g, lands, rests };
}

const wall = (id: string, declarations: string): TerrainPlacement => ({
  id: `p-${id}`, mesh: { builtin: 'b' }, position: { x: 0, y: 0, z: 0 }, yaw: { degrees: 0 }, scale: 1,
  volumes: [{ id, volume: { kind: 'solid', footprint: { kind: 'rect', a: 1, b: 1 }, offset: { x: 0, y: 0, length: 0 }, yaw: { degrees: 0 }, bottom: 0, top: 2, declarations } }],
} as unknown as TerrainPlacement);

/** Two squad models, one more on the shelf, a rival on the table. */
function rig(terrain: TerrainPlacement[] = []) {
  const squad = unitOf('Squad', [1, 2, 3].map((i) => slotOf({ name: `S${i}`, ordinal: i })));
  const rivals = unitOf('Rivals', [slotOf({ name: 'Rival' })]);
  const board = boardOf([{ participantId: P1, units: [squad] }, { participantId: P2, units: [rivals] }], { ...emptyTable(), terrain });
  const applier = new Applier(board, gameOf());
  const [a, b, c] = squad.models.map((m) => m.id) as [string, string, string];
  const rival = rivals.models[0]!.id;
  const on = (id: string, to: Placement) => ({ modelId: id, from: { kind: 'shelf' } as const, to: { kind: 'table', placement: to } as const });
  applier.apply({ seq: 1, actor: P1, payload: { kind: 'models_moved', moves: [on(a, at(10, 10)), on(b, at(12, 10)), on(rival, at(40, 30))] } } as never);
  return { applier, p: applier.projection, a, b, c, rival };
}
const none = new Map<string, Placement>();

describe('ModelDrag', () => {
  it('stationary keeps every unheld model on the table and a ghost where it is drawn', () => {
    const r = rig();
    const ghost = at(20, 20, 90);
    const out = stationary(r.p, new Set([r.a]), new Map([[r.c, ghost]]));
    expect(out.map((m) => m.placement)).toEqual([at(12, 10), ghost, at(40, 30)]);   // b, the ghost of c, the rival; a is held
    expect(stationary(r.p, new Set([r.a, r.b])).length).toBe(1 + 0 + 0);            // c is on the shelf with no ghost
  });

  it('grabs take a proposed ghost where it is drawn', () => {
    const drawn = at(5, 5), home = at(1, 1);
    const g = grabs(['m1', 'm2', 'm3'], new Set(['m1', 'm3']), (id) => (id === 'm1' ? drawn : null), () => home);
    expect(g.get('m1')).toBe(drawn);
    expect(g.get('m2')).toBe(home);                  // not proposing: the projection's
    expect(g.get('m3')).toBe(home);                  // proposing with no ghost drawn
  });

  it('the picks given, else the unit; only models the board draws', () => {
    const r = rig();
    const { g } = fake();
    const drag = new ModelDrag(g);
    expect(drag.start(r.p, r.b, { x: 12, y: 10 }, null, new Set(), none).ids).toEqual([r.a, r.b]);   // c is shelved: not drawn
    expect(drag.start(r.p, r.b, { x: 12, y: 10 }, [r.b], new Set(), none).ids).toEqual([r.b]);
    expect(drag.start(r.p, r.b, { x: 12, y: 10 }, null, new Set([r.c]), new Map([[r.c, at(30, 30)]])).ids).toEqual([r.a, r.b, r.c]);
  });

  it('move sends held models in drag order with their grab offsets', () => {
    const r = rig();
    const { g, lands } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, null, new Set(), none);
    const held = drag.move(r.p, { x: 15, y: 13 }, false, null, none)!;
    expect(lands[0]!.held.map((h) => [h.id, h.to])).toEqual([[r.a, { x: 15, y: 13 }], [r.b, { x: 17, y: 13 }]]);
    expect(lands[0]!.held[0]!.at.placement.position).toEqual({ x: 10, y: 10, z: 0 });
    expect(lands[0]!.stationary.length).toBe(1);                                    // the rival
    expect(held.ends.get(r.a)).toMatchObject({ drawn: { z: 0.5 }, lands: { x: 15, y: 13, z: 0 } });
    expect(held.origins.get(r.a)).toEqual(at(10, 10));
  });

  it('walls are the Ask\'s impassable ids inside a move, else every Impassable volume', () => {
    const r = rig([wall('v1', 'impassable'), wall('v2', 'cover'), wall('v3', 'impassable')]);
    const { g, lands } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, [r.a], new Set(), none);
    drag.move(r.p, { x: 11, y: 10 }, false, null, none);
    expect(lands[0]!.walls).toEqual(['v1', 'v3']);
    const region: MoveRegion = { kind: 'move_region', modelIds: [r.a], allowance: 6, impassableVolumeIds: ['v2'], difficultVolumeIds: [], stoppable: false, passesModels: false, ignoresTerrain: false };
    drag.move(r.p, { x: 12, y: 10 }, false, region, none);
    expect(lands[1]!.walls).toEqual(['v2']);
  });

  it('clampToReach holds a target to the allowance on the straight line from the origin', () => {
    expect(clampToReach({ x: 0, y: 0 }, { x: 3, y: 4 }, 6)).toEqual({ x: 3, y: 4 });
    expect(clampToReach({ x: 0, y: 0 }, { x: 6, y: 8 }, 5)).toEqual({ x: 3, y: 4 });
    expect(clampToReach({ x: 1, y: 1 }, { x: 9, y: 1 }, 0)).toEqual({ x: 1, y: 1 });
  });

  it('inside a move each held model lands at most the allowance from its origin; Alt, a hand move and a disembark are not held', () => {
    const r = rig();
    const { g, lands } = fake();
    const drag = new ModelDrag(g);
    const region: MoveRegion = { kind: 'move_region', modelIds: [r.a, r.b], allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false, ignoresTerrain: false };
    drag.start(r.p, r.a, { x: 10, y: 10 }, null, new Set([r.a, r.b]), none);
    const held = drag.move(r.p, { x: 20, y: 10 }, false, region, none)!;          // 10" to the right
    expect(lands[0]!.held.map((h) => [h.id, h.to])).toEqual([[r.a, { x: 16, y: 10 }], [r.b, { x: 18, y: 10 }]]);
    expect(held.ends.get(r.a)!.lands).toEqual({ x: 16, y: 10, z: 0 });
    drag.move(r.p, { x: 20, y: 10 }, false, null, none);                             // a hand move
    expect(lands[1]!.held[0]!.to).toEqual({ x: 20, y: 10 });
    drag.move(r.p, { x: 20, y: 10 }, false, { ...region, containment: { enterable: [], exitingId: r.rival, exitWithin: 6 } }, none);
    expect(lands[2]!.held[0]!.to).toEqual({ x: 20, y: 10 });
    expect(drag.move(r.p, { x: 20, y: 10 }, true, region, none)!.ends.get(r.a)!.lands).toMatchObject({ x: 20, y: 10 });
  });

  it('abutmentOf is a charge Ask target models where they stand and its grace; nothing for any other move or a disembark', () => {
    const { p, a, rival } = rig();
    const charge: MoveRegion = { kind: 'move_region', modelIds: [a], allowance: 12, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false,
      ignoresTerrain: false, contactWith: [rival], contactGrace: 0.5 };
    expect(abutmentOf(p, charge)).toEqual({ models: [{ body: { base: { shape: 'circle', a: 1, b: 1 }, height: 1 }, placement: at(40, 30) }], within: 0.5 });
    const { contactWith: _w, contactGrace: _g, ...advance } = charge;
    expect(abutmentOf(p, { ...charge, contactVertical: 3 })?.vertical).toBe(3);
    expect(abutmentOf(p, advance)).toBeUndefined();
    expect(abutmentOf(p, { ...charge, containment: { enterable: [], exitingId: rival, exitWithin: 6 } })).toBeUndefined();
    expect(abutmentOf(p, null)).toBeUndefined();
  });

  it('a charge drag lands with the pull, a hand move without', () => {
    const { g, lands } = fake();
    const { p, a, rival } = rig();
    const charge: MoveRegion = { kind: 'move_region', modelIds: [a], allowance: 40, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false,
      ignoresTerrain: false, contactWith: [rival], contactGrace: 0.5 };
    const drag = new ModelDrag(g);
    drag.start(p, a, { x: 10, y: 10 }, [a], new Set([a]), new Map());
    drag.move(p, { x: 38.7, y: 30 }, false, charge, new Map());
    expect(lands.at(-1)!.abut?.within).toBe(0.5);
    drag.cancel();
    drag.start(p, a, { x: 10, y: 10 }, [a], new Set(), new Map());
    drag.move(p, { x: 38.7, y: 30 }, false, null, new Map());
    expect(lands.at(-1)!.abut).toBeUndefined();
  });

  it('alt places straight at the cursor resting on the highest surface', () => {
    const r = rig();
    const { g, lands, rests } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, [r.a], new Set(), none);
    const held = drag.move(r.p, { x: 20, y: 22 }, true, null, none)!;
    expect(lands).toHaveLength(0);
    expect(rests[0]!.placement.position).toEqual({ x: 20, y: 22, z: 0 });
    expect(held.ends.get(r.a)).toEqual({ id: r.a, drawn: { x: 20, y: 22, z: 3 }, lands: { x: 20, y: 22, z: 3 } });
  });

  it('turn adds 15 degrees to each held model, 30 with shift, and re-lands', () => {
    const r = rig();
    const { g, lands } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, null, new Set(), none);
    drag.move(r.p, { x: 20, y: 20 }, false, null, none);
    let held = drag.turn(r.p, 15, null, none)!;
    expect(held.yaws.get(r.a)!.degrees).toBe(15);
    expect(held.yaws.get(r.b)!.degrees).toBe(15);
    held = drag.turn(r.p, -30, null, none)!;
    expect(held.yaws.get(r.a)!.degrees).toBe(345);
    expect(lands.at(-1)!.held[0]!.at.placement.yaw.degrees).toBe(345);
    expect(lands.at(-1)!.held[0]!.to).toEqual({ x: 20, y: 20 });                   // at the same landing
  });

  it('arrange lays out rows about the cursor', () => {
    const r = rig();
    const { g, lands } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, null, new Set(), none);
    drag.move(r.p, { x: 30, y: 20 }, false, null, none);
    const held = drag.arrange(r.p, 2, null, none)!;
    expect(held.rows).toBe(2);
    const to = lands.at(-1)!.held.map((h) => h.to);
    expect(to).toHaveLength(2);
    expect(to[0]!.y).not.toBe(to[1]!.y);                                           // two rows of one
    expect((to[0]!.x + to[1]!.x) / 2).toBeCloseTo(30, 6);
    const turned = drag.turn(r.p, 90, null, none)!;
    expect(turned.yaws.get(r.a)!.degrees).toBe(90);
    expect(turned.yaws.get(r.b)!.degrees).toBe(90);
  });

  it('a still drop ends with null', () => {
    const r = rig();
    const { g } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, null, new Set(), none);
    drag.move(r.p, { x: 10, y: 10 }, false, null, none);
    expect(drag.end()).toBeNull();
    expect(drag.held).toBeNull();
    drag.start(r.p, r.a, { x: 10, y: 10 }, [r.a], new Set(), none);
    drag.move(r.p, { x: 14, y: 10 }, false, null, none);
    expect([...drag.end()!]).toEqual([[r.a, at(14, 10)]]);
  });

  it('the drop lands against the projection current at the drop', () => {
    const r = rig();
    const { g, lands } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, [r.a], new Set(), none);
    drag.move(r.p, { x: 14, y: 10 }, false, null, none);
    expect(lands.at(-1)!.stationary.map((m) => m.placement)).toContainEqual(at(40, 30));
    r.p.models.get(r.rival)!.location = { kind: 'table', placement: at(15, 10) };    // another player's frame moves a model
    drag.move(r.p, { x: 14, y: 10 }, false, null, none);
    expect(lands.at(-1)!.stationary.map((m) => m.placement)).toContainEqual(at(15, 10));
    expect(lands.at(-1)!.stationary.map((m) => m.placement)).not.toContainEqual(at(40, 30));
  });

  it('a held model that leaves the projection cancels the drag', () => {
    const r = rig();
    const { g } = fake();
    const drag = new ModelDrag(g);
    drag.start(r.p, r.a, { x: 10, y: 10 }, [r.a], new Set(), none);
    r.p.models.get(r.a)!.location = { kind: 'shelf' };
    expect(drag.move(r.p, { x: 14, y: 10 }, false, null, none)).toBeNull();
    expect(drag.held).toBeNull();
    drag.start(r.p, r.a, { x: 10, y: 10 }, [r.a], new Set([r.a]), new Map([[r.a, at(1, 1)]]));
    expect(drag.move(r.p, { x: 14, y: 10 }, false, null, new Map([[r.a, at(1, 1)]]))).not.toBeNull();   // a proposal may be off the table
    r.p.models.delete(r.a);
    expect(drag.move(r.p, { x: 15, y: 10 }, false, null, none)).toBeNull();
  });
});
