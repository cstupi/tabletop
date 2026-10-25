<!-- viewer/src/lib/play/DecisionBox.svelte -->
<!-- DecisionBox.cs's panel: binds the controller's DecisionPanel at the top of the play page's right column. Every decision is in
     decisionBox.ts; this only lays it out: the confirming button is the primary one, a key in a label shows as a key. Buttons never take
     focus, so the keys (digits, Enter, Esc) stay with the table. -->
<script lang="ts">
  import type { DecisionBox, DecisionPanel } from './decisionBox';
  import { labelParts } from './decisionLabels';

  let { box, panel }: { box: DecisionBox; panel: DecisionPanel } = $props();

  /** The buttons that send or commit what the box holds: the one main action. */
  const PRIMARY = new Set(['commit', 'place', 'enter', 'spend', 'done', 'fire', 'confirm']);
  const QUIET = new Set(['mute', 'unmute']);
  const noFocus = (e: Event) => e.preventDefault();
  /** A box with nothing to answer only says whom the game waits on. */
  const waiting = $derived(panel.controls.length === 0 && panel.seat === '');
</script>

{#if panel.visible}
  <section class="panel decision" class:waiting aria-label="Decision" aria-live="polite">
    {#if panel.seat !== ''}<p class="seat">{panel.seat}</p>{/if}
    {#if panel.title !== ''}<h2 class="title">{panel.title}</h2>{/if}
    {#if panel.revealed !== null}<p class="revealed">{panel.revealed}</p>{/if}
    {#if panel.note !== ''}<p class="note">{panel.note}</p>{/if}
    {#if panel.controls.length > 0}
      <div class="controls">
        {#each panel.controls as control, i (i)}
          {#if control.kind === 'button'}
            {@const parts = labelParts(control.label)}
            <button
              type="button" tabindex="-1" disabled={control.disabled} title={control.tooltip}
              class:primary={PRIMARY.has(control.id)} class:quiet={QUIET.has(control.id)} class:option={parts.digit !== null}
              onmousedown={noFocus}
              onclick={() => box.press(control.id)}
              onpointerenter={() => box.hover(control.id)}
              onpointerleave={() => box.hover(null)}
            >
              {#if parts.digit !== null}<kbd>{parts.digit}</kbd>{/if}
              <span class="text">{parts.text}</span>
              {#if parts.key !== null}<kbd>{parts.key}</kbd>{/if}
            </button>
          {:else if control.kind === 'chip'}
            <button
              type="button" class="small toggle" tabindex="-1" aria-pressed={control.pressed} disabled={control.disabled ?? false}
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
            <p class="label">{control.text}</p>
          {:else if control.text !== ''}
            <p class="warn reason">{control.text}</p>
          {/if}
        {/each}
      </div>
    {/if}
    {#if panel.refusal !== null}<p role="alert">{panel.refusal}</p>{/if}
    {#if panel.hint !== ''}<p class="hint">{panel.hint}</p>{/if}
  </section>
{/if}

<style>
  /* The box takes its full height, up to most of the column (the unit card and the log below give way); past that the whole box scrolls,
     so no part of it, the main action least, is squeezed away. */
  .decision { display: flex; flex-direction: column; gap: var(--s2); margin: 0; padding: var(--s4); flex: none; min-height: 0; max-height: 70%; overflow-y: auto; }
  .decision > * { flex: none; }
  :global([data-theme='fantasy']) .decision { padding: calc(var(--s4) + 4px); }
  .decision.waiting { opacity: .85; }
  p { margin: 0; white-space: pre-line; }
  .seat { font-size: .8rem; color: var(--dim); text-transform: uppercase; letter-spacing: .08em; }
  .title { margin: 0; font-size: 1.1rem; color: var(--text); }
  .waiting .title { font-size: .95rem; color: var(--dim); }
  .revealed { color: var(--accent); }
  .controls { display: flex; flex-direction: column; gap: var(--s2); margin: 0 calc(-1 * var(--s1)); padding: 2px var(--s1); }
  .controls > button { justify-content: flex-start; width: 100%; flex: none; text-align: left; white-space: normal; }
  .controls > button .text { flex: 1; min-width: 0; }
  .controls > button.quiet { align-self: flex-start; width: auto; font-size: .9rem; }
  .controls > .toggle { align-self: flex-start; width: auto; }
  kbd { font-family: var(--font-num); font-size: .75rem; line-height: 1; padding: 3px 5px; border: 1px solid currentColor; border-radius: 3px; opacity: .7; flex: none; }
  .note { color: var(--text); }
  .warn { color: var(--warning); }
  .reason { font-size: .85rem; line-height: 1.3; margin-top: calc(-1 * var(--s1)); }
  .label { color: var(--text); }
  .hint { color: var(--dim); font-size: .9rem; }
  .spin { display: flex; gap: var(--s2); align-items: center; justify-content: space-between; }
  .spin input { width: 5.5rem; }
  [role='alert'] { margin: 0; }
</style>
