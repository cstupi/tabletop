<!-- viewer/src/lib/play/UnitActions.svelte -->
<!-- UnitActionsNode.cs: the action buttons over the selected unit during my activation (spec 2026-11-21-actions-over-unit-design.md): a round
     icon button per option the decision box offers that unit, in the box's order, centred above the unit's models on the board and kept inside
     it (placeRect). A click presses the box's tile for that option; the pointer on a button lights its tile, and on a tile lights its button. -->
<script lang="ts">
  import type { ModelShape } from '../board/frame';
  import type { DecisionBox, DecisionPanel } from './decisionBox';
  import DecisionIcon from './DecisionIcon.svelte';
  import { placeRect, tipOf, type UnitActions } from './unitActionsView';

  let { actions, box, panel, models, project, scale, size }: {
    actions: UnitActions; box: DecisionBox; panel: DecisionPanel; models: readonly ModelShape[];
    project: (p: { x: number; y: number }) => { x: number; y: number }; scale: number; size: { width: number; height: number };
  } = $props();

  const BUTTON = 42, GAP = 4, SPACE = 8;   // a button's size (the tile's 26 px icon in an 8 px inset); --s1 between buttons, --s2 above the models

  const rect = $derived.by(() => {
    const centre = project(actions.anchor);
    // The top edge of the unit's bases on screen, the table's far side up.
    const top = models.reduce((t, m) => {
      const r = m.circle.kind === 'circle' ? m.circle.radius : 0;
      return Math.min(t, project(m.centre).y - r * scale);
    }, centre.y);
    const w = actions.options.length * BUTTON + (actions.options.length - 1) * GAP;
    return placeRect(centre.x, top - SPACE, w, BUTTON, { x: 0, y: 0, w: size.width, h: size.height }, [], SPACE);
  });
  const lit = (_: DecisionPanel, optionId: string): boolean => box.hoveredControl === `opt:${optionId}`;
  const noFocus = (e: Event) => e.preventDefault();
</script>

<div class="actions" role="toolbar" aria-label="Actions" style:left="{rect.x}px" style:top="{rect.y}px">
  {#each actions.options as o (o.optionId)}
    <button type="button" class="act" class:lit={lit(panel, o.optionId)} tabindex="-1" title={tipOf(o)} aria-label={tipOf(o)} onmousedown={noFocus}
      onclick={() => box.press(`opt:${o.optionId}`)} onpointerenter={() => box.hover(`opt:${o.optionId}`)} onpointerleave={() => box.hover(null)}>
      {#if o.icon !== null}<DecisionIcon name={o.icon} size={26} />{:else}{o.word}{/if}
    </button>
  {/each}
</div>

<style>
  .actions { position: absolute; z-index: 1; display: flex; gap: var(--s1); }
  .act { width: 42px; height: 42px; min-height: 0; padding: var(--s2); border-radius: 50%; clip-path: none; box-shadow: 0 2px 8px #0000004d; }
  .act.lit:not(:disabled) { background-color: var(--btn-hover); }
  :global([data-theme='fantasy']) .act { border-radius: 50%; }
</style>
