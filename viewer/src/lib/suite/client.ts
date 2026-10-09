// Client for the runner service (sim serve): GET /suite, POST /runs, GET /runs/{id}/events.
export type SuiteGame = { readonly name: string; readonly group: string; readonly covers: readonly string[] };
export type DriverName = 'in-process' | 'socket';
export type RunRequest = { readonly only: readonly string[]; readonly driver: DriverName; readonly keep: boolean };

export type SuiteGameResult = {
  readonly name: string; readonly group: string; readonly passed: boolean; readonly failures: readonly string[];
  readonly seed: number; readonly replay: string; readonly recordingPath: string | null; readonly gameId: string | null;
};
export type ExactResult = { readonly game: string; readonly check: string; readonly passed: boolean };
export type CoverageEntry = {
  readonly name: string; readonly kind: string; readonly games: readonly string[]; readonly fired: boolean;
  readonly exact: readonly ExactResult[];
};
export type SuiteReport = {
  readonly runId: string; readonly games: readonly SuiteGameResult[]; readonly coverage: readonly CoverageEntry[];
  readonly passed: boolean;
};

export type RunEvent =
  | { readonly type: 'game'; readonly result: SuiteGameResult }
  | { readonly type: 'done'; readonly report: SuiteReport }
  | { readonly type: 'error'; readonly message: string };

export class SuiteError extends Error {}

export async function fetchSuite(base = '', fetchImpl: typeof fetch = fetch): Promise<SuiteGame[]> {
  const response = await fetchImpl(`${base}/suite`);
  if (!response.ok) throw new SuiteError(`/suite answered ${response.status} ${response.statusText}`);
  const body = await response.json() as { games?: SuiteGame[] };
  if (!Array.isArray(body.games)) throw new SuiteError('/suite did not return a games list');
  return body.games;
}

/** Starts a run; the body is always explicit (an empty body would start an all-games in-process run). */
export async function startRun(request: RunRequest, base = '', fetchImpl: typeof fetch = fetch): Promise<string> {
  const response = await fetchImpl(`${base}/runs`, {
    method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(request),
  });
  const body = await response.json().catch(() => ({})) as { runId?: string; error?: string };
  if (response.status === 409) throw new SuiteError(body.error ?? 'a run is already active');
  if (!response.ok || body.runId === undefined) throw new SuiteError(body.error ?? `/runs answered ${response.status}`);
  return body.runId;
}

export const recordingUrl = (runId: string, game: string, base = ''): string =>
  `${base}/runs/${runId}/recordings/${encodeURIComponent(game)}`;

/** Splits an event-stream into `{event, data}` messages; tolerant of chunk boundaries anywhere. */
export async function* sseMessages(body: ReadableStream<Uint8Array>): AsyncGenerator<{ event: string; data: string }> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  const take = (block: string) => {
    let event = 'message';
    const data: string[] = [];
    for (const line of block.split('\n')) {
      if (line.startsWith('event:')) event = line.slice(6).trim();
      else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''));
    }
    return data.length === 0 ? null : { event, data: data.join('\n') };
  };
  try {
    for (;;) {
      const { done, value } = await reader.read();
      buffer += decoder.decode(value, { stream: !done }).replace(/\r\n/g, '\n');
      let cut: number;
      while ((cut = buffer.indexOf('\n\n')) >= 0) {
        const message = take(buffer.slice(0, cut));
        buffer = buffer.slice(cut + 2);
        if (message !== null) yield message;
      }
      if (done) break;
    }
    const rest = take(buffer);
    if (rest !== null) yield rest;
  } finally {
    reader.releaseLock();
  }
}

export async function* runEvents(runId: string, base = '', fetchImpl: typeof fetch = fetch): AsyncGenerator<RunEvent> {
  const response = await fetchImpl(`${base}/runs/${runId}/events`);
  if (!response.ok || response.body === null) throw new SuiteError(`events answered ${response.status}`);
  for await (const { event, data } of sseMessages(response.body)) {
    if (event === 'game') yield { type: 'game', result: JSON.parse(data) as SuiteGameResult };
    else if (event === 'done') { yield { type: 'done', report: JSON.parse(data) as SuiteReport }; return; }
    else if (event === 'error') { yield { type: 'error', message: (JSON.parse(data) as { message: string }).message }; return; }
  }
}
