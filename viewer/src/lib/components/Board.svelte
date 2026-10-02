<script lang="ts">
  import type { Scene } from '../board/frame';
  import { draw } from '../board/draw';
  import { type Hit, pickAt } from '../board/pick';
  import { type View, fit, panBy, toTable, zoomAt } from '../board/transform';

  let {
    scene, selected = null, onPick, showGrid = true, showZones = true, showLabels = true,
  }: {
    scene: Scene; selected?: string | null; onPick: (hit: Hit | null) => void;
    showGrid?: boolean; showZones?: boolean; showLabels?: boolean;
  } = $props();

  let canvas: HTMLCanvasElement | undefined = $state();
  let box = $state({ width: 800, height: 600 });
  let view: View | null = $state(null);
  let hovered: string | undefined = $state(undefined);
  let dragging: { x: number; y: number } | null = null;

  const current = $derived(view ?? fit(scene.table, box));

  $effect(() => {
    if (canvas === undefined) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(box.width * dpr);
    canvas.height = Math.round(box.height * dpr);
    const ctx = canvas.getContext('2d')!;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    draw(ctx, scene, current, box, { selected: selected ?? undefined, hovered, showGrid, showZones, showLabels });
  });

  $effect(() => {
    if (canvas === undefined) return;
    const observer = new ResizeObserver(([entry]) => {
      const rect = entry!.contentRect;
      box = { width: Math.max(1, rect.width), height: Math.max(1, rect.height) };
    });
    observer.observe(canvas);
    return () => observer.disconnect();
  });

  function local(event: PointerEvent | WheelEvent): { x: number; y: number } {
    const rect = canvas!.getBoundingClientRect();
    return { x: event.clientX - rect.left, y: event.clientY - rect.top };
  }
</script>

<div class="board">
  <canvas
    bind:this={canvas}
    onwheel={(e) => { e.preventDefault(); view = zoomAt(current, scene.table, local(e), e.deltaY < 0 ? 1.15 : 1 / 1.15); }}
    onpointerdown={(e) => { dragging = local(e); canvas!.setPointerCapture(e.pointerId); }}
    onpointermove={(e) => {
      const p = local(e);
      if (dragging !== null) { view = panBy(current, p.x - dragging.x, p.y - dragging.y); dragging = p; return; }
      const hit = pickAt(scene, toTable(current, scene.table, p));
      hovered = hit !== null && hit.kind === 'model' ? hit.id : undefined;
    }}
    onpointerup={(e) => {
      const p = local(e);
      const moved = dragging !== null && (Math.abs(p.x - dragging.x) > 2 || Math.abs(p.y - dragging.y) > 2);
      dragging = null;
      canvas!.releasePointerCapture(e.pointerId);
      if (!moved) onPick(pickAt(scene, toTable(current, scene.table, p)));
    }}
  ></canvas>
  <button onclick={() => (view = null)}>Reset view</button>
</div>

<style>
  .board { position: relative; width: 100%; height: 100%; }
  canvas { display: block; width: 100%; height: 100%; touch-action: none; cursor: crosshair; }
  button { position: absolute; top: 0.5rem; right: 0.5rem; }
</style>
