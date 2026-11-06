import { describe, expect, it } from 'vitest';
import type { Contact } from '../src/lib/geometry';
import { P1, TEAM1, newId } from './play';
import { at, moveRig, tick } from './moveRig';

const P3 = newId();
const contact = (modelId: string): Contact => ({ modelId, kind: 'on_base' });

function armed() {
  const r = moveRig();
  r.openDeployPick();
  return r;
}
/** The pick placed: the click at (30, 20) laid the unit out. */
function placed(over: Parameters<typeof moveRig>[0] = {}) {
  const r = moveRig(over);
  r.openDeployPick();
  r.box.placeAt({ x: 30, y: 20, z: 0 });
  return r;
}

describe('deploy placement', () => {
  it('a mid-deploy pick asks for a click and the click lays the unit out', () => {
    const r = armed();
    expect(r.panel.title).toBe('Place the models of Reserve');
    expect(r.panel.hint).toBe('Click the table where the unit goes');
    expect(r.box.deployUnit).toBe(r.reserve.id);
    expect(r.panel.visible).toBe(true);
    expect(r.box.placeAt({ x: 30, y: 20, z: 0 })).toBe(true);
    expect([...r.box.ghosts.keys()]).toEqual(r.reserveIds);
    for (const g of r.box.ghosts.values()) expect(Math.hypot(g.position.x - 30, g.position.y - 20)).toBeLessThan(2);
    expect([...r.box.proposing]).toEqual(r.reserveIds);
    expect(r.panel.hint).toBe('Drag to place · Shift: pick models · 1–5 while holding: rows · Enter: place · Esc: clear');
    expect(r.control('place')).toMatchObject({ label: 'Place (Enter)', disabled: false });
    expect(r.box.placeAt({ x: 1, y: 1, z: 0 })).toBe(false);                   // the pick is spent: no second click
  });

  it('a click where nothing fits keeps the pick armed', () => {
    const r = armed();
    for (const id of r.reserveIds) r.applier.projection.models.get(id)!.location = { kind: 'table', placement: at(5, 5) };   // no model left on the shelf
    expect(r.box.placeAt({ x: 30, y: 20, z: 0 })).toBe(true);
    expect(r.box.ghosts.size).toBe(0);
    expect(r.panel.hint).toBe('Click the table where the unit goes');
    expect(r.box.placementPoint).not.toBeNull();
  });

  it('a ghost on a base is named and strict waits', () => {
    const strict = placed({ enforcement: 'strict' });
    strict.setFound(new Map([[strict.reserveIds[0]!, contact(strict.reserveIds[0]!)]]));
    strict.box.dragged(strict.reserveIds[0]!, at(40.5, 30));
    expect(strict.reason()).toBe('overlaps a base.');
    expect(strict.disabled('place')).toBe(true);
    strict.box.enter();
    expect(strict.sent).toHaveLength(0);
    const warn = placed({ enforcement: 'warn' });
    warn.setFound(new Map([[warn.reserveIds[0]!, contact(warn.reserveIds[0]!)]]));
    warn.box.dragged(warn.reserveIds[0]!, at(40.5, 30));
    expect(warn.reason()).toBe('overlaps a base.');
    expect(warn.disabled('place')).toBe(false);
  });

  it('Place sends MoveModels from the shelf with each ghost\'s facing', () => {
    const r = placed();
    const [a, b] = r.reserveIds as [string, string];
    r.box.dragged(a, at(31, 21, 90));
    r.box.enter();
    expect(r.sent).toHaveLength(1);
    const command = r.sent[0] as unknown as { kind: string; moves: { modelId: string; from: unknown; to: { placement: unknown } }[] };
    expect(command.kind).toBe('move_models');
    expect(command.moves.map((m) => [m.modelId, m.from])).toEqual([[a, { kind: 'shelf' }], [b, { kind: 'shelf' }]]);
    expect(command.moves[0]!.to.placement).toEqual(at(31, 21, 90));
    expect(r.disabled('place')).toBe(true);                                    // while the answer travels
  });

  it('a refused placement comes back marked', async () => {
    const r = placed();
    const [a, b] = r.reserveIds as [string, string];
    r.box.dragged(a, at(31, 21, 90));
    r.box.enter();
    r.refuse(0, 'Reserve must be placed within the zone');
    await tick();
    expect(r.panel.refusal).toBe('The server refused: Reserve must be placed within the zone');
    expect(r.box.ghosts.get(a)).toEqual(at(31, 21, 90));
    expect(r.box.ghosts.has(b)).toBe(true);
    expect(r.violations.marked.get(a)).toBe('Reserve must be placed within the zone');
    expect(r.violations.marked.get(b)).toBe('Reserve must be placed within the zone');   // from the shelf: every model moved
    expect(r.control('place')).toBeDefined();
    r.box.dragged(b, at(33, 21));                                              // a dragged model lapses the refusal
    expect(r.violations.marked.size).toBe(0);
  });

  it('a send that fails says why and asks for a click again', async () => {
    const r = placed();
    r.box.enter();
    r.fail(0, new Error('socket closed'));
    await tick();
    expect(r.panel.refusal).toBe('Could not place the models: socket closed');
    expect(r.box.ghosts.size).toBe(0);
    expect(r.panel.hint).toBe('Click the table where the unit goes');
  });

  it('Esc clears the ghosts and asks for a click again', () => {
    const r = placed();
    r.box.dragged(r.reserveIds[0]!, at(31, 21));
    expect(r.box.cancel()).toBe(true);
    expect(r.box.ghosts.size).toBe(0);
    expect(r.panel.hint).toBe('Click the table where the unit goes');
    expect(r.box.deployUnit).toBe(r.reserve.id);
    expect(r.box.placementPoint).not.toBeNull();
    expect(r.control('place')).toBeUndefined();
  });

  it('a reconnect brings back the ghosts of models still on the shelf', () => {
    const r = placed();
    const [a, b] = r.reserveIds as [string, string];
    r.box.dragged(a, at(33, 22, 45));
    r.reconnect(true); r.box.render();
    expect(r.box.ghosts.size).toBe(0);
    r.applier.projection.models.get(b)!.location = { kind: 'table', placement: at(5, 5) };      // placed meanwhile: not on the shelf any more
    r.reconnect(false); r.box.render();
    expect([...r.box.ghosts]).toEqual([[a, at(33, 22, 45)]]);
    expect(r.control('place')).toBeDefined();
  });

  it('either player of the team places the pick', () => {
    const participants = [
      { id: P1, accountId: newId(), name: 'Clay', teamId: TEAM1, armyId: newId() },
      { id: P3, accountId: newId(), name: 'Dee', teamId: TEAM1, armyId: newId() },
    ];
    const r = moveRig({ participantId: P3, participants });
    r.openDeployPick();
    expect(r.panel.title).toBe('Place the models of Reserve');
    expect(r.box.placeAt({ x: 30, y: 20, z: 0 })).toBe(true);
    r.box.enter();
    expect(r.sent).toHaveLength(1);
  });
});
