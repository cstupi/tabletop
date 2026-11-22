<!-- viewer/src/lib/play/DecisionBox.svelte -->
<!-- DecisionBox.cs's card (UX redesign part 2 → Decision box): binds the controller's DecisionPanel at the top of the play page's right
     column. Every decision is in decisionBox.ts; this only lays it out: the title band in my team's colour with the Ask's icon, the body,
     the actions at the foot (the primary one lit) with their key caps; with nothing of mine open, a quiet band per team the game waits on.
     Buttons never take focus, so the keys (digits, Enter, Esc) stay with the table. -->
<script lang="ts">
  import { teamColour } from '../board/palette';
  import type { Control, DecisionBox, DecisionPanel } from './decisionBox';
  import { keyCap } from './decisionCard';
  import DecisionIcon from './DecisionIcon.svelte';
  import HudIcon from './HudIcon.svelte';

  let { box, panel }: { box: DecisionBox; panel: DecisionPanel } = $props();

  const noFocus = (e: Event) => e.preventDefault();
  const colour = (ordinal: number): string => (ordinal > 0 ? teamColour(ordinal) : 'var(--dim)');
  const body = $derived(panel.controls.filter((c) => c.kind !== 'button'));
  /** The tile lit from the button over the unit (its hover), read again with each panel. */
  const lit = (_: DecisionPanel, id: string): boolean => box.hoveredControl === id;
  const foot = $derived(panel.controls.filter((c): c is Extract<Control, { kind: 'button' }> => c.kind === 'button'));
</script>

{#if panel.visible}
  {#if panel.head === null && panel.quiet.length > 0 && panel.refusal === null && panel.note === '' && panel.revealed === null}
    <section class="quiet-bands" aria-label="Waiting" aria-live="polite">
      {#each panel.quiet as q, i (i)}
        <p class="quiet" style:--team={colour(q.ordinal)}><DecisionIcon name="team" size={18} /><strong>{q.team}</strong><span>{q.doing}</span></p>
      {/each}
    </section>
  {:else}
    <section class="panel decision" aria-label="Decision" aria-live="polite" style:--team={colour(panel.ordinal)}>
      {#if panel.head !== null}
        <header class="band">
          <DecisionIcon name={panel.head.icon} size={22} />
          <h2>{panel.head.title}{#if panel.head.subject !== null}<span class="subject"> · {panel.head.subject}</span>{/if}</h2>
          {#if panel.seat !== ''}<span class="seat">{panel.seat}</span>{/if}
        </header>
      {/if}
      <div class="body">
        {#if panel.revealed !== null}
          <div class="reveal">
            {#each panel.revealed.flat() as chip, i (i)}
              <p class="quiet" style:--team={colour(chip.ordinal)}><strong>{chip.team}</strong><span>{chip.choice}</span></p>
            {/each}
          </div>
        {/if}
        {#if panel.head?.prompt}<p class="prompt">{panel.head.prompt}</p>{/if}
        {#if panel.note !== ''}<p class="note">{panel.note}</p>{/if}
        {#each body as control, i (i)}
          {#if control.kind === 'group'}
            {@const g = control.group}
            <div class="group" class:unit={g.unit !== null} style:--team={colour(g.teamOrdinal)}>
              {#if g.unit !== null}<p class="group-head"><strong>{g.unit}</strong>{#if g.pips !== null}<span class="pips">{g.pips}</span>{/if}</p>{/if}
              <div class="tiles" style:--cols={Math.min(4, Math.max(1, control.tiles.length))}>
                {#each control.tiles as tile (tile.id)}
                  <button type="button" class="tile" class:lit={lit(panel, tile.id)} tabindex="-1" disabled={tile.disabled} title={tile.tooltip ?? tile.label} onmousedown={noFocus}
                    onclick={() => box.press(tile.id)} onpointerenter={() => box.hover(tile.id)} onpointerleave={() => box.hover(null)}>
                    {#if tile.icon !== null}<DecisionIcon name={tile.icon} size={26} />{/if}
                    <span class="word">{tile.word}</span>
                    {#if keyCap(tile.key) !== ''}<kbd>{keyCap(tile.key)}</kbd>{/if}
                  </button>
                {/each}
              </div>
              {#if g.note !== null}<p class="warn">{g.note}</p>{/if}
            </div>
          {:else if control.kind === 'row'}
            <div class="pick-line">
              <button type="button" class="tile row" tabindex="-1" disabled={control.disabled} onmousedown={noFocus} style:--team={colour(control.ordinal)}
                onclick={() => box.press(control.id)} onpointerenter={() => box.hover(control.id)} onpointerleave={() => box.hover(null)}>
                <span class="team-icon"><DecisionIcon name="army" size={20} /></span>
                <span class="word">{control.label}
                  {#if control.chip}<span class="aside-chip" title={control.chip.tip}><HudIcon name="wait" size={12} />{control.chip.name}</span>{/if}
                </span>
                {#if control.pips !== null}<span class="pips">{control.pips}</span>{/if}
                {#if control.key !== ''}<kbd>{control.key}</kbd>{/if}
              </button>
              {#if control.aside}
                <button type="button" class="aside-btn flat" tabindex="-1" title={control.aside.tip} onmousedown={noFocus} onclick={() => box.press(control.aside!.id)}>
                  <HudIcon name="wait" size={14} />Set aside
                </button>
              {/if}
            </div>
          {:else if control.kind === 'bar'}
            {#if control.bar !== null}
              <div class="bar" class:over={control.bar.over}>
                <div class="track"><div class="fill" style:width={`${control.bar.fraction * 100}%`}></div>
                  {#if control.bar.minimumFraction !== null}<div class="mark" style:left={`${control.bar.minimumFraction * 100}%`}></div>{/if}
                </div>
                <span class="inches">{control.bar.text}</span>
              </div>
            {/if}
          {:else if control.kind === 'counter'}
            <p class="counter"><strong>{control.text}</strong><span class="casualties">{control.pips}</span></p>
          {:else if control.kind === 'chip'}
            <button type="button" class="tile chip" tabindex="-1" aria-pressed={control.pressed} disabled={control.disabled ?? false}
              onmousedown={noFocus} onclick={() => box.press(control.id)}>
              <span>{control.label}</span>
              {#if control.target}<span>→</span><span class="target" style:--team={colour(control.target.ordinal)}>{control.target.name}</span>
              {:else}<span class="dim">holds fire</span>{/if}
            </button>
          {:else if control.kind === 'spin'}
            <label class="spin">
              <span>{control.label}</span>
              {#if control.pips}<span class="pips">{control.pips}</span>{/if}
              <input type="number" min="0" max={control.max} step="1" value={control.value}
                oninput={(e) => box.spin(control.id, e.currentTarget.valueAsNumber)} />
            </label>
          {:else if control.kind === 'label'}
            <p class="label">{control.text}</p>
          {:else if (control.kind === 'note' || control.kind === 'reason') && control.text !== ''}
            <p class="warn reason">{control.text}</p>
          {/if}
        {/each}
        {#if panel.refusal !== null}<p role="alert">{panel.refusal}</p>{/if}
        {#if foot.length > 0}
          <div class="foot">
            {#each foot as b (b.id)}
              <button type="button" tabindex="-1" disabled={b.disabled} title={b.tooltip} class:primary={b.primary === true}
                class:flat={b.primary !== true} onmousedown={noFocus} onclick={() => box.press(b.id)}
                onpointerenter={() => box.hover(b.id)} onpointerleave={() => box.hover(null)}>
                {#if b.icon !== undefined}<DecisionIcon name={b.icon} size={18} />{/if}
                <span>{b.label}</span>
                {#if b.key !== undefined}<kbd>{b.key}</kbd>{/if}
              </button>
            {/each}
          </div>
        {/if}
      </div>
    </section>
  {/if}
{/if}

<style>
  /* The card takes its full height, up to most of the column (the unit card and the log below give way); past that the whole card scrolls,
     so no part of it, the main action least, is squeezed away. */
  .decision { display: flex; flex-direction: column; margin: 0; padding: 0; flex: none; min-height: 0; max-height: 70%; overflow-y: auto; }
  .decision > * { flex: none; }
  .band { display: flex; align-items: center; gap: var(--space-2); padding: var(--space-2) var(--space-3); background: var(--team); color: var(--bg); }
  .band h2 { margin: 0; flex: 1; min-width: 0; font-family: var(--font-display); font-size: 1.05rem; text-transform: uppercase; letter-spacing: .06em; color: inherit; }
  .band .subject { opacity: .85; }
  .seat { font-size: .8rem; text-transform: uppercase; letter-spacing: .08em; }
  .body { display: flex; flex-direction: column; gap: var(--space-2); padding: var(--space-3); }
  p { margin: 0; white-space: pre-line; }
  .prompt { color: var(--text); }
  .note, .dim { color: var(--dim); }
  .warn { color: var(--warning); }
  .reason { font-size: .9rem; line-height: 1.3; }
  .group { display: flex; flex-direction: column; gap: var(--space-2); }
  .group.unit { border: 2px solid color-mix(in srgb, var(--team) 85%, transparent); border-left-width: 7px; padding: var(--space-2); background: var(--panel-solid); }
  .group-head { display: flex; align-items: center; gap: var(--space-2); }
  .group-head strong { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: var(--font-display); }
  .pips { color: var(--team, var(--text)); letter-spacing: .05em; white-space: nowrap; overflow: hidden; max-width: 12rem; }
  .tiles { display: grid; grid-template-columns: repeat(var(--cols), minmax(0, 1fr)); gap: var(--space-2); }
  .tile { position: relative; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: var(--space-1); padding: var(--space-2) var(--space-2);
    min-height: 0; white-space: normal; text-align: center; }
  .tile kbd { position: absolute; top: 4px; right: 4px; }
  .tile.lit:not(:disabled) { background-color: var(--btn-hover); }
  .pick-line { display: flex; gap: var(--space-2); align-items: stretch; }
  .pick-line .tile.row { flex: 1; min-width: 0; }
  .aside-chip { display: inline-flex; align-items: center; gap: var(--space-1); font-size: .75rem; padding: 0 var(--space-1); color: inherit;
    border: 1px solid color-mix(in srgb, currentColor 45%, transparent); line-height: 1.4; white-space: nowrap; flex: none; }
  .aside-btn { flex: none; display: inline-flex; align-items: center; gap: var(--space-1); white-space: nowrap; min-width: 0; width: auto; padding: var(--space-1) var(--space-2); font-size: .8rem; }
  .tile.row { flex-direction: row; justify-content: flex-start; text-align: left; gap: var(--space-2); }
  .tile.row .word { flex: 1; min-width: 0; display: flex; flex-direction: column; align-items: flex-start; gap: var(--space-1); }
  .tile.row kbd { position: static; }
  .team-icon { color: var(--team); display: inline-flex; }
  .tile.chip { flex-direction: row; display: inline-flex; width: auto; align-self: flex-start; padding: var(--space-2) var(--space-2); }
  .tile.chip[aria-pressed='true'] { box-shadow: inset 0 0 0 2px var(--accent); }
  .target { color: var(--team); border-left: 5px solid var(--team); padding-left: var(--space-1); }
  .bar { display: flex; align-items: center; gap: var(--space-2); }
  .track { position: relative; flex: 1; height: 10px; background: var(--bg); border: 1px solid var(--line, var(--dim)); }
  .fill { height: 100%; background: var(--accent); }
  .bar.over .fill { background: var(--danger-text); }
  .bar.over .inches { color: var(--danger-text); }
  .mark { position: absolute; top: -3px; bottom: -3px; width: 2px; background: var(--text); }
  .inches { font-family: var(--font-num); white-space: nowrap; }
  .counter { display: flex; align-items: center; gap: var(--space-2); }
  .counter strong { font-family: var(--font-display); font-size: 1.3rem; }
  .casualties { color: var(--danger-text); letter-spacing: .08em; }
  .foot { display: flex; flex-wrap: wrap; gap: var(--space-2); align-items: center; }
  .foot button { display: inline-flex; align-items: center; gap: var(--space-2); width: auto; }
  .foot button.flat { background: none; box-shadow: none; clip-path: none; border-color: transparent; }
  kbd { font-family: var(--font-num); font-size: .75rem; line-height: 1; padding: var(--space-1) var(--space-1); border: 1px solid currentColor; border-radius: 3px; opacity: .7; flex: none; }
  .label { color: var(--text); font-family: var(--font-num); }
  .spin { display: flex; gap: var(--space-2); align-items: center; justify-content: space-between; }
  .spin > span:first-child { flex: 1; }
  .spin input { width: 5.5rem; }
  [role='alert'] { margin: 0; }
  .quiet-bands { display: flex; flex-direction: column; gap: var(--space-2); flex: none; }
  .reveal { display: flex; flex-wrap: wrap; gap: var(--space-2); }
  .quiet { display: flex; align-items: center; gap: var(--space-2); padding: var(--space-2) var(--space-2); background: var(--panel); border: 1px solid color-mix(in srgb, var(--dim) 50%, transparent);
    border-left: 6px solid var(--team); color: var(--team); }
  .quiet strong { text-transform: uppercase; letter-spacing: .06em; font-family: var(--font-display); }
  .quiet span { color: var(--dim); }
</style>
