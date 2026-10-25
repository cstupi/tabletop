<!-- viewer/src/lib/game/Confirm.svelte -->
<!-- "Asked first": a modal question with one confirming button. `ask` null closes it. -->
<script module lang="ts">
  export interface Question { title: string; text: string; ok: string; run: () => void | Promise<void> }
</script>

<script lang="ts">
  let { ask = $bindable(null) }: { ask: Question | null } = $props();
  let dialog: HTMLDialogElement | undefined = $state();

  $effect(() => { if (ask !== null) dialog?.showModal(); else dialog?.close(); });

  async function confirmed() {
    const q = ask;
    ask = null;
    await q?.run();
  }
</script>

<dialog bind:this={dialog} onclose={() => (ask = null)} aria-labelledby="confirm-title">
  {#if ask !== null}
    <h2 id="confirm-title">{ask.title}</h2>
    <p>{ask.text}</p>
    <div class="actions">
      <button onclick={confirmed}>{ask.ok}</button>
      <button onclick={() => (ask = null)}>Cancel</button>
    </div>
  {/if}
</dialog>

<style>
  dialog { background: var(--panel); color: var(--ink); border: 1px solid var(--line); max-width: 26rem; }
  dialog::backdrop { background: rgba(0, 0, 0, 0.5); }
  h2 { margin-top: 0; font-size: 1rem; }
  .actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
</style>
