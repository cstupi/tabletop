import { describe, expect, it } from 'vitest';
import type { Contact } from '../src/lib/geometry';
import type { Frame } from '../src/lib/live/apply';
import type { Held } from '../src/lib/play/modelDrag';
import { at, moveRig, onTable, region, tick } from './moveRig';

const answerOf = (c: unknown) => (c as { answer: { kind: string; moves: { modelId: string; from: unknown; to: any }[]; continues: boolean } }).answer;
const contact = (modelId: string, kind: Contact['kind'] = 'on_base'): Contact => ({ modelId, kind });

describe('the move Ask', () => {
  it('a move from the table proposes each model where it stands and notes the allowance and what was used', () => {
    const r = moveRig();
    const [a, b] = r.ids as [string, string];
    r.applier.projection.stack.push({
      id: 'f1', sequence: 'activation', context: {} as never, outline: [],
      steps: [{ step: 'move', answer: undefined, rolls: [], values: undefined, applied: undefined,
        changes: [{ kind: 'models_moved', moves: [{ modelId: a, from: onTable(7, 6), to: onTable(10, 10) }] }] }],
    } as Frame);
    r.openMove(region({ modelIds: [a, b] }), { frameId: 'f1' });
    expect([...r.box.ghosts]).toEqual([[a, at(10, 10)], [b, at(12, 10)]]);
    expect([...r.box.proposing]).toEqual([a, b]);
    expect(r.panel.note).toBe('Move up to 6.0" (used 5.0") · Alt: place freely.');
    expect(r.hooks.allowance()).toEqual([{ centre: { x: 10, y: 10 }, radius: 6 }]);
    r.hooks.picked([b]);
    expect(r.hooks.allowance()).toEqual([{ centre: { x: 10, y: 10 }, radius: 6 }, { centre: { x: 12, y: 10 }, radius: 6 }]);
    expect(r.panel.controls.map((c) => ('label' in c ? c.label : c.kind))).toContain('Commit move (Enter)');
  });

  it('a minimum distance notes the range', () => {
    const r = moveRig();
    r.openMove(region({ modelIds: r.ids, allowance: 36, minDistance: 30 }));
    expect(r.panel.note).toBe('Move 30.0" to 36.0" (used 0.0") · Alt: place freely.');
  });

  it('a shelf placement rings the anchor and notes the prompt', () => {
    const r = moveRig();
    r.openMove(region({ modelIds: r.reserveIds, allowance: 0, separation: 1, passesModels: true, containment: { enterable: [], exitingId: r.truck, exitWithin: 6 } }));
    expect(r.panel.note).toBe('Drag them into place, then commit.');
    expect(r.box.ghosts.size).toBe(2);
    for (const g of r.box.ghosts.values()) expect(Math.hypot(g.position.x - 20, g.position.y - 10)).toBeCloseTo(2 + 1 + 0.5, 6);   // the anchor's half, the separation, the model's half
    expect(r.hooks.allowance()).toEqual([]);
  });

  it('a disembark rings the container and names it', () => {
    const r = moveRig();
    for (const id of r.ids) r.applier.projection.models.get(id)!.location = { kind: 'inside', containerId: r.truck };
    r.openMove(region({ modelIds: r.ids, allowance: 0, containment: { enterable: [], exitingId: r.truck, exitWithin: 6 } }));
    expect(r.panel.note).toBe('Disembark: place every model fully within 6.0" of Truck (Truck). Drag them out, then commit.');
    expect(r.box.ghosts.size).toBe(2);
    expect(r.control('stop')).toBeUndefined();
  });

  it('a disembark answers the exit yaw at the drop, not the ghost\'s', () => {
    const r = moveRig();
    for (const id of r.ids) r.applier.projection.models.get(id)!.location = { kind: 'inside', containerId: r.truck };
    const walls = ['11111111-0000-7000-8000-000000000001'];
    r.openMove(region({ modelIds: r.ids, allowance: 0, impassableVolumeIds: walls, containment: { enterable: [], exitingId: r.truck, exitWithin: 6 } }));
    r.setExit({ degrees: 45 });
    r.box.dragged(r.ids[0]!, at(14, 10, 135));                                // the player turned it to 135°
    r.box.enter();
    expect(answerOf(r.sent[0]).moves[0]!.to).toEqual({ kind: 'table', placement: { position: { x: 14, y: 10, z: 0 }, yaw: { degrees: 45 } } });
    const [body, container, within, drop, , impassable] = r.exitCalls[0]! as [any, any, number, any, unknown, string[]];
    expect([body.base.shape, container.placement.position, within, drop, impassable]).toEqual(['circle', { x: 20, y: 10, z: 0 }, 6, { x: 14, y: 10 }, walls]);
  });

  it('a disembark no facing fits answers the container\'s facing', () => {
    const r = moveRig();
    for (const id of r.ids) r.applier.projection.models.get(id)!.location = { kind: 'inside', containerId: r.truck };
    r.applier.projection.models.get(r.truck)!.location = { kind: 'table', placement: at(20, 10, 90) };
    r.openMove(region({ modelIds: r.ids, allowance: 0, containment: { enterable: [], exitingId: r.truck, exitWithin: 6 } }));
    r.setExit(null);
    r.box.dragged(r.ids[0]!, at(40, 40, 135));                                // out of reach: no facing fits
    r.box.enter();
    expect(answerOf(r.sent[0]).moves[0]!.to.placement.yaw).toEqual({ degrees: 90 });
  });

  it('a dragged ghost regates with contacts and readouts', () => {
    const r = moveRig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids, separation: 1 }));
    expect(r.reason()).toBe('');
    r.setFound(new Map([[a, contact(a, 'too_close')]]));
    r.box.dragged(a, at(18, 10));                                              // 8" from where it stood: past the allowance
    expect(r.reason()).toBe('1 model past its allowance · within 1" of another unit.');
    expect(r.contactCalls.length).toBeGreaterThan(1);
    expect([...(r.contactCalls.at(-1)![1] as Map<string, unknown>).keys()]).toEqual(r.ids);
    const held = { ids: [a], origins: new Map([[a, at(10, 10)]]), ends: new Map([[a, { id: a, drawn: { x: 18, y: 10, z: 0.5 }, lands: { x: 16, y: 10, z: 0 } }]]) } as unknown as Held;
    expect(r.hooks.readouts(held)).toMatchObject([{ modelId: a, from: { x: 10, y: 10 }, to: { x: 16, y: 10 }, readout: { distance: 6, overAllowance: false } }]);
    expect(r.hooks.readouts(null)).toEqual([]);
  });

  it('under strict a mark disables Commit and Stop', () => {
    const r = moveRig({ enforcement: 'strict' });
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids, stoppable: true }));
    expect(r.disabled('commit')).toBe(false);
    r.setFound(new Map([[a, contact(a)]]));
    r.box.dragged(a, at(11, 10));
    expect(r.disabled('commit')).toBe(true);
    expect(r.disabled('stop')).toBe(true);
    expect(r.reason()).toBe('overlaps a base.');
    r.box.press('commit'); r.box.enter(); r.box.stop();
    expect(r.sent).toHaveLength(0);
    r.setFound(new Map());
    r.box.dragged(a, at(12, 11));
    expect(r.disabled('commit')).toBe(false);
  });

  it('under warn the reason shows and Commit sends', () => {
    const r = moveRig({ enforcement: 'warn' });
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.setFound(new Map([[a, contact(a)]]));
    r.box.dragged(a, at(11, 10));
    expect(r.reason()).toBe('overlaps a base.');
    expect(r.disabled('commit')).toBe(false);
    r.box.enter();
    expect(r.sent).toHaveLength(1);
  });

  it('commit sends Destinations with each ghost\'s facing', () => {
    const r = moveRig();
    const [a, b] = r.ids as [string, string];
    const ask = r.openMove(region({ modelIds: r.ids }));
    r.box.dragged(a, at(13, 12, 90));
    r.box.enter();
    expect(r.sent[0]).toMatchObject({ kind: 'answer_ask', askId: ask.id });
    const answer = answerOf(r.sent[0]);
    expect(answer.kind).toBe('destinations');
    expect(answer.continues).toBe(false);
    expect(answer.moves).toEqual([
      { modelId: a, from: onTable(10, 10), to: { kind: 'table', placement: at(13, 12, 90) } },
      { modelId: b, from: onTable(12, 10), to: { kind: 'table', placement: at(12, 10) } },
    ]);
    expect(r.drops.holds(a)).toBe(true);                                       // drawn at its drop until the move's event
    expect(r.box.ghosts.size).toBe(0);
  });

  it('the stop sends continues true', () => {
    const r = moveRig();
    r.openMove(region({ modelIds: r.ids, stoppable: true }));
    expect(r.panel.controls.map((c) => ('label' in c ? c.label : ''))).toContain('Stop here to use a rule (C)');
    expect(r.box.stop()).toBe(true);
    expect(answerOf(r.sent[0]).continues).toBe(true);
  });

  it('a click on an enterable container answers Inside for every model', () => {
    const r = moveRig();
    r.openMove(region({ modelIds: r.ids, containment: { enterable: [r.truck] } }));
    expect(r.panel.note).toBe('Move up to 6.0" (used 0.0") · Alt: place freely. Or click a container in reach to embark: Truck (Truck).');
    expect([...r.box.eligible]).toEqual([r.truck]);
    r.box.target(r.rival);
    expect(r.sent).toHaveLength(0);
    r.box.target(r.truck);
    expect(answerOf(r.sent[0]).moves.map((m) => [m.modelId, m.to])).toEqual(r.ids.map((id) => [id, { kind: 'inside', containerId: r.truck }]));
  });

  it('Esc puts the ghosts back and clears the marks', () => {
    const r = moveRig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.setFound(new Map([[a, contact(a)]]));
    r.box.dragged(a, at(15, 12));
    expect(r.violations.marked.size).toBe(1);
    r.setFound(new Map());
    expect(r.box.cancel()).toBe(true);
    expect(r.box.ghosts.get(a)).toEqual(at(10, 10));
    expect(r.violations.marked.size).toBe(0);
  });

  it('a refused commit returns its ghosts marked with the server\'s detail', async () => {
    const r = moveRig();
    const [a, b] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.box.dragged(a, at(14, 12, 45));
    r.box.enter();
    expect(r.drops.holds(a)).toBe(true);
    r.refuse(0, 'Squad (S1) would move through another model');
    await tick();
    expect(r.panel.refusal).toBe('The server refused: Squad (S1) would move through another model');
    expect(r.box.ghosts.get(a)).toEqual(at(14, 12, 45));
    expect(r.box.ghosts.get(b)).toEqual(at(12, 10));
    expect(r.violations.marked.get(a)).toBe('Squad (S1) would move through another model');
    expect(r.violations.marked.has(b)).toBe(false);                            // only the model it moved
    expect(r.drops.holds(a)).toBe(false);
    expect(r.control('commit')).toBeDefined();
  });

  it('the marks lapse when a refused model is dragged', async () => {
    const r = moveRig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.box.dragged(a, at(14, 12));
    r.box.enter();
    r.refuse(0, 'no');
    await tick();
    expect(r.violations.marked.size).toBe(1);
    r.box.dragged(a, at(15, 12));
    expect(r.violations.marked.size).toBe(0);
  });

  it('a refusal shows its reason once, over the gate\'s line of the same words', async () => {
    const r = moveRig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.box.dragged(a, at(14, 12));
    r.box.enter();
    r.refuse(0, 'Squad (S1) would move through another model');
    await tick();
    expect(r.panel.refusal).toBe('The server refused: Squad (S1) would move through another model');
    expect(r.reason()).toBe('');
  });

  it('a refusal clears once a ghost is dragged again', async () => {
    const r = moveRig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.box.dragged(a, at(14, 12));
    r.box.enter();
    r.refuse(0, 'no');
    await tick();
    expect(r.panel.refusal).toBe('The server refused: no');
    r.box.dragged(a, at(15, 12));
    expect(r.panel.refusal).toBeNull();
    expect(r.panel.visible).toBe(true);
  });

  it('a failed send frees the drop and shows why', async () => {
    const r = moveRig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.box.dragged(a, at(14, 12));
    r.box.enter();
    r.fail(0, new Error('socket closed'));
    await tick();
    expect(r.drops.holds(a)).toBe(false);
    expect(r.panel.refusal).toBe('Could not send the answer: socket closed');
  });

  it('ghosts kept across a reconnect come back for the same Ask and not for another', () => {
    const r = moveRig();
    const [a] = r.ids as [string, string];
    const ask = r.openMove(region({ modelIds: r.ids }));
    r.box.dragged(a, at(14, 12, 30));
    r.reconnect(true); r.box.render();
    expect(r.box.ghosts.size).toBe(0);
    r.reconnect(false); r.box.render();
    expect(r.box.ghosts.get(a)).toEqual(at(14, 12, 30));
    // Another Ask after a reconnect gets the projection's positions.
    r.reconnect(true); r.box.render();
    r.apply({ kind: 'ask_closed', askId: ask.id, answers: [] });
    r.openMove(region({ modelIds: r.ids }));
    r.reconnect(false); r.box.render();
    expect(r.box.ghosts.get(a)).toEqual(at(10, 10));
  });

  it('a container gone from the table notes it and offers nothing', () => {
    const r = moveRig();
    r.applier.projection.models.get(r.truck)!.location = { kind: 'shelf' };
    r.openMove(region({ modelIds: r.ids, allowance: 0, containment: { enterable: [], exitingId: r.truck, exitWithin: 6 } }));
    expect(r.panel.note).toBe('The container is no longer on the table.');
    expect(r.control('commit')).toBeUndefined();
    expect(r.box.ghosts.size).toBe(0);
    expect(r.box.proposing.size).toBe(0);
  });
});
