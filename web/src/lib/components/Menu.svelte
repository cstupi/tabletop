<script lang="ts">
  // The ▸ menu on a thing: its rare actions, behind one button. Closes on a choice, Escape or a click elsewhere.
  import type { Snippet } from 'svelte';
  import Icon from './Icon.svelte';
  import type { IconName } from './icons';
  let { label, icon = 'menu', text, children }: { label: string; icon?: IconName; text?: string; children: Snippet } = $props();
  let details = $state<HTMLDetailsElement>();
  const close = () => { if (details) details.open = false; };
  function outside(e: MouseEvent) { if (details?.open && !details.contains(e.target as Node)) close(); }
  function key(e: KeyboardEvent) { if (e.key === 'Escape' && details?.open) { close(); details.querySelector('summary')?.focus(); } }
</script>

<svelte:window onclick={outside} onkeydown={key} />
<details class="menu" bind:this={details}>
  <summary class="button icon-btn" aria-label={label} title={label}>{#if text}<span class="text">{text}</span>{/if}<Icon name={icon} size="1.1em" /></summary>
  <!-- a choice inside closes the menu after its own handler runs -->
  <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
  <div class="menu-list" onclick={(e) => { if ((e.target as HTMLElement).closest('button, a')) close(); }}>{@render children()}</div>
</details>
<style>
  summary { gap: var(--s1); }
  .text { max-width: 12rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
