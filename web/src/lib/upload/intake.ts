export type Intake<F extends { name: string } = File> =
  | { kind: 'stl'; files: F[] } | { kind: 'glb'; file: F } | { kind: 'image'; file: F; contentType: 'image/png' | 'image/jpeg' | 'image/webp' } | { kind: 'rejected'; reason: string };

export function classifyFiles<F extends { name: string }>(files: F[]): Intake<F> {
  const ext = (f: F) => f.name.toLowerCase().split('.').pop() ?? '';
  if (files.length === 0) return { kind: 'rejected', reason: 'Drop one or more .stl files, one .glb, or one .png/.jpg.' };
  if (files.every((f) => ext(f) === 'stl')) return { kind: 'stl', files };
  if (files.length === 1 && ext(files[0]!) === 'glb') return { kind: 'glb', file: files[0]! };
  const images: Record<string, 'image/png' | 'image/jpeg' | 'image/webp'> = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp' };
  if (files.length === 1 && images[ext(files[0]!)]) return { kind: 'image', file: files[0]!, contentType: images[ext(files[0]!)]! };
  if (files.some((f) => ext(f) === 'gltf')) return { kind: 'rejected', reason: 'A .gltf with external buffers is several files; export a single .glb (GLB) instead.' };
  return { kind: 'rejected', reason: 'Mix not understood: drop STLs together, or one GLB, or one image.' };
}
