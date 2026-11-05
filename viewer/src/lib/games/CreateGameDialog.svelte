<!-- viewer/src/lib/games/CreateGameDialog.svelte -->
<!-- HomeScreen.CreateDialog: name, rule set, activation, enforcement and an optional army. Create waits until the name passes. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import { nameProblem } from '../api/names';
  import type { ArmySummary, CreateGameRequest, Enforcement, PropertiesSummary, SchedulerShape } from '../api/types';
  import { createDefaults } from './listModel';
  import Loading from '../components/Loading.svelte';
  import { armyOptions } from '../lobby/lobbyView';
  import { ENFORCEMENT_HINT, ENFORCEMENT_LABEL, SHAPE_LABEL } from '../lobby/lobbyPage';

  let { api, onCreated, onClose }: {
    api: Api;
    /** The new game's id and the army to pick once the socket is welcomed (null: none). */
    onCreated: (gameId: string, armyId: string | null) => void;
    onClose: () => void;
  } = $props();

  let dialog: HTMLDialogElement | undefined = $state();
  let packs: PropertiesSummary[] = $state([]);
  let armies: ArmySummary[] = $state([]);
  let loaded = $state(false);
  let failure: string | null = $state(null);
  let busy = $state(false);

  let name = $state('');
  let hash = $state('');
  let shape: SchedulerShape = $state('alternating-activation');
  let enforcement: Enforcement = $state('warn');
  let armyId = $state('');

  const problem = $derived(nameProblem(name, 'A game name'));
  // The army list follows the selected set: another edition's armies are listed disabled with the reason, and a pick of one is dropped.
  const options = $derived(armyOptions(packs.find((p) => p.contentHash === hash)?.gameRules ?? '', armies));
  $effect(() => { if (armyId !== '' && !options.some((o) => o.army.id === armyId && o.disabledReason === null)) armyId = ''; });

  $effect(() => {
    dialog?.showModal();
    Promise.all([api.listProperties(), api.listArmies()]).then(([p, a]) => {
      packs = p; armies = a;
      const defaults = createDefaults(p);
      if (defaults === null) failure = 'The server has no rule set loaded, so no game can be created.';
      else { hash = defaults.propertiesHash; shape = defaults.schedulerShape; enforcement = defaults.enforcement; }
      loaded = true;
    }).catch((e: Error) => { failure = e.message; loaded = true; });
  });

  async function create(event: SubmitEvent) {
    event.preventDefault();
    if (problem !== null || busy) return;
    busy = true; failure = null;
    // WaitsBetweenSteps is a development flag; players never get the "Continue" stops.
    const request: CreateGameRequest = { name: name.trim(), propertiesHash: hash, schedulerShape: shape, enforcement, waitsBetweenSteps: false };
    try {
      const game = await api.createGame(request);
      onCreated(game.id, armyId === '' ? null : armyId);
    } catch (e) { failure = (e as Error).message; busy = false; }
  }
</script>

<dialog bind:this={dialog} onclose={onClose} aria-labelledby="create-title">
  <form onsubmit={create}>
    <h2 id="create-title">Create game</h2>
    {#if !loaded}
      <Loading text="Loading rule sets and armies…" inline />
    {:else}
      <label>Name
        <input type="text" bind:value={name} maxlength="64" placeholder="Friday night skirmish" />
      </label>
      {#if name !== '' && problem !== null}<small class="warn">{problem}</small>{/if}
      {#if packs.length > 1}
        <label>Rule set
          <select bind:value={hash}>
            {#each packs as pack (pack.contentHash)}<option value={pack.contentHash}>{pack.name}</option>{/each}
          </select>
        </label>
      {/if}
      <div class="two">
        <label>Activation
          <select bind:value={shape}>
            {#each Object.entries(SHAPE_LABEL) as [value, label] (value)}<option {value}>{label}</option>{/each}
          </select>
        </label>
        <label>Enforcement
          <select bind:value={enforcement}>
            {#each Object.entries(ENFORCEMENT_LABEL) as [value, label] (value)}<option {value}>{label}</option>{/each}
          </select>
        </label>
      </div>
      <small class="dim">{ENFORCEMENT_HINT[enforcement]}</small>
      <label>Your army
        <select bind:value={armyId}>
          <option value="">Choose in the lobby</option>
          {#each options as o (o.army.id)}<option value={o.army.id} disabled={o.disabledReason !== null} title={o.disabledReason}>{o.disabledReason === null ? `${o.label} · ${o.army.units} units` : o.text}</option>{/each}
        </select>
      </label>
      {#if armies.length === 0}<small class="dim">You have no armies yet: import one on the website's Armies page.</small>{/if}
    {/if}
    {#if failure !== null}<p role="alert">{failure}</p>{/if}
    <div class="actions">
      <button type="button" onclick={() => dialog?.close()}>Cancel</button>
      <button type="submit" class="primary" disabled={!loaded || packs.length === 0 || problem !== null || busy}>{busy ? 'Creating…' : 'Create game'}</button>
    </div>
  </form>
</dialog>

<style>
  dialog { width: min(30rem, calc(100vw - 2rem)); }
  form { display: flex; flex-direction: column; gap: var(--s3); }
  h2 { margin: 0 0 var(--s1); }
  label { display: flex; flex-direction: column; gap: var(--s1); color: var(--dim); font-size: .95rem; }
  label input, label select { width: 100%; color: var(--text); font-size: 1rem; }
  .two { display: grid; grid-template-columns: 1fr 1fr; gap: var(--s3); }
  small { font-size: .85rem; margin-top: calc(-1 * var(--s2)); }
  .warn { color: var(--warning); }
  .actions { justify-content: flex-end; margin: var(--s2) 0 0; }
</style>
