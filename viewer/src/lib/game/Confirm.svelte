<!-- viewer/src/lib/game/Confirm.svelte -->
<!-- "Asked first": a modal question with one confirming button (`danger` for what cannot be taken back). `ask` null closes it. -->
<script module lang="ts">
  export interface Question { title: string; text: string; ok: string; danger?: boolean; run: () => void | Promise<void> }
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
      <button onclick={() => (ask = null)}>Cancel</button>
      <button class={ask.danger ? 'danger' : 'primary'} onclick={confirmed}>{ask.ok}</button>
    </div>
  {/if}
</dialog>

<style>
  dialog { max-width: 28rem; }
</style>
