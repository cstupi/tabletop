<!-- viewer/src/lib/components/SourcePicker.svelte -->
<script lang="ts">
  import { fromFile, fromUrl } from '../recording/source';
  import type { Feed } from '../feed/feed';
  import { recordingFeed } from '../feed/recordingFeed';

  let { onFeed, onError }: { onFeed: (feed: Feed) => void; onError: (message: string) => void } = $props();
  let url = $state('');

  async function load(work: () => Promise<Feed>) {
    try { onFeed(await work()); } catch (error) { onError((error as Error).message); }
  }
</script>

<div class="picker">
  <label>
    Recording
    <input type="file" accept="application/json,.json" onchange={(e) => {
      const file = e.currentTarget.files?.[0];
      if (file !== undefined) load(async () => recordingFeed(await fromFile(file)));
    }} />
  </label>
  <input type="url" placeholder="or a URL to a recording" bind:value={url} style="width: 18rem" />
  <button disabled={url.length === 0} onclick={() => load(async () => recordingFeed(await fromUrl(url)))}>Load</button>
</div>

<style>
  .picker { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; }
</style>
