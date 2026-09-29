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

  let { glb, scale, orientation, base, mode = 'orbit', volumes = [], onmeasure, onerror }: {
    glb: Uint8Array | null; scale: number; orientation: Orientation; base: Base | null;
    mode?: 'orbit' | 'top'; volumes?: Volume[]; onmeasure?: (m: Measure) => void; onerror?: (message: string) => void;
  } = $props();

  let canvas: HTMLCanvasElement;
  const scene = new THREE.Scene();
  const model = new THREE.Group();      // the GLB, with scale and orientation applied
  const overlay = new THREE.Group();    // ring, arrow, volume outlines
  scene.add(model, overlay, new THREE.HemisphereLight(0xffffff, 0x444466, 1.2));
  const sun = new THREE.DirectionalLight(0xffffff, 1.5); sun.position.set(3, -4, 6); scene.add(sun);
  let renderer: THREE.WebGLRenderer, camera: THREE.Camera, controls: OrbitControls | null = null;
  let loadToken = 0;

  function outline(points: THREE.Vector3[], color: number) { return new THREE.LineLoop(new THREE.BufferGeometry().setFromPoints(points), new THREE.LineBasicMaterial({ color })); }
  function ellipse(rx: number, ry: number, z = 0) { return Array.from({ length: 96 }, (_, i) => { const t = (i / 96) * 2 * Math.PI; return new THREE.Vector3(rx * Math.cos(t), ry * Math.sin(t), z); }); }
  function rect(w: number, l: number, z = 0) { return [new THREE.Vector3(-w / 2, -l / 2, z), new THREE.Vector3(w / 2, -l / 2, z), new THREE.Vector3(w / 2, l / 2, z), new THREE.Vector3(-w / 2, l / 2, z)]; }

  function drawOverlay() {
    overlay.clear();
    if (base) {
      const pts = base.shape === 'circle' ? ellipse(base.a / 2, base.a / 2) : base.shape === 'oval' ? ellipse(base.b / 2, base.a / 2) : rect(base.b, base.a);
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
    const t = ++loadToken;
    if (bytes) {
      try {
        const gltf = await new GLTFLoader().parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer, '');
        if (t === loadToken) model.add(gltf.scene);
      } catch (e) {
        if (t === loadToken) {
          const message = e instanceof Error ? e.message : String(e);
          if (onerror) onerror(message);
          else console.error(message);
        }
      }
    }
    if (t === loadToken) { applyTransform(); fit(); }
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
