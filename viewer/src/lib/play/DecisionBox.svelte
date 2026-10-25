<!-- viewer/src/lib/play/DecisionBox.svelte -->
<!-- DecisionBox.cs's panel: binds the controller's DecisionPanel. Every decision is in decisionBox.ts. Buttons never take focus, so the
     keys (digits, Enter, Esc) stay with the table. -->
<script lang="ts">
  import type { DecisionBox, DecisionPanel } from './decisionBox';

  let { box, panel }: { box: DecisionBox; panel: DecisionPanel } = $props();

  const noFocus = (e: Event) => e.preventDefault();
</script>

{#if panel.visible}
  <section class="decision" aria-label="Decision">
    {#if panel.seat !== ''}<p class="seat">{panel.seat}</p>{/if}
    {#if panel.title !== ''}<p class="title">{panel.title}</p>{/if}
    {#if panel.revealed !== null}<p class="revealed">{panel.revealed}</p>{/if}
    {#if panel.note !== ''}<p class="warn">{panel.note}</p>{/if}
    <div class="controls">
      {#each panel.controls as control, i (i)}
        {#if control.kind === 'button'}
          <button
            type="button" tabindex="-1" disabled={control.disabled} title={control.tooltip}
            onmousedown={noFocus}
            onclick={() => box.press(control.id)}
            onpointerenter={() => box.hover(control.id)}
            onpointerleave={() => box.hover(null)}
          >{control.label}</button>
        {:else if control.kind === 'chip'}
          <button
            type="button" class="chip" tabindex="-1" aria-pressed={control.pressed} disabled={control.disabled ?? false}
            onmousedown={noFocus} onclick={() => box.press(control.id)}
          >{control.label}</button>
        {:else if control.kind === 'spin'}
          <label class="spin">
            <span>{control.label}</span>
            <input
              type="number" min="0" max={control.max} step="1" value={control.value}
              oninput={(e) => box.spin(control.id, e.currentTarget.valueAsNumber)}
            />
          </label>
        {:else if control.kind === 'label'}
          <p>{control.text}</p>
        {:else}
          <p class="warn">{control.text}</p>
        {/if}
      {/each}
    </div>
    {#if panel.refusal !== null}<p class="warn" role="alert">{panel.refusal}</p>{/if}
    {#if panel.hint !== ''}<p class="hint">{panel.hint}</p>{/if}
  </section>
{/if}

<style>
  .decision { display: flex; flex-direction: column; gap: 0.4rem; padding: 0.6rem; border: 1px solid var(--line); border-radius: 4px; background: var(--panel, transparent); }
  p { margin: 0; white-space: pre-line; }
  .seat { color: var(--accent, inherit); font-weight: 600; }
  .title { font-weight: 600; }
  .controls { display: flex; flex-direction: column; gap: 0.3rem; max-height: 60vh; overflow-y: auto; padding-right: 0.5rem; }
  .controls .chip { display: inline-block; }
  .chip[aria-pressed='true'] { outline: 2px solid var(--accent, currentColor); }
  .warn { color: var(--warning, #ffc44e); }
  .hint { color: var(--muted); font-size: 0.85em; }
  .spin { display: flex; gap: 0.5rem; align-items: center; justify-content: space-between; }
  .spin input { width: 5rem; }
</style>
