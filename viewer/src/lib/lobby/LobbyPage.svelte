<!-- viewer/src/lib/lobby/LobbyPage.svelte -->
<!-- The lobby (LobbyPanel.cs's edits): the teams on the left, the Game panel on the right with the creator's settings and Start setup
     at its foot; everyone else sees the settings in words and what they wait for. Every control is one edit Godot sends. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import { nameProblem } from '../api/names';
  import type { ArmySummary } from '../api/types';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { sendEdit } from '../game/send';
  import type { Edit, Enforcement, SchedulerShape } from '../protocol/wire.generated';
  import { createTeam, startSetup } from './lobbyEdits';
  import { seatLine } from './lobbyView';
  import { ENFORCEMENT_HINT, ENFORCEMENT_LABEL, SHAPE_LABEL, configChanged, settingsSummary, waitingFor } from './lobbyPage';
  import { CUSTOM, configurationOf, presetOf, valueOf } from './rulesOptions';
  import { isCreator } from './seats';
  import TeamCards from './TeamCards.svelte';

  let { connection, snap, api }: { connection: GameConnection; snap: ConnectionState; api: Api } = $props();

  const game = $derived(snap.game!);
  const creator = $derived(isCreator(game, snap.participantId));
  const catalog = $derived(snap.options);
  const participant = $derived(game.participants.some((p) => p.id === snap.participantId));

  let problem: string | null = $state(null);
  let armies: ArmySummary[] = $state([]);
  let name = $state('');
  let shape: SchedulerShape = $state('alternating-activation');
  let enforcement: Enforcement = $state('warn');
  let chosen: Record<string, string> = $state({});

  // The form follows the game's configuration whenever another edit changes it.
  const configKey = $derived(JSON.stringify([game.name, game.schedulerShape, game.enforcement, game.optionalRules]));
  $effect(() => {
    void configKey;
    name = game.name;
    shape = game.schedulerShape;
    enforcement = game.enforcement;
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, game.optionalRules)]));
  });

  $effect(() => {
    api.listArmies().then((list) => { armies = list; }).catch((e: Error) => { problem = e.message; });
  });

  const preset = $derived(presetOf(catalog, chosen));
  const changed = $derived(configChanged(game, catalog, name, shape, enforcement, chosen));
  const waiting = $derived(waitingFor(game, creator));

  async function send(edit: Edit) { problem = await sendEdit(connection, edit); }

  function pickPreset(value: string) {
    const picked = catalog.presets.find((p) => p.name === value);
    if (picked === undefined) return;                                 // Custom: the selects stay as they are
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, picked.optionalRules)]));
    if (picked.enforcement !== undefined) enforcement = picked.enforcement;
  }

  function apply(event: SubmitEvent) {
    event.preventDefault();
    const bad = nameProblem(name, 'A game name');
    if (bad !== null) { problem = bad; return; }
    void send(configurationOf(game, name, shape, enforcement, catalog, chosen));
  }

  function revert() {
    name = game.name; shape = game.schedulerShape; enforcement = game.enforcement;
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, game.optionalRules)]));
  }
</script>

<div class="lobby">
  <div class="inner">
    <section class="teams" aria-label="Teams">
      <div class="head">
        <h2>Teams</h2>
        {#if participant}<button class="small" onclick={() => send(createTeam())}>Add team</button>{/if}
      </div>
      <p class="seat">{seatLine(game, snap.participantId)}</p>
      {#if problem !== null}<p role="alert">{problem} <button class="quiet" onclick={() => (problem = null)}>Dismiss</button></p>{/if}
      {#if game.teams.length === 0}
        <div class="panel empty">
          <p>No teams yet. Add one for each side of the table.</p>
          {#if participant}<button class="primary" onclick={() => send(createTeam())}>Add team</button>{/if}
        </div>
      {/if}
      <TeamCards {snap} {armies} {creator} {send} />
    </section>

    <aside class="panel settings" aria-label="Game">
      <h2>Game</h2>
      {#if creator}
        <form id="game-settings" class="body" onsubmit={apply}>
          <label>Name <input bind:value={name} maxlength="64" placeholder="Game name" /></label>
          <label>Activation
            <select bind:value={shape}>
              {#each Object.entries(SHAPE_LABEL) as [value, label] (value)}<option {value}>{label}</option>{/each}
            </select>
          </label>
          <label>Enforcement
            <select bind:value={enforcement}>
              {#each Object.entries(ENFORCEMENT_LABEL) as [value, label] (value)}<option {value}>{label}</option>{/each}
            </select>
            <small>{ENFORCEMENT_HINT[enforcement]}</small>
          </label>
          {#if catalog.options.length > 0}
            <fieldset>
              <legend>Optional rules</legend>
              <label>Preset
                <select value={preset} onchange={(e) => pickPreset(e.currentTarget.value)}>
                  {#each catalog.presets as p (p.name)}<option value={p.name}>{p.name}</option>{/each}
                  <option value={CUSTOM}>{CUSTOM}</option>
                </select>
              </label>
              {#each catalog.options as option (option.key)}
                <label><span class="lt">{option.label} <span class="page">{option.page}</span></span>
                  <select value={chosen[option.key] ?? option.default} onchange={(e) => { chosen = { ...chosen, [option.key]: e.currentTarget.value }; }}>
                    {#each option.values as v (v.value)}<option value={v.value}>{v.label}</option>{/each}
                  </select>
                </label>
              {/each}
            </fieldset>
          {/if}
        </form>
        <div class="foot">
          {#if changed}
            <div class="apply">
              <button type="submit" form="game-settings">Apply changes</button>
              <button type="button" class="quiet" onclick={revert}>Undo</button>
            </div>
          {/if}
          <button class="primary start" disabled={changed} title={changed ? 'Apply or undo your changes first' : ''}
            onclick={() => send(startSetup())}>Start setup</button>
          <small>Everyone can then lay out the table; you end setup to start the game.</small>
        </div>
      {:else}
        <dl class="body">
          {#each settingsSummary(game, catalog) as item (item.label)}
            <div><dt>{item.label}</dt><dd>{item.value}</dd></div>
          {/each}
        </dl>
        {#if waiting !== null}<p class="notice waiting" role="status">{waiting}</p>{/if}
      {/if}
    </aside>
  </div>
</div>

<style>
  /* Wide: the teams scroll on their own and the Game panel stays whole, its Start setup always in view. Narrow: one scrolling column. */
  .lobby { min-height: 0; }
  .inner { height: 100%; max-width: 80rem; margin: 0 auto; padding: var(--s5) var(--s4); display: grid; grid-template-columns: minmax(0, 1fr) 22rem; grid-template-rows: minmax(0, 1fr);
    gap: var(--s5); align-items: start; }
  .teams { max-height: 100%; overflow-y: auto; padding-right: var(--s1); }
  .settings { max-height: 100%; display: flex; flex-direction: column; }
  .settings > h2 { flex: none; }
  .body { flex: 1 1 auto; min-height: 0; overflow-y: auto; margin: 0 calc(-1 * var(--s2)); padding: 0 var(--s2); }
  @media (max-width: 60rem) {
    .lobby { overflow-y: auto; }
    .inner { height: auto; grid-template-columns: minmax(0, 1fr); grid-template-rows: none; }
    .teams, .settings { max-height: none; overflow: visible; }
  }
  .head { display: flex; align-items: center; justify-content: space-between; gap: var(--s3); margin-bottom: var(--s2); }
  .head h2 { margin: 0; }
  .seat { margin: 0 0 var(--s4); color: var(--text); }
  .settings { margin: 0; }
  form { display: flex; flex-direction: column; gap: var(--s3); }
  label { display: flex; flex-direction: column; align-items: stretch; gap: var(--s1); margin: 0; color: var(--dim); font-size: .95rem; }
  label :global(input), label select { width: 100%; color: var(--text); font-size: 1rem; }
  label small { font-size: .85rem; }
  .page { color: var(--dim); font-size: .8rem; }
  fieldset { display: flex; flex-direction: column; gap: var(--s3); padding: var(--s3); }
  .apply { display: flex; align-items: center; gap: var(--s3); }
  .foot { flex: none; display: flex; flex-direction: column; gap: var(--s2); margin-top: var(--s4); padding-top: var(--s4); border-top: 1px solid var(--rule); }
  .start { width: 100%; }
  .foot small { color: var(--dim); }
  dl { margin: 0; display: flex; flex-direction: column; }
  dl > div { display: flex; justify-content: space-between; gap: var(--s3); padding: var(--s2) 0; border-top: 1px solid var(--rule); }
  dl > div:first-child { border-top: 0; padding-top: 0; }
  dt { color: var(--dim); }
  dd { margin: 0; text-align: right; }
  .waiting { margin: var(--s4) 0 0; }
</style>
