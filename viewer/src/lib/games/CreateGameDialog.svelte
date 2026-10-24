<!-- viewer/src/lib/games/CreateGameDialog.svelte -->
<!-- HomeScreen.CreateDialog: name, rule set, activation, enforcement and an optional army. Create waits until the name passes. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import { nameProblem } from '../api/names';
  import type { ArmySummary, CreateGameRequest, Enforcement, PropertiesSummary, SchedulerShape } from '../api/types';
  import { createDefaults } from './listModel';

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
      <p class="muted">Loading rule sets and armies…</p>
    {:else}
      <label>Game name
        <input type="text" bind:value={name} maxlength="64" placeholder="at most 64 characters" />
      </label>
      {#if name !== '' && problem !== null}<p class="warn" role="alert">{problem}</p>{/if}
      <label>Rule set
        <select bind:value={hash}>
          {#each packs as pack (pack.contentHash)}<option value={pack.contentHash}>{pack.name} ({pack.gameRules})</option>{/each}
        </select>
      </label>
      <label>Activation
        <select bind:value={shape}>
          <option value="alternating-activation">Alternating activation</option>
          <option value="player-turn">Player turn</option>
        </select>
      </label>
      <label>Enforcement
        <select bind:value={enforcement}>
          <option value="silent">Silent</option><option value="warn">Warn</option><option value="strict">Strict</option>
        </select>
      </label>
      <label>Army
        <select bind:value={armyId}>
          <option value="">No army (pick one in the lobby)</option>
          {#each armies as army (army.id)}<option value={army.id}>{army.name} ({army.units} units)</option>{/each}
        </select>
      </label>
      {#if armies.length === 0}<p class="muted">You have no armies yet: import one in the web app.</p>{/if}
    {/if}
    {#if failure !== null}<p class="error" role="alert">{failure}</p>{/if}
    <div class="actions">
      <button type="submit" disabled={!loaded || packs.length === 0 || problem !== null || busy}>{busy ? 'Creating…' : 'Create'}</button>
      <button type="button" onclick={() => dialog?.close()}>Cancel</button>
    </div>
  </form>
</dialog>

<style>
  dialog { background: var(--panel); color: var(--ink); border: 1px solid var(--line); min-width: 22rem; }
  dialog::backdrop { background: rgba(0, 0, 0, 0.5); }
  form { display: grid; gap: 0.5rem; }
  h2 { margin: 0; font-size: 1.05rem; }
  label { display: grid; gap: 0.15rem; }
  .actions { display: flex; gap: 0.5rem; justify-content: flex-end; }
  .warn { color: #e0a04a; }
</style>
