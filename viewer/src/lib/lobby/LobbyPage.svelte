<!-- viewer/src/lib/lobby/LobbyPage.svelte -->
<!-- The lobby (spec 2026-11-07-lobby-and-setup-redesign-design.md): a head with the creator's Start setup and Still to do, the team columns
     across the page, the Game panel on the right with the creator's settings; everyone else sees the settings in words. Every control is
     one edit Godot sends. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import { nameProblem } from '../api/names';
  import type { ArmySummary, PropertiesSummary } from '../api/types';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { sendEdit } from '../game/send';
  import type { Edit, Enforcement, SchedulerShape } from '../protocol/wire.generated';
  import { startSetup } from './lobbyEdits';
  import { ENFORCEMENT_HINT, ENFORCEMENT_LABEL, SHAPE_LABEL, configChanged, settingsSummary } from './lobbyPage';
  import { CUSTOM, configurationOf, presetOf, valueOf } from './rulesOptions';
  import { isCreator } from './seats';
  import { stillToDo } from './teamColumns';
  import TeamColumns from './TeamColumns.svelte';
  import TeamIcon from './TeamIcon.svelte';

  let { connection, snap, api }: { connection: GameConnection; snap: ConnectionState; api: Api } = $props();

  const game = $derived(snap.game!);
  const creator = $derived(isCreator(game, snap.participantId));
  const catalog = $derived(snap.options);

  let problem: string | null = $state(null);
  let armies: ArmySummary[] = $state([]);
  let name = $state('');
  let shape: SchedulerShape = $state('alternating-activation');
  let enforcement: Enforcement = $state('warn');
  let chosen: Record<string, string> = $state({});
  let sets: PropertiesSummary[] = $state([]);
  let hash = $state('');

  // The form follows the game's configuration whenever another edit changes it.
  const configKey = $derived(JSON.stringify([game.name, game.propertiesHash, game.schedulerShape, game.enforcement, game.optionalRules]));
  $effect(() => {
    void configKey;
    name = game.name;
    hash = game.propertiesHash;
    shape = game.schedulerShape;
    enforcement = game.enforcement;
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, game.optionalRules)]));
  });

  $effect(() => {
    api.listArmies().then((list) => { armies = list; }).catch((e: Error) => { problem = e.message; });
  });

  $effect(() => {
    api.listProperties().then((list) => { sets = list; }).catch((e: Error) => { problem = e.message; });
  });

  const preset = $derived(presetOf(catalog, chosen));
  const changed = $derived(configChanged(game, catalog, name, shape, enforcement, chosen, hash));
  const todo = $derived(stillToDo(game, creator));

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
    void send(configurationOf(game, name, shape, enforcement, catalog, chosen, hash));
  }

  function revert() {
    name = game.name; hash = game.propertiesHash; shape = game.schedulerShape; enforcement = game.enforcement;
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, game.optionalRules)]));
  }
</script>

<div class="lobby">
  <div class="inner">
    <section class="teams" aria-label="Teams">
      <div class="head">
        <h2>Lobby</h2>
        {#if creator}
          <button class="primary start" disabled={changed} title={changed ? 'Apply or undo your changes first' : ''}
            onclick={() => send(startSetup())}>Start setup</button>
        {/if}
      </div>
      <ul class="todo" aria-label="Still to do">
        {#each todo as line (line.text)}
          <li class:missing={line.missing}>{#if line.missing}<TeamIcon name="warning" size={16} />{/if}{line.text}</li>
        {/each}
      </ul>
      {#if problem !== null}<p role="alert">{problem} <button class="quiet" onclick={() => (problem = null)}>Dismiss</button></p>{/if}
      <TeamColumns {snap} {armies} {send} />
    </section>

    <aside class="panel settings" aria-label="Game">
      <h2>Game settings</h2>
      {#if creator}
        <form id="game-settings" class="body" onsubmit={apply}>
          <label>Name <input bind:value={name} maxlength="64" placeholder="Game name" /></label>
          <label>Rule set
            <select bind:value={hash}>
              {#each sets as s (s.contentHash)}<option value={s.contentHash}>{s.name}</option>{/each}
            </select>
          </label>
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
        {#if changed}
          <div class="foot">
            <div class="apply">
              <button type="submit" form="game-settings">Apply changes</button>
              <button type="button" class="quiet" onclick={revert}>Undo</button>
            </div>
          </div>
        {/if}
      {:else}
        <dl class="body">
          {#each settingsSummary(game, catalog) as item (item.label)}
            <div><dt>{item.label}</dt><dd>{item.value}</dd></div>
          {/each}
        </dl>
      {/if}
    </aside>
  </div>
</div>

<style>
  /* Wide: the teams scroll on their own and the Game panel stays whole, its Start setup always in view. Narrow: one scrolling column. */
  .lobby { min-height: 0; }
  .inner { height: 100%; max-width: 110rem; margin: 0 auto; padding: var(--s5) var(--s4); display: grid; grid-template-columns: minmax(0, 1fr) 22rem; grid-template-rows: minmax(0, 1fr);
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
  .head { display: flex; align-items: center; gap: var(--s5); margin-bottom: var(--s3); }
  .head h2 { margin: 0; }
  .start { min-width: 12rem; }
  .todo { list-style: none; margin: 0 0 var(--s5); padding: 0; display: flex; flex-wrap: wrap; gap: var(--s2) var(--s5); color: var(--accent); }
  .todo li { display: inline-flex; align-items: center; gap: var(--s1); }
  .todo li.missing { color: var(--warning); }
  .settings { margin: 0; }
  form { display: flex; flex-direction: column; gap: var(--s3); }
  label { display: flex; flex-direction: column; align-items: stretch; gap: var(--s1); margin: 0; color: var(--dim); font-size: .95rem; }
  label :global(input), label select { width: 100%; color: var(--text); font-size: 1rem; }
  label small { font-size: .85rem; }
  .page { color: var(--dim); font-size: .8rem; }
  fieldset { display: flex; flex-direction: column; gap: var(--s3); padding: var(--s3); }
  .apply { display: flex; align-items: center; gap: var(--s3); }
  .foot { flex: none; display: flex; flex-direction: column; gap: var(--s2); margin-top: var(--s4); padding-top: var(--s4); border-top: 1px solid var(--rule); }
  dl { margin: 0; display: flex; flex-direction: column; }
  dl > div { display: flex; justify-content: space-between; gap: var(--s3); padding: var(--s2) 0; border-top: 1px solid var(--rule); }
  dl > div:first-child { border-top: 0; padding-top: 0; }
  dt { color: var(--dim); }
  dd { margin: 0; text-align: right; }
</style>
