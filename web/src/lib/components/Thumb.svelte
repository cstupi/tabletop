<script lang="ts">
  // An asset's thumbnail (the presigned URL management lists), or its kind's icon when it has none or the picture fails to load.
  import Icon from './Icon.svelte';
  import type { IconName } from './icons';
  let { url, icon = 'mesh', size = '3rem', alt = '' }: { url?: string | null; icon?: IconName; size?: string; alt?: string } = $props();
  let failed = $state<string | null>(null);
</script>

<span class="thumb" style:--thumb={size}>
  {#if url && failed !== url}<img src={url} {alt} loading="lazy" decoding="async" onerror={() => (failed = url ?? null)} />
  {:else}<Icon name={icon} size="45%" label={alt || undefined} />{/if}
</span>
