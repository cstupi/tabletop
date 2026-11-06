// viewer/src/lib/play/modelPicks.ts
// Ports ModelPicks (client/src/Core/ModelPicks.cs), members camelCased: the picked models (in-game UX spec → Moves, Picking models), one
// unit's, in pick order. Shift+click toggles a model, a Shift+drag box adds the models inside it, and a pick naming another unit starts over
// with that unit. A plain drag on a picked model holds the picks; one on any other model holds its whole unit and clears them. Immutable:
// every change returns the next picks. Godot boxes base centres on screen; the browser's board is top-down, so here they are table inches.

import type { XY } from '../geometry';
import type { Selection } from './selectionView';

/** PickCandidate: a model a Shift+drag box may take: its id, its unit, and its base centre. */
export interface PickCandidate { readonly modelId: string; readonly unitId: string; readonly x: number; readonly y: number }

/** ScreenBox: a Shift+drag box from the press (from) to the cursor (to), drawn either way. */
export interface PickBox { readonly from: XY; readonly to: XY }

export function boxContains(box: PickBox, x: number, y: number): boolean {
  return x >= Math.min(box.from.x, box.to.x) && x <= Math.max(box.from.x, box.to.x) && y >= Math.min(box.from.y, box.to.y) && y <= Math.max(box.from.y, box.to.y);
}

export class ModelPicks {
  static readonly none = new ModelPicks(null, []);

  constructor(readonly unitId: string | null, readonly modelIds: readonly string[]) {}

  get isEmpty(): boolean { return this.modelIds.length === 0; }

  contains(modelId: string): boolean { return this.modelIds.includes(modelId); }

  /** The picks a selection names: its model ids under its piece, none when it names no model. */
  static of(selection: Selection | null): ModelPicks {
    return selection !== null && selection.modelIds.length > 0 ? new ModelPicks(selection.pieceId, selection.modelIds) : ModelPicks.none;
  }

  /** The selection that shows the picks: the unit with the picked models, or the whole unit with none. */
  selection(unitId: string): Selection { return { pieceId: unitId, modelIds: this.unitId === unitId ? [...this.modelIds] : [] }; }

  /** Shift+click: the model in, or out when already picked. A model of another unit starts over with it alone. */
  toggle(unitId: string, modelId: string): ModelPicks {
    if (this.unitId !== unitId || this.isEmpty) return new ModelPicks(unitId, [modelId]);
    if (!this.contains(modelId)) return new ModelPicks(this.unitId, [...this.modelIds, modelId]);
    const left = this.modelIds.filter((m) => m !== modelId);
    return left.length === 0 ? ModelPicks.none : new ModelPicks(this.unitId, left);
  }

  /** The unit a box takes and its models inside it, in candidate order: the unit with most base centres inside, on a tie the unit of the
   *  model nearest the press (the box's `from` corner). Null when no centre is inside. */
  static inBox(candidates: readonly PickCandidate[], box: PickBox): { readonly unitId: string; readonly modelIds: readonly string[] } | null {
    const inside = candidates.filter((c) => boxContains(box, c.x, c.y));
    if (inside.length === 0) return null;
    const d2 = (c: PickCandidate): number => (c.x - box.from.x) ** 2 + (c.y - box.from.y) ** 2;
    const groups = new Map<string, { count: number; nearest: number }>();
    for (const c of inside) {
      const g = groups.get(c.unitId) ?? { count: 0, nearest: Infinity };
      groups.set(c.unitId, { count: g.count + 1, nearest: Math.min(g.nearest, d2(c)) });
    }
    let unit = '', best = { count: -1, nearest: Infinity };
    for (const [id, g] of groups) if (g.count > best.count || (g.count === best.count && g.nearest < best.nearest)) { unit = id; best = g; }
    return { unitId: unit, modelIds: inside.filter((c) => c.unitId === unit).map((c) => c.modelId) };
  }

  /** Shift+drag released: the box's models added to the picks of their unit, or the picks start over with them for another unit. A box
   *  with no model inside changes nothing. */
  boxed(candidates: readonly PickCandidate[], box: PickBox): ModelPicks {
    const hit = ModelPicks.inBox(candidates, box);
    if (hit === null) return this;
    if (this.unitId !== hit.unitId || this.isEmpty) return new ModelPicks(hit.unitId, hit.modelIds);
    return new ModelPicks(this.unitId, [...this.modelIds, ...hit.modelIds.filter((m) => !this.contains(m))]);
  }

  /** What a plain drag pressed on this model holds: the picks among the unit's drawn models, in the unit's order, when the model is
   *  picked; else every model of its unit. */
  held(pressedModelId: string, unitModelIds: readonly string[]): readonly string[] {
    return this.contains(pressedModelId) ? unitModelIds.filter((m) => this.contains(m)) : unitModelIds;
  }

  /** The picks after a plain press on this model: kept when it is picked, cleared when it is not. */
  pressed(pressedModelId: string): ModelPicks { return this.contains(pressedModelId) ? this : ModelPicks.none; }
}
