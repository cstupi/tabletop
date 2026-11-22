<!-- viewer/src/lib/play/Banner.svelte -->
<!-- TurnBanner.cs, the roll-off's result only: over the board's centre, the winner's band and icon, the text in its colour and each team's
     roll in that team's colour, for the banner's seconds, one after another (live batches only). Clicks pass through. -->
<script lang="ts">
  import type { GameConnection } from '../connection/gameConnection';
  import { Names } from './names';
  import { rollOffBannerOf, type Banner } from './rollOffBanners';
  import { teamColour } from '../board/palette';
  import HudIcon from './HudIcon.svelte';

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
  <div class="panel banner" role="status" style:--team={banner.team > 0 ? teamColour(banner.team) : null}>
    <div class="text" class:teamed={banner.team > 0}>{#if banner.team > 0}<span class="band"></span><HudIcon name="team" size={30} />{/if}{banner.text}</div>
    {#if banner.parts.length > 0}
      <div class="parts">{#each banner.parts as part, i (i)}<span style:color={teamColour(part.team)}>{part.text}</span>{/each}</div>
    {:else if banner.detail !== null}<div class="detail">{banner.detail}</div>{/if}
  </div>
{/if}

<style>
  .banner { pointer-events: none; text-align: center; margin: 0; padding: var(--space-3) var(--space-5); background-color: var(--panel-solid); }
  .text { font-family: var(--font-display); font-weight: var(--display-weight); text-transform: var(--display-case); letter-spacing: var(--display-tracking);
    font-size: 1.5rem; color: var(--accent); }
  :global([data-theme='fantasy']) .text { color: var(--wood); }
  .text.teamed, :global([data-theme='fantasy']) .text.teamed { display: inline-flex; align-items: center; gap: var(--space-2); color: var(--team); }
  .band { width: 6px; align-self: stretch; background: var(--team); }
  .parts { display: flex; justify-content: center; gap: var(--space-4); font-weight: 700; }
  .detail { color: var(--dim); }
</style>
