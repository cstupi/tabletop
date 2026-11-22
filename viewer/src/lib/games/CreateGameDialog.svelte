<!-- viewer/src/lib/games/CreateGameDialog.svelte -->
<!-- HomeScreen.CreateDialog: name, rule set, activation and enforcement. Create waits until the name passes; armies are picked in the lobby. -->
<script lang="ts">
  import type { Api } from '../api/client';
  import { nameProblem } from '../api/names';
  import type { CreateGameRequest, Enforcement, PropertiesSummary, SchedulerShape } from '../api/types';
  import { createDefaults } from './listModel';
  import Loading from '../components/Loading.svelte';
  import { ENFORCEMENT_HINT, ENFORCEMENT_LABEL, SHAPE_LABEL } from '../lobby/lobbyPage';

  let { api, onCreated, onClose }: {
    api: Api;
    /** The new game's id. */
    onCreated: (gameId: string) => void;
    onClose: () => void;
  } = $props();

  let dialog: HTMLDialogElement | undefined = $state();
  let packs: PropertiesSummary[] = $state([]);
  let loaded = $state(false);
  let failure: string | null = $state(null);
  let busy = $state(false);

  let name = $state('');
  let hash = $state('');
  let shape: SchedulerShape = $state('alternating-activation');
  let enforcement: Enforcement = $state('warn');

  const problem = $derived(nameProblem(name, 'A game name'));

  $effect(() => {
    dialog?.showModal();
    api.listProperties().then((p) => {
      packs = p;
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
      onCreated(game.id);
    } catch (e) { failure = (e as Error).message; busy = false; }
  }
</script>

<dialog bind:this={dialog} onclose={onClose} aria-labelledby="create-title">
  <form onsubmit={create}>
    <h2 id="create-title">Create game</h2>
    {#if !loaded}
      <Loading text="Loading rule sets…" inline />
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
  form { display: flex; flex-direction: column; gap: var(--space-3); }
  h2 { margin: 0; }
  label { display: flex; flex-direction: column; gap: var(--space-1); color: var(--dim); font-size: .95rem; }
  label input, label select { width: 100%; color: var(--text); font-size: 1rem; }
  .two { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
  small { font-size: .85rem; margin-top: calc(-1 * var(--space-2)); }   /* 8px under its field: a field's note belongs to it */
  .warn { color: var(--warning); }
  .actions { margin-top: var(--space-2); }   /* with the form's 16px gap, the dialogs' 24px above the buttons */
</style>
