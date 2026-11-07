// web/src/lib/thumbnail/render.ts — a mesh asset's thumbnail, drawn in the browser with three.js and sent to management
// (PUT /v1/assets/{id}/thumbnail). WebP where the browser encodes it, PNG otherwise; a transparent background so either theme shows through.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { api } from '$lib/api/client';
import type { Guid, Orientation } from '$lib/api/types';
import { eulerFor } from '$lib/mesh/three-frame';
import { thumbnailView, THUMBNAIL_SIZE } from './frame';

const FOV = 30;

function encode(canvas: HTMLCanvasElement, type: string): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, 0.85));
}

/** The GLB with its scale and orientation, seen as the preview starts, as a square image. */
export async function renderThumbnail(glb: Uint8Array, scale: number, orientation: Orientation): Promise<Blob> {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = THUMBNAIL_SIZE;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: true });
  try {
    renderer.setSize(THUMBNAIL_SIZE, THUMBNAIL_SIZE, false);
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    const scene = new THREE.Scene();
    const pmrem = new THREE.PMREMGenerator(renderer), room = new RoomEnvironment();
    scene.environment = pmrem.fromScene(room, 0.04).texture; scene.environmentIntensity = 0.5;
    room.dispose(); pmrem.dispose();
    const gltf = await new GLTFLoader().parseAsync(glb.buffer.slice(glb.byteOffset, glb.byteOffset + glb.byteLength) as ArrayBuffer, '');
    const model = new THREE.Group(); model.add(gltf.scene);
    const e = eulerFor(orientation); model.rotation.set(e.x, e.y, e.z, e.order); model.scale.setScalar(scale);
    scene.add(model, new THREE.HemisphereLight(0xffffff, 0x8a8478, 0.6));
    const box = new THREE.Box3().setFromObject(model);
    const view = thumbnailView({ min: [box.min.x, box.min.y, box.min.z], max: [box.max.x, box.max.y, box.max.z] }, FOV);
    const camera = new THREE.PerspectiveCamera(FOV, 1, 0.01, 1000);
    camera.up.set(0, 0, 1); camera.position.set(...view.position); camera.lookAt(...view.target);
    const key = new THREE.DirectionalLight(0xffffff, 1.3);
    key.position.set(view.position[0] - 2, view.position[1], view.position[2] + 4); key.target.position.set(...view.target);
    scene.add(key, key.target);
    renderer.render(scene, camera);
    const webp = await encode(canvas, 'image/webp');
    const out = webp?.type === 'image/webp' ? webp : await encode(canvas, 'image/png');
    if (!out) throw new Error('The browser could not encode the thumbnail.');
    scene.environment?.dispose();
    return out;
  } finally { renderer.dispose(); }
}

/** Renders and sends the thumbnail; a failure leaves the asset without one, which draws its kind icon. */
export async function sendThumbnail(id: Guid, glb: Uint8Array, scale: number, orientation: Orientation): Promise<boolean> {
  try { await api.assets.putThumbnail(id, await renderThumbnail(glb, scale, orientation)); return true; }
  catch (e) { console.warn('thumbnail', e); return false; }
}
