<!-- viewer/src/lib/play/PlayPage.svelte -->
<!-- A game in play (from `playing` on): the board of the projection, the decision box and the Game over modal. The page binds; the
     decisions are in decisionBox.ts, scene.ts and keys.ts. Moves and deploy placement are plan D2's. -->
<script lang="ts">
  import { untrack } from 'svelte';
  import type { Api } from '../api/client';
  import type { BoardHit, BoardInteraction, Modifiers } from '../board/interaction';
  import { extentOf } from '../board/table';
  import type { View } from '../board/transform';
  import Board from '../components/Board.svelte';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { loadGeometry, type Geometry } from '../geometry';
  import { acting, teamsOf } from '../lobby/seats';
  import { DecisionBox, type DecisionPanel } from './decisionBox';
  import DecisionBoxView from './DecisionBox.svelte';
  import GameOverModal from './GameOverModal.svelte';
  import { intentOf, textFocused } from './keys';
  import { fetchRuleTexts, RuleTexts } from './ruleTexts';
  import { sceneOfPlay } from './scene';
  import { loadSettings, saveSettings, type PlaySettings, type StorageLike } from './settings';

  let { connection, api, view = $bindable(null) }: { connection: GameConnection; api: Api; view?: View | null } = $props();

  let snap = $state.raw<ConnectionState | null>(null);
  let panel = $state.raw<DecisionPanel>({ seat: '', title: '', revealed: null, note: '', controls: [], refusal: null, hint: '', visible: false });
  let box = $state.raw<DecisionBox | null>(null);
  let geometry = $state.raw<Geometry | null>(null);
  let rules = $state.raw(RuleTexts.empty);
  let selected = $state.raw<ReadonlySet<string>>(new Set());
  let settings: PlaySettings = loadSettings(browserStorage());

  /** localStorage can throw (a private window, blocked site data): the settings then live for the page only. */
  function browserStorage(): StorageLike | null {
    try { return window.localStorage; } catch { return null; }
  }

  const myTeams = (s: ConnectionState): string[] => (s.game === null ? [] : teamsOf(s.game, s.participantId));

  $effect(() => {
    const conn = connection;
    const b = new DecisionBox({
      state: () => conn.state,
      myTeams: () => myTeams(conn.state),
      acting: () => { const s = conn.state; return acting(s.projection, myTeams(s), s.log); },
      settings: () => settings,
      updateSettings: (change) => { settings = change(settings); saveSettings(browserStorage(), settings); },
      rules: () => rules,
      send: (command) => conn.sendCommand(command),
      onRefused: (listener) => conn.onRefused((refused) => listener(refused)),
    });
    const stops = [
      b.subscribe((p) => { panel = p; }),
      conn.subscribe((s) => { snap = s; b.render(); }),
      conn.onEvents((events) => { b.reveal(events); b.render(); }),
    ];
    box = b;
    return () => { for (const stop of stops) stop(); b.dispose(); box = null; };
  });

  $effect(() => {
    loadGeometry().then((g) => { geometry = g; }).catch(() => { /* zones stay undrawn */ });
  });

  // The rule texts come once per property set, from playing on; a failure caches nothing and the next game frame tries again.
  const fetched = new Set<string>();
  $effect(() => {
    const game = snap?.game;
    if (game === null || game === undefined || (game.status !== 'playing' && game.status !== 'ended')) return;
    const hash = game.propertiesHash;
    untrack(() => {
      if (fetched.has(hash)) return;
      fetched.add(hash);
      void fetchRuleTexts(api, hash).then((got) => {
        if (got === null) { fetched.delete(hash); return; }
        rules = got.rules;
        box?.render();
      });
    });
  });

  const meshName = (piece: { mesh: { builtin?: string | undefined; assetId?: string | undefined } }): string => piece.mesh.builtin ?? piece.mesh.assetId ?? 'terrain';
  const scene = $derived(snap?.projection != null && snap.game !== null
    ? sceneOfPlay(snap.projection, snap.game, geometry?.zonePolygons ?? (() => []), meshName) : null);
  const extent = $derived(scene === null ? undefined : extentOf(scene));
  // The box's glow and marks are not reactive: a new panel (every change of either publishes one) hands the board new ones to redraw by.
  const glow = $derived.by(() => { void panel; const b = box; return (m: string, u: string) => b?.glowOf(m, u) ?? null; });
  const marks = $derived.by(() => { void panel; return box?.marks; });
  const teams = $derived(snap === null ? [] : myTeams(snap));
  const actingTeam = $derived(snap?.projection != null ? acting(snap.projection, teams, snap.log) : null);

  /** TableInput.OnLeftDown: a click on a model the open Ask offers answers (the model or its unit); any other model click selects its unit. */
  function clickModel(id: string): void {
    const b = box;
    const p = snap?.projection;
    if (b === null || p === null || p === undefined) return;
    const unit = p.models.get(id)?.unitId;
    const eligible = b.eligible;
    if (eligible.has(id)) b.model(id, false);
    if (eligible.has(id) || (unit !== undefined && eligible.has(unit))) { b.target(eligible.has(id) ? id : unit!); return; }
    if (unit === undefined) return;
    selected = new Set([...p.models.values()].filter((m) => m.unitId === unit).map((m) => m.id));
    b.unit(unit);
  }

  const interaction: BoardInteraction = {
    onSelect(hit: BoardHit | null) {
      if (hit?.kind === 'model') clickModel(hit.id);
      else selected = new Set();
    },
    onDragStart() { return false; },                          // hand moves are plan D2's: a drag pans
    onContextMenu(hit: BoardHit | null) {
      if (hit?.kind === 'model' && box?.eligible.has(hit.id) === true) box.model(hit.id, true);
    },
  };

  function onKeydown(e: KeyboardEvent): void {
    if (e.defaultPrevented || textFocused(e.target) || box === null) return;
    const mods: Modifiers = { shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey, alt: e.altKey };
    const intent = intentOf(e.key, mods);
    if (intent === null) return;
    const used = intent.kind === 'option' ? box.number(intent.key)
      : intent.kind === 'confirm' ? box.enter()
      : intent.kind === 'cancel' ? box.cancel()
      : intent.kind === 'stopHere' ? box.stop()
      : false;
    if (used) e.preventDefault();
  }
</script>

<svelte:window onkeydown={onKeydown} />

<div class="play">
  <div class="stage">
    {#if scene === null || snap === null}
      <p class="muted pad">Loading the game…</p>
    {:else}
      <Board {scene} bind:view {extent} {interaction} {selected} models="circles" {glow} {marks} />
    {/if}
  </div>
  <aside class="side">
    {#if box !== null}<DecisionBoxView {box} {panel} />{/if}
  </aside>
  {#if snap !== null && box !== null}
    <GameOverModal {connection} {snap} myTeams={teams} {actingTeam} />
  {/if}
</div>

<style>
  .play { display: grid; grid-template-columns: minmax(0, 1fr) 24rem; height: 100%; min-height: 0; }
  .stage { position: relative; min-height: 0; min-width: 0; }
  .side { overflow: auto; border-left: 1px solid var(--line); padding: 0.5rem 0.6rem; }
</style>
