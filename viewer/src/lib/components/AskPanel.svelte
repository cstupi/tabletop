<script lang="ts">
  import type { RecordedAsk } from '../protocol/recording.generated';
  import { describeAsk } from '../asks/describe';

  let { asks, teamLabels }: { asks: readonly RecordedAsk[]; teamLabels: ReadonlyMap<string, string> } = $props();
  const views = $derived(asks.map((a) => describeAsk(a, teamLabels)));
</script>

{#if views.length === 0}
  <p class="muted">no open Ask</p>
{/if}
{#each views as view (view.id)}
  <section class="ask">
    <h3>{view.heading}</h3>
    <p>{view.prompt}</p>
    <p class="muted">
      asked of
      {#each view.addressees as team, i (team.id)}<span class:answered={team.answered}
        >{i > 0 ? ', ' : ''}{team.label}{team.answered ? ' ✓' : ''}</span>{/each}
    </p>
    {#if view.waitingOn.length > 0}<p class="muted">waiting on {view.waitingOn.join(', ')}</p>{/if}
    <p class="muted">legal set: {view.legal}</p>
    {#each view.notes as note (note)}<p class="muted">· {note}</p>{/each}
    {#if view.options.length > 0}
      <ul>{#each view.options as option (option.id)}<li><code>{option.id}</code> {option.label}</li>{/each}</ul>
    {/if}
    <!-- Display only. The viewer answers no Ask (spec → Interactive debugging is a door, not a room). -->
  </section>
{/each}

<style>
  .ask { border: 1px solid var(--line); border-radius: 3px; padding: var(--space-2) var(--space-2); margin-bottom: var(--space-2); }
  h3 { margin: 0 0 var(--space-1); font-size: 1rem; }
  p { margin: var(--space-1) 0; }
  ul { margin: var(--space-1) 0 0; padding-left: var(--space-3); }
  .answered { color: var(--ink); }
</style>
