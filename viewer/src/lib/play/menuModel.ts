// viewer/src/lib/play/menuModel.ts
// client/src/Core/MenuModel.cs, the play items (setup's are setup/edits.ts): every property is editable, each item one ordinary command
// through the same path automation uses. No admin mode.

import type { Ask, Command, GameEvent, GameInfo, Marker, ModelRelocation, Point3 } from '../protocol/wire.generated';
import type { ModelInPlay, Projection } from '../live/apply';
import type { MarkerLocks } from './markerLocks';
import type { Names } from './names';
import type { GrantChoice } from './ruleTexts';

export type MenuInput = 'none' | 'integer' | 'text' | 'pieceId' | 'tablePoint' | 'choice';

/** One menu entry: a label, what it asks the player for, and the command it becomes. */
export interface MenuItem {
  readonly label: string;
  readonly input: MenuInput;
  /** A Choice item's labels; the chosen one is its input. */
  readonly choices: readonly string[];
  /** The command the input builds; null when it does not parse. A local item (a marker lock) builds nothing and applies itself. */
  build(input: string | null): Command | null;
  readonly local?: () => void;
}

const item = (label: string, input: MenuInput, build: (s: string | null) => Command | null, choices: readonly string[] = []): MenuItem =>
  ({ label, input, choices, build });

/** C#'s int.TryParse on a typed number. */
const int = (s: string | null): number | null => {
  if (s === null || !/^\s*[+-]?\d+\s*$/.test(s)) return null;
  const n = Number(s);
  return Number.isSafeInteger(n) && n >= -2147483648 && n <= 2147483647 ? n : null;
};
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const id = (s: string | null): string | null => (s !== null && uuid.test(s.trim()) ? s.trim().toLowerCase() : null);
/** A TablePoint item's input: the table point the player clicked, as "x,y,z" with invariant numbers (MenuModel.Point). */
function point(s: string | null): Point3 | null {
  const parts = s?.split(',');
  if (parts === undefined || parts.length !== 3 || parts.some((x) => x.trim() === '')) return null;
  const [x, y, z] = parts.map(Number) as [number, number, number];
  return [x, y, z].every(Number.isFinite) ? { x, y, z } : null;
}
const present = (s: string | null): string | null => (s === null || s.trim() === '' ? null : s.trim());
const move = (m: ModelInPlay, to: ModelRelocation['to']): ModelRelocation => ({ modelId: m.id, from: m.location, to });
const modelsOf = (p: Projection, unitId: string): ModelInPlay[] => [...p.models.values()].filter((m) => m.unitId === unitId);

export function forUnit(p: Projection, unitId: string, actionNames: readonly string[], grantable: readonly GrantChoice[] = []): MenuItem[] {
  const items: MenuItem[] = [];
  const subject = { kind: 'unit', id: unitId } as const;
  for (const a of actionNames) items.push(item(`Mark as having taken ${a}`, 'none', () => ({ kind: 'record_action', unitId, action: a })));
  items.push(item('Clear the action record', 'none', () => ({ kind: 'clear_action_record', unitId })));
  items.push(item('Add a condition', 'text', (s) => { const c = present(s); return c === null ? null : { kind: 'set_condition', subject, condition: c, present: true }; }));
  const unit = p.units.get(unitId);
  for (const c of unit?.conditions ?? []) items.push(item(`Remove ${c}`, 'none', () => ({ kind: 'set_condition', subject, condition: c, present: false })));
  items.push(item('Set wounds this activation', 'integer', (s) => { const n = int(s); return n === null ? null : { kind: 'set_wounds_this_activation', unitId, wounds: n }; }));
  if (grantable.length > 0)                                         // one use, the unit as its own grantor: a hand edit like any other
    items.push(item('Add a grant', 'choice', (s) => {
      const c = grantable.find((x) => x.label === s);
      return c === undefined ? null : { kind: 'add_grant', unitId, grant: { id: crypto.randomUUID(), effectId: c.effectId, entryName: c.entryName, casterUnitId: unitId, remainingUses: 1 } };
    }, grantable.map((c) => c.label)));
  for (const g of unit?.grants ?? []) items.push(item(`Remove grant ${g.entryName}`, 'none', () => ({ kind: 'remove_grant', unitId, grantId: g.id })));
  const models = modelsOf(p, unitId);
  items.push(item('Move the unit to the shelf', 'none', () => ({ kind: 'move_models', moves: models.map((m) => move(m, { kind: 'shelf' })) })));
  return items;
}

export function forModel(p: Projection, modelId: string, actionNames: readonly string[]): MenuItem[] {
  const model = p.models.get(modelId)!;
  const subject = { kind: 'model', id: modelId } as const;
  const items: MenuItem[] = [
    item('Set wounds', 'integer', (s) => { const n = int(s); return n === null ? null : { kind: 'set_wounds', modelId, wounds: n }; }),
    item('Set spell tokens', 'integer', (s) => { const n = int(s); return n === null ? null : { kind: 'set_spell_tokens', modelId, tokens: n }; }),
    item('Add a condition', 'text', (s) => { const c = present(s); return c === null ? null : { kind: 'set_condition', subject, condition: c, present: true }; }),
    item('Move to the shelf', 'none', () => ({ kind: 'move_models', moves: [move(model, { kind: 'shelf' })] })),
    item('Put inside a container', 'pieceId', (s) => { const c = id(s); return c === null ? null : { kind: 'move_models', moves: [move(model, { kind: 'inside', containerId: c })] }; }),
  ];
  for (const c of model.conditions) items.push(item(`Remove ${c}`, 'none', () => ({ kind: 'set_condition', subject, condition: c, present: false })));
  // "move to the shelf or back", "put inside a container or take out": coming back is one relocation to a table placement, and the
  // position comes from the next click on the table.
  if (model.location.kind !== 'table') items.push(backOnTable(model, model.location.kind === 'inside' ? 'Take out of the container, here' : 'Place on the table, here'));
  items.push(...forUnit(p, model.unitId, actionNames).filter((i) => i.label.startsWith('Mark') || i.label === 'Clear the action record'));
  return items;
}

const backOnTable = (model: ModelInPlay, label: string): MenuItem => item(label, 'tablePoint', (s) => {
  const at = point(s);
  return at === null ? null : { kind: 'move_models', moves: [move(model, { kind: 'table', placement: { position: at, yaw: { degrees: 0 } } })] };
});

/** A shelf or container model has no node to right-click, so the team panel lists each one: the click's table point places it. */
export function offTable(p: Projection, teamId: string, n: Names): MenuItem[] {
  return [...p.models.values()].filter((m) => m.location.kind !== 'table' && n.teamOfModel(m.id) === teamId)
    .map((m) => backOnTable(m, m.location.kind === 'inside' ? `Take ${n.model(m.id)} out of the container, here` : `Place ${n.model(m.id)} on the table, here`));
}

/** The condition the server reads as a unit kept off the deployment (setup phase → Ambush and Scout), and the properties whose units may take it. */
export const SET_ASIDE = 'set-aside';
const SETS_ASIDE = ['Ambush', 'Scout'];

/**
 * The roster's right-click on a unit of my team while playing. Every model on the shelf unwounded (never deployed): Set aside / Return to
 * deployment when it carries one of SETS_ASIDE, then Put in transport per Transport model the server names for it (ModelFacts.DeployInside;
 * passengers spec). Inside a container still on the shelf: Take out of transport. The server judges each hand move.
 */
export function forRosterUnit(game: GameInfo, p: Projection, unitId: string, myTeam: string | null, deployInside?: ReadonlyMap<string, readonly string[]>): MenuItem[] {
  const u = p.units.get(unitId);
  if (game.status !== 'playing' || myTeam === null || u === undefined) return [];
  if (game.participants.find((x) => x.id === u.participantId)?.teamId !== myTeam) return [];
  const models = modelsOf(p, unitId);
  if (models.length > 0 && models.every((m) => m.location.kind === 'inside' && p.models.get(m.location.containerId)?.location.kind === 'shelf'))
    return [item('Take out of transport', 'none', () => ({ kind: 'move_models', moves: models.map((m) => move(m, { kind: 'shelf' })) }))];
  if (models.some((m) => m.location.kind !== 'shelf' || m.wounds > 0)) return [];   // wounded on the shelf: removed, not waiting
  const items: MenuItem[] = [];
  const subject = { kind: 'unit', id: unitId } as const;
  if ([...u.definition.properties, ...u.definition.models.flatMap((s) => s.properties)].some((x) => SETS_ASIDE.includes(x.name))) {
    const aside = u.conditions.has(SET_ASIDE);
    items.push(item(aside ? 'Return to deployment' : 'Set aside', 'none', () => ({ kind: 'set_condition', subject, condition: SET_ASIDE, present: !aside })));
  }
  for (const c of deployInside?.get(unitId) ?? []) {
    const hull = p.models.get(c);
    const transport = hull === undefined ? undefined : p.units.get(hull.unitId);
    if (transport !== undefined)
      items.push(item(`Put in transport: ${transport.definition.name}`, 'none', () => ({ kind: 'move_models', moves: models.map((m) => move(m, { kind: 'inside', containerId: c })) })));
  }
  return items;
}

/**
 * The table's right-click on a model my open move Ask moves, while it offers containers to enter (MoveContainment.enterable: the rules
 * decide reach; p.15 "only one model needs to reach the Transport for everyone to embark"): Board transport per container, answering
 * the Ask with every model inside it, as a click on the container does.
 */
export function forBoarding(p: Projection, ask: Ask | null, modelId: string): MenuItem[] {
  const legal = ask?.legal;
  if (ask === null || legal === undefined || legal.kind !== 'move_region' || legal.containment === undefined || legal.containment === null
    || legal.containment.enterable.length === 0 || !legal.modelIds.includes(modelId)) return [];
  const region = legal;
  return legal.containment.enterable.filter((c) => p.models.has(c)).map((c) =>
    item(`Board transport: ${p.units.get(p.models.get(c)!.unitId)!.definition.name}`, 'none', () => ({
      kind: 'answer_ask', askId: ask.id,
      answer: { kind: 'destinations', continues: false, moves: region.modelIds.map((m) => move(p.models.get(m)!, { kind: 'inside', containerId: c })) },
    })));
}

export function forTeam(_game: GameInfo, _p: Projection, teamId: string): MenuItem[] {
  return [
    item('Set score', 'integer', (s) => { const n = int(s); return n === null ? null : { kind: 'set_score', teamId, score: n }; }),
    item('Make this the active team', 'none', () => ({ kind: 'set_active_team', teamId })),
    item('Set the round', 'integer', (s) => { const n = int(s); return n === null ? null : { kind: 'set_round', round: n }; }),
  ];
}

/**
 * Remove a marker placed in play (a setup row is never removable, moved or not). Any objective, from setup or play, can be given to a
 * team or made neutral: the override of who controls it (objectives spec → The menu). With locks, an objective in play ends with its
 * local Unlock or Lock item.
 */
export function forMarker(game: GameInfo, p: Projection, markerId: string, locks?: MarkerLocks): MenuItem[] {
  const items: MenuItem[] = [];
  if (p.markers.has(markerId) && p.board.table.markers.every((m) => m.id !== markerId)) items.push(item('Remove', 'none', () => ({ kind: 'remove_marker', markerId })));
  const marker = p.markers.get(markerId) ?? p.board.table.markers.find((m) => m.id === markerId);
  if (marker?.kind !== 'objective') return items;
  for (const team of [...game.teams].sort((a, b) => a.ordinal - b.ordinal))
    items.push(item(`Give to Team ${team.ordinal}`, 'none', () => ({ kind: 'set_objective_control', markerId, teamId: team.id })));
  items.push(item('Make neutral', 'none', () => ({ kind: 'set_objective_control', markerId })));
  const lock = locks?.itemFor(game.status, p, markerId);
  if (lock !== null && lock !== undefined) items.push({ label: lock.label, input: 'none', choices: [], build: () => null, local: () => lock.apply() });
  return items;
}

/** Abandon the top frame: pops it and everything above; applied effects stay applied. */
export function forFrame(p: Projection): MenuItem[] {
  const top = p.stack[p.stack.length - 1];
  return top === undefined ? [] : [item(`Abandon ${top.sequence}`, 'none', () => ({ kind: 'abandon_sequence', frameId: top.id }))];
}

/** The action names the client has seen: every action recorded in the log and each unit's current record. The rows themselves never reach
 * the client, and the activate Ask's option labels are display text, never parsed. */
export function knownActionNames(p: Projection, log: readonly GameEvent[]): string[] {
  const names: string[] = [];
  for (const e of log) if (e.payload.kind === 'action_recorded') names.push(e.payload.action);
  for (const u of p.units.values()) names.push(...u.actions);
  return [...new Set(names)];
}

/** GameScreen.Play.Token: Alt+right-click on the table drops a labelled chip. */
export const token = (at: Point3): MenuItem => item('Drop a token here', 'text', (label) => ({
  kind: 'place_marker',
  marker: { id: crypto.randomUUID(), kind: 'token', position: at, yaw: { degrees: 0 }, diameter: 1, ...(label === null ? {} : { label }), visual: { builtin: 'chip' } } satisfies Marker,
}));
