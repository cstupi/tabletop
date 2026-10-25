<!-- viewer/src/lib/play/UndoButton.svelte -->
<!-- UndoButton.cs: undoes the last action in the game, whoever took it. Hidden for a watcher; disabled only when nothing is undoable;
     the tooltip names the action. When the undo would take dice rolls with it, a dialog lists them, one per line, first. -->
<script lang="ts">
  import { NOTHING_TO_UNDO, type UndoPlan } from './undo';

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
  <button type="button" tabindex="-1" disabled={plan === null} title={plan?.tooltip ?? NOTHING_TO_UNDO} class="small" onmousedown={(e) => e.preventDefault()} onclick={press}>Undo <kbd>Ctrl+Z</kbd></button>
{/if}

<dialog bind:this={dialog} onclose={() => (asking = null)} aria-labelledby="undo-title">
  {#if asking !== null}
    <h2 id="undo-title">Undo dice rolls?</h2>
    <p class="rolls">{asking.lostRolls.join('\n')}</p>
    <div class="actions">
      <button type="button" onclick={() => (asking = null)}>Cancel</button>
      <button type="button" class="danger" onclick={confirmed}>Undo</button>
    </div>
  {/if}
</dialog>

<style>
  kbd { font-family: var(--font-num); font-size: .7rem; line-height: 1; padding: 2px 4px; border: 1px solid currentColor; border-radius: 3px; opacity: .7; }
  dialog { max-width: 30rem; }
  h2 { margin-top: 0; }
  .rolls { white-space: pre-line; overflow-wrap: anywhere; }
  .actions { justify-content: flex-end; margin-bottom: 0; }
</style>
