// web/src/lib/validation.ts — the checks management makes (management design §6, §14), made before sending so the form can
// say what is wrong next to the field. Management still checks; these mirror Http/Limits.cs and Content/Assets/UploadLimits.cs.
import type { AssetKind } from '$lib/api/types';

export const MAX_NAME = 64;
export const MIB = 1 << 20;
/** Bytes per uploaded file: a miniature or terrain GLB, a board texture. */
export const MAX_BYTES: Record<AssetKind, number> = { miniature: 64 * MIB, terrain: 64 * MIB, texture: 16 * MIB };

/** Unicode Cc, and the bidirectional formatting characters management refuses (LRM, RLM, ALM, U+202A–E, U+2066–9). */
const FORBIDDEN = /[\p{Cc}‎‏؜‪-‮⁦-⁩]/u;

/** Null when management will accept the name (as trimmed), else a sentence for the field. */
export function nameProblem(text: string, what = 'Name'): string | null {
  const t = text.trim();
  if (!t) return `${what} is required.`;
  if (t.length > MAX_NAME) return `${what} is too long: ${t.length} of at most ${MAX_NAME} characters.`;
  if (FORBIDDEN.test(t)) return `${what} contains an invisible control or text-direction character. Retype it.`;
  return null;
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < MIB) return `${(n / 1024).toFixed(0)} KB`;
  return `${(n / MIB).toFixed(n < 10 * MIB ? 1 : 0)} MB`;
}

/** Null when a file of this kind fits management's size cap. */
export function sizeProblem(kind: AssetKind, bytes: number): string | null {
  const max = MAX_BYTES[kind];
  if (bytes <= max) return null;
  const what = kind === 'texture' ? 'A board texture' : 'A mesh';
  const hint = kind === 'texture' ? ' Resize or compress the image.' : ' Lower the triangle budget, or simplify the model before exporting.';
  return `${what} is at most ${max / MIB} MB; this one is ${formatBytes(bytes)}.${hint}`;
}
