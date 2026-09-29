// web/src/lib/mesh/convert.ts — the pipeline, in the spec's order: parse, merge, weld, decimate, normals, export, then the analysis the preview needs.
import type { Volume } from '$lib/api/types';
import { terrainVolume } from './bounds';
import { BUDGETS, decimate } from './decimate';
import { exportGlb, readGlb } from './glb';
import { type Proposal, type UpAxis, guessUpAxis, propose, recentre } from './guess';
import { creasedNormals } from './normals';
import { mergeSoups, parseStl } from './stl';
import { type SupportReport, detectSupports } from './supports';
import { type Bounds, bounds, triangleCount } from './types';
import { weld } from './weld';

export interface ConversionInput { kind: 'miniature' | 'terrain'; upAxis?: UpAxis; budget?: number; creaseAngle?: number; files: { name: string; bytes: ArrayBuffer }[] }
export interface ConversionResult { glb: Uint8Array; trianglesBefore: number; trianglesAfter: number; bounds: Bounds; proposal: Proposal; supports: SupportReport; volume: Volume }
export type Progress = { step: 'parse' | 'weld' | 'decimate' | 'normals' | 'export' | 'analyse' };

export async function convert(input: ConversionInput, onProgress: (p: Progress) => void = () => {}): Promise<ConversionResult> {
  onProgress({ step: 'parse' });
  const soup = mergeSoups(input.files.map((f) => parseStl(f.bytes)));
  onProgress({ step: 'weld' });                                    // weld, then centre on the footprint (mesh conversion → What conversion does: Centre)
  const raw = weld(soup);
  // One decision, taken before decimation and reused (the caller may force it): the error ladder can move a near-cubic mesh's extremes far
  // enough to flip the tallest axis, which would centre and floor the mesh on one axis and record an orientation for another.
  const upAxis = input.upAxis ?? guessUpAxis(bounds(raw.positions));
  const welded = recentre(raw, upAxis);
  const trianglesBefore = triangleCount(welded);
  const supports = detectSupports(welded);                       // on the full mesh: decimation can merge thin pillars
  onProgress({ step: 'decimate' });
  const decimated = await decimate(welded, input.budget ?? BUDGETS[input.kind]);
  onProgress({ step: 'normals' });
  const shaded = creasedNormals(decimated, input.creaseAngle ?? 60);
  onProgress({ step: 'export' });
  const glb = await exportGlb(shaded, input.files[0]?.name.replace(/\.stl$/i, '') ?? 'mesh');
  onProgress({ step: 'analyse' });
  const b = bounds(shaded.positions), proposal = propose(b, upAxis);
  return { glb, trianglesBefore, trianglesAfter: triangleCount(shaded), bounds: b, proposal, supports, volume: terrainVolume(b, proposal) };
}

/** A dropped GLB skips conversion; the preview still needs its count and extent, and terrain still needs its volume. */
export async function describeGlb(bytes: Uint8Array): Promise<{ triangles: number; bounds: Bounds }> {
  const { positions, triangles } = await readGlb(bytes);
  return { triangles, bounds: bounds(positions) };
}

export function convertInWorker(input: ConversionInput, onProgress: (p: Progress) => void = () => {}): Promise<ConversionResult> {
  return new Promise((resolve, reject) => {
    const worker = new Worker(new URL('./convert.worker.ts', import.meta.url), { type: 'module' });
    worker.onmessage = (e: MessageEvent<{ progress?: Progress; result?: ConversionResult; error?: string }>) => {
      if (e.data.progress) onProgress(e.data.progress);
      if (e.data.result) { resolve(e.data.result); worker.terminate(); }
      if (e.data.error) { reject(new Error(e.data.error)); worker.terminate(); }
    };
    const fail = (message: string) => { reject(new Error(message)); worker.terminate(); };
    worker.onerror = (e) => fail(e.message || 'The conversion worker failed to load');
    worker.onmessageerror = () => fail('The conversion worker sent a message that could not be read');
    worker.postMessage(input, input.files.map((f) => f.bytes));
  });
}
