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
  .ask { border: 1px solid var(--line); border-radius: 3px; padding: 0.4rem 0.5rem; margin-bottom: 0.5rem; }
  h3 { margin: 0 0 0.25rem; font-size: 1rem; }
  p { margin: 0.15rem 0; }
  ul { margin: 0.3rem 0 0; padding-left: 1.1rem; }
  .answered { color: var(--ink); }
</style>
