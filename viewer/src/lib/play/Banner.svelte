<!-- viewer/src/lib/play/Banner.svelte -->
<!-- TurnBanner.cs, the roll-off's result only: over the board's centre, the text and its detail for the banner's seconds, one after another
     (live batches only). Clicks pass through. -->
<script lang="ts">
  import type { GameConnection } from '../connection/gameConnection';
  import { Names } from './names';
  import { rollOffBannerOf, type Banner } from './rollOffBanners';

  let { connection }: { connection: GameConnection } = $props();

  let banner = $state.raw<Banner | null>(null);
  $effect(() => {
    const conn = connection;
    const queue: Banner[] = [];
    let timer: ReturnType<typeof setTimeout> | null = null;
    const next = (): void => {
      timer = null;
      banner = queue.shift() ?? null;
      if (banner !== null) timer = setTimeout(next, banner.seconds * 1000);
    };
    const stop = conn.onEvents((events) => {
      const s = conn.state;
      if (s.game === null) return;
      const b = rollOffBannerOf(events, new Names(s.game, s.projection), s.catchingUp);
      if (b === null) return;
      queue.push(b);
      if (timer === null) next();
    });
    return () => { stop(); if (timer !== null) clearTimeout(timer); banner = null; };
  });
</script>

{#if banner !== null}
  <div class="panel banner" role="status">
    <div class="text">{banner.text}</div>
    {#if banner.detail !== null}<div class="detail">{banner.detail}</div>{/if}
  </div>
{/if}

<style>
  .banner { pointer-events: none; text-align: center; margin: 0; padding: var(--s4) var(--s6); background-color: var(--panel-solid); }
  .text { font-family: var(--font-display); font-weight: var(--display-weight); text-transform: var(--display-case); letter-spacing: var(--display-tracking);
    font-size: 1.5rem; color: var(--accent); }
  :global([data-theme='fantasy']) .text { color: var(--wood); }
  .detail { color: var(--dim); }
</style>
