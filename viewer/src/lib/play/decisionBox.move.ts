// viewer/src/lib/play/decisionBox.move.ts
// client/src/Hud/DecisionBox.Move.cs: the move half of the decision box (a move Ask's ghosts, allowance, contacts, gate, commit, stop,
// embark and refused commits) and deployment placement. The area drawn round each listed model is Godot's (MoveRegions.Bands through
// Geometry.moveBands, in its bands), and each ghost's ruler reads the path the server measures (Geometry.moveRoutes: MoveRegions.Route, the unit
// held to the cap once any path goes in or through difficult ground, MoveRegions.Allowance). The spec's cuts: a target is held only to the allowance
// on a straight line (ModelDrag), the allowance is the Ask's and the difficult cap is the server's to judge; a disembark or a shelf placement
// starts its ghosts in rings about the container (MoveRuler.exitStart) and the server judges p.15. Every ghost is a Placement in
// `box.ghosts`: its position and the facing the player gave it, which the answer carries.

import { placed, type Geometry, type MoveBands, type RouteFound, type RoutesRequest, type XY } from '../geometry';
import type { Projection } from '../live/apply';
import type { Enforcement, KeepAway, ModelRelocation, MoveRegion, Placement, Point3, Yaw } from '../protocol/wire.generated';
import type { AskView } from './askView';
import { allWords, move as contactsOfMove, placement as contactsOfPlacement } from './contactCheck';
import { withKeepAway } from './keepAwayArea';
import { coherencyOf, coherencyWarnings, NO_COHERENCY, withCoherency, type CoherencyPicture } from './coherencyView';
import { CommitHold } from './commitHold';
import { button, dim, type Control, type DecisionBox, type MoveHooks } from './decisionBox';
import { barOf, inches } from './decisionCard';
import { PLACING } from './cardActions';
import { stopLabel } from './decisionView';
import { layout, type MidDeploy } from './deployView';
import { stationary } from './modelDrag';
import { GhostStash } from './ghostStash';
import type { Held } from './modelDrag';
import { exitStart, exitWords, gate as gateOf, isDisembark, isShelfPlacement, read, readDistance, type MoveGate, type RulerReadout } from './moveRuler';
import { lines as warningLines, marks as warningMarks, pathOf, type ModelPath } from './movePaths';
import type { Names } from './names';
import type { PendingDrops } from './pendingDrops';
import type { Violations } from './violations';

export interface MoveDeps {
  geometry(): Geometry | null;
  violations: Violations;
  enforcement(): Enforcement;
  /** A commit's drops, drawn where they were dropped until the move's step or the answer's settling (the box unhooks on send, which clears
   *  its ghosts). */
  drops?: PendingDrops;
  /** The terrain piece a volume belongs to, by name (movePaths' volumeName), for the dangerous terrain gate line; "terrain" without it. */
  terrainName?(volumeId: string): string;
}

export type MoveBoxHooks = MoveHooks & {
  /** The rings the board draws: each listed model's start and the Ask's allowance (ShowRegions' ids). */
  allowance(): { readonly centre: XY; readonly radius: number }[];
  /** ShowRegions: each listed model's area in its bands, built once per model per Ask; empty without the geometry. */
  bands(): MoveBands[];
  /** One readout per held proposing model from its origin, for the rulers, with the path it is measured along when the geometry is loaded. */
  readouts(held: Held | null): { modelId: string; from: XY; to: XY; readout: RulerReadout; path?: readonly XY[] }[];
  /** TableInput.PickedUp: the rings follow the models now in hand. */
  picked(ids: readonly string[]): void;
  /** TableInput.Region: the open move's Ask, null outside one. */
  region(): MoveRegion | null;
  /** The open move's paths and footprints (MovePaths.Proposed), read again on every gate; empty outside one. */
  paths(): readonly ModelPath[];
  /** The placed ghosts' unit coherency (CoherencyView), read again on every gate: rings and a line per model out of it; none outside a placement. */
  coherency(): CoherencyPicture;
};

const xy = (p: { x: number; y: number }): XY => ({ x: p.x, y: p.y });
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
  let built: { legal: MoveRegion; byId: Map<string, MoveBands | null> } | null = null;
  let paths: readonly ModelPath[] = [];
  let coherency: CoherencyPicture = NO_COHERENCY;

  /** MoveRegions.Allowance and Route: each moved model's path from where it stands to its target, as the server measures it, and the unit's allowance
   *  (the Ask's, or its cap once any path goes in or through difficult ground; then each path is the shortest within it). The last answer is kept
   *  for the same targets, so a drag frame's gate and rulers ask once. */
  let lastRoutes: { key: string; legal: MoveRegion; value: { allowance: number; routes: Map<string, RouteFound> } } | null = null;
  const routesOf = (g: Geometry, p: Projection, legal: MoveRegion, targets: ReadonlyMap<string, XY>): { allowance: number; routes: Map<string, RouteFound> } => {
    const models = [...targets].flatMap(([id, to]) => {
      const m = p.models.get(id);
      if (m === undefined || m.location.kind !== 'table') return [];
      const from = m.location.placement.position;
      return Math.hypot(to.x - from.x, to.y - from.y) <= 1e-6 ? [] : [{ id, start: placed(m.slot, m.location.placement), to }];
    });
    const key = JSON.stringify(models.map((m) => [m.id, m.to.x, m.to.y]));
    if (lastRoutes !== null && lastRoutes.legal === legal && lastRoutes.key === key) return lastRoutes.value;
    const allowance = Math.max(0, legal.allowance);
    const request: RoutesRequest = {
      models, allowance, impassable: legal.impassableVolumeIds, difficult: legal.difficultVolumeIds, size: p.board.table.size,
      stationary: legal.passesModels ? [] : stationary(p, new Set(legal.modelIds)),
      ...(legal.difficultCap === undefined ? {} : { difficultCap: legal.difficultCap }),
      ...(legal.steepVolumeIds === undefined ? {} : { steep: legal.steepVolumeIds }),
      ...(legal.dangerousVolumeIds === undefined ? {} : { dangerous: legal.dangerousVolumeIds }),
      ...(legal.unstableVolumeIds === undefined ? {} : { unstable: legal.unstableVolumeIds }),
      ...(legal.unstableOver === undefined ? {} : { unstableOver: legal.unstableOver }),
    };
    let found = g.moveRoutes(request, p.board.table.terrain);
    let unit = allowance;
    if (legal.difficultCap !== undefined && legal.difficultCap < allowance && found.some((r) => r.entersDifficult)) {
      unit = legal.difficultCap;
      found = g.moveRoutes({ ...request, capped: true }, p.board.table.terrain);
    }
    const value = { allowance: unit, routes: new Map(found.map((r) => [r.id, r])) };
    lastRoutes = { key, legal, value };
    return value;
  };

  /** MoveRegions.ReachOf and Bands: the model's area from where it stands, by the Ask's parameters; null off the table. */
  const bandsOf = (g: Geometry, p: Projection, legal: MoveRegion, id: string): MoveBands | null => {
    const m = p.models.get(id);
    if (m === undefined || m.location.kind !== 'table') return null;
    return g.moveBands({
      start: placed(m.slot, m.location.placement), allowance: Math.max(0, legal.allowance), impassable: legal.impassableVolumeIds,
      difficult: legal.difficultVolumeIds, size: p.board.table.size, stationary: legal.passesModels ? [] : stationary(p, new Set(legal.modelIds)),
      ...(legal.difficultCap === undefined ? {} : { difficultCap: legal.difficultCap }),
      ...(legal.steepVolumeIds === undefined ? {} : { steep: legal.steepVolumeIds }),
      ...(legal.dangerousVolumeIds === undefined ? {} : { dangerous: legal.dangerousVolumeIds }),
      ...(legal.unstableVolumeIds === undefined ? {} : { unstable: legal.unstableVolumeIds }),
      ...(legal.unstableOver === undefined ? {} : { unstableOver: legal.unstableOver }),
      ...(legal.minDistance === undefined ? {} : { minDistance: legal.minDistance }),
    }, p.board.table.terrain);
  };

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
    for (const id of ids) box.replace(id, (c) => { if (c.kind !== 'button') return c; const { tooltip: _t, ...rest } = c; return { ...rest, ...dim(!gate.allowed, gate.reason ?? null) }; });
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
    rings = null; moving = null; minimum = null; paths = []; coherency = NO_COHERENCY;
    if (legal.modelIds.length === 0) return;                               // nothing to move; the server closes the Ask
    const lead = p.models.get(legal.modelIds[0]!);
    if (lead === undefined) return;
    const size = p.board.table.size;
    let allowance: number | null = null;
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
      const unitName = n.unit(lead.unitId);
      box.setHead(fromShelf ? { icon: 'army', title: 'Place', subject: unitName, prompt: ask.prompt }   // the Ask's prompt says what is placed
        : { icon: 'advance', title: 'Disembark', subject: unitName, prompt: `Fully within ${inches(within)} of ${n.piece(containerId)}` });
      box.pinned = new Set([containerId]);                                 // the transport (or the anchor) stays put: a drag over it takes a ghost or nothing
      // The ghosts start around the container, clear of it and within the exit reach (MoveRuler.ExitStart): a commit as they stand leaves it.
      start = () => {
        box.ghosts.clear();
        for (const r of exitStart(deps.geometry(), p, legal, size)) if (r.to.kind === 'table') box.ghosts.set(r.modelId, r.to.placement);
      };
    } else {
      if (lead.location.kind !== 'table') { box.proposing = new Set(); return; }
      // The Ask's allowance is already what this segment has left: never subtract `used` again.
      const remaining = Math.max(0, legal.allowance);
      allowance = remaining;
      minimum = legal.minDistance ?? null;
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
    box.setTexts({ note: offerEmbark(box, ask, legal, p, n) });
    // The bar: the farthest ghost over the allowance, a mark at the least; the words `Move up to 12"` go.
    if (allowance !== null) box.add({ kind: 'bar', id: 'bar', bar: null });

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
    box.add(reasonOf(null));
    box.add(button('commit', 'Commit', undefined, true, 'ready'), () => relocate(false));
    const ids = ['commit'];
    const stop = legal.stoppable && !isDisembark(legal) ? stopLabel(p, legal) : null;
    if (stop !== null) {
      box.handlers.onStop = () => relocate(true);
      box.add(button('stop', stop, undefined, false, stop.startsWith('Cast') ? 'cast' : 'hold'), () => relocate(true));
      ids.push('stop');
    }
    // Any ghost out of range is counted and each contact named; under strict the commit and the stop wait until none is.
    let current: MoveGate = { allowed: true, reason: null };
    regate = () => {
      const g = deps.geometry();
      const readouts: RulerReadout[] = [];
      let unit = allowance;
      if (allowance !== null) {
        const measured = g === null ? null : routesOf(g, p, legal, new Map([...box.ghosts].map(([id, ghost]) => [id, xy(ghost.position)])));
        if (measured !== null) unit = measured.allowance;
        // Each moved model's path and footprints, and a warning where its swept base crosses dangerous ground: never holding the commit.
        paths = measured === null ? [] : legal.modelIds.flatMap((id) => {
          const m = p.models.get(id), route = measured.routes.get(id);
          return m === undefined || m.location.kind !== 'table' || route === undefined ? [] : [pathOf(placed(m.slot, m.location.placement), route)];
        });
        for (const id of legal.modelIds) {
          const l = p.models.get(id)?.location, ghost = box.ghosts.get(id);
          if (l?.kind !== 'table' || ghost === undefined) continue;
          const route = measured?.routes.get(id);
          readouts.push(route === undefined ? read(xy(l.placement.position), xy(ghost.position), unit, minimum) : readDistance(route.distance, unit, minimum));
        }
      }
      // p.7 unit coherency, as the server measures it (CoherencyView): a mark where the server refuses it, else a warning.
      coherency = g === null ? NO_COHERENCY : coherencyOf(g, p, new Map(box.ghosts), box.host.state().coherency, (id) => p.models.get(id)?.slot.name ?? n.model(id), true);
      if (g !== null) {
        // A disembarking ghost the exit does not allow where it stands (p.15: beyond 6" of the transport) is marked as its contacts are.
        const contacts = allWords(contactsOfMove(g, p, legal, new Map(box.ghosts), size), legal.separation, n.unit.bind(n));
        for (const [id, words] of exitWords(g, p, legal, new Map(box.ghosts), size, n.piece.bind(n))) if (!contacts.has(id)) contacts.set(id, words);
        deps.violations.measure(legal.modelIds, withCoherency(withKeepAway(contacts, g, p, legal.keepAway ?? null, new Map(box.ghosts), n.unit.bind(n)), coherency));
      }
      deps.violations.warn(legal.modelIds, coherencyWarnings(warningMarks(paths), coherency));
      const warnings = [...warningLines(paths, (id) => p.models.get(id)?.slot.name ?? n.model(id), (v) => deps.terrainName?.(v) ?? 'terrain'),
        ...(coherency.blocks ? [] : coherency.words.values())];
      current = gateOf(readouts, deps.enforcement(), marked(legal.modelIds), warnings);
      if (unit !== null) box.replace('bar', { kind: 'bar', id: 'bar', bar: barOf(readouts, unit, minimum) });
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
    return `Click to embark: ${enterable.map((id) => n.piece(id)).join(', ')}`;
  }

  // ---- deployment placement ----

  let deployKeep: KeepAway | null = null;   // the pick's distance from enemy units (an Ambusher, a 3.6 Scout or Reinforcement copy)

  function startDeploy(box: DecisionBox, mid: MidDeploy, n: Names): void {
    const unit = mid.unitId;
    box.deployUnit = unit; deployKeep = mid.keepAway;
    box.setHead({ icon: 'army', title: 'Place', subject: n.unit(unit), prompt: 'Click the table where the unit goes' });
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
    const head = box.head;
    if (head !== null) box.setHead({ ...head, prompt: null });          // the ghosts are down: the card is the Place button
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
    box.add(reasonOf(null));
    box.cardActions = PLACING;
    box.add(button('place', 'Place', undefined, true, 'ready'), place);
    // A ghost on another model's base is named; under strict Place waits until none is.
    regate = () => {
      const st = box.host.state();
      const g = deps.geometry();
      if (st.projection === null || st.game === null) return;
      const ids = [...box.ghosts.keys()];
      const unitName = (id: string): string => st.projection!.units.get(id)?.definition.name ?? id;
      if (g !== null) deps.violations.measure(ids, withKeepAway(allWords(contactsOfPlacement(g, st.projection, new Map(box.ghosts)), undefined, unitName),
        g, st.projection, deployKeep, new Map(box.ghosts), unitName));
      // p.7 unit coherency of the ghosts: the server does not judge a deployment's, so it warns and never holds Place.
      coherency = g === null ? NO_COHERENCY : coherencyOf(g, st.projection, new Map(box.ghosts), st.coherency, (id) => st.projection!.models.get(id)?.slot.name ?? id, false);
      deps.violations.warn(ids, coherency.words);
      current = gateOf([], deps.enforcement(), marked(ids), [...coherency.words.values()]);
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
    settled(_box, token) { deps.drops?.refuse(token); },                    // a model the step did not move (a zero move, a landing it dropped) is held no longer
    dragged(_box, modelIds) {
      deps.violations.moved(modelIds);                                      // a refused commit's reason lapses once its models move
      regate?.();
    },
    unhook() {
      regate = null; rings = null; moving = null; minimum = null; paths = []; coherency = NO_COHERENCY;
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
    bands() {
      const g = deps.geometry();
      if (rings === null || moving === null || moving.allowance === null || g === null) return [];
      const r = rings;
      if (built === null || built.legal !== r.legal) built = { legal: r.legal, byId: new Map() };
      const byId = built.byId;
      return r.ids.flatMap((id) => {
        if (!byId.has(id)) byId.set(id, bandsOf(g, r.p, r.legal, id));
        const b = byId.get(id);
        return b === null || b === undefined ? [] : [b];
      });
    },
    readouts(held) {
      if (held === null || moving === null || moving.allowance === null) return [];
      const m = moving, g = deps.geometry();
      const ends = new Map(held.ids.flatMap((id) => { const end = held.ends.get(id); return end === undefined || !m.legal.modelIds.includes(id) ? [] : [[id, xy(end.lands)] as const]; }));
      const measured = g === null ? null : routesOf(g, m.p, m.legal, ends);
      const unit = measured?.allowance ?? m.allowance;
      return held.ids.flatMap((id) => {
        const origin = held.origins.get(id), to = ends.get(id);
        if (origin === undefined || to === undefined) return [];
        const from = xy(origin.position), route = measured?.routes.get(id);
        return [route === undefined ? { modelId: id, from, to, readout: read(from, to, unit, minimum) }
          : { modelId: id, from, to, readout: readDistance(route.distance, unit, minimum), path: route.points }];
      });
    },
    region() { return moving?.legal ?? null; },
    paths() { return moving === null ? [] : paths; },
    coherency() { return coherency; },
    picked(ids) {
      if (rings !== null) rings = { ...rings, ids: [...new Set([...rings.ids.slice(0, 1), ...ids])].filter((id) => rings!.legal.modelIds.includes(id)) };
    },
  };
}

const ZERO: Placement = { position: { x: 0, y: 0, z: 0 }, yaw: { degrees: 0 } };
