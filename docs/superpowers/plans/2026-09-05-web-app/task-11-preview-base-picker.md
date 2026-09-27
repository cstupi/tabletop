# Task 11: The preview and the base picker

**Files:**
- Create: `web/src/lib/components/MeshPreview.svelte`, `web/src/lib/components/BasePicker.svelte`, `web/src/lib/mesh/three-frame.ts`
- Test: `web/tests/three-frame.test.ts`

**Interfaces:**
- Consumes: `Base`, `Orientation`, `Volume`; `BASE_PRESETS`, `presetFor`, `baseLabel`, `mmToInches`, `inchesToMm`; `three` (`GLTFLoader`, `OrbitControls` from `three/addons`).
- Produces: `eulerFor(orientation): { x: number; y: number; z: number; order: 'ZYX' }` (radians; pitch about X, roll about Y, yaw about Z, applied in that order — the same convention as `toWorld`); `MeshPreview` with props `glb: Uint8Array | null`, `scale: number`, `orientation: Orientation`, `base: Base | null`, `mode?: 'orbit' | 'top'` (default `'orbit'`), `volumes?: Volume[]` (drawn as outlines in `'top'` mode), `onmeasure?: (m: Measure) => void` where `Measure = { width: number; length: number; height: number; frame: { minX: number; maxX: number; minY: number; maxY: number } }`, exported from the component's `<script module>` so `VolumeEditor` can import the type (`frame` is what the top-down camera shows, so the volume editor can map pointer positions back to table coordinates); `BasePicker` with `bind:value: Base | null`, `allowNone?: boolean`, `onchangevalue?: (b: Base | null) => void` (fires after every change, for callers that apply an edit rather than bind) — a preset dropdown plus a *Custom* entry that reveals shape and millimetre inputs.
- Decisions: three.js is the preview (web app → Deliberately not decided: "How a glTF preview is embedded — `<model-viewer>`, three.js, or otherwise" — three because the top-down volume editor needs the same scene with an orthographic camera and drawn footprints, which `<model-viewer>` cannot do). The world is drawn Z-up by setting `camera.up = (0, 0, 1)` and leaving the GLB's Y-up data alone; the stored orientation is what stands it up. The ring is the modelled base in inches: a circle or oval outline with the oval's `a` along +Y, a square as edges, and a short arrow along +Y for facing.

Spec: mesh conversion → The flow ("The mesh stands on a ring of the chosen modelled-base size, with the proposed scale and orientation applied … corrects scale or facing if the guess is wrong"); geometry values → Bases (three shapes; "Presets are UI over a shape"); geometry plan → Coordinate frame (yaw 0 faces +Y; an oval's length along the facing axis).

- [ ] **Step 1: Write the failing test and `three-frame.ts`**

```ts
// web/tests/three-frame.test.ts
import { describe, expect, it } from 'vitest';
import { eulerFor } from '../src/lib/mesh/three-frame';
describe('eulerFor', () => {
  it('maps pitch/roll/yaw degrees onto a ZYX Euler in radians', () => {
    expect(eulerFor({ pitch: 90, yaw: 0, roll: 0 })).toEqual({ x: Math.PI / 2, y: 0, z: 0, order: 'ZYX' });
    expect(eulerFor({ pitch: 0, yaw: -45, roll: 10 })).toEqual({ x: 0, y: (10 * Math.PI) / 180, z: (-45 * Math.PI) / 180, order: 'ZYX' });
  });
});
```

```ts
// web/src/lib/mesh/three-frame.ts — the one place the Orientation convention meets three.js. Order 'ZYX' builds Rz·Ry·Rx, so X (pitch) is applied to the mesh first.
import type { Orientation } from '$lib/api/types';
const rad = (d: number) => (d * Math.PI) / 180;
export const eulerFor = (o: Orientation) => ({ x: rad(o.pitch), y: rad(o.roll), z: rad(o.yaw), order: 'ZYX' as const });
```

- [ ] **Step 2: Write `MeshPreview.svelte`**

```svelte
<script module lang="ts">
  // A type an importer can name (`import MeshPreview, { type Measure } from './MeshPreview.svelte'`).
  // It has to live in the module script: Svelte 5's instance script has no module exports.
  export type Measure = { width: number; length: number; height: number; frame: { minX: number; maxX: number; minY: number; maxY: number } };
</script>

<script lang="ts">
  import { onMount } from 'svelte';
  import * as THREE from 'three';
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
  import type { Base, Orientation, Volume } from '$lib/api/types';
  import { eulerFor } from '$lib/mesh/three-frame';

  let { glb, scale, orientation, base, mode = 'orbit', volumes = [], onmeasure }: {
    glb: Uint8Array | null; scale: number; orientation: Orientation; base: Base | null;
    mode?: 'orbit' | 'top'; volumes?: Volume[]; onmeasure?: (m: Measure) => void;
  } = $props();

  let canvas: HTMLCanvasElement;
  const scene = new THREE.Scene();
  const model = new THREE.Group();      // the GLB, with scale and orientation applied
  const overlay = new THREE.Group();    // ring, arrow, volume outlines
  scene.add(model, overlay, new THREE.HemisphereLight(0xffffff, 0x444466, 1.2));
  const sun = new THREE.DirectionalLight(0xffffff, 1.5); sun.position.set(3, -4, 6); scene.add(sun);
  let renderer: THREE.WebGLRenderer, camera: THREE.Camera, controls: OrbitControls | null = null;

  function outline(points: THREE.Vector3[], color: number) { return new THREE.LineLoop(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color })); }
  function ellipse(rx: number, ry: number, z = 0) { return Array.from({ length: 96 }, (_, i) => { const t = (i / 96) * 2 * Math.PI; return new THREE.Vector3(rx * Math.cos(t), ry * Math.sin(t), z); }); }
  function rect(w: number, l: number, z = 0) { return [new THREE.Vector3(-w / 2, -l / 2, z), new THREE.Vector3(w / 2, -l / 2, z), new THREE.Vector3(w / 2, l / 2, z), new THREE.Vector3(-w / 2, l / 2, z)]; }

  function drawOverlay() {
    overlay.clear();
    if (base) {
      const pts = base.shape === 'circle' ? ellipse(base.a / 2, base.a / 2) : base.shape === 'oval' ? ellipse(base.b / 2, base.a / 2) : rect(base.a, base.a);
      overlay.add(outline(pts, 0x3399ff));
      overlay.add(new THREE.ArrowHelper(new THREE.Vector3(0, 1, 0), new THREE.Vector3(0, 0, 0.01), base.a * 0.75, 0x3399ff));
    }
    for (const v of volumes) {
      const g = new THREE.Group();
      const yaw = (v.yaw.degrees * Math.PI) / 180;
      for (const z of [v.bottom, v.top]) g.add(outline(v.footprint.kind === 'circle' ? ellipse(v.footprint.a, v.footprint.a, z) : rect(v.footprint.a, v.footprint.b, z), v.kind === 'solid' ? 0xff8800 : 0x33cc66));
      g.position.set(v.offset.x, v.offset.y, 0); g.rotation.z = yaw; overlay.add(g);
    }
  }

  function fit() {
    const box = new THREE.Box3().setFromObject(model);
    if (box.isEmpty()) box.set(new THREE.Vector3(-1, -1, 0), new THREE.Vector3(1, 1, 1));
    const size = box.getSize(new THREE.Vector3()), centre = box.getCenter(new THREE.Vector3());
    const radius = Math.max(size.length() / 2, base?.a ?? 0, 0.5), aspect = canvas.clientWidth / canvas.clientHeight;
    onmeasure?.({ width: size.x, length: size.y, height: size.z, frame: { minX: centre.x - radius * aspect, maxX: centre.x + radius * aspect, minY: centre.y - radius, maxY: centre.y + radius } });
    if (mode === 'top') {
      const cam = new THREE.OrthographicCamera(-radius * aspect, radius * aspect, radius, -radius, 0.01, radius * 10);
      cam.position.set(centre.x, centre.y, box.max.z + radius); cam.up.set(0, 1, 0); cam.lookAt(centre.x, centre.y, 0); camera = cam;
      controls?.dispose(); controls = null;
    } else {
      const cam = new THREE.PerspectiveCamera(40, aspect, 0.01, radius * 50);
      cam.up.set(0, 0, 1); cam.position.set(centre.x + radius * 1.6, centre.y - radius * 2.2, centre.z + radius * 1.2); cam.lookAt(centre); camera = cam;
      controls?.dispose(); controls = new OrbitControls(cam, canvas); controls.target.copy(centre);
    }
  }

  async function load(bytes: Uint8Array | null) {
    model.clear();
    if (bytes) {
      const gltf = await new GLTFLoader().parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
      model.add(gltf.scene);
    }
    applyTransform(); fit();
  }
  function applyTransform() { const e = eulerFor(orientation); model.rotation.set(e.x, e.y, e.z, e.order); model.scale.setScalar(scale); drawOverlay(); }

  onMount(() => {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    const resize = () => { renderer.setSize(canvas.clientWidth, canvas.clientHeight, false); fit(); };
    const observer = new ResizeObserver(resize); observer.observe(canvas);
    let frame = 0; const loop = () => { controls?.update(); renderer.render(scene, camera); frame = requestAnimationFrame(loop); };
    resize(); loop();
    return () => { cancelAnimationFrame(frame); observer.disconnect(); controls?.dispose(); renderer.dispose(); };
  });
  $effect(() => { void load(glb); });
  $effect(() => { scale; orientation; base; volumes; if (renderer) { applyTransform(); fit(); } });
</script>

<canvas bind:this={canvas} style="width: 100%; height: 100%; display: block; min-height: 20rem"></canvas>
```

- [ ] **Step 3: Write `BasePicker.svelte`**

```svelte
<script lang="ts">
  import type { Base, BaseShape } from '$lib/api/types';
  import { BASE_PRESETS, presetFor } from '$lib/units/bases';
  import { inchesToMm, mmToInches, round } from '$lib/units/units';

  let { value = $bindable(), allowNone = false, onchangevalue }: { value: Base | null; allowNone?: boolean; onchangevalue?: (b: Base | null) => void } = $props();
  let choice = $state<string>(value === null ? 'none' : presetFor(value)?.label ?? 'custom');
  let shape = $state<BaseShape>(value?.shape ?? 'circle');
  let aMm = $state(round(inchesToMm(value?.a ?? 32 / 25.4), 1));
  let bMm = $state(round(inchesToMm(value?.b ?? 32 / 25.4), 1));

  function pick(label: string) {
    choice = label;
    if (label === 'none') value = null;
    else if (label === 'custom') applyCustom();
    else value = BASE_PRESETS.find((p) => p.label === label)!.base;
    if (label !== 'custom') onchangevalue?.(value);
  }
  function applyCustom() {
    const a = mmToInches(aMm), b = shape === 'oval' ? mmToInches(bMm) : a;
    value = { shape, a, b };
    onchangevalue?.(value);
  }
</script>

<select value={choice} onchange={(e) => pick(e.currentTarget.value)}>
  {#if allowNone}<option value="none">No base</option>{/if}
  {#each BASE_PRESETS as p}<option value={p.label}>{p.label}</option>{/each}
  <option value="custom">Custom…</option>
</select>
{#if choice === 'custom'}
  <select bind:value={shape} onchange={applyCustom}><option value="circle">round</option><option value="oval">oval</option><option value="rect">rectangle</option></select>
  <input type="number" min="1" step="0.5" bind:value={aMm} onchange={applyCustom} aria-label={shape === 'oval' ? 'length (mm)' : 'size (mm)'} /> mm
  {#if shape === 'oval'}× <input type="number" min="1" step="0.5" bind:value={bMm} onchange={applyCustom} aria-label="width (mm)" /> mm{/if}
{/if}
```

- [ ] **Step 4: Run `npm test && npm run check`; expect 34 passed, 0 errors. Commit: `Add the three.js mesh preview and the base preset picker`.**
