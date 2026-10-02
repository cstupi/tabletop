<script lang="ts">
  import type { RecordedRoll } from '../protocol/recording.generated';

  let { rolls }: { rolls: readonly RecordedRoll[] } = $props();
</script>

{#if rolls.length === 0}
  <p class="muted">no dice this frame</p>
{/if}
{#each rolls as roll (roll.id)}
  <section class="roll">
    <h3>{roll.roll}{roll.threshold === undefined ? '' : ` — needs ${roll.threshold}+`}</h3>
    <p class="dice">
      {#each roll.raw as raw, i (i)}
        <span class="die" class:hit={roll.success[i]}>
          {raw}{roll.modified[i] === raw ? '' : `→${roll.modified[i]}`}
        </span>
      {/each}
    </p>
    <p class="muted">
      net {roll.net >= 0 ? `+${roll.net}` : roll.net}
      {#if roll.modifiers.length > 0}
        · {roll.modifiers.map((m) => `${m.source} ${m.amount >= 0 ? `+${m.amount}` : m.amount}`).join(', ')}
      {/if}
      · {roll.success.filter(Boolean).length}/{roll.raw.length} passed
    </p>
  </section>
{/each}

<style>
  .roll { border: 1px solid var(--line); border-radius: 3px; padding: 0.4rem 0.5rem; margin-bottom: 0.5rem; }
  h3 { margin: 0 0 0.25rem; font-size: 1rem; }
  p { margin: 0.15rem 0; }
  .dice { display: flex; flex-wrap: wrap; gap: 0.25rem; }
  .die { border: 1px solid var(--line); border-radius: 3px; padding: 0 0.3rem; color: var(--muted); }
  .die.hit { border-color: #6ddf7c; color: #6ddf7c; }
</style>
