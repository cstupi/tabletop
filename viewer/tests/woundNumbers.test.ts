import { describe, expect, it } from 'vitest';
import { STAGGER, SECONDS, WoundFloats, WoundNumbers, woundLook } from '../src/lib/play/woundNumbers';
import type { EventPayload, GameEvent, ModelsMoved, StateChange } from '../src/lib/protocol/wire.generated';
import { FRAME, P1, at, boardOf, ev, gameOf, projectionOf, slotOf, unitOf } from './play';

const a = slotOf({ name: 'Sergeant', height: 1.5 }), b = slotOf({ name: 'Trooper' }), c = slotOf({ name: 'Trooper' });
const squad = unitOf('Squad', [a, b, c]);
const game = gameOf();
const board = boardOf([{ participantId: P1, units: [squad] }]);
const deploy: EventPayload = { kind: 'models_moved', moves: [
  { modelId: a.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } },
  { modelId: b.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(12, 10) } },
  { modelId: c.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(14, 10) } },
] } as ModelsMoved;
const step = (seq: number, ...changes: StateChange[]): GameEvent => ev(seq, { kind: 'step_completed', frameId: FRAME, step: 'assign', rolls: [], changes } as EventPayload);
const wounds = (modelId: string, n: number): StateChange => ({ kind: 'wounds_set', modelId, wounds: n });
const removed = (modelId: string, x: number): StateChange =>
  ({ kind: 'models_moved', moves: [{ modelId, from: { kind: 'table', placement: at(x, 10) }, to: { kind: 'shelf' } }] }) as StateChange;

function live() {
  const applier = projectionOf(game, board, [deploy, { kind: 'sequence_started', frameId: FRAME, sequence: 'deal-wounds', context: { actorUnitId: squad.id, targetUnitIds: [], weaponIds: [] } } as EventPayload]);
  const numbers = new WoundNumbers(); numbers.seed(applier.projection);
  const apply = (batch: GameEvent[], catchingUp = false, max: ReadonlyMap<string, number> = new Map()) => {
    applier.applyAll(batch);
    return numbers.onBatch(batch, applier.projection, catchingUp, max);
  };
  return { applier, numbers, apply };
}

describe('WoundNumbers', () => {
  it('each wound on a model left standing is one number over it, staggered', () => {
    const shown = live().apply([step(12, wounds(a.id, 3))]);
    expect(shown.map((n) => [n.modelId, n.at.x, n.at.y, n.index])).toEqual([[a.id, 10, 10, 0], [a.id, 10, 10, 1], [a.id, 10, 10, 2]]);
    expect(shown.map((n) => n.delay)).toEqual([0, STAGGER, 2 * STAGGER]);
    expect(shown[0]!.radius).toBe(0.5);
  });

  it('counts only the rise of a total', () => {
    const l = live();
    l.apply([step(12, wounds(a.id, 1))]);
    expect(l.apply([step(13, wounds(a.id, 3))])).toHaveLength(2);
  });

  it('a model destroyed by the same wounds shows nothing; its unit mates still show', () => {
    const shown = live().apply([step(12, wounds(a.id, 2), wounds(b.id, 1), removed(b.id, 12))]);
    expect(shown.map((n) => n.modelId)).toEqual([a.id, a.id]);
  });

  it('a model at its max wounds shows nothing even while it stands', () => {
    const shown = live().apply([step(12, wounds(a.id, 3), wounds(c.id, 1))], false, new Map([[a.id, 3], [c.id, 3]]));
    expect(shown.map((n) => n.modelId)).toEqual([c.id]);
  });

  it('healing shows nothing', () => {
    const l = live();
    l.apply([step(12, wounds(a.id, 2))]);
    expect(l.apply([step(13, wounds(a.id, 0))])).toEqual([]);
  });

  it('a menu wound edit shows, and the stagger runs on across a batch', () => {
    const shown = live().apply([step(12, wounds(a.id, 1)), ev(13, wounds(a.id, 2) as EventPayload)]);
    expect(shown.map((n) => n.delay)).toEqual([0, STAGGER]);
  });

  it('catching up, an undo and an empty batch show nothing and re-read the totals', () => {
    const l = live();
    expect(l.apply([step(12, wounds(a.id, 1))], true)).toEqual([]);
    expect(l.apply([step(13, wounds(a.id, 2))])).toHaveLength(1);
    expect(l.numbers.onBatch([], l.applier.projection, false, new Map())).toEqual([]);
    expect(l.numbers.onBatch([ev(14, { kind: 'undone', toSeq: 12 } as EventPayload), step(15, wounds(a.id, 3))], l.applier.projection, false, new Map())).toEqual([]);
  });

  it('rises and fades out over its seconds', () => {
    expect(woundLook(0)).toEqual({ rise: 0, alpha: 1 });
    const end = woundLook(SECONDS);
    expect(end.rise).toBeCloseTo(1); expect(end.alpha).toBeCloseTo(0);
  });
});

describe('WoundFloats', () => {
  const fakeCtx = () => {
    const texts: Array<{ text: string; x: number; y: number; alpha: number }> = [];
    const ctx = { globalAlpha: 1, font: '', textAlign: '', textBaseline: '', lineWidth: 0, strokeStyle: '', fillStyle: '', lineJoin: '',
      save() {}, restore() {}, strokeText() {}, fillText(text: string, x: number, y: number) { texts.push({ text, x, y, alpha: ctx.globalAlpha }); } };
    return { ctx: ctx as unknown as CanvasRenderingContext2D, texts };
  };

  it('starts, draws each number after its delay over the model, and stops when all are gone', () => {
    const l = live(); const floats = new WoundFloats(); let started = 0;
    floats.onStart(() => { started++; });
    floats.add(l.apply([step(12, wounds(a.id, 2))]), 1000);
    expect(started).toBe(1);
    const { ctx, texts } = fakeCtx();
    const project = (p: { x: number; y: number }) => ({ x: p.x * 10, y: p.y * 10 });
    expect(floats.draw(ctx, project, 10, 1000)).toBe(true);
    expect(texts.map((t) => t.text)).toEqual(['-1']);                 // the second waits out its stagger
    expect(texts[0]!.y).toBeLessThan(100);                             // above the model's centre
    texts.length = 0;
    expect(floats.draw(ctx, project, 10, 1000 + STAGGER * 1000 + 1)).toBe(true);
    expect(texts).toHaveLength(2);
    expect(floats.draw(ctx, project, 10, 1000 + (SECONDS + 2 * STAGGER) * 1000 + 1)).toBe(false);
  });

  it('an empty add starts nothing; clear drops what floats', () => {
    const floats = new WoundFloats(); let started = 0;
    floats.onStart(() => { started++; });
    floats.add([], 0);
    expect(started).toBe(0);
    const l = live();
    floats.add(l.apply([step(12, wounds(a.id, 1))]), 0);
    floats.clear();
    expect(floats.draw(fakeCtx().ctx, (p) => p, 10, 0)).toBe(false);
  });
});
