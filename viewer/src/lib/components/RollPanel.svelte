<script lang="ts">
  import type { RecordedRoll } from '../protocol/recording.generated';
  import { modifierText } from '../recording/modifier';

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
        · {roll.modifiers.map(modifierText).join(', ')}
      {/if}
      · {roll.success.filter(Boolean).length}/{roll.raw.length} passed
    </p>
  </section>
{/each}

<style>
  .roll { border: 1px solid var(--line); border-radius: 3px; padding: var(--space-2) var(--space-2); margin-bottom: var(--space-2); }
  h3 { margin: 0 0 var(--space-1); font-size: 1rem; }
  p { margin: var(--space-1) 0; }
  .dice { display: flex; flex-wrap: wrap; gap: var(--space-1); }
  .die { border: 1px solid var(--line); border-radius: 3px; padding: 0 var(--space-1); color: var(--muted); }
  .die.hit { border-color: #6ddf7c; color: #6ddf7c; }
</style>
