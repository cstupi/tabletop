<!-- viewer/src/lib/play/PieceMenu.svelte -->
<!-- PieceMenu.cs at the pointer: the items; Esc or a click outside closes it. An integer or text item asks in a small dialog (focused,
     Enter confirms, text stops at 64 characters); a choice item lists its choices behind a filter. What a pick (piece or table click)
     or a send does is the page's: this hands back the chosen item and the step to take. -->
<script lang="ts">
  import { choose, type MenuStep } from './menus';
  import type { MenuItem } from './menuModel';

  let { items, at, onStep, onClose }: {
    items: readonly MenuItem[]; at: { x: number; y: number };
    onStep: (item: MenuItem, step: MenuStep) => void; onClose: () => void;
  } = $props();

  let asking = $state.raw<{ item: MenuItem; input: 'integer' | 'text' | 'choice'; choices: readonly string[] } | null>(null);
  let value = $state('');
  let filter = $state('');
  let picked = $state<string | null>(null);
  const shown = $derived(asking === null ? [] : asking.choices.filter((c) => filter === '' || c.toLowerCase().includes(filter.toLowerCase())));

  function pick(item: MenuItem): void {
    const step = choose(item, null);
    if (step.kind === 'dialog') { asking = { item, input: step.input, choices: step.choices }; value = step.input === 'integer' ? '0' : ''; filter = ''; picked = null; }
    else { onStep(item, step); onClose(); }
  }

  function confirm(): void {
    const a = asking;
    if (a === null) return;
    const input = a.input === 'choice' ? (picked ?? shown[0] ?? null) : value;
    if (input === null) return;
    onStep(a.item, choose(a.item, input));
    onClose();
  }

  const born = performance.now();            // the pointerdown that opened the menu is not a click outside it
  const focusOnMount = (node: HTMLElement) => { node.focus(); };
</script>

<svelte:window
  onkeydown={(e) => { if (e.key === 'Escape') { e.stopPropagation(); onClose(); } }}
  onpointerdown={(e) => { if (e.timeStamp <= born) return; if (!(e.target as Element).closest('.piece-menu, .menu-dialog')) onClose(); }}
/>

{#if asking === null}
  <ul class="piece-menu" role="menu" style:left={`${at.x}px`} style:top={`${at.y}px`}>
    {#each items as item (item.label)}
      <li role="none"><button role="menuitem" onclick={() => pick(item)}>{item.label}</button></li>
    {/each}
  </ul>
{:else}
  <form class="menu-dialog" style:left={`${at.x}px`} style:top={`${at.y}px`} onsubmit={(e) => { e.preventDefault(); confirm(); }}>
    <label>{asking.item.label}
      {#if asking.input === 'integer'}
        <input use:focusOnMount type="number" min="-99" max="99" step="1" bind:value />
      {:else if asking.input === 'text'}
        <input use:focusOnMount type="text" maxlength="64" placeholder="at most 64 characters" bind:value />
      {:else}
        <input use:focusOnMount type="text" placeholder="filter" bind:value={filter} />
      {/if}
    </label>
    {#if asking.input === 'choice'}
      <ul class="choices" role="listbox">
        {#each shown as c (c)}
          <li role="option" aria-selected={(picked ?? shown[0]) === c}>
            <button type="button" class:on={(picked ?? shown[0]) === c} onclick={() => (picked = c)} ondblclick={() => { picked = c; confirm(); }}>{c}</button>
          </li>
        {/each}
      </ul>
    {/if}
    <div class="actions"><button type="button" class="small" onclick={onClose}>Cancel</button><button type="submit" class="primary small">OK</button></div>
  </form>
{/if}

<style>
  .piece-menu, .menu-dialog { position: fixed; z-index: 20; margin: 0; padding: var(--space-1); background: var(--panel-solid); color: var(--text);
    border: 1px solid var(--panel-border); min-width: 11rem; box-shadow: 0 4px 16px #0000004d; }
  :global([data-theme='fantasy']) .piece-menu, :global([data-theme='fantasy']) .menu-dialog { border: 2px solid var(--wood); border-radius: 4px; }
  .piece-menu { list-style: none; }
  /* Menu items are rows, not themed buttons. */
  .piece-menu button, .choices button { display: block; width: 100%; min-height: 2rem; padding: var(--space-1) var(--space-2); text-align: left; border: 0; border-radius: 0;
    clip-path: none; border-image: none; box-shadow: none; background: none; color: var(--text); }
  .piece-menu button:hover:not(:disabled), .choices button:hover:not(:disabled) { background: var(--rule); color: var(--text); }
  .menu-dialog { padding: var(--space-2); display: grid; gap: var(--space-2); min-width: 16rem; }
  .menu-dialog label { display: grid; gap: var(--space-1); }
  .choices { list-style: none; margin: 0; padding: 0; max-height: 14rem; overflow: auto; border: 1px solid var(--rule); }
  .choices button.on { background: color-mix(in srgb, var(--accent) 18%, transparent); }
  .actions { display: flex; gap: var(--space-2); justify-content: flex-end; }
</style>
