import { describe, expect, it } from 'vitest';
import type { Marker } from '../src/lib/protocol/wire.generated';
import { MarkerLocks } from '../src/lib/play/markerLocks';
import {
  SET_ASIDE, forBoarding, forFrame, forMarker, forModel, forRosterUnit, forTeam, forUnit, knownActionNames, offTable,
} from '../src/lib/play/menuModel';
import { Names } from '../src/lib/play/names';
import { grantChoices } from '../src/lib/play/ruleTexts';
import type { EventPayload, GameEvent, Location } from '../src/lib/protocol/wire.generated';
import { ASK_ID, FRAME, P1, P2, TEAM1, TEAM2, askOf, at, boardOf, emptyTable, ev, gameOf, markerOf, newId, projectionOf, sample, slotOf, unitOf } from './play';

const s = sample();
const game = s.game;
const objective: Marker = { ...markerOf('centre'), id: newId() };
const table = { ...emptyTable(), markers: [objective] };
const board = boardOf([{ participantId: P1, units: [s.squad] }, { participantId: P2, units: [s.rivals] }], table);
const after = (...p: EventPayload[]) => projectionOf(game, board, p).projection;
const place = (id: string, x = 10, y = 10, from: Location = { kind: 'shelf' }): EventPayload => ({ kind: 'models_moved', moves: [{ modelId: id, from, to: { kind: 'table', placement: at(x, y) } }] });
const label = (items: readonly { label: string }[]) => items.map((i) => i.label);
const one = <T>(items: readonly T[], f: (i: T) => boolean): T => { const m = items.filter(f); expect(m).toHaveLength(1); return m[0]!; };
const byLabel = (items: readonly { label: string }[], l: string) => one(items, (i) => i.label === l) as ReturnType<typeof forTeam>[number];

describe('MenuModel play items', () => {
  it('menu items build the commands the spec lists', () => {
    const p = after();
    const recorded = projectionOf(game, board, [
      { kind: 'ask_opened', ask: askOf({ kind: 'options', choices: [{ id: 'a', label: 'Battle Brothers: Advance', pieces: [] }] }, { id: ASK_ID }) },
      { kind: 'action_recorded', unitId: s.squad.id, action: 'Advance' }, { kind: 'action_record_cleared', unitId: s.squad.id }, { kind: 'action_recorded', unitId: s.squad.id, action: 'Hold' },
    ]);
    const actions = knownActionNames(recorded.projection, [
      ev(1, { kind: 'action_recorded', unitId: s.squad.id, action: 'Advance' }), ev(2, { kind: 'action_recorded', unitId: s.squad.id, action: 'Hold' }),
    ]);
    expect(actions).toEqual(['Advance', 'Hold']);
    const unit = forUnit(p, s.squad.id, actions);
    expect(byLabel(unit, 'Mark as having taken Advance').build(null)).toEqual({ kind: 'record_action', unitId: s.squad.id, action: 'Advance' });
    expect(unit.some((i) => i.label.startsWith('Mark') && i.input === 'text')).toBe(false);
    expect(byLabel(unit, 'Clear the action record').build(null)).toEqual({ kind: 'clear_action_record', unitId: s.squad.id });
    expect(byLabel(unit, 'Add a condition').build('shaken')).toMatchObject({ kind: 'set_condition', condition: 'shaken', present: true });
    expect(byLabel(unit, 'Add a condition').build('  ')).toBeNull();
    const model = forModel(p, s.model.id, actions);
    expect(byLabel(model, 'Set wounds').build('3')).toEqual({ kind: 'set_wounds', modelId: s.model.id, wounds: 3 });
    expect(byLabel(model, 'Set wounds').build('x')).toBeNull();
    expect(byLabel(model, 'Move to the shelf').build(null)).toMatchObject({ moves: [{ to: { kind: 'shelf' } }] });
    expect(byLabel(model, 'Put inside a container').input).toBe('pieceId');
    expect(byLabel(model, 'Put inside a container').build(s.squad.id)).toMatchObject({ moves: [{ to: { kind: 'inside', containerId: s.squad.id } }] });
    const hull = s.rival.id;
    const occupant = forModel(after({ kind: 'models_moved', moves: [{ modelId: s.model.id, from: { kind: 'shelf' }, to: { kind: 'inside', containerId: hull } }] }), s.model.id, actions);
    const takeOut = byLabel(occupant, 'Take out of the container, here');
    expect(takeOut.input).toBe('tablePoint');
    expect(takeOut.build('14,12,0')).toMatchObject({ moves: [{ from: { kind: 'inside' }, to: { kind: 'table', placement: { position: { x: 14, y: 12, z: 0 } } } }] });
    expect(label(occupant)).not.toContain('Place on the table, here');
    const team = forTeam(game, p, TEAM1);
    expect(byLabel(team, 'Set score').build('2')).toEqual({ kind: 'set_score', teamId: TEAM1, score: 2 });
    expect(byLabel(team, 'Make this the active team').build(null)).toEqual({ kind: 'set_active_team', teamId: TEAM1 });
    expect(byLabel(team, 'Set the round').build('3')).toEqual({ kind: 'set_round', round: 3 });
    const framed = after({ kind: 'sequence_started', frameId: FRAME, sequence: 'attack-sequence', context: { actorUnitId: s.squad.id, targetUnitIds: [], weaponIds: [] } });
    expect(forFrame(framed).map((i) => i.build(null))).toEqual([{ kind: 'abandon_sequence', frameId: FRAME }]);
    expect(forFrame(p)).toEqual([]);
  });

  it('the team menu lists off table models of that team only', () => {
    const shelf = after();
    const n = new Names(game, shelf);
    const place1 = offTable(shelf, TEAM1, n).filter((i) => i.label.includes(n.model(s.model.id)));
    expect(place1).toHaveLength(1);
    expect([place1[0]!.label, place1[0]!.input]).toEqual([`Place ${n.model(s.model.id)} on the table, here`, 'tablePoint']);
    const mv = place1[0]!.build('5,6,0') as unknown as { moves: { modelId: string; from: Location; to: { placement: { position: unknown } } }[] };
    expect([mv.moves[0]!.modelId, mv.moves[0]!.from.kind, mv.moves[0]!.to.placement.position]).toEqual([s.model.id, 'shelf', { x: 5, y: 6, z: 0 }]);
    expect(place1[0]!.build('nope')).toBeNull();
    expect(offTable(shelf, TEAM2, n).every((i) => !i.label.includes(n.model(s.model.id)))).toBe(true);
    const inside = after({ kind: 'models_moved', moves: [{ modelId: s.model.id, from: { kind: 'shelf' }, to: { kind: 'inside', containerId: s.rival.id } }] });
    const take = offTable(inside, TEAM1, new Names(game, inside));
    expect(take.map((i) => i.label)).toEqual([`Take ${new Names(game, inside).model(s.model.id)} out of the container, here`]);
    expect(offTable(after(place(s.model.id)), TEAM1, new Names(game, shelf))).toEqual([]);
  });

  it('an objective moved or not can be given to a team or made neutral', () => {
    const p = after({ kind: 'marker_moved', markerId: objective.id, position: { x: 9, y: 9, z: 0 }, yaw: { degrees: 0 } });
    const items = forMarker(game, p, objective.id);
    expect(label(items)).toEqual(['Give to Team 1', 'Give to Team 2', 'Make neutral']);
    expect(items[1]!.build(null)).toEqual({ kind: 'set_objective_control', markerId: objective.id, teamId: TEAM2 });
    expect(items[2]!.build(null)).toEqual({ kind: 'set_objective_control', markerId: objective.id });
    expect(forMarker(game, p, newId())).toEqual([]);
  });

  it('the marker menu carries the lock item last', () => {
    const locks = new MarkerLocks();
    const p = after();
    expect(label(forMarker(game, p, objective.id, locks))).toEqual(['Give to Team 1', 'Give to Team 2', 'Make neutral', 'Unlock']);
    const last = forMarker(game, p, objective.id, locks).at(-1)!;
    expect([last.build(null), last.input]).toEqual([null, 'none']);
    last.local!();
    expect(forMarker(game, p, objective.id, locks).at(-1)!.label).toBe('Lock');
  });
});

describe('roster and passenger items', () => {
  const ambushers = unitOf('Ambushers', [slotOf({ name: 'M' })]);
  const withProp = (u: ReturnType<typeof unitOf>, name: string) => ({ ...u, properties: [{ name }] });
  const amb = withProp(ambushers, 'Ambush'), scouts = withProp(unitOf('Scouts', [slotOf({ name: 'M' })]), 'Scout');
  const line = unitOf('Line', [slotOf({ name: 'M' })]);
  const theirs = withProp(unitOf('Theirs', [slotOf({ name: 'M' })]), 'Ambush');
  const rhino = unitOf('Rhino', [slotOf({ name: 'Hull' })]);
  const squad = unitOf('Squad', [slotOf({ name: 'A', ordinal: 1 }), slotOf({ name: 'B', ordinal: 2 })]);
  const b = boardOf([{ participantId: P1, units: [amb, scouts, line, rhino, squad] }, { participantId: P2, units: [theirs] }]);
  const g = gameOf();
  const of = (extra: EventPayload[] = [], game2 = g) => projectionOf(game2, b, extra).projection;
  const hull = rhino.models[0]!.id;
  const facts = new Map([[squad.id, [hull]]]);
  const aside = new Set([amb.id, scouts.id, theirs.id]);   // ModelFacts.maySetAside as the server names it
  const inHull: EventPayload = { kind: 'models_moved', moves: squad.models.map((m) => ({ modelId: m.id, from: { kind: 'shelf' }, to: { kind: 'inside', containerId: hull } })) };

  it('my shelved ambush or scout unit can be set aside and returned', () => {
    const p = of();
    const item = forRosterUnit(g, p, amb.id, TEAM1, undefined, aside)[0]!;
    expect([item.label, item.build(null)]).toEqual(['Set aside', { kind: 'set_condition', subject: { kind: 'unit', id: amb.id }, condition: SET_ASIDE, present: true }]);
    expect(forRosterUnit(g, p, scouts.id, TEAM1, undefined, aside)[0]!.label).toBe('Set aside');
    const setAside = of([{ kind: 'condition_set', subject: { kind: 'unit', id: amb.id }, condition: SET_ASIDE, present: true }]);
    const back = forRosterUnit(g, setAside, amb.id, TEAM1, undefined, aside)[0]!;
    expect([back.label, back.build(null)]).toEqual(['Return to deployment', { kind: 'set_condition', subject: { kind: 'unit', id: amb.id }, condition: SET_ASIDE, present: false }]);
  });

  it('nothing for other units, other teams, a unit on the table, a destroyed one or outside play', () => {
    const p = of();
    expect(forRosterUnit(g, p, line.id, TEAM1, undefined, aside)).toEqual([]);
    expect(forRosterUnit(g, p, theirs.id, TEAM1, undefined, aside)).toEqual([]);
    expect(forRosterUnit(g, p, amb.id, null, undefined, aside)).toEqual([]);
    expect(forRosterUnit(g, of([{ kind: 'models_moved', moves: [{ modelId: amb.models[0]!.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(5, 5) } }] }]), amb.id, TEAM1, undefined, aside)).toEqual([]);
    expect(forRosterUnit(g, of([{ kind: 'wounds_set', modelId: amb.models[0]!.id, wounds: 1 }]), amb.id, TEAM1, undefined, aside)).toEqual([]);
    const ended = { ...g, status: 'ended' as const };
    expect(forRosterUnit(ended, of([], ended), amb.id, TEAM1, undefined, aside)).toEqual([]);
  });

  it('set aside follows the server\'s set, not property names', () => {
    const p = of();
    expect(forRosterUnit(g, p, line.id, TEAM1, undefined, new Set([line.id]))[0]!.label).toBe('Set aside');   // Infiltrate: counts as having Ambush
    expect(forRosterUnit(g, p, amb.id, TEAM1, undefined, new Set())).toEqual([]);
    expect(forRosterUnit(g, p, amb.id, TEAM1)).toEqual([]);                                                    // no facts, no offer
  });

  it('a shelved unit is offered each Transport the server named', () => {
    const p = of();
    const item = forRosterUnit(g, p, squad.id, TEAM1, facts)[0]!;
    expect(item.label).toBe('Put in transport: Rhino');
    expect(item.build(null)).toMatchObject({ kind: 'move_models', moves: [{ to: { kind: 'inside', containerId: hull } }, { to: { kind: 'inside', containerId: hull } }] });
    expect(forRosterUnit(g, p, squad.id, TEAM1)).toEqual([]);                                  // no facts, no offer
    expect(forRosterUnit(g, p, theirs.id, TEAM1, new Map([[theirs.id, [hull]]]))).toEqual([]); // not mine
  });

  it('a unit inside a Transport on the shelf may be taken out', () => {
    const item = forRosterUnit(g, of([inHull]), squad.id, TEAM1, facts)[0]!;
    expect(item.label).toBe('Take out of transport');
    expect(item.build(null)).toMatchObject({ moves: squad.models.map((m) => ({ modelId: m.id, from: { kind: 'inside', containerId: hull }, to: { kind: 'shelf' } })) });
    const deployed = of([inHull, { kind: 'models_moved', moves: [{ modelId: hull, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } }] }]);
    expect(forRosterUnit(g, deployed, squad.id, TEAM1, facts)).toEqual([]);                   // the Transport deployed
  });

  it('board transport answers my move Ask with every model inside', () => {
    const onTable: EventPayload = { kind: 'models_moved', moves: [hull, ...squad.models.map((m) => m.id)].map((id) => ({ modelId: id, from: { kind: 'shelf' } as Location, to: { kind: 'table', placement: at(10, 10) } as Location })) };
    const p = of([onTable]);
    const ask = askOf({ kind: 'move_region', modelIds: squad.models.map((m) => m.id), allowance: 6, impassableVolumeIds: [], difficultVolumeIds: [], stoppable: false, passesModels: false, ignoresTerrain: false,
      containment: { enterable: [hull] } }, { question: 'move', teams: [TEAM1], frameId: FRAME });
    const item = forBoarding(p, ask, squad.models[1]!.id)[0]!;
    expect(item.label).toBe('Board transport: Rhino');
    expect(item.build(null)).toMatchObject({ kind: 'answer_ask', askId: ask.id, answer: { kind: 'destinations', moves: squad.models.map((m) => ({ modelId: m.id, to: { kind: 'inside', containerId: hull } })) } });
    expect(forBoarding(p, ask, hull)).toEqual([]);
    expect(forBoarding(p, null, squad.models[0]!.id)).toEqual([]);
  });

  it('the unit menu adds a grant of the chosen effect to the unit with one use', () => {
    const [hexA, hexB, growth] = [newId(), newId(), newId()];
    const choices = grantChoices([{ entryName: 'Growth', effectId: growth, duration: 'marker' }, { entryName: 'Hex', effectId: hexA, duration: 'once' }, { entryName: 'Hex', effectId: hexB, duration: 'once' }]);
    expect(choices.map((c) => c.label)).toEqual(['Growth', 'Hex (1 of 2)', 'Hex (2 of 2)']);
    const p = of();
    const item = byLabel(forUnit(p, squad.id, [], choices), 'Add a grant');
    expect([item.input, item.choices]).toEqual(['choice', ['Growth', 'Hex (1 of 2)', 'Hex (2 of 2)']]);
    const add = item.build('Hex (2 of 2)') as unknown as { unitId: string; grant: { effectId: string; entryName: string; casterUnitId: string; remainingUses: number } };
    expect([add.unitId, add.grant.effectId, add.grant.entryName, add.grant.casterUnitId, add.grant.remainingUses]).toEqual([squad.id, hexB, 'Hex', squad.id, 1]);
    expect(item.build('Nothing')).toBeNull();
    expect(label(forUnit(p, squad.id, []))).not.toContain('Add a grant');
  });
});
