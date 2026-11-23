// MoveWorkers over workers that answer later: each worker its own facade (the WebAssembly Geometry, called when the test lets a message through),
// so the areas, frames and their order are what the page's Web Workers give, and every answer is the inline work's.
import { beforeAll, describe, expect, it } from 'vitest';
import type { Geometry, MoveBands, XY } from '../src/lib/geometry';
import { inlineMoveWork } from '../src/lib/play/moveWork';
import { MoveWorkers, areaWorkers, type FacadeWorker } from '../src/lib/play/moveWorkers';
import type { Placement } from '../src/lib/protocol/wire.generated';
import { loadTestGeometry } from './geometry';
import { at, moveRig, region } from './moveRig';

let g: Geometry;
beforeAll(async () => { g = await loadTestGeometry(); });

/** Workers whose messages wait in one queue until `run` lets them through, each answered by the WebAssembly facade in the order sent. The facade is
 *  one runtime here, so each worker's kept Asks are keyed apart by the worker's index. */
function workers() {
  const queue: { worker: number; method: string; json: string; resolve: (s: string) => void; reject: (e: Error) => void }[] = [];
  const calls: { worker: number; method: string; request: Record<string, unknown> }[] = [];
  let made = 0;
  const make = (): FacadeWorker => {
    const worker = made++;
    return {
      call(method, json) {
        const request = JSON.parse(json) as Record<string, unknown>;
        calls.push({ worker, method, request });
        const keyed = JSON.stringify({ ...request, key: `${worker}:${String(request.key)}` });
        return new Promise((resolve, reject) => { queue.push({ worker, method, json: keyed, resolve, reject }); });
      },
      terminate() {},
    };
  };
  const run = async (n = Infinity): Promise<void> => {
    for (let i = 0; i < n && queue.length > 0; i++) {
      const m = queue.shift()!;
      try { m.resolve(g.call(m.method, m.json)); } catch (e) { m.reject(e as Error); }
      for (let k = 0; k < 4; k++) await Promise.resolve();
    }
  };
  return { make, run, calls, queue };
}

const sorted = (b: MoveBands): string[] =>
  [b.clear, b.difficult, b.dangerous].map((band) => {
    const tris: string[] = [];
    for (let i = 0; i + 2 < band.length; i += 3) tris.push([band[i]!, band[i + 1]!, band[i + 2]!].map((p: XY) => `${p.x.toFixed(9)},${p.y.toFixed(9)}`).join(' '));
    return tris.sort().join('|');
  });

const ghostsOf = (entries: [string, Placement][]) => new Map(entries);

describe('MoveWorkers', () => {
  it('walks each model\'s area in shares the idle walkers take in turn, the lead first, and the area is the inline one', async () => {
    const r = moveRig({ squad: 3 });
    const legal = region({ modelIds: r.ids, allowance: 6 });
    const p = r.applier.projection;
    const w = workers();
    const work = new MoveWorkers(w.make, 2);
    let changed = 0;
    work.onChanged(() => { changed++; });
    work.open('ask', legal, p);
    expect(work.area('ask', r.ids[0]!)).toBeNull();                         // not walked yet: the board draws the allowance circle
    await w.run();
    const inline = inlineMoveWork(() => g);
    inline.open('ask', legal, p);
    for (const id of r.ids) expect(sorted(work.area('ask', id)!)).toEqual(sorted(inline.area('ask', id)!));
    expect(changed).toBe(3);
    const areas = w.calls.filter((c) => c.method === 'MoveAskArea');
    expect(areas.map((c) => [c.request.id, c.request.part, c.request.parts])).toEqual(r.ids.flatMap((id) => [0, 1, 2, 3, 4, 5, 6, 7].map((k) => [id, k, 8])));
    expect(new Set(areas.map((c) => c.worker))).toEqual(new Set([1, 2]));
  });

  it('walks a model a drag holds before the models not yet started', async () => {
    const r = moveRig({ squad: 4 });
    const w = workers();
    const work = new MoveWorkers(w.make, 1);
    work.open('ask', region({ modelIds: r.ids }), r.applier.projection);
    work.area('ask', r.ids[0]!);
    await w.run(4);                                                          // the runtimes started and the opens: the lead's first share in flight
    work.area('ask', r.ids[3]!);                                             // picked up
    await w.run();
    const order = w.calls.filter((c) => c.method === 'MoveAskArea').map((c) => c.request.id);
    expect(order.filter((id, i) => order.indexOf(id) === i)).toEqual([r.ids[0], r.ids[3], r.ids[1], r.ids[2]]);
  });

  it('a frame is answered later, the latest ghosts asked once the answer in flight lands, and its answer is the inline one', async () => {
    const r = moveRig({ squad: 2 });
    const [a, b] = r.ids as [string, string];
    const legal = region({ modelIds: r.ids, allowance: 6, separation: 1 });
    const p = r.applier.projection;
    const w = workers();
    const work = new MoveWorkers(w.make, 1);
    let changed = 0;
    work.onChanged(() => { changed++; });
    work.open('ask', legal, p);
    const still = ghostsOf([[a, at(10, 10)], [b, at(12, 10)]]);
    expect(work.frame('ask', still)).toMatchObject({ allowance: 6, routes: new Map(), contacts: new Map() });   // nothing moved: answered at once
    const first = ghostsOf([[a, at(10, 13)], [b, at(12, 10)]]);
    expect(work.frame('ask', first)).not.toMatchObject({ targets: new Map([[a, { x: 10, y: 13 }]]) });
    work.frame('ask', ghostsOf([[a, at(10, 14)], [b, at(12, 10)]]));
    const last = ghostsOf([[a, at(10, 15)], [b, at(13, 12)]]);
    work.frame('ask', last);
    await w.run();
    const frames = w.calls.filter((c) => c.method === 'MoveAskFrame').map((c) => Object.keys(c.request.proposed as object).length && (c.request.proposed as Record<string, Placement>)[a]!.position.y);
    expect(frames).toEqual([13, 15]);                                        // the one in flight, then the latest; the one between is dropped
    const answer = work.frame('ask', last)!;
    const inline = inlineMoveWork(() => g);
    inline.open('ask', legal, p);
    const expected = inline.frame('ask', last)!;
    expect(answer.allowance).toBe(expected.allowance);
    expect([...answer.routes.keys()]).toEqual([...expected.routes.keys()]);
    for (const [id, route] of expected.routes) expect(answer.routes.get(id)!.distance).toBeCloseTo(route.distance, 6);
    expect(answer.contacts).toEqual(expected.contacts);
    expect(changed).toBeGreaterThanOrEqual(2);
    expect(work.frame('ask', ghostsOf([[a, at(9, 9)], [b, at(12, 10)]]), false)).toBe(answer);   // a read asks nothing
    expect(w.queue).toHaveLength(0);
  });

  it('a closed Ask is dropped from every worker', async () => {
    const r = moveRig();
    const w = workers();
    const work = new MoveWorkers(w.make, 2);
    work.open('ask', region({ modelIds: r.ids }), r.applier.projection);
    await w.run();
    work.close('ask');
    await w.run();
    expect(w.calls.filter((c) => c.method === 'MoveAskClose' && c.request.key === 'ask').map((c) => c.worker)).toEqual([0, 1, 2]);
    expect(work.area('ask', r.ids[0]!)).toBeNull();
    expect(work.frame('ask', ghostsOf([[r.ids[0]!, at(11, 11)]]))).toBeNull();
  });

  it('starts every runtime with the page, before an Ask opens', () => {
    const w = workers();
    new MoveWorkers(w.make, 2).start();
    expect(w.calls.map((c) => [c.worker, c.method, c.request.key])).toEqual([[0, 'MoveAskClose', ''], [1, 'MoveAskClose', ''], [2, 'MoveAskClose', '']]);
  });

  it('walks with two to four workers', () => {
    expect([undefined, 1, 4, 6, 16].map(areaWorkers)).toEqual([2, 2, 2, 4, 4]);
  });
});
