// viewer/src/lib/play/playDrag.ts
// TableInput's drags on the play board: OnLeftDown's pick-up (MayDrag), OnMotion, Hold's Turn and Arrange, OnLeftUp's drop. The page binds
// the board's pointer to it and draws its overlays; the decisions are here, over ModelDrag, HandMove and the box's ghosts.

import type { Rect } from './hudFrame';
import type { Modifiers, Overlay } from '../board/interaction';
import { teamColour } from '../board/palette';
import type { BoardHit } from '../board/interaction';
import type { ConnectionState, SendOutcome } from '../connection/gameConnection';
import { refusalText } from '../connection/feedback';
import type { Geometry, Placement, Point3, XY, Yaw } from '../geometry';
import type { Command, MoveModels, Refused } from '../protocol/wire.generated';
import type { DecisionBox } from './decisionBox';
import type { MoveBoxHooks } from './decisionBox.move';
import { handMarks, handMoveCommand, markerMove, mayDrag } from './handMove';
import type { MarkerLocks } from './markerLocks';
import { ModelDrag, type Held } from './modelDrag';
import { ModelPicks, shiftPress, type PickBox, type PickCandidate } from './modelPicks';
import { allowanceOverlay, bandsOverlay, boxOverlay, coherencyOverlay, labelOverlay, landingOverlay, landingRings, pathsOverlay, rulerLines, rulerOverlay, violationLabels } from './moveOverlays';
import { isVehicle } from './movePaths';
import { read } from './moveRuler';

/** Ui.Warning on the board: the marks' labels and the coherency lines. */
const WARNING = '#ffc44e';
import { Names } from './names';
import { teamsOf } from '../lobby/seats';
import type { Selection } from './selectionView';
import type { PendingDrops } from './pendingDrops';
import type { Violations } from './violations';
import { isWatching } from './waiting';

export interface DragDeps {
  state(): ConnectionState;
  box(): DecisionBox | null;
  hooks(): MoveBoxHooks | null;
  geometry(): Geometry | null;
  violations: Violations; drops: PendingDrops; locks: MarkerLocks;
  send(command: Command): Promise<SendOutcome>;
  /** The page's selection: a pick-up selects the unit as a click does; picked models are its model ids (ModelPicks.selection). */
  select(selection: Selection): void;
  /** The page's selection now: its model ids are the picks (ModelPicks.of). */
  selection?(): Selection | null;
  /** Something the board draws changed. */
  changed(): void;
  /** Runs a pointer move's landing once per animation frame (the page passes requestAnimationFrame); without it each move lands at once. */
  frame?(run: () => void): void;
}

interface HeldMarker { readonly id: string; readonly offset: XY; readonly grab: Point3; readonly yaw: Yaw; readonly radius: number; at: Point3 }

/** Q/E turn this much per key press, Shift double (counter-clockwise positive). */
export const TURN_STEP = 15;

export class PlayDrags {
  readonly #deps: DragDeps;
  #drag: ModelDrag | null = null;
  #geometry: Geometry | null = null;
  #held: Held | null = null;
  #proposing = false;
  #marker: HeldMarker | null = null;
  #tokens = 0;
  /** The latest pointer move not yet landed, and whether a frame is due to land it. */
  #pending: { readonly at: XY; readonly mods: Modifiers } | null = null;
  #scheduled = false;
  /** A Shift+drag box from the press to the pointer, while it is drawn. */
  #box: PickBox | null = null;
  /** The Ask the box last rendered: a change clears the picks. */
  #ask: string | null = null;
  readonly #byId = new Map<string, string>();

  constructor(deps: DragDeps) { this.#deps = deps; }

  get held(): Held | null { return this.#held; }
  /** True while a drag is in hand: Enter is held off and Q/E and 1–5 go to it. */
  get holding(): boolean { return this.#held !== null; }

  #models(): ModelDrag | null {
    const g = this.#deps.geometry();
    if (g === null) return null;
    if (this.#geometry !== g) { this.#drag = new ModelDrag(g); this.#geometry = g; }
    return this.#drag;
  }

  /** Where the board draws each model that is not where the projection has it: the drops waiting for their events, then the models in hand. */
  drawn(): Map<string, Placement> {
    const out = new Map(this.#deps.drops.all);
    const held = this.#held;
    if (held !== null) for (const id of held.ids) {
      const end = held.ends.get(id), yaw = held.yaws.get(id);
      if (end !== undefined && yaw !== undefined) out.set(id, { position: end.drawn, yaw });
    }
    return out;
  }

  /** The picked models: the selection's model ids. */
  get picks(): ModelPicks { return ModelPicks.of(this.#deps.selection?.() ?? null); }

  /** Whether a model is of a team this account holds. */
  #mine(modelId: string): boolean {
    const st = this.#deps.state(), p = st.projection, game = st.game;
    if (p === null || game === null) return false;
    const unit = p.units.get(p.models.get(modelId)?.unitId ?? '');
    const team = unit === undefined ? undefined : game.participants.find((x) => x.id === unit.participantId)?.teamId;
    return team !== undefined && teamsOf(game, st.participantId).includes(team);
  }

  /** Shift+click on a model of my team: it goes in or out of the picks. False for any other model (the page selects it as a click does). */
  shiftClick(modelId: string): boolean {
    const unit = this.#deps.state().projection?.models.get(modelId)?.unitId;
    if (unit === undefined || !this.#mine(modelId)) return false;
    const next = this.picks.toggle(unit, modelId);
    this.#deps.select(next.selection(unit));
    this.#deps.changed();
    return true;
  }

  /** A plain click on the table, a new Ask, the move's end or Esc: true when there were picks to clear (the unit stays selected). */
  clearPicks(): boolean {
    const picks = this.picks;
    if (picks.isEmpty) return false;
    this.#deps.select(ModelPicks.none.selection(picks.unitId!));
    this.#deps.changed();
    return true;
  }

  /** Shift+drag: a box from the press, for anyone who may drag (a watcher pans). */
  onBoxStart(at: XY): boolean {
    const st = this.#deps.state();
    if (st.projection === null || st.game === null || isWatching(st)) return false;
    this.#box = { from: at, to: at };
    return true;
  }

  onBoxMove(from: XY, to: XY): void { this.#box = { from, to }; this.#deps.changed(); }

  /** The box released: my team's drawn models whose base centre is inside it join the picks (ModelPicks.boxed). */
  onBoxEnd(from: XY, to: XY): void {
    this.#box = null;
    const p = this.#deps.state().projection;
    if (p !== null) {
      const picks = this.picks, next = picks.boxed(this.#candidates(), { from, to });
      if (next !== picks) this.#deps.select(next.selection(next.unitId!));
    }
    this.#deps.changed();
  }

  onBoxCancel(): void { this.#box = null; this.#deps.changed(); }

  /** My team's models the board draws, at their drawn base centres: a ghost or a drop where it is drawn, else on the table. */
  #candidates(): PickCandidate[] {
    const p = this.#deps.state().projection, ghosts = this.#deps.box()?.ghosts;
    if (p === null) return [];
    const drawn = this.drawn(), out: PickCandidate[] = [];
    for (const m of p.models.values()) {
      const at = drawn.get(m.id) ?? ghosts?.get(m.id) ?? (m.location.kind === 'table' ? m.location.placement : undefined);
      if (at !== undefined && this.#mine(m.id)) out.push({ modelId: m.id, unitId: m.unitId, x: at.position.x, y: at.position.y });
    }
    return out;
  }

  /** OnLeftDown: a click on a placement point is the box's; a pick answers and neither selects nor drags; the rest checks MayDrag. */
  onDragStart(hit: BoardHit, at: XY, mods: Modifiers = { shift: false, ctrl: false, alt: false }): boolean {
    const st = this.#deps.state(), p = st.projection, game = st.game, box = this.#deps.box();
    if (p === null || game === null || box === null || box.placementPoint !== null) return false;
    // Shift+drag: one model of my team held alone; from anywhere else the page boxes (shiftPress).
    if (mods.shift && (hit.kind !== 'model' || shiftPress(this.#mine(hit.id), true) !== 'holdOne')) return false;
    if (hit.kind === 'model') {
      const unit = p.models.get(hit.id)?.unitId;
      if (box.eligible.has(hit.id) || (unit !== undefined && box.eligible.has(unit))) return false;
      const drag = this.#models();
      if (drag === null || unit === undefined) return false;
      // A press on a picked model holds the picks; on any other model the whole unit, and the picks clear (ModelPicks.pressed, held).
      // A Shift press holds the model alone, and the picks keep it or start over with it (ModelPicks.shiftHeld).
      const picks = mods.shift ? this.picks.shiftHeld(unit, hit.id) : this.picks.pressed(hit.id);
      const unitModels = [...p.models.values()].filter((m) => m.unitId === unit && (m.location.kind === 'table' || box.ghosts.has(m.id))).map((m) => m.id);
      const held = drag.start(p, hit.id, at, mods.shift ? [hit.id] : picks.isEmpty ? null : picks.held(hit.id, unitModels), box.proposing, box.ghosts);
      const proposing = held.ids.every((id) => box.proposing.has(id));
      if (held.ids.length === 0 || !mayDrag({ kind: 'models', ids: held.ids }, isWatching(st), box.proposing, game.enforcement, game.status, p, this.#deps.locks, box.pinned)) {
        drag.cancel();
        return false;
      }
      this.#held = held; this.#proposing = proposing; this.#marker = null; this.#pending = null;
      this.#deps.select(picks.selection(unit));
      this.#deps.hooks()?.picked(held.ids);
      this.#deps.changed();
      return true;
    }
    if (hit.kind === 'marker') {
      if (!mayDrag({ kind: 'marker', id: hit.id }, isWatching(st), box.proposing, game.enforcement, game.status, p, this.#deps.locks)) return false;
      const m = p.markers.get(hit.id) ?? p.board.table.markers.find((x) => x.id === hit.id);
      if (m === undefined) return false;
      this.#marker = { id: m.id, offset: { x: at.x - m.position.x, y: at.y - m.position.y }, grab: m.position, yaw: m.yaw, radius: m.diameter / 2, at: m.position };
      this.#held = null;
      this.#deps.changed();
      return true;
    }
    return false;                                                          // terrain drags only in setup
  }

  onDragMove(at: XY, mods: Modifiers): void {
    const frame = this.#deps.frame;
    if (frame === undefined) { this.#move(at, mods); return; }
    this.#pending = { at, mods };
    if (this.#scheduled) return;
    this.#scheduled = true;
    frame(() => { this.#scheduled = false; this.#flush(); });
  }

  /** The waiting pointer move lands now: before a key acts on the drag. A drop or a cancel drops it instead. */
  #flush(): void {
    const pending = this.#pending;
    this.#pending = null;
    if (pending !== null) this.#move(pending.at, pending.mods);
  }

  #move(at: XY, mods: Modifiers): void {
    const marker = this.#marker;
    if (marker !== null) { marker.at = { x: at.x - marker.offset.x, y: at.y - marker.offset.y, z: 0 }; this.#deps.changed(); return; }
    const p = this.#deps.state().projection, drag = this.#drag, box = this.#deps.box();
    if (this.#held === null || drag === null || p === null || box === null) return;
    this.#landed(drag.move(p, at, mods.alt, this.#region(), box.ghosts));
  }

  onDragEnd(at: XY, mods: Modifiers): void {
    this.#pending = null;                                                  // the drop lands at its own point
    const marker = this.#marker;
    if (marker !== null) {
      this.#marker = null;
      const moved = marker.at.x !== marker.grab.x || marker.at.y !== marker.grab.y;
      if (moved) void this.#deps.send(markerMove(marker.id, marker.at, marker.yaw)).catch(() => { /* the marker snaps back */ });
      this.#deps.changed();
      return;
    }
    const st = this.#deps.state(), p = st.projection, drag = this.#drag, box = this.#deps.box();
    const held = this.#held;
    if (held === null || drag === null || p === null || box === null) return;
    drag.move(p, at, mods.alt, this.#region(), box.ghosts);               // against the projection current at the drop
    const grabs = held.grabs, ids = held.ids, proposing = this.#proposing;
    const landed = drag.end();
    this.#held = null;
    if (!proposing) this.#deps.violations.measure(ids, new Map());          // a hand move's marks end with its drag: the server judges the drop
    if (landed !== null) {
      if (proposing) box.draggedAll(landed);                              // the ghosts stay; the box gates the commit
      else if (ids.every((id) => p.models.has(id))) this.#handMove(handMoveCommand(grabs, landed));
    }
    this.#deps.changed();
  }

  onDragCancel(): void {
    this.#pending = null;
    const box = this.#deps.box(), held = this.#held;
    this.#marker = null;
    this.#drag?.cancel();
    this.#held = null;
    if (held !== null) {
      if (this.#proposing && box !== null) box.draggedAll(held.ids.map((id) => [id, held.grabs.get(id)!] as const));   // the held ones go back
      else this.#deps.violations.measure(held.ids, new Map());
    }
    this.#deps.changed();
  }

  /** The box unhooked (the Ask ended by any route): a drag of its ghosts would otherwise drop as a hand move. A new Ask, or the end of
   *  the one rendered (the move's end), clears the picks. */
  boxChanged(): void {
    const box = this.#deps.box();
    const ask = box?.renderedAsk ?? null;
    if (ask !== this.#ask) { this.#ask = ask; this.clearPicks(); }
    if (this.#held !== null && this.#proposing && box !== null && !this.#held.ids.every((id) => box.proposing.has(id))) {
      this.#drag?.cancel(); this.#held = null; this.#pending = null; this.#deps.changed();
    }
  }

  /** Q/E, 1–5 and Enter while a drag is in hand; true when handled. */
  onKey(intent: { readonly kind: string; readonly key?: number }, shift: boolean): boolean {
    if (this.#held === null) return false;
    if (intent.kind === 'confirm') return true;                            // Enter is held off mid-drag, as the box's _Input does
    this.#flush();
    if (this.#held === null) return false;
    const p = this.#deps.state().projection, drag = this.#drag, box = this.#deps.box();
    if (p === null || drag === null || box === null) return false;
    if (intent.kind === 'turnLeft' || intent.kind === 'turnRight') {
      this.#landed(drag.turn(p, (shift ? 2 : 1) * TURN_STEP * (intent.kind === 'turnLeft' ? 1 : -1), this.#region(), box.ghosts));
      return true;
    }
    if (intent.kind === 'option' && intent.key !== undefined && intent.key <= 5) {
      this.#landed(drag.arrange(p, intent.key, this.#region(), box.ghosts));
      return true;
    }
    return false;
  }

  /** A click on the table: a deploy's placement point, else nothing here (the page clears its selection). */
  placeAt(at: XY): boolean { return this.#deps.box()?.placeAt({ x: at.x, y: at.y, z: 0 }) ?? false; }

  /** A Refused for a hand move's send that settled before it (or after another frame): its drops are the projection's again. */
  refused(refused: Refused): void {
    const token = this.#byId.get(refused.messageId);
    if (token !== undefined) this.#refuseHand(token, refusalText(refused) ?? refused.detail ?? 'refused');
  }

  /** The move's area in bands (else its allowance rings), rulers, landing rings, violation labels (off the HUD frame's obstacles) and the marker
   *  in hand, in the order the plan draws them. */
  overlays(centreOf: (id: string) => XY | null, colour: number, obstacles: readonly Rect[] = []): Overlay[] {
    const out: Overlay[] = [];
    const hooks = this.#deps.hooks(), p = this.#deps.state().projection, held = this.#held;
    const bands = hooks?.bands() ?? [];
    const rings = bands.length > 0 ? [] : hooks?.allowance() ?? [];
    if (bands.length > 0) out.push(bandsOverlay(bands, teamColour(colour)));
    if (rings.length > 0) out.push(allowanceOverlay(rings, teamColour(colour)));
    const paths = hooks?.paths() ?? [];
    if (paths.length > 0) out.push(pathsOverlay(paths, teamColour(colour), (id) => p !== null && isVehicle(p, id)));
    const coherency = hooks?.coherency();
    if (coherency !== undefined && coherency.rings.length + coherency.lines.length > 0) out.push(coherencyOverlay(coherency, teamColour(colour), WARNING));
    if (held !== null && p !== null) {
      const readouts = this.#proposing ? hooks?.readouts(held) ?? []
        : held.ids.flatMap((id) => {
          const origin = held.origins.get(id), end = held.ends.get(id);
          if (origin === undefined || end === undefined) return [];
          const from = { x: origin.position.x, y: origin.position.y }, to = { x: end.lands.x, y: end.lands.y };
          return [{ modelId: id, from, to, readout: read(from, to, null) }];
        });
      out.push(rulerOverlay(rulerLines(readouts)));
      out.push(landingOverlay(landingRings(held, (id) => { const b = p.models.get(id)?.slot.base; return b === undefined ? 0.5 : Math.max(b.a, b.b) / 2; })));
    }
    const order = [...(held?.ids ?? []), ...this.#deps.violations.marked.keys(), ...this.#deps.violations.warned.keys()];
    const labels = violationLabels(this.#deps.violations, order, centreOf);
    if (labels.length > 0) out.push(labelOverlay(labels, WARNING, obstacles));
    if (this.#box !== null) out.push(boxOverlay(this.#box.from, this.#box.to));
    const marker = this.#marker;
    if (marker !== null) out.push(landingOverlay([{ centre: { x: marker.at.x, y: marker.at.y }, radius: Math.max(0.5, marker.radius) }]));
    return out;
  }

  #region() { return this.#proposing ? this.#deps.hooks()?.region() ?? null : null; }

  /** After a model drag's landing: a proposing model's ghost follows (and the box gates); a hand move's marks are measured. */
  #landed(held: Held | null): void {
    if (held === null) { this.#held = null; this.#deps.changed(); return; }
    this.#held = held;
    const box = this.#deps.box(), st = this.#deps.state(), g = this.#deps.geometry();
    if (box !== null) {
      if (this.#proposing) box.draggedAll(held.ids.map((id) => [id, { position: held.ends.get(id)!.lands, yaw: held.yaws.get(id)! }] as const));
      else if (st.projection !== null && st.game !== null && g !== null) {
        this.#deps.violations.moved(held.ids);
        const placements = new Map(held.ids.map((id) => [id, { position: held.ends.get(id)!.lands, yaw: held.yaws.get(id)! }] as const));
        this.#deps.violations.measure(held.ids, handMarks(g, st.projection, placements, new Names(st.game, st.projection)));
      }
    }
    this.#deps.changed();
  }

  /** OnLeftUp's free move: the drop is drawn where it fell until its event or refusal. */
  #handMove(command: MoveModels): void {
    const token = `hand:${++this.#tokens}`;
    this.#deps.drops.add(token, command.moves);
    this.#deps.send(command).then((outcome) => {
      this.#byId.set(outcome.id, token);
      if (outcome.refused !== null) this.#refuseHand(token, refusalText(outcome.refused) ?? outcome.refused.detail ?? 'refused');
    }, (e: unknown) => {
      this.#refuseHand(token, `Could not send the move: ${e instanceof Error ? e.message : String(e)}`);
    });
  }

  #refuseHand(token: string, text: string): void {
    if (this.#deps.drops.refuse(token).length === 0) return;
    const box = this.#deps.box();
    if (box !== null) { box.refuse(text); box.render(); }
    this.#deps.changed();
  }
}
