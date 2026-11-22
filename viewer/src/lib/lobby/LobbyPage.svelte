<!-- viewer/src/lib/lobby/LobbyPage.svelte -->
<!-- The lobby (spec 2026-11-21-setup-screen-layout-design.md → Lobby): Still to do and the team columns across the page, the Game settings
     panel on the right: the name, the rule set, activation and enforcement in a fold headed with their values, the optional rules, and at
     its foot the creator's Start setup (everyone else reads who starts it). The creator's edits stay in the form until Start setup sends
     them, then starts setup; the rule set applies as soon as it is picked. Every send is one edit Godot sends. -->
<script module lang="ts">
  /** The fold's open state, for the session (Godot's SetupFolds). */
  let foldOpenSeen = false;
</script>

<script lang="ts">
  import type { Api } from '../api/client';
  import { nameProblem } from '../api/names';
  import type { ArmySummary, PropertiesSummary } from '../api/types';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { sendEdit } from '../game/send';
  import type { Edit, Enforcement, SchedulerShape } from '../protocol/wire.generated';
  import { startSetup } from './lobbyEdits';
  import { ENFORCEMENT_HINT, ENFORCEMENT_LABEL, SHAPE_LABEL, configChanged, foldTitle, ruleSetEdit, ruleSetOf, rulesSummary, settingsSummary } from './lobbyPage';
  import { CUSTOM, configurationOf, presetOf, valueOf } from './rulesOptions';
  import { isCreator } from './seats';
  import { creatorName, stillToDo } from './teamColumns';
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
  let foldOpen = $state(foldOpenSeen);
  let starting = $state(false);
  $effect(() => { foldOpenSeen = foldOpen; });

  // The form follows each setting another edit changes (a rule set pick, another tab); my other edits stay.
  let seen: { name: string; shape: SchedulerShape; enforcement: Enforcement; rules: string } | null = null;
  $effect(() => {
    const now = { name: game.name, shape: game.schedulerShape, enforcement: game.enforcement,
      rules: JSON.stringify([game.propertiesHash, game.optionalRules, catalog.options.map((o) => o.key)]) };
    if (seen === null || seen.name !== now.name) name = now.name;
    if (seen === null || seen.shape !== now.shape) shape = now.shape;
    if (seen === null || seen.enforcement !== now.enforcement) enforcement = now.enforcement;
    if (seen === null || seen.rules !== now.rules) chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, game.optionalRules)]));
    seen = now;
  });

  $effect(() => {
    api.listArmies().then((list) => { armies = list; }).catch((e: Error) => { problem = e.message; });
  });

  $effect(() => {
    api.listProperties().then((list) => { sets = list; }).catch((e: Error) => { problem = e.message; });
  });

  const preset = $derived(presetOf(catalog, chosen));
  const todo = $derived(stillToDo(game, creator));
  const nameBad = $derived(name === '' ? null : nameProblem(name, 'A game name'));

  async function send(edit: Edit) { problem = await sendEdit(connection, edit); }

  function pickPreset(value: string) {
    const picked = catalog.presets.find((p) => p.name === value);
    if (picked === undefined) return;                                 // Custom: the selects stay as they are
    chosen = Object.fromEntries(catalog.options.map((o) => [o.key, valueOf(o, picked.optionalRules)]));
    if (picked.enforcement !== undefined) enforcement = picked.enforcement;
  }

  // The rule set decides the edition, and the edition which armies every picker offers: it applies on pick.
  function pickRuleSet(hash: string) {
    if (hash !== game.propertiesHash) void send(ruleSetEdit(game, catalog, hash));
  }

  // Start setup: the form first when it differs from the game, and setup starts only once the server took it.
  async function start() {
    const bad = nameProblem(name, 'A game name');
    if (bad !== null) { problem = bad; return; }
    starting = true;
    try {
      if (configChanged(game, catalog, name, shape, enforcement, chosen)) {
        problem = await sendEdit(connection, configurationOf(game, name, shape, enforcement, catalog, chosen));
        if (problem !== null) return;
      }
      problem = await sendEdit(connection, startSetup());
    } finally { starting = false; }
  }
</script>

<div class="lobby">
  <div class="inner">
    <section class="teams" aria-label="Teams">
      <div class="head"><h2>Lobby</h2></div>
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
        <form id="game-settings" class="body" onsubmit={(e) => { e.preventDefault(); void start(); }}>
          <label>Name <input bind:value={name} maxlength="64" placeholder="Game name" /></label>
          {#if nameBad !== null}<small class="warn">{nameBad}</small>{/if}
          <details bind:open={foldOpen}>
            <summary>{foldTitle(ruleSetOf(game), shape, enforcement)}</summary>
            <div class="fold">
              <label>Rule set
                <select value={game.propertiesHash} onchange={(e) => pickRuleSet(e.currentTarget.value)}>
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
            </div>
          </details>
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
                <label><span class="lt">{option.label}</span>
                  <select value={chosen[option.key] ?? option.default} onchange={(e) => { chosen = { ...chosen, [option.key]: e.currentTarget.value }; }}>
                    {#each option.values as v (v.value)}<option value={v.value}>{v.label}</option>{/each}
                  </select>
                </label>
              {/each}
            </fieldset>
          {/if}
        </form>
        <div class="foot">
          <button type="submit" form="game-settings" class="primary start" disabled={starting}>Start setup</button>
        </div>
      {:else}
        <div class="body">
          <dl><div><dt>Name</dt><dd>{game.name}</dd></div></dl>
          <details bind:open={foldOpen}>
            <summary>{foldTitle(ruleSetOf(game), game.schedulerShape, game.enforcement)}</summary>
            <dl class="fold">
              {#each rulesSummary(game) as item (item.label)}<div><dt>{item.label}</dt><dd>{item.value}</dd></div>{/each}
            </dl>
          </details>
          {#if catalog.options.length > 0}
            <h3>Optional rules</h3>
            <dl>
              {#each settingsSummary(game, catalog) as item (item.label)}<div><dt>{item.label}</dt><dd>{item.value}</dd></div>{/each}
            </dl>
          {/if}
        </div>
        <div class="foot"><p class="dim">{creatorName(game)} starts setup</p></div>
      {/if}
    </aside>
  </div>
</div>

<style>
  /* Wide: the teams scroll on their own and the Game panel stays whole, its Start setup always in view. Narrow: one scrolling column. */
  .lobby { min-height: 0; }
  .inner { height: 100%; max-width: 110rem; margin: 0 auto; padding: var(--space-4) var(--space-3); display: grid; grid-template-columns: minmax(0, 1fr) 22rem; grid-template-rows: minmax(0, 1fr);
    gap: var(--space-4); align-items: start; }
  .teams { max-height: 100%; overflow-y: auto; padding-right: var(--space-1); }
  .settings { max-height: 100%; display: flex; flex-direction: column; }
  .settings > h2 { flex: none; }
  .body { flex: 1 1 auto; min-height: 0; overflow-y: auto; margin: 0 calc(-1 * var(--space-2)); padding: 0 var(--space-2); }
  @media (max-width: 60rem) {
    .lobby { overflow-y: auto; }
    .inner { height: auto; grid-template-columns: minmax(0, 1fr); grid-template-rows: none; }
    .teams, .settings { max-height: none; overflow: visible; }
  }
  .head { margin-bottom: var(--space-3); }
  .head h2 { margin: 0; }
  .start { width: 100%; }
  .todo { list-style: none; margin: 0 0 var(--space-4); padding: 0; display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-4); color: var(--accent); }
  .todo li { display: inline-flex; align-items: center; gap: var(--space-1); }
  .todo li.missing { color: var(--warning); }
  .settings { margin: 0; }
  form { display: flex; flex-direction: column; gap: var(--space-3); }
  label { display: flex; flex-direction: column; align-items: stretch; gap: var(--space-1); margin: 0; color: var(--dim); font-size: .95rem; }
  label :global(input), label select { width: 100%; color: var(--text); font-size: 1rem; }
  label small { font-size: .85rem; }
  fieldset { display: flex; flex-direction: column; gap: var(--space-3); padding: var(--space-3); }
  .foot { flex: none; margin-top: var(--space-4); }
  .foot p { margin: 0; }
  div.body { display: flex; flex-direction: column; gap: var(--space-3); }
  details > summary { cursor: pointer; color: var(--text); }
  .fold { display: flex; flex-direction: column; gap: var(--space-3); margin-top: var(--space-3); }
  h3 { margin: 0; font-size: 1rem; }
  .warn { color: var(--warning); font-size: .85rem; margin-top: calc(-1 * var(--space-2)); }
  dl { margin: 0; display: flex; flex-direction: column; }
  dl > div { display: flex; justify-content: space-between; gap: var(--space-3); padding: var(--space-2) 0; border-top: 1px solid var(--rule); }
  dl > div:first-child { border-top: 0; padding-top: 0; }
  dt { color: var(--dim); }
  dd { margin: 0; text-align: right; }
</style>
