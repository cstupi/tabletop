// viewer/src/lib/play/moveWorkers.ts
// The play page's MoveWork: each move Ask's reaches kept in Web Workers (geometry/moveWorker.ts, Tabletop.Geometry's MoveAsk), so no area build,
// path search or contact test runs on the tab's thread. One worker answers the drag frames (its reaches built as the Ask opens, the latest ghosts
// asked when the last answer lands, the rest dropped); the others walk the areas, each model's split in shares (MoveReach.Bands' coarse cells
// c ≡ k mod n, several a walker) that an idle walker takes the next of, the models a drag holds first, then the lead, then the rest of the unit.
// The runtimes start with the page (`start`), so an Ask's first answers do not wait on them.

import type { Contact, MoveBands, RouteFound, TerrainPlacement, XY } from '../geometry';
import type { Projection } from '../live/apply';
import type { MoveRegion, Placement } from '../protocol/wire.generated';
import { bodiesOf } from './contactCheck';
import { answers, movedOf, regionTerms, type MoveFrameAnswer, type MoveWork } from './moveWork';

/** A worker that answers facade calls by id. */
export interface FacadeWorker {
  call(method: string, json: string): Promise<string>;
  terminate(): void;
}

/** A module worker over geometry/moveWorker.ts. */
export function facadeWorker(base: string): FacadeWorker {
  const worker = new Worker(new URL('../geometry/moveWorker.ts', import.meta.url), { type: 'module' });
  const waiting = new Map<number, { resolve: (s: string) => void; reject: (e: Error) => void }>();
  let next = 0;
  worker.onmessage = (e: MessageEvent<{ id: number; result?: string; error?: string }>) => {
    const w = waiting.get(e.data.id);
    if (w === undefined) return;
    waiting.delete(e.data.id);
    if (e.data.error !== undefined) w.reject(new Error(e.data.error)); else w.resolve(e.data.result!);
  };
  worker.onerror = (e) => { for (const w of waiting.values()) w.reject(new Error(e.message)); waiting.clear(); };
  return {
    call(method, json) {
      const id = ++next;
      return new Promise((resolve, reject) => { waiting.set(id, { resolve, reject }); worker.postMessage({ id, base, method, json }); });
    },
    terminate() { worker.terminate(); for (const w of waiting.values()) w.reject(new Error('terminated')); waiting.clear(); },
  };
}

interface Flat { readonly clear: number[]; readonly difficult: number[]; readonly dangerous: number[] }
interface Found { readonly allowance: number; readonly routes: RouteFound[]; readonly contacts: Contact[] }

interface Area { readonly shares: (Flat | null)[]; readonly started: boolean[]; done: MoveBands | null }

/** Shares a walker: enough that the walkers finish a model together however its work lies across the table. */
const SHARES_PER_WALKER = 4;

interface Ask {
  readonly legal: MoveRegion;
  readonly p: Projection;
  readonly areas: Map<string, Area>;
  /** Area order: the models asked for first, in the order they were asked, then the Ask's own. */
  readonly wanted: string[];
  latest: MoveFrameAnswer | null;
  /** The ghosts of the frame in flight, and the latest ghosts still to ask. */
  asked: string | null;
  pending: { readonly memo: string; readonly ghosts: ReadonlyMap<string, Placement> } | null;
  failed: boolean;
}

const points = (flat: readonly number[]): XY[] => { const out: XY[] = []; for (let i = 0; i + 1 < flat.length; i += 2) out.push({ x: flat[i]!, y: flat[i + 1]! }); return out; };
const memoOf = (ghosts: ReadonlyMap<string, Placement>): string => JSON.stringify([...ghosts].map(([id, g]) => [id, g.position.x, g.position.y, g.position.z, g.yaw.degrees]));
const pieces = (terrain: readonly TerrainPlacement[]) => terrain.map((t) => ({ id: t.id, position: t.position, yaw: t.yaw, scale: t.scale, volumes: t.volumes }));

/** How many workers walk the areas: the machine's cores less the tab's and the frame worker's, two to four. */
export function areaWorkers(cores: number | undefined): number {
  return Math.max(2, Math.min(4, (cores ?? 4) - 2));
}

export class MoveWorkers implements MoveWork {
  readonly #make: () => FacadeWorker;
  readonly #count: number;
  #frames: FacadeWorker | null = null;
  #walkers: FacadeWorker[] = [];
  #busy: boolean[] = [];
  readonly #asks = new Map<string, Ask>();
  readonly #listeners = new Set<() => void>();

  /** `make` starts one worker; `count` workers walk the areas. Nothing starts until the first Ask opens. */
  constructor(make: () => FacadeWorker, count: number) { this.#make = make; this.#count = Math.max(1, count); }

  onChanged(listener: () => void): () => void { this.#listeners.add(listener); return () => { this.#listeners.delete(listener); }; }
  #changed(): void { for (const l of this.#listeners) l(); }

  /** Starts the workers' runtimes now (each loads on its first message). */
  start(): void {
    if (this.#frames !== null) return;
    this.#frames = this.#make();
    this.#walkers = Array.from({ length: this.#count }, () => this.#make());
    this.#busy = this.#walkers.map(() => false);
    const none = JSON.stringify({ key: '' });
    for (const w of [this.#frames, ...this.#walkers]) void w.call('MoveAskClose', none).catch(() => {});
  }

  open(key: string, legal: MoveRegion, p: Projection): void {
    if (this.#asks.get(key)?.legal === legal) return;
    this.start();
    const ask: Ask = { legal, p, areas: new Map(), wanted: [...legal.modelIds], latest: null, asked: null, pending: null, failed: false };
    this.#asks.set(key, ask);
    const onTable = legal.modelIds.flatMap((id) => {
      const m = p.models.get(id);
      return m === undefined || m.location.kind !== 'table' ? [] : [{ id, start: { body: { base: m.slot.base, height: m.slot.height }, placement: m.location.placement } }];
    });
    const request = {
      key, models: onTable, ...regionTerms(legal, p), terrain: pieces(p.board.table.terrain), bodies: bodiesOf(p),
      ...(legal.separation === undefined ? {} : { separation: legal.separation }), passesModels: legal.passesModels, landing: !legal.ignoresTerrain,
      ...(legal.minDistance === undefined ? {} : { minDistance: legal.minDistance }),
    };
    const fail = (e: unknown): void => { ask.failed = true; console.error('move area worker', e); };
    void this.#frames!.call('MoveAskOpen', JSON.stringify({ ...request, prepare: true })).catch(fail);
    for (const w of this.#walkers) void w.call('MoveAskOpen', JSON.stringify(request)).catch(fail);
    for (const m of onTable) this.#areaOf(ask, m.id);
    this.#pump();
  }

  close(key: string): void {
    if (!this.#asks.delete(key) || this.#frames === null) return;
    const json = JSON.stringify({ key });
    for (const w of [this.#frames, ...this.#walkers]) void w.call('MoveAskClose', json).catch(() => {});
  }

  #areaOf(ask: Ask, id: string): Area {
    let area = ask.areas.get(id);
    if (area === undefined) {
      const parts = this.#walkers.length * SHARES_PER_WALKER;
      area = { shares: Array.from({ length: parts }, () => null), started: Array.from({ length: parts }, () => false), done: null };
      ask.areas.set(id, area);
    }
    return area;
  }

  area(key: string, id: string): MoveBands | null {
    const ask = this.#asks.get(key);
    if (ask === undefined || !ask.areas.has(id)) return null;
    const area = ask.areas.get(id)!;
    if (area.done === null) {                                              // asked for: walked before the models not yet asked for
      const at = ask.wanted.indexOf(id), asked = ask.wanted.findIndex((w) => ask.areas.get(w)?.started.some((s) => s) !== true);
      if (asked >= 0 && at > asked) { ask.wanted.splice(at, 1); ask.wanted.splice(asked, 0, id); }
    }
    return area.done;
  }

  /** Each idle walker takes the next share not yet taken of the first model in the Ask's order with one, the newest Ask first. */
  #pump(): void {
    const asks = [...this.#asks].reverse();
    this.#walkers.forEach((w, k) => {
      if (this.#busy[k]) return;
      for (const [key, ask] of asks) {
        if (ask.failed) continue;
        const id = ask.wanted.find((m) => ask.areas.get(m)?.started.includes(false) === true);
        if (id === undefined) continue;
        const area = ask.areas.get(id)!;
        const part = area.started.indexOf(false);
        area.started[part] = true;
        this.#busy[k] = true;
        w.call('MoveAskArea', JSON.stringify({ key, id, part, parts: area.shares.length })).then((json) => {
          area.shares[part] = JSON.parse(json) as Flat;
          if (area.shares.every((s) => s !== null)) {
            const all = area.shares as Flat[];
            area.done = { clear: points(all.flatMap((s) => s.clear)), difficult: points(all.flatMap((s) => s.difficult)), dangerous: points(all.flatMap((s) => s.dangerous)) };
            if (this.#asks.get(key) === ask) this.#changed();
          }
        }, (e) => { ask.failed = true; console.error('move area worker', e); }).finally(() => { this.#busy[k] = false; this.#pump(); });
        return;
      }
    });
  }

  frame(key: string, ghosts: ReadonlyMap<string, Placement>, ask = true): MoveFrameAnswer | null {
    const kept = this.#asks.get(key);
    if (kept === undefined) return null;
    if (!ask || answers(kept.latest, ghosts)) return kept.latest;
    if (movedOf(kept.p, ghosts).length === 0) {                            // every ghost where it stands: nothing moved, nothing to ask
      kept.latest = { allowance: Math.max(0, kept.legal.allowance), routes: new Map(), contacts: new Map(), targets: new Map([...ghosts].map(([id, g]) => [id, { x: g.position.x, y: g.position.y }])) };
      kept.pending = null;
      return kept.latest;
    }
    const memo = memoOf(ghosts);
    if (memo !== kept.asked) kept.pending = { memo, ghosts: new Map(ghosts) };
    this.#ask(key, kept);
    return kept.latest;
  }

  /** The frame worker asks one frame at a time: the latest ghosts once the answer in flight lands. */
  #ask(key: string, kept: Ask): void {
    if (kept.asked !== null || kept.pending === null || this.#frames === null || kept.failed) return;
    const { memo, ghosts } = kept.pending;
    kept.pending = null;
    kept.asked = memo;
    const proposed = Object.fromEntries(ghosts);
    this.#frames.call('MoveAskFrame', JSON.stringify({ key, proposed })).then((json) => {
      const found = JSON.parse(json) as Found;
      kept.latest = {
        allowance: found.allowance, routes: new Map(found.routes.map((r) => [r.id, r])), contacts: new Map(found.contacts.map((c) => [c.modelId, c])),
        targets: new Map([...ghosts].map(([id, g]) => [id, { x: g.position.x, y: g.position.y }])),
      };
      kept.asked = null;
      if (this.#asks.get(key) === kept) this.#changed();
      this.#ask(key, kept);
    }, (e) => { kept.asked = null; kept.failed = true; console.error('move frame worker', e); });
  }

  /** Every worker stopped (the page is gone). */
  dispose(): void {
    for (const w of [...(this.#frames === null ? [] : [this.#frames]), ...this.#walkers]) w.terminate();
    this.#frames = null; this.#walkers = []; this.#asks.clear(); this.#listeners.clear();
  }
}
