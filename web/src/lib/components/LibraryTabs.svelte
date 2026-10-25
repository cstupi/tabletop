<script lang="ts">
  // The Library's two views: the account's own assets and other accounts' shared ones.
  let { current }: { current: 'mine' | 'shared' } = $props();
</script>

<nav class="tabs" aria-label="Library views">
  <a href="/library" aria-current={current === 'mine' ? 'page' : undefined}>My library</a>
  <a href="/library/shared" aria-current={current === 'shared' ? 'page' : undefined}>Shared</a>
</nav>
<style>
  .tabs { display: flex; gap: var(--s4); margin: 0 0 var(--s4); border-bottom: 1px solid var(--rule) }
  .tabs a { padding: var(--s2) 0; color: var(--dim); text-decoration: none; border-bottom: 2px solid transparent; margin-bottom: -1px }
  .tabs a:hover { color: var(--text) }
  .tabs a[aria-current='page'] { color: var(--text); border-bottom-color: var(--accent) }
</style>
