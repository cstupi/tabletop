<!-- viewer/src/lib/play/UnitActions.svelte -->
<!-- UnitActionsNode.cs: the bar over the acting unit during every step of my activation (spec 2026-11-21-actions-over-unit-design.md): a round
     icon button per option the decision box offers, in the box's order, then the card's foot actions (the primary lit, a dim one with its reason as
     title), centred above the unit's models and its name label on the board and kept inside it (placeRect), held still while the pointer is over
     it or a press or drag is in progress and eased to its place otherwise (UnitActionsHold). A click presses the box's tile or
     button; the pointer on an option lights its tile, and on a tile lights its button. While I pick shooting targets, a target icon stands over
     each enemy unit I may shoot; a click on it is the board's click on that unit. -->
<script lang="ts">
  import type { Scene } from '../board/frame';
  import { actionTip, type CardAction } from './cardActions';
  import type { DecisionBox, DecisionPanel } from './decisionBox';
  import DecisionIcon from './DecisionIcon.svelte';
  import { hoverLabelBox } from './hoverLabels';
  import { placeRect, tipOf, UnitActionsHold, type Rect, type UnitActions, type UnitTarget } from './unitActionsView';

  let { actions, targets, box, panel, scene, woundLine, project, scale, size, names, onHoverUnit, obstacles = [] }: {
    actions: UnitActions; targets: readonly UnitTarget[]; box: DecisionBox; panel: DecisionPanel; scene: Scene; woundLine: boolean;
    project: (p: { x: number; y: number }) => { x: number; y: number }; scale: number; size: { width: number; height: number };
    names: (unitId: string) => string; onHoverUnit: (unitId: string | null) => void;
    /** The HUD frame's obstacles: the panels over the board, which the bar keeps off. */
    obstacles?: readonly Rect[];
  } = $props();

  const BUTTON = 42, GAP = 4, SPACE = 8;   // a button's size (the tile's 26 px icon in an 8 px inset); --s1 between buttons, --s2 above the models
  const view = $derived({ x: 0, y: 0, w: size.width, h: size.height });

  /** The top edge of a unit's bases on screen, the table's far side up, from its centre's projection. */
  function topOf(unitId: string, anchor: { x: number; y: number }): { x: number; y: number } {
    const centre = project(anchor);
    const y = scene.models.filter((m) => m.unitId === unitId).reduce((t, m) => {
      const r = m.circle.kind === 'circle' ? m.circle.radius : 0;
      return Math.min(t, project(m.centre).y - r * scale);
    }, centre.y);
    return { x: centre.x, y };
  }

  const count = $derived(actions.options.length + actions.actions.length);
  const rect = $derived.by(() => {
    const at = topOf(actions.unitId, actions.anchor);
    const w = count * BUTTON + (count - 1) * GAP;
    // Above the unit's name label too, shown or not, so a button that brings it up never moves out from under the pointer.
    return placeRect(at.x, at.y, w, BUTTON, view, obstacles, SPACE, hoverLabelBox(scene, actions.unitId, woundLine, project, scale));
  });
  // Where the bar is drawn: held while the pointer is over it or a button is down anywhere (a drag of the models moves the name label under
  // it, and a click aimed at Commit right after must land), else eased to rect each animation frame.
  const hold = new UnitActionsHold();
  let over = $state(false);
  let pressed = false;
  let drawn = $state<Rect | null>(null);
  $effect(() => {
    const want = rect, unit = actions.unitId;
    let last = performance.now(), frame = 0;
    const tick = (now: number): void => {
      const at = hold.step(unit, want, over || pressed, (now - last) / 1000);
      last = now;
      drawn = at;
      if (at.x !== want.x || at.y !== want.y) frame = requestAnimationFrame(tick);
    };
    tick(last);
    return () => cancelAnimationFrame(frame);
  });
  const targetRects = $derived(targets.map((t) => {
    const at = topOf(t.unitId, t.anchor);
    return { target: t, rect: placeRect(at.x, at.y, BUTTON, BUTTON, view, obstacles, SPACE, hoverLabelBox(scene, t.unitId, false, project, scale)) };
  }));
  // The foot's actions as the card shows them now (enabled as its buttons, a dim one's reason), else as the bar was made.
  const live = $derived.by((): readonly CardAction[] => { void panel; const now = box.actions; return actions.actions.map((a) => now.find((x) => x.id === a.id) ?? { ...a, enabled: false }); });
  const lit = (_: DecisionPanel, optionId: string): boolean => box.hoveredControl === `opt:${optionId}`;
  const noFocus = (e: Event) => e.preventDefault();
</script>

<svelte:window onpointerdowncapture={() => { pressed = true; }} onpointerupcapture={() => { pressed = false; }} onblur={() => { pressed = false; }} />

<div class="actions" role="toolbar" tabindex="-1" aria-label="Actions" style:left="{(drawn ?? rect).x}px" style:top="{(drawn ?? rect).y}px"
  onpointerenter={() => { over = true; }} onpointerleave={() => { over = false; }}>
  {#each actions.options as o (o.optionId)}
    <button type="button" class="act" class:lit={lit(panel, o.optionId)} tabindex="-1" title={tipOf(o)} aria-label={tipOf(o)} onmousedown={noFocus}
      onclick={() => box.press(`opt:${o.optionId}`)} onpointerenter={() => box.hover(`opt:${o.optionId}`)} onpointerleave={() => box.hover(null)}>
      {#if o.icon !== null}<DecisionIcon name={o.icon} size={26} />{:else}{o.word}{/if}
    </button>
  {/each}
  {#each live as a (a.id)}
    <button type="button" class="act" class:primary={a.primary} tabindex="-1" title={actionTip(a)} aria-label={actionTip(a)} disabled={!a.enabled}
      onmousedown={noFocus} onclick={() => box.press(a.id)}>
      {#if a.icon !== null}<DecisionIcon name={a.icon} size={26} />{:else}{a.word}{/if}
    </button>
  {/each}
</div>
{#each targetRects as { target, rect: r } (target.unitId)}
  <button type="button" class="act target" tabindex="-1" title="Aim at {names(target.unitId)}" aria-label="Aim at {names(target.unitId)}"
    style:left="{r.x}px" style:top="{r.y}px" onmousedown={noFocus} onclick={() => box.target(target.unitId)}
    onpointerenter={() => onHoverUnit(target.unitId)} onpointerleave={() => onHoverUnit(null)}>
    <DecisionIcon name="shoot" size={26} />
  </button>
{/each}

<style>
  .actions { position: absolute; z-index: 1; display: flex; gap: var(--space-1); }
  .act { width: 42px; height: 42px; min-height: 0; padding: var(--space-2); border-radius: 50%; clip-path: none; border-image: none; background-color: var(--panel-solid); border: 1px solid var(--panel-border); box-shadow: 0 2px 8px #0000004d; }
  .act.lit:not(:disabled) { background-color: color-mix(in srgb, var(--accent) 25%, var(--panel-solid)); }
  .act.primary:not(:disabled) { background-color: var(--accent); color: var(--bg); }
  .act:disabled { opacity: 0.45; }
  .target { position: absolute; z-index: 1; }
  :global([data-theme='fantasy']) .act { border-radius: 50%; }
</style>
