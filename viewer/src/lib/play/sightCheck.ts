// viewer/src/lib/play/sightCheck.ts
// client/src/Table/SightLines.cs without Godot: who observes and who is the target, when to ask the server, and what the answers draw.
// The observer is the shooter of my open Assign Ask (its frame's actor) or, while L is held, the selected unit; the target the hovered
// unit. Each assigned target is asked again only when its SightKey changed. `querySight` resolves with the answer, so only the newest
// hovered query is shown.

import type { Scene } from '../board/frame';
import type { Overlay } from '../board/interaction';
import { drawLabel } from '../board/labelPlacement';
import type { Projection } from '../live/apply';
import type { AskOption, GameInfo, SightAnswer } from '../protocol/wire.generated';
import type { Names } from './names';
import { SightCache, SightKey } from './sightKey';
import { dimmed, summaryText } from './sightSummary';

interface XY { readonly x: number; readonly y: number }
export type Tone = 'clear' | 'screened' | 'blocked';

export interface SightView {
  readonly lines: readonly { from: XY; to: XY; tone: Tone }[];
  /** Ids a blocked line names: a model, a unit (a unit perimeter blocks as one) or a terrain volume. */
  readonly blockers: readonly string[];
  readonly summary: { at: XY; text: string } | null;
  readonly dimmed: ReadonlySet<string>;
}

/** SightLines' (0.2, 0.9, 0.3), (1, 0.75, 0.1), (0.95, 0.15, 0.15). */
export const SIGHT_COLOURS: Record<Tone, string> = { clear: 'rgb(51, 230, 77)', screened: 'rgb(255, 191, 26)', blocked: 'rgb(242, 38, 38)' };

/** What the check reads of the decision box: the open assignment and the Ask it renders. */
export interface SightBox {
  readonly targets: { readonly groups: readonly AskOption[]; readonly assigned: ReadonlyMap<string, string> } | null;
  readonly renderedAsk: string | null;
}

export class SightCheck {
  readonly #query: (observer: string, target: string) => Promise<SightAnswer>;
  readonly #listeners = new Set<() => void>();
  // The hovered unit's newest query: its pair, key and an id counter, so an older answer is dropped.
  #hover: { target: string; key: SightKey } | null = null;
  #hoverSeq = 0;
  #shown: { answer: SightAnswer; target: string } | null = null;
  readonly #assignedSeq = new Map<string, number>();
  readonly #assignedAnswers = new Map<string, SightAnswer>();
  readonly #assignedKeys = new SightCache();
  #groups: readonly AskOption[] = [];
  #assigned: ReadonlyMap<string, string> = new Map();
  #seq = 0;

  constructor(query: (observer: string, target: string) => Promise<SightAnswer>) { this.#query = query; }

  subscribe(listener: () => void): () => void {
    this.#listeners.add(listener);
    return () => { this.#listeners.delete(listener); };
  }
  #notify(): void { for (const l of [...this.#listeners]) l(); }

  /** My open Ask, when it is a target assignment: the shooting unit is its frame's actor. */
  #shooter(p: Projection, box: SightBox): string | null {
    if (box.targets === null || box.renderedAsk === null) return null;
    const frameId = p.openAsks.get(box.renderedAsk)?.ask.frameId;
    return frameId === undefined ? null : p.stack.find((f) => f.id === frameId)?.context.actorUnitId ?? null;
  }

  /**
   * Re-reads observer and target (the shooter of my open Assign Ask, else the selected unit while L is held; the hovered unit), asks
   * when they changed, and asks each assigned target whose key changed; a failed query is forgotten.
   */
  update(p: Projection, _game: GameInfo, box: SightBox, hovered: string | null, selected: string | null, sightHeld: boolean): void {
    const shooter = this.#shooter(p, box);
    const observer = shooter ?? (sightHeld ? selected : null);
    this.#groups = box.targets?.groups ?? [];
    this.#assigned = box.targets?.assigned ?? new Map();
    if (observer === null || hovered === null || observer === hovered || !p.units.has(observer)) {
      if (this.#hover !== null || this.#shown !== null) { this.#hover = null; this.#shown = null; this.#hoverSeq++; this.#notify(); }
    } else {
      const key = SightKey.of(p, observer, hovered);
      if (this.#hover === null || this.#hover.target !== hovered || !this.#hover.key.equals(key)) {
        const mine = ++this.#hoverSeq;
        this.#hover = { target: hovered, key };
        void this.#query(observer, hovered).then((answer) => {
          if (mine !== this.#hoverSeq) return;
          this.#shown = { answer, target: hovered };
          this.#notify();
        }, () => { if (mine === this.#hoverSeq) this.#hover = null; });
      }
    }
    this.#askAssigned(p, shooter);
    if (this.#assigned.size === 0 && this.#assignedAnswers.size > 0) { this.#assignedAnswers.clear(); this.#notify(); }
  }

  /** Each assigned target that is new or whose pair's model placements or terrain changed since its last query; the other answers stand. */
  #askAssigned(p: Projection, from: string | null): void {
    const targets = [...new Set(this.#assigned.values())];
    for (const gone of [...this.#assignedSeq.keys()]) if (!targets.includes(gone)) { this.#assignedSeq.delete(gone); this.#assignedAnswers.delete(gone); }
    this.#assignedKeys.keep(targets);
    if (targets.length === 0 || from === null) return;
    for (const target of targets) {
      const key = SightKey.of(p, from, target);
      if (!this.#assignedKeys.needsAsk(target, key)) continue;
      const mine = ++this.#seq;
      this.#assignedSeq.set(target, mine);
      this.#assignedKeys.asked(target, key);
      void this.#query(from, target).then((answer) => {
        if (this.#assignedSeq.get(target) !== mine) return;
        this.#assignedAnswers.set(target, answer);
        this.#notify();
      }, () => { this.#assignedKeys.forget(target); });
    }
  }

  view(p: Projection, game: GameInfo, n: Names, shooting: boolean): SightView {
    const hovered = this.#shown;
    const dim = shooting ? dimmed(p, hovered?.answer ?? null, this.#groups, this.#assigned, this.#assignedAnswers) : new Set<string>();
    if (hovered === null) return { lines: [], blockers: [], summary: null, dimmed: dim };
    const centre = (id: string): XY | null => {
      const loc = p.models.get(id)?.location;
      return loc?.kind === 'table' ? { x: loc.placement.position.x, y: loc.placement.position.y } : null;
    };
    const lines: { from: XY; to: XY; tone: Tone }[] = [];
    for (const l of hovered.answer.lines) {
      const from = centre(l.fromModelId), to = centre(l.toModelId);
      if (from !== null && to !== null) lines.push({ from, to, tone: !l.clear ? 'blocked' : l.screened ? 'screened' : 'clear' });
    }
    const blockers = [...new Set(hovered.answer.lines.filter((l) => !l.clear).flatMap((l) => l.blockedBy))];
    const targets = [...p.models.values()].filter((m) => m.unitId === hovered.target).map((m) => centre(m.id)).filter((c): c is XY => c !== null);
    void game;
    const summary = targets.length === 0 ? null : {
      // Under the target, 2" below its lowest model: the hover label sits over its first model, so the two never share a line.
      at: { x: targets.reduce((a, c) => a + c.x, 0) / targets.length, y: Math.min(...targets.map((c) => c.y)) - 2 },
      text: summaryText(hovered.answer, n.unit(hovered.target), shooting),
    };
    return { lines, blockers, summary, dimmed: dim };
  }
}

/** The lines, the blockers outlined (model circles, or a volume's outline) at 35 % and the summary under the target. */
export function sightOverlay(view: SightView, scene: Scene): Overlay {
  return (ctx, project) => {
    ctx.save();
    const trace = (shape: Scene['models'][number]['circle']): void => {
      ctx.beginPath();
      if (shape.kind === 'circle') { const c = project(shape.centre), e = project({ x: shape.centre.x + shape.radius, y: shape.centre.y }); ctx.arc(c.x, c.y, Math.abs(e.x - c.x), 0, 2 * Math.PI); }
      else shape.points.forEach((pt, i) => { const s = project(pt); if (i === 0) ctx.moveTo(s.x, s.y); else ctx.lineTo(s.x, s.y); });
      ctx.closePath();
    };
    ctx.globalAlpha = 0.35;
    ctx.fillStyle = SIGHT_COLOURS.blocked;
    for (const id of view.blockers) {
      const shapes = [
        ...scene.models.filter((m) => m.id === id || m.unitId === id).map((m) => m.circle),
        ...scene.terrain.filter((t) => t.volumeId === id).map((t) => t.shape),
      ];
      for (const s of shapes) { trace(s); ctx.fill(); }
    }
    ctx.globalAlpha = 1;
    ctx.lineWidth = 2;
    for (const l of view.lines) {
      const a = project(l.from), b = project(l.to);
      ctx.strokeStyle = SIGHT_COLOURS[l.tone];
      ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke();
    }
    if (view.summary !== null) {
      const at = project(view.summary.at);
      ctx.font = '600 13px ui-monospace, SFMono-Regular, Menlo, monospace';
      ctx.textAlign = 'center'; ctx.textBaseline = 'top'; ctx.lineWidth = 3;
      drawLabel(ctx, view.summary.text, at, '#ffffff', { lineHeight: 16, grow: 'down', halo: 'rgba(0, 0, 0, 0.85)' });
    }
    ctx.restore();
  };
}
