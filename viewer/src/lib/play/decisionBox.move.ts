// viewer/src/lib/play/decisionBox.move.ts
// client/src/Hud/DecisionBox.Move.cs: the move half of the decision box (a move Ask's ghosts, allowance, contacts, gate, commit, stop,
// embark and refused commits) and deployment placement. The spec's cuts: no reach outline (MoveRegions is not ported), so a target is not
// clamped, the ruler is straight, the allowance is the Ask's and the difficult cap is the server's to judge; a disembark or a shelf placement
// starts its ghosts in rings about the container (DeployView.around) and the server judges p.15. Every ghost is a Placement in
// `box.ghosts`: its position and the facing the player gave it, which the answer carries.

import type { Geometry, XY } from '../geometry';
import type { Projection } from '../live/apply';
import type { Enforcement, ModelRelocation, MoveRegion, Placement, Point3, Yaw } from '../protocol/wire.generated';
import type { AskView } from './askView';
import { allWords, move as contactsOfMove, placement as contactsOfPlacement } from './contactCheck';
import { CommitHold } from './commitHold';
import { type Control, type DecisionBox, type MoveHooks } from './decisionBox';
import { stopLabel } from './decisionView';
import { around, layout, type MidDeploy } from './deployView';
import { GhostStash } from './ghostStash';
import type { Held } from './modelDrag';
import { gate as gateOf, isDisembark, isShelfPlacement, read, usedInFrame, type MoveGate, type RulerReadout } from './moveRuler';
import type { Names } from './names';
import type { PendingDrops } from './pendingDrops';
import type { Violations } from './violations';

export interface MoveDeps {
  geometry(): Geometry | null;
  violations: Violations;
  enforcement(): Enforcement;
  /** A commit's drops, drawn where they were dropped until the move's event (the box unhooks on send, which clears its ghosts). */
  drops?: PendingDrops;
}

export type MoveBoxHooks = MoveHooks & {
  /** The rings the board draws: each listed model's start and the Ask's allowance (ShowRegions' ids). */
  allowance(): { readonly centre: XY; readonly radius: number }[];
  /** One readout per held proposing model from its origin, for the rulers. */
  readouts(held: Held | null): { modelId: string; from: XY; to: XY; readout: RulerReadout }[];
  /** TableInput.PickedUp: the rings follow the models now in hand. */
  picked(ids: readonly string[]): void;
  /** TableInput.Region: the open move's Ask, null outside one. */
  region(): MoveRegion | null;
};

const fixed = (n: number): string => n.toFixed(1);
const xy = (p: { x: number; y: number }): XY => ({ x: p.x, y: p.y });
const button = (id: string, label: string, disabled = false): Control => ({ kind: 'button', id, label, disabled });
const reasonOf = (text: string | null): Control => ({ kind: 'reason', id: 'reason', text: text ?? '' });

/** DecisionBox.Resolve: what a table click names in a list of model ids: the clicked id itself, or the model of the clicked unit. */
function resolve(p: Projection, clicked: string, ids: readonly string[]): string | null {
  if (ids.includes(clicked)) return clicked;
  const unit = p.models.get(clicked)?.unitId;
  if (unit !== undefined && ids.includes(unit)) return unit;
  if (p.units.has(clicked)) for (const id of ids) if (p.models.get(id)?.unitId === clicked) return id;
  return null;
}

export function moveHooks(deps: MoveDeps): MoveBoxHooks {
  const hold = new CommitHold();
  const stash = new GhostStash();
  let regate: (() => void) | null = null;
  let verdict: { models: readonly string[]; reason: string } | null = null;
  let rings: { ids: string[]; legal: MoveRegion; p: Projection } | null = null;
  let minimum: number | null = null;
  let moving: { legal: MoveRegion; p: Projection; allowance: number | null } | null = null;

  const marked = (ids: Iterable<string>): Map<string, string> => {
    const out = new Map<string, string>();
    for (const id of ids) { const m = deps.violations.marked.get(id); if (m !== undefined) out.set(id, m); }
    return out;
  };

  const applyVerdict = (): void => {
    if (verdict === null) return;
    const v = verdict;
    verdict = null;
    deps.violations.refused(v.models, v.reason);
    regate?.();
  };

  /** The commit's disabled state and the reason line, read again after every gate. */
  const show = (box: DecisionBox, gate: MoveGate, ids: readonly string[]): void => {
    for (const id of ids) box.replace(id, (c) => (c.kind === 'button' ? { ...c, disabled: !gate.allowed } : c));
    box.replace('reason', reasonOf(gate.reason));
  };

  const ghostMaps = (box: DecisionBox) => {
    const at = new Map<string, Point3>(), yaws = new Map<string, Yaw>();
    for (const [id, g] of box.ghosts) { at.set(id, g.position); yaws.set(id, g.yaw); }
    return { at, yaws };
  };

  function restore(box: DecisionBox, key: string): void {
    const kept = stash.take(key);
    if (kept === null) return;
    for (const [id, ghost] of kept) {
      const now = box.ghosts.get(id);
      if (now === undefined) continue;
      box.ghosts.set(id, { position: ghost.position, yaw: ghost.yaw ?? now.yaw });
    }
    regate?.();
  }

  function renderMove(box: DecisionBox, mine: AskView, p: Projection, n: Names): void {
    const ask = mine.ask, legal = ask.legal as MoveRegion;
    rings = null; moving = null; minimum = null;
    if (legal.modelIds.length === 0) return;                               // nothing to move; the server closes the Ask
    const lead = p.models.get(legal.modelIds[0]!);
    if (lead === undefined) return;
    const size = p.board.table.size;
    let allowance: number | null = null;
    let note = '';
    let start: () => void;
    box.proposing = new Set(legal.modelIds);
    if (isDisembark(legal)) {
      // p.15 "fully within 6" of it when exiting": the server judges it; the ghosts start in rings about the container.
      const containerId = legal.containment!.exitingId!;
      const container = p.models.get(containerId);
      if (container === undefined || container.location.kind !== 'table') {
        box.setTexts({ note: 'The container is no longer on the table.' });
        box.glow([]); box.proposing = new Set();
        return;
      }
      const at = container.location.placement;
      const within = legal.containment!.exitWithin ?? 0;
      const fromShelf = isShelfPlacement(legal, p);                       // a Spawn or Split reserve: the Ask's prompt says what is placed
      note = fromShelf ? 'Drag them into place, then commit.'
        : `Disembark: place every model fully within ${fixed(within)}" of ${n.piece(containerId)}. Drag them out, then commit.`;
      start = () => {
        box.ghosts.clear();
        for (const r of around(legal.modelIds.map((id) => p.models.get(id)!), at, container.slot.base, size, legal.separation))
          if (r.to.kind === 'table') box.ghosts.set(r.modelId, r.to.placement);
      };
    } else {
      if (lead.location.kind !== 'table') { box.proposing = new Set(); return; }
      const frame = ask.frameId === undefined ? undefined : p.stack.find((f) => f.id === ask.frameId);
      const used = frame === undefined ? 0 : usedInFrame(frame, lead.id);
      // The Ask's allowance is already what this segment has left: never subtract `used` again.
      const remaining = Math.max(0, legal.allowance);
      allowance = remaining;
      minimum = legal.minDistance ?? null;
      note = legal.minDistance === undefined ? `Move up to ${fixed(remaining)}" (used ${fixed(used)}") · Alt: place freely.`
        : `Move ${fixed(legal.minDistance)}" to ${fixed(remaining)}" (used ${fixed(used)}") · Alt: place freely.`;
      rings = { ids: [lead.id], legal, p };
      // Drawn and committed agree: a rebuilt Ask (a refusal) or an Esc starts from the server's positions.
      start = () => {
        box.ghosts.clear();
        for (const id of legal.modelIds) { const l = p.models.get(id)?.location; if (l?.kind === 'table') box.ghosts.set(id, l.placement); }
      };
    }
    moving = { legal, p, allowance };
    start();
    box.handlers.onCancel = () => { start(); deps.violations.clear(); regate?.(); return true; };
    note += offerEmbark(box, ask, legal, p, n);
    box.setTexts({ note });

    const relocate = (continues: boolean): void => {
      if (!current.allowed) return;
      const moves: ModelRelocation[] = legal.modelIds.map((id) => {
        const from = p.models.get(id)!.location;
        const ghost = box.ghosts.get(id) ?? (from.kind === 'table' ? from.placement : ZERO);
        return { modelId: id, from, to: { kind: 'table', placement: { position: ghost.position, yaw: facingOf(p, legal, id, ghost) } } };
      });
      hold.remember(ask.id, moves);
      const token = box.answer(ask, { kind: 'destinations', moves, continues });
      hold.sent(token);
      deps.drops?.add(token, moves);
    };
    box.handlers.onEnter = () => relocate(false);
    box.add(button('commit', 'Commit move (Enter)'), () => relocate(false));
    const ids = ['commit'];
    const stop = legal.stoppable && !isDisembark(legal) ? stopLabel(p, legal) : null;
    if (stop !== null) {
      box.handlers.onStop = () => relocate(true);
      box.add(button('stop', `${stop} (C)`), () => relocate(true));
      ids.push('stop');
    }
    box.add(reasonOf(null));
    // Any ghost out of range is counted and each contact named; under strict the commit and the stop wait until none is.
    let current: MoveGate = { allowed: true, reason: null };
    regate = () => {
      const g = deps.geometry();
      const readouts: RulerReadout[] = [];
      if (allowance !== null)
        for (const id of legal.modelIds) {
          const l = p.models.get(id)?.location, ghost = box.ghosts.get(id);
          if (l?.kind === 'table' && ghost !== undefined) readouts.push(read(xy(l.placement.position), xy(ghost.position), allowance, minimum));
        }
      if (g !== null)
        deps.violations.measure(legal.modelIds, allWords(contactsOfMove(g, p, legal, new Map(box.ghosts), size), legal.separation, n.unit.bind(n)));
      current = gateOf(readouts, deps.enforcement(), marked(legal.modelIds));
      show(box, current, ids);
    };
    regate();
    restore(box, ask.id);
    applyVerdict();
  }

  /** AskAnswers.FacingOf: a model on the table takes its ghost's facing; a disembarking one whatever fits at the drop (p.7; the container's facing
   *  when none does or the geometry is not loaded); a reserve placed from the shelf its ghost's. */
  function facingOf(p: Projection, legal: MoveRegion, id: string, ghost: Placement): Yaw {
    const model = p.models.get(id)!;
    switch (model.location.kind) {
      case 'table': return ghost.yaw;
      case 'shelf': return ghost.yaw;
      case 'inside': {
        const container = p.models.get(model.location.containerId);
        if (container === undefined || container.location.kind !== 'table') return { degrees: 0 };
        const g = deps.geometry();
        if (g === null) return container.location.placement.yaw;
        const body = (m: typeof model) => ({ base: m.slot.base, height: m.slot.height });
        return g.exitYaw(body(model), { body: body(container), placement: container.location.placement }, legal.containment?.exitWithin ?? 0,
          xy(ghost.position), p.board.table.terrain, legal.impassableVolumeIds, p.board.table.size) ?? container.location.placement.yaw;
      }
    }
  }

  /** p.15: "only one model needs to reach the Transport for everyone to embark". A click on a listed container answers the whole unit into it. */
  function offerEmbark(box: DecisionBox, ask: AskView['ask'], legal: MoveRegion, p: Projection, n: Names): string {
    const enterable = legal.containment?.enterable ?? [];
    if (enterable.length === 0) return '';
    box.eligible = new Set(enterable);
    box.handlers.onTarget = (clicked) => {
      const container = resolve(p, clicked, enterable);
      if (container === null || !p.models.has(container)) return;
      box.answer(ask, {
        kind: 'destinations', continues: false,
        moves: legal.modelIds.map((id): ModelRelocation => ({ modelId: id, from: p.models.get(id)!.location, to: { kind: 'inside', containerId: container } })),
      });
    };
    return ` Or click a container in reach to embark: ${enterable.map((id) => n.piece(id)).join(', ')}.`;
  }

  // ---- deployment placement ----

  function startDeploy(box: DecisionBox, mid: MidDeploy, n: Names): void {
    const unit = mid.unitId;
    box.deployUnit = unit;
    box.setTexts({ title: `Place the models of ${n.unit(unit)}`, hint: 'Click the table where the unit goes' });
    box.placementPoint = (at) => dropDeploy(box, unit, at);                // its own slot: a menu point pick cannot take it
    const p = box.host.state().projection;
    const kept = stash.take(unit);
    if (kept !== null && p !== null) {                                     // the placement a reconnect interrupted, for the models still on the shelf
      const back: ModelRelocation[] = [...kept]
        .filter(([id]) => p.models.get(id)?.unitId === unit && p.models.get(id)?.location.kind === 'shelf')
        .map(([id, g]) => ({ modelId: id, from: { kind: 'shelf' }, to: { kind: 'table', placement: { position: g.position, yaw: g.yaw ?? { degrees: 0 } } } }));
      if (back.length > 0) showDeploy(box, back);
    }
  }

  function dropDeploy(box: DecisionBox, unit: string, at: { x: number; y: number; z: number }): void {
    const p = box.host.state().projection;
    if (box.deployUnit !== unit || p === null) return;                     // the deploy moved on before the click
    const models = [...p.models.values()].filter((m) => m.unitId === unit && m.location.kind === 'shelf').sort((a, b) => a.slot.ordinal - b.slot.ordinal);
    const placed = layout(models, at, p.board.table.size);
    if (placed.length === 0) return;                                       // nothing fits there: the pick stands for another click
    showDeploy(box, placed);
  }

  function showDeploy(box: DecisionBox, placed: readonly ModelRelocation[]): void {
    box.placementPoint = null;
    box.ghosts.clear();
    for (const r of placed) if (r.to.kind === 'table') box.ghosts.set(r.modelId, r.to.placement);
    box.proposing = new Set(placed.map((r) => r.modelId));
    box.setTexts({ hint: 'Drag to place · Shift: one model · 1–5 while holding: rows · Enter: place · Esc: clear' });
    let current: MoveGate = { allowed: true, reason: null };
    const place = (): void => {
      const st = box.host.state();
      const unit = box.deployUnit;
      if (st.projection === null || unit === null || !current.allowed) return;
      const p = st.projection;
      const moves: ModelRelocation[] = [...box.ghosts].map(([id, placement]) => ({ modelId: id, from: p.models.get(id)!.location, to: { kind: 'table', placement } }));
      hold.remember(unit, moves);
      hold.sent(box.sendCommand({ kind: 'move_models', moves }, (message) => `Could not place the models: ${message}`));
    };
    box.handlers.onEnter = place;
    box.handlers.onCancel = () => { box.rebuild(); return true; };          // the rebuild drops the deploy unit, so the render arms a fresh click
    box.add(button('place', 'Place (Enter)'), place);
    box.add(reasonOf(null));
    // A ghost on another model's base is named; under strict Place waits until none is.
    regate = () => {
      const st = box.host.state();
      const g = deps.geometry();
      if (st.projection === null || st.game === null) return;
      const ids = [...box.ghosts.keys()];
      if (g !== null) deps.violations.measure(ids, allWords(contactsOfPlacement(g, st.projection, new Map(box.ghosts)), undefined, (id) => st.projection!.units.get(id)?.definition.name ?? id));
      current = gateOf([], deps.enforcement(), marked(ids));
      show(box, current, ['place']);
    };
    regate();
    applyVerdict();
    box.render();
  }

  return {
    renderMove,
    startDeploy,
    keepGhosts(box) {
      const { at, yaws } = ghostMaps(box);
      if (at.size === 0) return;
      if (box.renderedAsk !== null) stash.keep(box.renderedAsk, at, yaws);
      else if (box.deployUnit !== null) stash.keep(box.deployUnit, at, yaws);
    },
    refused(_box, token, detail) {
      deps.drops?.refuse(token);
      const held = hold.refused(token);
      if (held === null) return;
      stash.keep(held.key, held.at, held.yaws);                              // the re-render puts them back and marks them
      verdict = { models: held.moved, reason: detail };
    },
    failed(_box, token) { deps.drops?.refuse(token); },
    dragged(_box, modelId) {
      deps.violations.moved([modelId]);                                     // a refused commit's reason lapses once its models move
      regate?.();
    },
    unhook() {
      regate = null; rings = null; moving = null; minimum = null;
      deps.violations.clear();                                              // a refused commit's marks come back with its ghosts (applyVerdict)
    },
    allowance() {
      if (rings === null || moving === null || moving.allowance === null) return [];
      const r = rings;
      return r.ids.flatMap((id) => {
        const l = r.p.models.get(id)?.location;
        return l?.kind === 'table' ? [{ centre: xy(l.placement.position), radius: moving!.allowance! }] : [];
      });
    },
    readouts(held) {
      if (held === null || moving === null || moving.allowance === null) return [];
      const m = moving;
      return held.ids.flatMap((id) => {
        const origin = held.origins.get(id), end = held.ends.get(id);
        if (origin === undefined || end === undefined || !m.legal.modelIds.includes(id)) return [];
        const from = xy(origin.position), to = xy(end.lands);
        return [{ modelId: id, from, to, readout: read(from, to, m.allowance, minimum) }];
      });
    },
    region() { return moving?.legal ?? null; },
    picked(ids) {
      if (rings !== null) rings = { ...rings, ids: [...new Set([...rings.ids.slice(0, 1), ...ids])].filter((id) => rings!.legal.modelIds.includes(id)) };
    },
  };
}

const ZERO: Placement = { position: { x: 0, y: 0, z: 0 }, yaw: { degrees: 0 } };
