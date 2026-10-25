<!-- viewer/src/lib/setup/SetupPage.svelte -->
<!-- Setup on the 2D board: the draft as a scene, solid drags through Geometry, and the toolbar and card beside it. Every control sends
     one edit Godot sends; the server judges. -->
<script lang="ts">
  import { untrack } from 'svelte';
  import type { Api } from '../api/client';
  import type { BoardHit, BoardInteraction, Modifiers, Overlay } from '../board/interaction';
  import { extentOf, ghostOverlay, pieceOutlines, sceneOfTable } from '../board/table';
  import type { Point, View } from '../board/transform';
  import Board from '../components/Board.svelte';
  import type { ConnectionState, GameConnection } from '../connection/gameConnection';
  import { sendEdit } from '../game/send';
  import { loadGeometry, type Geometry } from '../geometry';
  import { endSetupText } from '../lobby/lobbyView';
  import type { Edit } from '../protocol/wire.generated';
  import { type Catalog, loadCatalog, nameOf } from './catalog';
  import { SetupDrag, type Held } from './drag';
  import { removeFromDraft, setupMenu, turnInDraft, type MenuItem, TERRAIN_TURN } from './edits';
  import PieceMenu from './PieceMenu.svelte';
  import SetupToolbar from './SetupToolbar.svelte';
  import TerrainCard from './TerrainCard.svelte';

  let { connection, snap, api, view = $bindable(null), creator }: {
    connection: GameConnection; snap: ConnectionState; api: Api; view?: View | null; creator: boolean;
  } = $props();

  let geometry = $state.raw<Geometry | null>(null);
  let catalog = $state.raw<Catalog | null>(null);
  let loadError: string | null = $state(null);
  let problem: string | null = $state(null);
  let selected: string | null = $state(null);
  let held = $state.raw<Held | null>(null);
  let menu = $state.raw<{ items: MenuItem[]; at: Point } | null>(null);

  const draft = $derived(snap.draft);
  const drag = $derived(geometry === null ? null : new SetupDrag(geometry));
  const picked: ReadonlySet<string> = $derived(new Set(selected === null ? [] : [selected]));

  $effect(() => {
    loadGeometry().then((g) => { geometry = g; }).catch((e: Error) => { loadError = `The table tools did not load: ${e.message}`; });
    loadCatalog(api).then((c) => { catalog = c; }).catch((e: Error) => { loadError = `The terrain catalog did not load: ${e.message}`; });
  });

  async function send(edit: Edit): Promise<void> { problem = await sendEdit(connection, edit); }
  const report = (text: string | null) => { problem = text; };

  const scene = $derived(draft !== null && geometry !== null && catalog !== null
    ? sceneOfTable(draft, geometry.zonePolygons, (p) => nameOf(p, catalog!)) : null);
  const extent = $derived(scene === null ? undefined : extentOf(scene));

  // A piece or marker another player removed leaves my selection and any drag of it.
  $effect(() => {
    const d = draft;
    untrack(() => {
      if (d !== null && selected !== null && !d.terrain.some((t) => t.id === selected) && !d.markers.some((m) => m.id === selected)) selected = null;
      if (held !== null && drag !== null && drag.held === null) held = null;
    });
  });

  const ring = (at: Point, radius: number, colour: string): Overlay => (ctx, project, ppi) => {
    const p = project(at);
    ctx.strokeStyle = colour;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.x, p.y, Math.max(4, radius * ppi), 0, 2 * Math.PI);
    ctx.stroke();
  };

  const overlays = $derived.by((): Overlay[] => {
    if (held === null || draft === null) return [];
    if (held.kind === 'marker') return [ring(held.at, 0.75, '#ffffff')];
    const piece = draft.terrain.find((t) => t.id === held!.id);
    if (piece === undefined) return [];
    return [
      ghostOverlay(pieceOutlines(piece, { position: held.end.drawn, yaw: held.yaw }), '#8ab4ff'),
      ring(held.end.lands, 0.75, '#6ddf7c'),
    ];
  });

  const interaction: BoardInteraction = {
    onSelect(hit: BoardHit | null) { menu = null; selected = hit !== null && hit.kind !== 'model' ? hit.id : null; },
    onDragStart(hit: BoardHit, at: Point) {
      menu = null;
      if (draft === null || drag === null) return false;
      const taken = drag.start(draft, hit, at);
      if (taken) { selected = hit.kind === 'model' ? null : hit.id; held = drag.held; }
      return taken;
    },
    onDragMove(at: Point) { if (draft !== null && drag !== null) held = drag.move(draft, at); },
    onDragEnd() {
      if (draft === null || drag === null) return;
      const edit = drag.end(draft);
      held = null;
      if (edit !== null) void send(edit);
    },
    onDragCancel() { drag?.cancel(); held = null; },
    onContextMenu(hit: BoardHit | null, _at: Point, screen: Point) {
      if (hit === null || hit.kind === 'model' || draft === null || geometry === null) { menu = null; return; }
      selected = hit.id;
      const items = setupMenu(geometry, draft, hit.id);
      menu = items.length === 0 ? null : { items, at: screen };
    },
    onKey(key: string, mods: Modifiers) {
      if (draft === null || geometry === null || drag === null) return false;
      const k = key.toLowerCase();
      if (k === 'q' || k === 'e') {
        const step = (mods.shift ? 2 : 1) * TERRAIN_TURN * (k === 'q' ? 1 : -1);
        if (held !== null) { held = drag.turn(draft, step); return true; }
        const piece = selected === null ? undefined : draft.terrain.find((t) => t.id === selected);
        if (piece === undefined) return false;
        const turn = turnInDraft(geometry, draft, piece.id, piece.yaw.degrees + step);
        if (turn !== null) void send(turn);
        return true;
      }
      if ((key === 'Delete' || key === 'Backspace') && selected !== null) {
        const remove = removeFromDraft(draft, selected);
        if (remove !== null) void send(remove);
        return true;
      }
      return false;
    },
  };

  async function runMenu(item: MenuItem) {
    menu = null;
    let text = '';
    if (item.input === 'integer') {
      const answer = window.prompt(item.label.replace('…', ''), '0');
      if (answer === null) return;
      text = answer;
    }
    const edit = item.edit(text);
    if (edit === null) { problem = 'That is not a whole number of degrees.'; return; }
    await send(edit);
  }
</script>

<div class="setup">
  <div class="stage">
    <div class="bar">
      <span class="muted">Drag terrain and markers; Q and E turn, Delete removes, right-click for the menu.</span>
      {#if creator}<button onclick={() => send({ kind: 'end_setup' })}>{endSetupText(draft)}</button>{/if}
    </div>
    {#if loadError !== null}
      <p class="error pad" role="alert">{loadError}</p>
    {:else if scene === null}
      <p class="muted pad">Loading the table tools…</p>
    {:else}
      <div class="board">
        <Board {scene} bind:view {extent} {interaction} {overlays} selected={picked} />
        {#if menu !== null}
          <PieceMenu items={menu.items} at={menu.at} onPick={runMenu} onClose={() => (menu = null)} />
        {/if}
      </div>
    {/if}
  </div>
  <aside class="side">
    {#if problem !== null}<p class="error" role="alert">{problem}</p>{/if}
    {#if draft !== null && catalog !== null && geometry !== null}
      <TerrainCard {draft} {catalog} {geometry} {api} {selected} {send} {report} />
      <SetupToolbar {draft} {catalog} {api} {send} {report} />
    {/if}
  </aside>
</div>

<style>
  .setup { display: grid; grid-template-columns: minmax(0, 1fr) 22rem; height: 100%; min-height: 0; }
  .stage { display: grid; grid-template-rows: auto minmax(0, 1fr); min-height: 0; min-width: 0; }
  .bar { display: flex; gap: 0.75rem; align-items: center; justify-content: space-between; padding: 0.3rem 0.6rem; border-bottom: 1px solid var(--line); }
  .board { position: relative; min-height: 0; }
  .side { overflow: auto; border-left: 1px solid var(--line); padding: 0.4rem 0.6rem; }
</style>
