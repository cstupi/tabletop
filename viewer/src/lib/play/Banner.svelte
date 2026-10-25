<!-- viewer/src/lib/play/Banner.svelte -->
<!-- TurnBanner.cs, the roll-off's result only: the text and its detail for the banner's seconds. Clicks pass through. -->
<script lang="ts">
  import type { Banner } from './rollOffBanners';

  let { banner }: { banner: Banner | null } = $props();
</script>

{#if banner !== null}
  <div class="banner" role="status">
    <div class="text">{banner.text}</div>
    {#if banner.detail !== null}<div class="detail">{banner.detail}</div>{/if}
  </div>
{/if}

<style>
  .banner { pointer-events: none; text-align: center; background: color-mix(in srgb, var(--panel) 88%, transparent); border: 1px solid var(--line);
    border-radius: 6px; padding: 0.6rem 1.6rem; }
  .text { font-size: 1.6rem; font-weight: 600; }
  .detail { font-size: 0.95rem; opacity: 0.8; }
</style>
