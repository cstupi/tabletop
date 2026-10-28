import { describe, expect, it } from 'vitest';
import { MarkerLocks } from '../src/lib/play/markerLocks';
import { choose, pieceMenu, rosterMenu, teamMenu, type MenuContext } from '../src/lib/play/menus';
import { forMarker, forModel } from '../src/lib/play/menuModel';
import { Names } from '../src/lib/play/names';
import type { EventPayload, Marker } from '../src/lib/protocol/wire.generated';
import { P1, P2, TEAM1, boardOf, emptyTable, gameOf, markerOf, newId, projectionOf, slotOf, unitOf, at } from './play';

const mine = unitOf('Mine', [slotOf({ name: 'M' })]);
const theirs = unitOf('Theirs', [slotOf({ name: 'T' })]);
const hull = { ...unitOf('Aside', [slotOf({ name: 'H' })]), properties: [{ name: 'Ambush' }] };
const objective: Marker = { ...markerOf('mid'), id: newId() };
const game = gameOf();
const board = boardOf([{ participantId: P1, units: [mine, hull] }, { participantId: P2, units: [theirs] }], { ...emptyTable(), markers: [objective] });
const p = (...e: EventPayload[]) => projectionOf(game, board, e).projection;
const ctx = (over: Partial<MenuContext> = {}): MenuContext => ({
  game, p: p(), log: [], watching: false, myTeam: TEAM1, mine: null, grantable: [], deployInside: new Map(), maySetAside: new Set([hull.id]), locks: new MarkerLocks(), ...over,
});
const labels = (items: readonly { label: string }[]) => items.map((i) => i.label);

describe('menus', () => {
  it('a watcher gets no menu and the roster menu is my team\'s only', () => {
    const w = ctx({ watching: true, myTeam: null });
    expect([pieceMenu(w, mine.models[0]!.id, false), teamMenu(w, TEAM1, new Names(game, w.p)), rosterMenu(w, hull.id)]).toEqual([[], [], []]);
    expect(labels(rosterMenu(ctx(), hull.id))).toEqual(['Set aside']);
    expect(rosterMenu(ctx(), theirs.id)).toEqual([]);
    expect(pieceMenu(ctx(), theirs.models[0]!.id, false).length).toBeGreaterThan(0);          // the model menu is any participant's
  });

  it('a model\'s menu leads with boarding and ends with the frame\'s abandon', () => {
    const c = ctx({ p: p({ kind: 'sequence_started', frameId: newId(), sequence: 'attack-sequence', context: { actorUnitId: mine.id, targetUnitIds: [], weaponIds: [] } }) });
    const items = labels(pieceMenu(c, mine.models[0]!.id, false));
    expect(items[0]).toBe('Set wounds');
    expect(items.at(-1)).toBe('Abandon attack-sequence');
    expect(labels(forModel(c.p, mine.models[0]!.id, []))).toEqual(items.slice(0, -1));
  });

  it('shift gives the unit\'s menu with its grants', () => {
    const items = pieceMenu(ctx({ grantable: [{ label: 'Hex', entryName: 'Hex', effectId: newId() }] }), mine.models[0]!.id, true);
    expect(labels(items)).toContain('Add a grant');
    expect(labels(items)).toContain('Move the unit to the shelf');
    expect(labels(items)).not.toContain('Set wounds');
  });

  it('a marker\'s menu ends with the lock item', () => {
    const c = ctx();
    expect(labels(pieceMenu(c, objective.id, false))).toEqual(labels(forMarker(game, c.p, objective.id, c.locks)));
    expect(labels(pieceMenu(c, objective.id, false)).at(-1)).toBe('Unlock');
  });

  it('choose sends a None item at once, asks for an Integer and refuses a bad one', () => {
    const items = pieceMenu(ctx(), mine.models[0]!.id, false);
    const wounds = items.find((i) => i.label === 'Set wounds')!;
    expect(choose(wounds, null)).toEqual({ kind: 'dialog', input: 'integer', choices: [] });
    expect(choose(wounds, '3')).toEqual({ kind: 'send', command: { kind: 'set_wounds', modelId: mine.models[0]!.id, wounds: 3 } });
    expect(choose(wounds, 'x')).toEqual({ kind: 'invalid' });
    expect(choose(items.find((i) => i.label === 'Move to the shelf')!, null).kind).toBe('send');
    expect(choose(items.find((i) => i.label === 'Add a condition')!, null)).toMatchObject({ kind: 'dialog', input: 'text' });
    expect(choose(items.find((i) => i.label === 'Put inside a container')!, null)).toEqual({ kind: 'pick', what: 'piece' });
  });

  it('a table point item waits for a table click', () => {
    const c = ctx({ p: p({ kind: 'models_moved', moves: [{ modelId: mine.models[0]!.id, from: { kind: 'table', placement: at(1, 1) }, to: { kind: 'shelf' } }] }) });
    const place = pieceMenu(c, mine.models[0]!.id, false).find((i) => i.label === 'Place on the table, here')!;
    expect(choose(place, null)).toEqual({ kind: 'pick', what: 'table' });
    expect(choose(place, '4,5,0').kind).toBe('send');
    expect(choose(place, 'nope')).toEqual({ kind: 'invalid' });
  });

  it('a lock item applies locally and sends nothing', () => {
    const c = ctx();
    const lock = pieceMenu(c, objective.id, false).at(-1)!;
    expect(choose(lock, null)).toEqual({ kind: 'local' });
    lock.local!();
    expect(pieceMenu(c, objective.id, false).at(-1)!.label).toBe('Lock');
  });
});
