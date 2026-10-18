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
  import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
  import type { Base, Orientation, Volume } from '$lib/api/types';
  import { eulerFor } from '$lib/mesh/three-frame';

  let { glb, scale, orientation, base, mode = 'orbit', volumes = [], highlight, onmeasure, onerror }: {
    glb: Uint8Array | null; scale: number; orientation: Orientation; base: Base | null;
    mode?: 'orbit' | 'top'; volumes?: Volume[]; highlight?: number; onmeasure?: (m: Measure) => void; onerror?: (message: string) => void;
  } = $props();

  let canvas: HTMLCanvasElement;
  const scene = new THREE.Scene();
  const model = new THREE.Group();      // the GLB, with scale and orientation applied
  const overlay = new THREE.Group();    // ring, arrow, volume outlines
  // Light that reads from every orbit angle: a soft room environment built in code (no files, so the CSP is untouched), a
  // hemisphere fill so no side goes black, a key light from above the camera's shoulder and a rim light from behind, both
  // moved with the camera each frame.
  const key = new THREE.DirectionalLight(0xffffff, 1.2), rim = new THREE.DirectionalLight(0xdde6ff, 0.6);
  scene.add(model, overlay, new THREE.HemisphereLight(0xffffff, 0x8a8478, 0.5), key, key.target, rim, rim.target);
  const view = new THREE.Vector3(), side = new THREE.Vector3(), zUp = new THREE.Vector3(0, 0, 1);
  function placeLights() {
    const target = controls?.target ?? new THREE.Vector3();
    view.copy(camera.position).sub(target); const d = view.length() || 1;
    side.crossVectors(view, zUp).normalize();
    key.position.copy(camera.position).addScaledVector(zUp, d * 0.6).addScaledVector(side, -d * 0.4); key.target.position.copy(target);
    rim.position.copy(target).addScaledVector(view, -1).addScaledVector(zUp, d * 0.8); rim.target.position.copy(target);
  }
  let renderer: THREE.WebGLRenderer, camera: THREE.Camera = new THREE.PerspectiveCamera(), controls: OrbitControls | null = null;
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
    for (let i = 0; i < volumes.length; i++) {
      const v = volumes[i]!;
      const g = new THREE.Group();
      const yaw = (v.yaw.degrees * Math.PI) / 180;
      const color = i === highlight ? 0x3399ff : v.kind === 'solid' ? 0xff8800 : 0x33cc66;
      for (const z of [v.bottom, v.top]) g.add(outline(v.footprint.kind === 'circle' ? ellipse(v.footprint.a, v.footprint.a, z) : rect(v.footprint.a, v.footprint.b, z), color));
      g.position.set(v.offset.x, v.offset.y, 0); g.rotation.z = yaw; overlay.add(g);
    }
  }

  // Orbit mode looks at a miniature on a table: free around the vertical, from just above the table plane (MAX_POLAR from
  // straight up) to straight down, starting at START_POLAR (mid-range) from the front-right, where the facing arrow points.
  const MAX_POLAR = (85 * Math.PI) / 180, START_POLAR = (45 * Math.PI) / 180, START_AZIMUTH = (30 * Math.PI) / 180, FOV = 35;
  const startDirection = () => new THREE.Vector3(Math.sin(START_POLAR) * Math.sin(START_AZIMUTH), Math.sin(START_POLAR) * Math.cos(START_AZIMUTH), Math.cos(START_POLAR));

  function aspect() { return canvas.clientWidth > 0 && canvas.clientHeight > 0 ? canvas.clientWidth / canvas.clientHeight : 1; }

  /** Frames the model and its base ring. `reset` restores the starting angle; otherwise the viewer's angle is kept. */
  function fit(reset = false) {
    const box = new THREE.Box3().setFromObject(model);
    if (box.isEmpty()) box.set(new THREE.Vector3(-1, -1, 0), new THREE.Vector3(1, 1, 1));
    const size = box.getSize(new THREE.Vector3()), centre = box.getCenter(new THREE.Vector3());
    const radius = Math.max(size.length() / 2, base?.a ?? 0, 0.5), ratio = aspect();
    if ((model.children.length > 0 || !glb) && onmeasure) onmeasure({ width: size.x, length: size.y, height: size.z, frame: { minX: centre.x - radius * ratio, maxX: centre.x + radius * ratio, minY: centre.y - radius, maxY: centre.y + radius } });
    if (mode === 'top') {
      const cam = new THREE.OrthographicCamera(-radius * ratio, radius * ratio, radius, -radius, 0.01, radius * 10);
      cam.position.set(centre.x, centre.y, box.max.z + radius); cam.up.set(0, 1, 0); cam.lookAt(centre.x, centre.y, 0); camera = cam;
      controls?.dispose(); controls = null;
      return;
    }
    const framed = box.clone().union(new THREE.Box3().setFromObject(overlay));
    const sphere = framed.getBoundingSphere(new THREE.Sphere());
    const half = (FOV * Math.PI) / 360, halfH = Math.atan(Math.tan(half) * ratio);
    const distance = (sphere.radius / Math.sin(Math.min(half, halfH))) * 0.95;
    if (!(camera instanceof THREE.PerspectiveCamera) || !controls) {
      const cam = new THREE.PerspectiveCamera(FOV, ratio, 0.01, 100);
      cam.up.set(0, 0, 1);   // before OrbitControls is built: it fixes its orbit frame from camera.up
      camera = cam; controls?.dispose();
      controls = new OrbitControls(cam, canvas);
      controls.minPolarAngle = 0; controls.maxPolarAngle = MAX_POLAR;
      controls.enablePan = false;
      reset = true;
    }
    const cam = camera as THREE.PerspectiveCamera;
    const direction = reset ? startDirection() : cam.position.clone().sub(controls.target).normalize();
    controls.target.copy(sphere.center);
    cam.position.copy(sphere.center).addScaledVector(direction, distance);
    cam.aspect = ratio; cam.near = distance / 100; cam.far = distance * 10; cam.updateProjectionMatrix();
    controls.minDistance = sphere.radius * 0.5; controls.maxDistance = distance * 4;
    controls.update();
  }
  function resized() {
    renderer.setSize(canvas.clientWidth, canvas.clientHeight, false);
    if (camera instanceof THREE.PerspectiveCamera && controls) { camera.aspect = aspect(); camera.updateProjectionMatrix(); } else fit();
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
    if (t === loadToken) { applyTransform(); fit(true); }
  }
  function applyTransform() { const e = eulerFor(orientation); model.rotation.set(e.x, e.y, e.z, e.order); model.scale.setScalar(scale); drawOverlay(); }

  onMount(() => {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
    const pmrem = new THREE.PMREMGenerator(renderer), room = new RoomEnvironment();
    scene.environment = pmrem.fromScene(room, 0.04).texture; scene.environmentIntensity = 0.4;
    renderer.toneMapping = THREE.ACESFilmicToneMapping; room.dispose(); pmrem.dispose();
    const observer = new ResizeObserver(resized); observer.observe(canvas);
    let frame = 0; const loop = () => { controls?.update(); placeLights(); renderer.render(scene, camera); frame = requestAnimationFrame(loop); };
    resized(); loop();
    return () => { cancelAnimationFrame(frame); observer.disconnect(); controls?.dispose(); scene.environment?.dispose(); renderer.dispose(); };
  });
  $effect(() => { void load(glb); });
  $effect(() => { scale; orientation; base; volumes; highlight; if (renderer) { applyTransform(); fit(); } });
</script>

<canvas bind:this={canvas} style="width: 100%; height: 100%; display: block; min-height: 20rem"></canvas>
