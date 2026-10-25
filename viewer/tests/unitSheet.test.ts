import { describe, expect, it } from 'vitest';
import { Applier } from '../src/lib/live/apply';
import { Names } from '../src/lib/play/names';
import { RuleTexts } from '../src/lib/play/ruleTexts';
import { reserveOf } from '../src/lib/play/rosterReserve';
import { unitSheetOf, type SheetChip } from '../src/lib/play/unitSheet';
import type { Board, EventPayload, GameInfo, ModelSlot, Unit, Weapon } from '../src/lib/protocol/wire.generated';
import { BoardSample, Model, Unit as UnitId, grant } from './logSamples';
import { P1, at, boardOf, ev, gameOf, slotOf, unitOf, weaponOf } from './play';

// UnitSheetTests, UnitSheetLoadoutTests and ReserveLabelTests.
const catalog = RuleTexts.from([{ name: 'Tough', kind: 'model', played: true, takesParameter: false, ruleText: 'Must take X wounds to be killed.' }]);
const noFacts: ReadonlyMap<string, number> = new Map();

function applier(board: Board, game: GameInfo, ...history: EventPayload[]): Applier {
  const a = new Applier(board, game);
  history.forEach((p, i) => a.apply(ev(i + 1, p, P1)));
  return a;
}
const sheetOf = (a: Applier, game: GameInfo, unit: string, maxWounds: ReadonlyMap<string, number>, rules = catalog) =>
  unitSheetOf(a.projection, new Names(game, a.projection), unit, rules, maxWounds);

const ranged = (name: string, range: string, ...props: { name: string; parameter?: number }[]): Weapon =>
  ({ ...weaponOf(name), stats: { Range: range, Attacks: '1' }, properties: props });
const melee = (): Weapon => ({ ...weaponOf('CCW'), stats: { Attacks: '2' } });
const slot = (ordinal: number, ...weapons: Weapon[]): ModelSlot => slotOf({ name: `Trooper ${ordinal}`, ordinal, weapons });
const squad: Unit = {
  ...unitOf('Squad', [slot(1, ranged('Rifle', '24"'), melee()), slot(2, ranged('Rifle', '24"'), melee()), slot(3, ranged('Plasma Gun', '24"', { name: 'AP', parameter: 4 }), melee())]),
  stats: { Quality: '4+', Defense: '5+' },
};
const game = gameOf();
const squadBoard = boardOf([{ participantId: P1, units: [squad] }]);

// The sample unit (Battle Brothers: Fearless; Battle Brother: Tough(3); Heavy Rifle with AP(1), Range and Attacks).
const sampleUnit: Unit = {
  ...BoardSample.armies[0]!.snapshot.units[0]!,
  stats: { Quality: '3+', Defense: '3+' }, properties: [{ name: 'Fearless' }],
  models: [{ ...BoardSample.armies[0]!.snapshot.units[0]!.models[0]!, properties: [{ name: 'Tough', parameter: 3 }],
    weapons: [{ ...BoardSample.armies[0]!.snapshot.units[0]!.models[0]!.weapons[0]!, stats: { Range: '24"', Attacks: '1' }, properties: [{ name: 'AP', parameter: 1 }] }] }],
};
const sampleBoard = boardOf([{ participantId: P1, units: [sampleUnit] }]);

describe('UnitSheet', () => {
  it('the card reads the snapshot and the catalog', () => {
    const s = sheetOf(applier(sampleBoard, game), game, UnitId, new Map([[Model, 3]]));
    expect([s.unitId, s.team, s.name, s.alive, s.size]).toEqual([UnitId, 'Team 1', 'Battle Brothers', 1, 1]);
    expect([s.quality, s.defense]).toEqual(['3+', '3+']);
    expect(s.chips).toEqual([]);
    // Fearless is the unit's, Tough(3) the slot's. Only Tough has a catalog entry here, so Fearless reads as not automated.
    expect(s.rules.map((r) => [r.label, r.text, r.notAutomated])).toEqual([['Fearless', null, true], ['Tough(3)', 'Must take X wounds to be killed.', false]]);
    expect(s.weapons).toHaveLength(1);
    const rifle = s.weapons[0]!;
    expect([rifle.name, rifle.count, rifle.range, rifle.attacks, rifle.ap]).toEqual(['Heavy Rifle', 1, '24"', '1', '1']);
    expect(rifle.rules).toEqual([]);                                   // AP(1) moved to its column
    expect([rifle.apRule!.label, rifle.apRule!.notAutomated]).toEqual(['AP(1)', true]);
    expect(s.models).toHaveLength(1);
    const model = s.models[0]!;
    expect([model.id, model.label, model.left, model.max, model.removed]).toEqual([sampleUnit.models[0]!.id, 'Battle Brother', 3, 3, false]);
  });

  it('weapons group by name with a count and a melee weapon has no range', () => {
    const s = sheetOf(applier(squadBoard, game), game, squad.id, noFacts);
    expect(s.weapons.map((w) => [w.name, w.count, w.range, w.attacks])).toEqual([['Rifle', 2, '24"', '1'], ['CCW', 3, '-', '2'], ['Plasma Gun', 1, '24"', '1']]);
    expect(s.weapons.map((w) => w.ap)).toEqual(['-', '-', '4']);
    expect(s.weapons[2]!.rules).toEqual([]);
    expect([s.quality, s.defense, s.alive, s.size]).toEqual(['4+', '5+', 3, 3]);
  });

  it('a removed model is struck through and its weapons leave the table', () => {
    const gunner = squad.models[2]!.id;
    const s = sheetOf(applier(squadBoard, game, { kind: 'wounds_set', modelId: gunner, wounds: 1 }), game, squad.id, noFacts);   // on the shelf with its one wound taken
    expect([s.alive, s.size]).toEqual([2, 3]);
    expect(s.weapons.map((w) => [w.name, w.count])).toEqual([['Rifle', 2], ['CCW', 2]]);
    expect(s.models.map((m) => [m.left, m.max, m.removed])).toEqual([[1, 1, false], [1, 1, false], [0, 1, true]]);
  });

  it('wounds left come from the facts and a shelved model short of its max is not removed', () => {
    const wound: EventPayload = { kind: 'wounds_set', modelId: sampleUnit.models[0]!.id, wounds: 1 };
    const s = sheetOf(applier(sampleBoard, game, wound), game, UnitId, new Map([[sampleUnit.models[0]!.id, 3]]));
    expect([s.models[0]!.left, s.models[0]!.max, s.models[0]!.removed]).toEqual([2, 3, false]);
    expect(s.alive).toBe(1);
    const unknown = sheetOf(applier(sampleBoard, game, wound), game, UnitId, noFacts);   // no fact: one wound
    expect([unknown.models[0]!.left, unknown.models[0]!.max, unknown.models[0]!.removed]).toEqual([0, 1, true]);
  });

  it('chips show the action conditions spell tokens and grants', () => {
    const a = applier(sampleBoard, game,
      { kind: 'action_recorded', unitId: UnitId, action: 'Advance' },
      { kind: 'condition_set', subject: { kind: 'unit', id: UnitId }, condition: 'shaken', present: true },
      { kind: 'spell_tokens_set', modelId: sampleUnit.models[0]!.id, tokens: 2 },
      { kind: 'grant_added', unitId: UnitId, grant });
    const chips: SheetChip[] = [
      { kind: 'activated', text: 'Activated: Advance' }, { kind: 'condition', text: 'Shaken', condition: 'shaken' },
      { kind: 'spell_tokens', text: '2 spell tokens' }, { kind: 'grant', text: 'Regeneration from Battle Brothers · 1 use' },
    ];
    expect(sheetOf(a, game, UnitId, noFacts).chips).toEqual(chips);
  });

  it('an AP rule without a parameter stays in the rules', () => {
    const odd: Unit = unitOf('Odd', [slotOf({ name: 'Trooper 1', weapons: [ranged('Lance', '18"', { name: 'AP' }, { name: 'Rending' })] })]);
    const lance = sheetOf(applier(boardOf([{ participantId: P1, units: [odd] }]), game), game, odd.id, noFacts).weapons[0]!;
    expect([lance.ap, lance.apRule]).toEqual(['-', null]);
    expect(lance.rules.map((r) => r.label)).toEqual(['AP', 'Rending']);
  });

  // Until the catalog loads, or after its fetch failed, no rule is tagged: an unknown catalog says nothing about automation.
  it('before the catalog loads no rule is tagged', () => {
    const s = sheetOf(applier(sampleBoard, game), game, UnitId, noFacts, RuleTexts.empty);
    for (const r of [...s.rules, ...s.weapons.flatMap((w) => w.rules)]) expect(r.notAutomated).toBe(false);
  });

  it('an unplayed rule shows its text tagged not automated', () => {
    const rules = RuleTexts.from([{ name: 'Tough', kind: 'model', played: true, takesParameter: false, ruleText: 'Must take X wounds to be killed.' },
      { name: 'Fearless', kind: 'model', played: false, takesParameter: false, ruleText: 'Gets +1 to morale tests.' }]);
    const s = sheetOf(applier(sampleBoard, game), game, UnitId, new Map([[sampleUnit.models[0]!.id, 3]]), rules);
    expect(s.rules.map((r) => [r.label, r.text, r.notAutomated])).toContainEqual(['Fearless', 'Gets +1 to morale tests.', true]);
  });
});

describe('UnitSheetLoadout', () => {
  const w = (name: string): Weapon => ({ ...weaponOf(name), stats: { Range: '12"', Attacks: '1' } });
  const mk = (ordinal: number, name: string, stats: Record<string, string>, props: { name: string; parameter?: number }[], ...weapons: Weapon[]): ModelSlot =>
    slotOf({ name, ordinal, stats, properties: props, weapons });
  // As Army Forge imports: numbered slot names, an upgraded model, a joined hero with its own Quality and Defense.
  const mixed: Unit = {
    ...unitOf('Specialists', []), stats: { Quality: '5+', Defense: '5+' },
    models: [
      mk(1, 'Specialists 1', {}, [], w('CCW'), w('Railgun'), w('Bomb Drone')),
      mk(2, 'Specialists 2', {}, [], w('CCW'), w('Railgun')),
      mk(3, 'Specialists 3.1', {}, [], w('CCW'), w('Railgun')),
      mk(4, 'Orc Leader 1', { Quality: '4+', Defense: '5+' }, [{ name: 'Hero' }, { name: 'Tough', parameter: 3 }], w('CCW'), w('Rocket Launcher'), w('Rocket Launcher')),
    ],
  };
  const sheet = () => sheetOf(applier(boardOf([{ participantId: P1, units: [mixed] }]), game), game, mixed.id, new Map([[mixed.models[3]!.id, 3]]), RuleTexts.empty);

  it('a model carries its name without the number and its loadout', () => {
    const m = sheet().models;
    expect(m.map((x) => x.label)).toEqual(['Specialists', 'Specialists', 'Specialists', 'Orc Leader']);
    expect(m[0]!.loadout).toEqual(['CCW', 'Railgun', 'Bomb Drone']);
    // Hero marks the model rather than listing; a slot stat shows only where it differs from the card's badge.
    expect(m[3]!.loadout).toEqual(['CCW', 'Rocket Launcher ×2', 'Tough(3)', 'Quality 4+']);
    expect(m.map((x) => x.hero)).toEqual([false, false, false, true]);
  });

  it('models alike in name weapons rules and stats share a kind', () => {
    const m = sheet().models;
    expect(m[1]!.kind).toBe(m[2]!.kind);
    expect(m[0]!.kind).not.toBe(m[1]!.kind);
    expect(m[1]!.kind).not.toBe(m[3]!.kind);
  });
});

describe('ReserveLabel', () => {
  const m = (name: string): ModelSlot => slotOf({ name });
  const u = (name: string, slot: ModelSlot, reserve?: { property: string; of: string }): Unit => ({ ...unitOf(name, [slot]), reserve });
  const mother = m('Mother');
  const brood = u('Brood', mother);
  const spawned = u('Spawnlings', m('S'), { property: 'Spawn', of: mother.id });          // held at a model
  const copy = u('Second wave', m('W'), { property: 'Reinforcement', of: brood.id });      // held at a unit
  const plain = u('Plain', m('P'));
  const board = boardOf([{ participantId: P1, units: [brood, spawned, copy, plain] }]);

  it('the property and the unit it is held at whether a model or a unit', () => {
    const p = applier(board, game).projection;
    expect(reserveOf(p, p.units.get(spawned.id)!)).toBe('Reserve: Spawn (Brood)');
    expect(reserveOf(p, p.units.get(copy.id)!)).toBe('Reserve: Reinforcement (Brood)');
    expect(reserveOf(p, p.units.get(plain.id)!)).toBeNull();
  });

  // Placed by its property, the unit is no longer held off the table: no chip.
  it('a placed unit drops it', () => {
    const a = applier(board, game);
    a.apply(ev(1, { kind: 'models_moved', moves: [{ modelId: spawned.models[0]!.id, from: { kind: 'shelf' }, to: { kind: 'table', placement: at(10, 10) } }] }));
    expect(reserveOf(a.projection, a.projection.units.get(spawned.id)!)).toBeNull();
    expect(reserveOf(a.projection, a.projection.units.get(copy.id)!)).toBe('Reserve: Reinforcement (Brood)');
  });

  it('the card carries it as a chip', () => {
    const a = applier(board, game);
    const s = unitSheetOf(a.projection, new Names(game, a.projection), spawned.id, RuleTexts.empty, noFacts);
    expect(s.chips).toContainEqual({ kind: 'reserve', text: 'Reserve: Spawn (Brood)' });
  });
});

