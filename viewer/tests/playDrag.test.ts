import { describe, expect, it } from 'vitest';
import { NO_MODIFIERS } from '../src/lib/board/interaction';
import type { SendOutcome } from '../src/lib/connection/gameConnection';
import type { Geometry, HeldModel } from '../src/lib/geometry';
import { MarkerLocks } from '../src/lib/play/markerLocks';
import { PlayDrags } from '../src/lib/play/playDrag';
import type { Selection } from '../src/lib/play/selectionView';
import type { Command, Marker } from '../src/lib/protocol/wire.generated';
import { markerOf } from './play';
import { at, moveRig, region, tick } from './moveRig';

const geometry = {
  landModels: (held: readonly HeldModel[]) => held.map((h) => ({ id: h.id, drawn: { x: h.to.x, y: h.to.y, z: 0.5 }, lands: { x: h.to.x, y: h.to.y, z: 0 } })),
  contacts: () => new Map(), restModel: () => 0, edgeToEdge: () => 1,
} as unknown as Geometry;

function rig(over: Parameters<typeof moveRig>[0] & { frame?: (run: () => void) => void } = {}) {
  const r = moveRig(over);
  const sent: Command[] = [];
  let outcome: Promise<SendOutcome> = Promise.resolve({ id: 'x', refused: null });
  let changes = 0;
  const locks = new MarkerLocks();
  let selection: Selection | null = null;
  const drags = new PlayDrags({
    state: () => r.box.host.state(), box: () => r.box, hooks: () => r.hooks, geometry: () => geometry,
    violations: r.violations, drops: r.drops, locks,
    send: (command) => { sent.push(command); return outcome; }, select: (sel) => { selection = sel; }, selection: () => selection, changed: () => { changes++; },
    ...(over.frame !== undefined ? { frame: over.frame } : {}),
  });
  return { ...r, drags, sent, locks, get panel() { return r.box.panel; }, setOutcome(o: Promise<SendOutcome>) { outcome = o; }, get changes() { return changes; }, get selection() { return selection; } };
}
const model = (id: string) => ({ kind: 'model', id }) as const;

describe('PlayDrags', () => {
  it('a hand drag outside an Ask sends MoveModels and is drawn at its drop until the event', async () => {
    const r = rig();
    const [a, b] = r.ids as [string, string];
    r.box.render();
    expect(r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS)).toBe(true);
    r.drags.onDragMove({ x: 15, y: 13 }, NO_MODIFIERS);
    expect(r.drags.drawn().get(a)).toEqual({ position: { x: 15, y: 13, z: 0.5 }, yaw: { degrees: 0 } });      // drawn lifted
    expect(r.drags.drawn().get(b)!.position).toMatchObject({ x: 17, y: 13 });
    r.drags.onDragEnd({ x: 15, y: 13 }, NO_MODIFIERS);
    const command = r.sent[0] as unknown as { kind: string; moves: { modelId: string; to: { placement: unknown } }[] };
    expect(command.kind).toBe('move_models');
    expect(command.moves.map((m) => m.modelId)).toEqual([a, b]);
    expect(command.moves[0]!.to.placement).toEqual(at(15, 13));                                              // the landing, not the lifted spot
    expect(r.drops.holds(a)).toBe(true);
    expect(r.drags.drawn().get(a)).toEqual(at(15, 13));
    await tick();
    expect(r.drops.holds(a)).toBe(true);                                                                     // waits for the event
  });

  it('a refused hand move is drawn back and says why', async () => {
    const r = rig();
    const [a] = r.ids as [string, string];
    r.setOutcome(Promise.resolve({ id: 'm9', refused: { kind: 'refused', messageId: 'm9', reason: 'illegal', detail: 'not now' } }));
    r.box.render();
    r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS);
    r.drags.onDragMove({ x: 15, y: 13 }, NO_MODIFIERS);
    r.drags.onDragEnd({ x: 15, y: 13 }, NO_MODIFIERS);
    await tick();
    expect(r.drops.holds(a)).toBe(false);
    expect(r.panel.refusal).toContain('not now');
  });

  it('under strict a hand drag does not start, and proposing models still do', () => {
    const r = rig({ enforcement: 'strict' });
    const [a] = r.ids as [string, string];
    r.box.render();
    expect(r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS)).toBe(false);
    r.openMove(region({ modelIds: r.ids }));
    expect(r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS)).toBe(true);
  });

  it('a drag of proposed models moves their ghosts and sends nothing; Esc puts them back', () => {
    const r = rig();
    const [a, b] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    expect(r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS)).toBe(true);
    r.drags.onDragMove({ x: 14, y: 12 }, NO_MODIFIERS);
    expect(r.box.ghosts.get(a)!.position).toMatchObject({ x: 14, y: 12, z: 0 });
    expect(r.box.ghosts.get(b)!.position).toMatchObject({ x: 16, y: 12 });
    expect(r.reason()).toBe('');
    r.drags.onDragMove({ x: 24, y: 10 }, NO_MODIFIERS);                                                      // 14" against a 6" allowance
    expect(r.box.ghosts.get(a)!.position).toMatchObject({ x: 16, y: 10 });                                  // held to its reach
    expect(r.box.ghosts.get(b)!.position).toMatchObject({ x: 18, y: 10 });
    expect(r.reason()).toBe('');
    r.drags.onDragMove({ x: 24, y: 10 }, { ...NO_MODIFIERS, alt: true });                                    // Alt places freely
    expect(r.reason()).toBe('2 models past their allowance.');
    r.drags.onDragEnd({ x: 24, y: 10 }, { ...NO_MODIFIERS, alt: true });
    expect(r.sent).toHaveLength(0);
    expect(r.drags.held).toBeNull();
    expect(r.box.ghosts.get(a)!.position).toMatchObject({ x: 24, y: 10 });
    r.drags.onDragStart(model(a), { x: 24, y: 10 }, NO_MODIFIERS);
    r.drags.onDragMove({ x: 26, y: 10 }, NO_MODIFIERS);
    r.drags.onDragCancel();
    expect(r.box.ghosts.get(a)!.position).toMatchObject({ x: 24, y: 10 });                                  // back where it was picked up
  });

  it('a landing of every held ghost regates once: one Contacts per pointer move, the gate over all of them', () => {
    const r = rig({ squad: 4 });
    const [a] = r.ids as [string];
    r.openMove(region({ modelIds: r.ids }));
    r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS);
    const before = r.contactCalls.length;
    r.drags.onDragMove({ x: 24, y: 10 }, { ...NO_MODIFIERS, alt: true });                                    // Alt: not held to the allowance
    expect(r.contactCalls.length - before).toBe(1);
    expect(r.reason()).toBe('4 models past their allowance.');
    r.drags.onDragEnd({ x: 24, y: 10 }, { ...NO_MODIFIERS, alt: true });
    expect(r.contactCalls.length - before).toBe(2);
    r.drags.onDragStart(model(a), { x: 24, y: 10 }, NO_MODIFIERS);
    r.drags.onDragCancel();
    expect(r.contactCalls.length - before).toBe(3);
  });

  it('pointer moves wait for the next frame and only the last one lands; a drop or a key takes the latest at once', () => {
    const frames: (() => void)[] = [];
    const r = rig({ frame: (run) => { frames.push(run); } });
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS);
    const before = r.contactCalls.length;
    r.drags.onDragMove({ x: 11, y: 10 }, NO_MODIFIERS);
    r.drags.onDragMove({ x: 12, y: 10 }, NO_MODIFIERS);
    r.drags.onDragMove({ x: 13, y: 11 }, NO_MODIFIERS);
    expect(frames).toHaveLength(1);
    expect(r.contactCalls.length).toBe(before);
    frames.shift()!();
    expect(r.contactCalls.length - before).toBe(1);
    expect(r.box.ghosts.get(a)!.position).toMatchObject({ x: 13, y: 11 });
    r.drags.onDragMove({ x: 14, y: 12 }, NO_MODIFIERS);
    expect(r.drags.onKey({ kind: 'turnLeft' }, false)).toBe(true);                                           // the key turns at the latest point
    expect(r.box.ghosts.get(a)).toMatchObject({ position: { x: 14, y: 12 }, yaw: { degrees: 15 } });
    r.drags.onDragMove({ x: 15, y: 12 }, NO_MODIFIERS);
    r.drags.onDragEnd({ x: 15.5, y: 12 }, NO_MODIFIERS);                                                    // inside the 6" allowance
    for (const run of frames.splice(0)) run();                                                               // a frame after the drop does nothing
    expect(r.box.ghosts.get(a)!.position).toMatchObject({ x: 15.5, y: 12 });
    expect(r.drags.held).toBeNull();
  });

  it('Q and E turn the held models, 1 to 5 arrange them, and Enter is held off', () => {
    const r = rig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    expect(r.drags.onKey({ kind: 'turnLeft' }, false)).toBe(false);                                           // nothing in hand
    r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS);
    r.drags.onDragMove({ x: 12, y: 10 }, NO_MODIFIERS);
    expect(r.drags.onKey({ kind: 'turnLeft' }, false)).toBe(true);
    expect(r.box.ghosts.get(a)!.yaw.degrees).toBe(15);
    expect(r.drags.onKey({ kind: 'turnRight' }, true)).toBe(true);
    expect(r.box.ghosts.get(a)!.yaw.degrees).toBe(345);
    expect(r.drags.onKey({ kind: 'option', key: 2 }, false)).toBe(true);
    expect(r.drags.held!.rows).toBe(2);
    expect(r.drags.onKey({ kind: 'confirm' }, false)).toBe(true);
    expect(r.sent).toHaveLength(0);
    expect(r.drags.onKey({ kind: 'option', key: 7 }, false)).toBe(false);
  });

  it('a click on a deploy\'s placement point is the box\'s, and the Ask ending drops a drag of its ghosts', () => {
    const r = rig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS);
    r.box.rebuild();                                                                                         // the Ask re-renders: its ghosts are new
    r.box.proposing = new Set();
    r.drags.boxChanged();
    expect(r.drags.held).toBeNull();
    expect(r.drags.placeAt({ x: 1, y: 1 })).toBe(false);
  });

  it('a marker drag sends MoveMarker unless the objective is locked', () => {
    const r = rig();
    const objective = markerOf('Obj');
    const p = r.applier.projection;
    (p.board.table.markers as Marker[]).push(objective);
    r.box.render();
    expect(r.drags.onDragStart({ kind: 'marker', id: objective.id }, { x: 1, y: 1 }, NO_MODIFIERS)).toBe(false);
    r.locks.itemFor('playing', p, objective.id)!.apply();
    expect(r.drags.onDragStart({ kind: 'marker', id: objective.id }, { x: 1, y: 1 }, NO_MODIFIERS)).toBe(true);
    r.drags.onDragMove({ x: 5, y: 6 }, NO_MODIFIERS);
    r.drags.onDragEnd({ x: 5, y: 6 }, NO_MODIFIERS);
    expect(r.sent[0]).toEqual({ kind: 'move_marker', markerId: objective.id, position: { x: 5, y: 6, z: 0 }, yaw: { degrees: 0 } });
  });

  it('overlays: rings and rulers while a move is in hand', () => {
    const r = rig();
    const [a] = r.ids as [string, string];
    r.openMove(region({ modelIds: r.ids }));
    expect(r.drags.overlays(() => null, 1)).toHaveLength(1);                                                 // the allowance ring
    r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS);
    r.drags.onDragMove({ x: 14, y: 10 }, NO_MODIFIERS);
    expect(r.drags.overlays(() => null, 1).length).toBe(3);                                                  // ring, rulers, landing rings
  });

  describe('picking models', () => {
    it('Shift+click toggles a model of my team, starts over on another unit, and leaves an enemy to the page', () => {
      const r = rig({ squad: 3 });
      const [a, b] = r.ids as [string, string, string];
      r.box.render();
      expect(r.drags.shiftClick(a)).toBe(true);
      expect(r.drags.shiftClick(b)).toBe(true);
      expect(r.selection).toEqual({ pieceId: r.squad.id, modelIds: [a, b] });
      r.drags.shiftClick(a);
      expect(r.drags.picks.modelIds).toEqual([b]);
      r.drags.shiftClick(r.truck);
      expect(r.selection).toEqual({ pieceId: r.transport.id, modelIds: [r.truck] });
      expect(r.drags.shiftClick(r.rival)).toBe(false);
    });

    it('a Shift+drag box picks my models inside it, the unit with most winning, and draws a box while it travels', () => {
      const r = rig({ squad: 3 });
      const [, b, c] = r.ids as [string, string, string];
      r.box.render();
      expect(r.drags.onBoxStart({ x: 11, y: 9 })).toBe(true);
      r.drags.onBoxMove({ x: 11, y: 9 }, { x: 21, y: 11 });               // b, c and the truck
      expect(r.drags.overlays(() => null, 1).length).toBe(1);
      r.drags.onBoxEnd({ x: 11, y: 9 }, { x: 21, y: 11 });
      expect(r.selection).toEqual({ pieceId: r.squad.id, modelIds: [b, c] });
      expect(r.drags.overlays(() => null, 1)).toEqual([]);
      r.drags.onBoxStart({ x: 35, y: 25 });
      r.drags.onBoxEnd({ x: 35, y: 25 }, { x: 45, y: 35 });               // only the enemy: nothing changes
      expect(r.drags.picks.modelIds).toEqual([b, c]);
    });

    it('a plain drag on a picked model holds the picks and the rest of the unit stays; on another model the whole unit, and the picks clear', () => {
      const r = rig({ squad: 3 });
      const [a, b, c] = r.ids as [string, string, string];
      r.box.render();
      r.drags.shiftClick(a); r.drags.shiftClick(c);
      expect(r.drags.onDragStart(model(c), { x: 14, y: 10 }, NO_MODIFIERS)).toBe(true);
      expect(r.drags.held!.ids).toEqual([a, c]);
      r.drags.onDragMove({ x: 14, y: 20 }, NO_MODIFIERS);
      r.drags.onDragEnd({ x: 14, y: 20 }, NO_MODIFIERS);
      const moved = (r.sent[0] as unknown as { moves: { modelId: string }[] }).moves.map((m) => m.modelId);
      expect(moved).toEqual([a, c]);
      expect(r.drags.picks.modelIds).toEqual([a, c]);                      // the picks stay for the next drag
      expect(r.drags.onDragStart(model(b), { x: 12, y: 10 }, NO_MODIFIERS)).toBe(true);
      expect(r.drags.held!.ids).toEqual([a, b, c]);
      expect(r.drags.picks.isEmpty).toBe(true);
      expect(r.selection).toEqual({ pieceId: r.squad.id, modelIds: [] });
    });

    it('a Shift drag on my model holds it alone, the rest of the unit standing; on an enemy or a marker it is no drag (the page boxes)', () => {
      const r = rig({ squad: 3 });
      const [a, b, c] = r.ids as [string, string, string];
      r.box.render();
      const shift = { ...NO_MODIFIERS, shift: true };
      r.drags.shiftClick(a); r.drags.shiftClick(c);
      expect(r.drags.onDragStart(model(b), { x: 12, y: 10 }, shift)).toBe(true);
      expect(r.drags.held!.ids).toEqual([b]);
      expect(r.selection).toEqual({ pieceId: r.squad.id, modelIds: [b] });          // b was not picked: the picks start over with it
      r.drags.onDragMove({ x: 12, y: 20 }, shift);
      r.drags.onDragEnd({ x: 12, y: 20 }, shift);
      const moved = (r.sent[0] as unknown as { moves: { modelId: string }[] }).moves.map((m) => m.modelId);
      expect(moved).toEqual([b]);
      r.drags.shiftClick(a);                                                         // picks b, a
      expect(r.drags.onDragStart(model(a), { x: 10, y: 10 }, shift)).toBe(true);
      expect(r.drags.held!.ids).toEqual([a]);
      expect(r.drags.picks.modelIds).toEqual([b, a]);                                // a was picked: the picks stay
      r.drags.onDragEnd({ x: 10, y: 10 }, shift);
      expect(r.drags.onDragStart(model(r.rival), { x: 40, y: 30 }, shift)).toBe(false);
    });

    it('in a move Ask the picks are what a drag holds, and a new Ask or Esc clears them', () => {
      const r = rig({ squad: 3 });
      const [a] = r.ids as [string, string, string];
      const first = r.openMove(region({ modelIds: r.ids }));
      r.drags.boxChanged();
      r.drags.shiftClick(a);
      r.drags.boxChanged();                                               // the same Ask: the picks stay
      expect(r.drags.onDragStart(model(a), { x: 10, y: 10 }, NO_MODIFIERS)).toBe(true);
      expect(r.drags.held!.ids).toEqual([a]);
      r.drags.onDragEnd({ x: 10, y: 12 }, NO_MODIFIERS);
      expect(r.drags.picks.modelIds).toEqual([a]);
      r.apply({ kind: 'ask_closed', askId: first.id, answers: [] });
      r.openMove(region({ modelIds: r.ids }));                            // a new Ask
      r.drags.boxChanged();
      expect(r.drags.picks.isEmpty).toBe(true);
      expect(r.selection).toEqual({ pieceId: r.squad.id, modelIds: [] });
      r.drags.shiftClick(a);
      expect(r.drags.clearPicks()).toBe(true);
      expect(r.drags.clearPicks()).toBe(false);                           // a second Esc is the box's
    });
  });
});
