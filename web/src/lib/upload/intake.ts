import { MAX_NAME } from '$lib/validation';

export type ImageType = 'image/png' | 'image/jpeg' | 'image/webp';
/** One file of a batch: a GLB or an image, uploaded as its own asset. */
export type BatchItem<F extends { name: string } = File> = { file: F; contentType: 'model/gltf-binary' | ImageType };
export type Intake<F extends { name: string } = File> =
  | { kind: 'stl'; files: F[] } | { kind: 'glb'; file: F } | { kind: 'image'; file: F; contentType: ImageType }
  | { kind: 'batch'; items: BatchItem<F>[] } | { kind: 'rejected'; reason: string };

const IMAGES: Record<string, ImageType> = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp' };

/** The file picker's choices, one filter each: a browser shows a single-type filter as that type alone, and a list of types
 * as "all supported types", so GLB gets the main button and its own filter. */
export const PICKS: { label: string; accept: string }[] = [
  { label: 'Choose GLB files', accept: '.glb' },
  { label: 'STL files', accept: '.stl' },
  { label: 'Textures', accept: '.png,.jpg,.jpeg,.webp' },
];

/** An asset's name from its file: the extension dropped, cut to the longest name management takes. */
export function assetName(fileName: string): string {
  return fileName.replace(/\.[^.]+$/, '').slice(0, MAX_NAME);
}

export function classifyFiles<F extends { name: string }>(files: F[]): Intake<F> {
  const ext = (f: F) => f.name.toLowerCase().split('.').pop() ?? '';
  if (files.length === 0) return { kind: 'rejected', reason: 'Drop .glb models, .stl files or .png/.jpg/.webp images.' };
  if (files.every((f) => ext(f) === 'stl')) return { kind: 'stl', files };
  if (files.length === 1 && ext(files[0]!) === 'glb') return { kind: 'glb', file: files[0]! };
  if (files.length === 1 && IMAGES[ext(files[0]!)]) return { kind: 'image', file: files[0]!, contentType: IMAGES[ext(files[0]!)]! };
  if (files.some((f) => ext(f) === 'gltf')) return { kind: 'rejected', reason: 'A .gltf with external buffers is several files; export a single .glb (GLB) instead.' };
  if (files.every((f) => ext(f) === 'glb' || IMAGES[ext(f)]))
    return { kind: 'batch', items: files.map((file) => ({ file, contentType: ext(file) === 'glb' ? 'model/gltf-binary' as const : IMAGES[ext(file)]! })) };
  return { kind: 'rejected', reason: 'Mix not understood: drop STLs on their own (a kit\'s parts merge into one model), or GLBs and images.' };
}
