// viewer/src/lib/play/playDrag.ts
// TableInput's drags on the play board: OnLeftDown's pick-up (MayDrag), OnMotion, Hold's Turn and Arrange, OnLeftUp's drop. The page binds
// the board's pointer to it and draws its overlays; the decisions are here, over ModelDrag, HandMove and the box's ghosts.

import type { Modifiers, Overlay } from '../board/interaction';
import { teamColour } from '../board/palette';
import type { BoardHit } from '../board/interaction';
import type { ConnectionState, SendOutcome } from '../connection/gameConnection';
import { refusalText } from '../connection/feedback';
import type { Geometry, Placement, Point3, XY, Yaw } from '../geometry';
import type { Command, Refused } from '../protocol/wire.generated';
import type { DecisionBox } from './decisionBox';
import type { MoveBoxHooks } from './decisionBox.move';
import { handMarks, handMoves, markerMove, mayDrag } from './handMove';
import type { MarkerLocks } from './markerLocks';
import { ModelDrag, type Held } from './modelDrag';
import { allowanceOverlay, labelOverlay, landingOverlay, landingRings, rulerLines, rulerOverlay, violationLabels } from './moveOverlays';
import { read } from './moveRuler';
import { Names } from './names';
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
  /** The page's selection: a pick-up selects the unit as a click does. */
  select(modelIds: ReadonlySet<string>, unitId: string | null): void;
  /** Something the board draws changed. */
  changed(): void;
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

  /** OnLeftDown: a click on a placement point is the box's; a pick answers and neither selects nor drags; the rest checks MayDrag. */
  onDragStart(hit: BoardHit, at: XY, mods: Modifiers): boolean {
    const st = this.#deps.state(), p = st.projection, game = st.game, box = this.#deps.box();
    if (p === null || game === null || box === null || box.placementPoint !== null) return false;
    if (hit.kind === 'model') {
      const unit = p.models.get(hit.id)?.unitId;
      if (box.eligible.has(hit.id) || (unit !== undefined && box.eligible.has(unit))) return false;
      const drag = this.#models();
      if (drag === null) return false;
      const held = drag.start(p, hit.id, at, mods.shift, box.proposing, box.ghosts);
      const proposing = held.ids.every((id) => box.proposing.has(id));
      if (held.ids.length === 0 || !mayDrag({ kind: 'models', ids: held.ids }, isWatching(st), box.proposing, game.enforcement, game.status, p, this.#deps.locks)) {
        drag.cancel();
        return false;
      }
      this.#held = held; this.#proposing = proposing; this.#marker = null;
      this.#deps.select(new Set(mods.shift ? [hit.id] : held.ids), unit ?? null);
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
    const marker = this.#marker;
    if (marker !== null) { marker.at = { x: at.x - marker.offset.x, y: at.y - marker.offset.y, z: 0 }; this.#deps.changed(); return; }
    const p = this.#deps.state().projection, drag = this.#drag, box = this.#deps.box();
    if (this.#held === null || drag === null || p === null || box === null) return;
    this.#landed(drag.move(p, at, mods.alt, this.#region(), box.ghosts));
  }

  onDragEnd(at: XY, mods: Modifiers): void {
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
      if (proposing) for (const [id, placement] of landed) box.dragged(id, placement);   // the ghosts stay; the box gates the commit
      else if (ids.every((id) => p.models.has(id))) this.#handMove(handMoves(grabs, landed));
    }
    this.#deps.changed();
  }

  onDragCancel(): void {
    const box = this.#deps.box(), held = this.#held;
    this.#marker = null;
    this.#drag?.cancel();
    this.#held = null;
    if (held !== null) {
      if (this.#proposing && box !== null) for (const id of held.ids) box.dragged(id, held.grabs.get(id)!);   // the held ones go back
      else this.#deps.violations.measure(held.ids, new Map());
    }
    this.#deps.changed();
  }

  /** The box unhooked (the Ask ended by any route): a drag of its ghosts would otherwise drop as a hand move. */
  boxChanged(): void {
    const box = this.#deps.box();
    if (this.#held !== null && this.#proposing && box !== null && !this.#held.ids.every((id) => box.proposing.has(id))) {
      this.#drag?.cancel(); this.#held = null; this.#deps.changed();
    }
  }

  /** Q/E, 1–5 and Enter while a drag is in hand; true when handled. */
  onKey(intent: { readonly kind: string; readonly key?: number }, shift: boolean): boolean {
    if (this.#held === null) return false;
    if (intent.kind === 'confirm') return true;                            // Enter is held off mid-drag, as the box's _Input does
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

  /** Rings, rulers, landing rings, violation labels and the marker in hand, in the order the plan draws them. */
  overlays(centreOf: (id: string) => XY | null, colour: number): Overlay[] {
    const out: Overlay[] = [];
    const hooks = this.#deps.hooks(), p = this.#deps.state().projection, held = this.#held;
    const rings = hooks?.allowance() ?? [];
    if (rings.length > 0) out.push(allowanceOverlay(rings, teamColour(colour)));
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
    const order = [...(held?.ids ?? []), ...this.#deps.violations.marked.keys()];
    const labels = violationLabels(this.#deps.violations, order, centreOf);
    if (labels.length > 0) out.push(labelOverlay(labels, '#ffc44e'));
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
      if (this.#proposing) for (const id of held.ids) box.dragged(id, { position: held.ends.get(id)!.lands, yaw: held.yaws.get(id)! });
      else if (st.projection !== null && st.game !== null && g !== null) {
        this.#deps.violations.moved(held.ids);
        const placements = new Map(held.ids.map((id) => [id, { position: held.ends.get(id)!.lands, yaw: held.yaws.get(id)! }] as const));
        this.#deps.violations.measure(held.ids, handMarks(g, st.projection, placements, new Names(st.game, st.projection)));
      }
    }
    this.#deps.changed();
  }

  /** OnLeftUp's free move: the drop is drawn where it fell until its event or refusal. */
  #handMove(moves: ReturnType<typeof handMoves>): void {
    const token = `hand:${++this.#tokens}`;
    this.#deps.drops.add(token, moves);
    this.#deps.send({ kind: 'move_models', moves }).then((outcome) => {
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
