// viewer/src/lib/play/combatLogAttacks.ts
// client/src/Core/CombatLog.Attacks.cs: each frame's parent and sequence, read ahead of the log build, and the attack a roll belongs to.

import type { GameEvent, RollRecord, SequenceContext, SequenceStarted, StepCompleted } from '../protocol/wire.generated';
import type { Names } from './names';
import type { AttackTag } from './rollEntry';

/** CombatLog.FrameTree: a frame started while others are open is a child of the innermost one still open; a finish or abandonment closes
 * it and any opened inside it, an undo closes those started after its seq. tag names the attack of a roll in a frame: the parent of the
 * innermost `resolve-hits` frame at or above it, else the frame itself. */
export class FrameTree {
  private readonly parent = new Map<string, string | null>();
  private readonly started = new Map<string, SequenceStarted>();
  private readonly applied = new Map<string, string>();
  private readonly woundMultiplier = new Map<string, number>();   // by attack frame, from its assign steps' `wound_multiplier`

  static of(log: readonly GameEvent[]): FrameTree {
    const t = new FrameTree();
    const open: { frame: string; seq: number }[] = [];
    const closeFrom = (i: number): void => { if (i >= 0) open.splice(i); };
    const lastIndex = (frame: string): number => { for (let i = open.length - 1; i >= 0; i--) if (open[i]!.frame === frame) return i; return -1; };
    for (const e of log) {
      const p = e.payload;
      if (p.kind === 'sequence_started') {
        if (!t.started.has(p.frameId)) { t.parent.set(p.frameId, open.length > 0 ? open[open.length - 1]!.frame : null); t.started.set(p.frameId, p); }
        open.push({ frame: p.frameId, seq: e.seq });
      } else if (p.kind === 'sequence_finished' || p.kind === 'sequence_abandoned') closeFrom(lastIndex(p.frameId));
      else if (p.kind === 'undone') closeFrom(open.findIndex((x) => x.seq > p.toSeq));
      else if (p.kind === 'step_completed') {
        const times = p.values?.['wound_multiplier'];
        if (times !== undefined && times > 1) t.woundMultiplier.set(t.attackFrame(p.frameId), times);
      }
    }
    return t;
  }

  /** A roll-less step's first applied rule: the rule that dealt the hits a later block of the frame resolves. */
  see(step: StepCompleted): void {
    if (step.rolls.length === 0 && step.applied !== undefined && step.applied.length > 0) this.applied.set(step.frameId, step.applied[0]!);
  }

  private *up(frame: string): Generator<string> {
    for (let f: string | null | undefined = frame; f != null; f = this.parent.get(f)) yield f;
  }

  private sequence(frame: string): string | undefined { return this.started.get(frame)?.sequence; }

  /** The parent of the innermost `resolve-hits` frame at or above the frame, else the frame itself. */
  private attackFrame(frame: string): string {
    let resolve: string | null = null;
    for (const f of this.up(frame)) if (this.sequence(f) === 'resolve-hits') { resolve = f; break; }
    return (resolve !== null ? this.parent.get(resolve) ?? null : null) ?? frame;
  }

  tag(step: StepCompleted, roll: RollRecord, rollContext: SequenceContext, n: Names): AttackTag {
    const frame = this.attackFrame(step.frameId);
    const own = this.started.get(frame)?.context;
    const c = own !== undefined && own.actorUnitId !== undefined ? own : rollContext;
    const melee = [...this.up(frame)].some((f) => this.sequence(f) === 'melee');
    const source = this.sequence(frame) === 'attack-sequence' ? null
      : step.frameId === frame && roll.roll !== 'hit' && roll.roll !== 'defense' && step.applied !== undefined && step.applied.length > 0 ? step.applied[0]!
        : this.applied.get(frame) ?? null;
    return {
      frame, actor: c.actorUnitId !== undefined ? n.unit(c.actorUnitId) : '', targets: c.targetUnitIds.map((u) => n.unit(u)).join(', '), melee, source,
      weapons: [...new Set(c.weaponIds.map((w) => n.weapon(w)))].join(', '), woundMultiplier: this.woundMultiplier.get(frame) ?? 1,
    };
  }
}
