<!-- viewer/src/lib/play/UndoButton.svelte -->
<!-- UndoButton.cs: the `undo` icon alone, a square button in the play view's top right corner; undoes the last action in the game, whoever
     took it. Hidden for a watcher; disabled only when nothing is undoable; the tooltip is its word and key, then the action. When the undo would take dice rolls with it, a dialog lists them, one per line, first. -->
<script lang="ts">
  import { NOTHING_TO_UNDO, type UndoPlan } from './undo';
  import HudIcon from './HudIcon.svelte';

  let { plan, watching, onUndo }: { plan: UndoPlan | null; watching: boolean; onUndo: (toSeq: number) => void } = $props();

  let dialog: HTMLDialogElement | undefined = $state();
  let asking: UndoPlan | null = $state(null);

  $effect(() => { if (asking !== null) dialog?.showModal(); else dialog?.close(); });

  /** Ctrl+Z presses it too (PlayPage's InfoColumn routes the key here). */
  export function press(): void {
    if (watching || plan === null || asking !== null) return;
    if (plan.lostRolls.length === 0) onUndo(plan.toSeq); else asking = plan;
  }

  function confirmed(): void {
    const p = asking;
    asking = null;
    if (p !== null) onUndo(p.toSeq);
  }
</script>

{#if !watching}
  <button type="button" tabindex="-1" disabled={plan === null} title={`Undo (Ctrl+Z)\n${plan?.tooltip ?? NOTHING_TO_UNDO}`} aria-label="Undo" class="corner-button"
    onmousedown={(e) => e.preventDefault()} onclick={press}><HudIcon name="undo" size={22} /></button>
{/if}

<dialog bind:this={dialog} onclose={() => (asking = null)} aria-labelledby="undo-title">
  {#if asking !== null}
    <h2 id="undo-title">Undo dice rolls?</h2>
    <p class="rolls">{asking.lostRolls.join('\n')}</p>
    <div class="actions">
      <button type="button" class="flat" onclick={() => (asking = null)}>Cancel</button>
      <button type="button" class="primary" onclick={confirmed}>Undo</button>
    </div>
  {/if}
</dialog>

<style>
  .flat { border: 0; background: none; box-shadow: none; clip-path: none; color: var(--text); }
  dialog { max-width: 30rem; }
  h2 { margin-top: 0; }
  .rolls { white-space: pre-line; overflow-wrap: anywhere; }
</style>
