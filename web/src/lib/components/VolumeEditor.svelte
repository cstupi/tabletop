<script lang="ts">
  import type { Orientation } from '$lib/api/types';

  let { assetId, glb, scale, orientation }: {
    assetId: string; glb: Uint8Array; scale: number; orientation: Orientation;
  } = $props();
</script>
