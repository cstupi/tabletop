// web/src/lib/api/errors.ts — one plain sentence for any failure a page shows. Management's problem details are fixed,
// readable text (management design §12); this adds what the page knows that the server does not: which flow failed, and
// what to do next.
import { ApiError } from './http';

/** The flow that failed: it decides what a 401 or a 502 means. */
export type ErrorContext = 'auth' | 'import' | 'upload' | 'general';

const NETWORK = 'Could not reach the server. Check your connection and try again.';

/** "Wait a minute" unless the response named a Retry-After. */
export function retryAdvice(seconds: number | undefined): string {
  if (seconds === undefined || !Number.isFinite(seconds) || seconds <= 0) return 'Wait a minute and try again.';
  return seconds === 1 ? 'Try again in 1 second.' : seconds < 120 ? `Try again in ${Math.ceil(seconds)} seconds.` : `Try again in ${Math.ceil(seconds / 60)} minutes.`;
}

/** Rewrites the 422 details whose wording names a wire field or an id, and adds the way out to the count and quota limits. */
function unprocessable(detail: string): string {
  const field = /^(name|source) (is required|is at most (\d+) characters|contains a control or bidirectional formatting character)\.$/.exec(detail);
  if (field) {
    const what = field[1] === 'name' ? 'Name' : 'The Army Forge link';
    if (field[2] === 'is required') return `${what} is required.`;
    if (field[3]) return `${what} is too long: at most ${field[3]} characters.`;
    return `${what} contains an invisible control or text-direction character. Retype it.`;
  }
  if (/^Asset \S+ is not an uploaded asset of yours\.$/.test(detail))
    return 'That mesh is no longer in your library, or its upload never finished. Pick another.';
  const kind = /^Asset \S+ is (\w+); this reference takes (.+)\.$/.exec(detail);
  if (kind) return `That asset is ${kind[1]}; this slot takes ${kind[2]}.`;
  if (/^An account (holds|keeps) at most \d+ (armies|folders|layouts|assets)\.$/.test(detail)) return `${detail} Delete some to make room.`;
  if (/^An account holds at most \d+ GiB of uploads\.$/.test(detail)) return `${detail} Delete assets you no longer use to make room.`;
  return detail;
}

export function errorMessage(e: unknown, context: ErrorContext = 'general'): string {
  if (e instanceof TypeError) return NETWORK;                       // fetch rejects with a TypeError when nothing answered
  if (!(e instanceof ApiError)) return e instanceof Error && e.message ? e.message : 'Something went wrong. Try again.';
  switch (e.status) {
    case 401: return context === 'auth' ? 'Wrong email, password or code.' : 'Your session has ended. Sign in again.';
    case 404: return 'Not found. It may have been deleted.';
    case 409: return /abandoned/.test(e.detail ?? '') ? 'The upload took too long and was discarded. Upload the file again.' : (e.detail ?? 'That changed while you were working. Reload and try again.');
    case 413: return 'That request is too large to send.';
    case 415: return e.detail ?? 'That file type is not accepted.';
    case 422: return e.detail ? unprocessable(e.detail) : 'Those details were not accepted.';
    case 426: return 'This page is out of date. Reload it.';
    case 429: return `Too many requests. ${retryAdvice(e.retryAfter)}`;
    case 502:
      if (context === 'import') return `${e.detail ?? 'Army Forge could not be reached.'} Check the link is a public share link and try again.`;
      if (context === 'auth') return e.detail ?? 'The sign-in service is unavailable. Try again shortly.';
      return e.detail ?? 'A service the server depends on did not answer. Try again shortly.';
    default:
      if (e.status >= 500) return 'The server had a problem. Try again shortly.';
      return e.detail ?? e.title;
  }
}
