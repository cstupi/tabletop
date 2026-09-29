import { api } from '$lib/api/client';
import { ApiError } from '$lib/api/http';
import type { Army } from '$lib/api/types';

const ID = /^[A-Za-z0-9_-]{4,64}$/;

/** A share link is passed through whole (management reads its id= and keeps the link as `source`); other URLs with an id= collapse to the id; a bare id passes. */
export function normaliseSource(text: string): string | null {
  const t = text.trim();
  if (!t) return null;
  if (/^https:\/\/army-forge\.onepagerules\.com\/share\?/.test(t) && /[?&]id=[A-Za-z0-9_-]+/.test(t)) return t;
  const m = /[?&]id=([A-Za-z0-9_-]+)/.exec(t);
  if (m) return m[1]!;
  return ID.test(t) ? t : null;
}

export function importErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return 'Something went wrong. Try again.';
  if (e.status === 502) return `Army Forge did not answer (${e.detail ?? e.title}). Check the link is a public share link and try again.`;
  return e.detail ?? e.title;
}

/** A changed list means a re-import: a new army from the same source, then the old one goes. Nothing is reconciled. */
export async function reimport(army: { id: string; source: string }): Promise<Army> {
  const fresh = await api.armies.import(army.source);
  await api.armies.delete(army.id);
  return fresh;
}
