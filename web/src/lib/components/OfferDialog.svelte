<script lang="ts">
  // Offer assets or a folder to players the account has been in a game with. A modal <dialog>: its close event is the one exit.
  import { api } from '$lib/api/client';
  import { errorMessage } from '$lib/api/errors';
  import type { Guid, PlayerDto } from '$lib/api/types';
  import Icon from '$lib/components/Icon.svelte';
  import { filterPlayers, playerContext } from '$lib/library/offers';

  /** `what` names the offer in the heading ("3 models", "folder Orks"). Exactly one of `assetIds` and `folderId`. */
  let { open, what, assetIds = null, folderId = null, onclose }: { open: boolean; what: string; assetIds?: Guid[] | null; folderId?: Guid | null; onclose: () => void } = $props();

  let dialog = $state<HTMLDialogElement>(); let heading = $state<HTMLHeadingElement>();
  let players = $state<PlayerDto[] | null>(null); let q = $state(''); let chosen = $state<Set<Guid>>(new Set());
  let busy = $state(false); let done = $state<string | null>(null); let error = $state<string | null>(null);
  const shown = $derived(players ? filterPlayers(players, q) : []);

  $effect(() => {
    if (open && dialog && !dialog.open) {
      players = null; q = ''; chosen = new Set(); done = null; error = null;
      dialog.showModal(); heading?.focus();
      api.offers.players().then((p) => (players = p)).catch((e) => { players = []; error = errorMessage(e); });
    }
  });
  function toggle(id: Guid, on: boolean) { const next = new Set(chosen); if (on) next.add(id); else next.delete(id); chosen = next; }
  async function send(e: SubmitEvent) {
    e.preventDefault();
    if (chosen.size === 0) return;
    busy = true; error = null; done = null;
    try {
      const r = await api.offers.create({ recipientIds: [...chosen], assetIds: folderId ? null : assetIds, folderId });
      const names = r.offers.map((o) => o.recipientName).join(', ');
      done = `Offered to ${names}.${r.notFound.length ? ` ${r.notFound.length} not uploaded, left out.` : ''}`;
      chosen = new Set();
    } catch (err) { error = errorMessage(err); } finally { busy = false; }
  }
</script>

<dialog bind:this={dialog} aria-labelledby="offer-heading" onclose={onclose}>
  <header class="head"><h2 id="offer-heading" tabindex="-1" bind:this={heading}>Offer {what}</h2><button onclick={() => dialog?.close()}><Icon name="close" size="1em" />Close</button></header>
  <p><small>Players you have been in a game with. Nothing reaches their library until they accept.</small></p>
  {#if players === null}
    <p class="loading" role="status">Loading…</p>
  {:else if players.length === 0}
    <p class="empty">No players yet. Play a game with someone, then offer them models.</p>
  {:else}
    <form class="stack" onsubmit={send}>
      {#if players.length > 8}<label>Find <input type="search" bind:value={q} maxlength="64" placeholder="Player name" /></label>{/if}
      <ul class="players" aria-label="Players">
        {#each shown as p (p.accountId)}
          <li><label class="check"><input type="checkbox" checked={chosen.has(p.accountId)} disabled={busy} onchange={(e) => toggle(p.accountId, e.currentTarget.checked)} />
            <span><strong>{p.name}</strong><br /><small class="dim">{playerContext(p, players)}</small></span></label></li>
        {:else}<li><small>No player matches.</small></li>{/each}
      </ul>
      <div class="actions"><button type="submit" class="primary" disabled={busy || chosen.size === 0}>Offer{chosen.size > 1 ? ` to ${chosen.size}` : ''}</button></div>
    </form>
  {/if}
  {#if done}<p class="notice" role="status">{done}</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
</dialog>
<style>
  dialog { width: min(32rem, calc(100vw - 2rem)); max-height: calc(100dvh - 2rem); overflow: auto }
  .head { display: flex; justify-content: space-between; align-items: center; gap: var(--space-3) }
  .head h2 { margin: 0 }
  .players { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--space-2); max-height: 22rem; overflow: auto }
  .check { display: flex; gap: var(--space-2); align-items: flex-start; overflow-wrap: anywhere }
  .dim { color: var(--dim) }
</style>
