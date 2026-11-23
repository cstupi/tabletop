// viewer/src/lib/geometry/moveWorker.ts
// A Web Worker holding its own Tabletop.Geometry runtime (the page's MoveWorkers start several): each message names a facade method and its JSON
// request, and the answer goes back under the message's id, in the order the messages came. The runtime loads on the first message, from the
// `base` it names (the page's `${BASE_URL}geometry/_framework/`).

interface Request { readonly id: number; readonly base: string; readonly method: string; readonly json: string }
type Exports = Record<string, (json: string) => string>;

// dotnet.js reads a worker with its own onmessage as a thread of a multi-threaded runtime and waits for a main thread that never comes; marked
// as a sidecar, it starts a runtime of its own.
(globalThis as { dotnetSidecar?: boolean }).dotnetSidecar = true;

let runtime: Promise<Exports> | null = null;

async function load(base: string): Promise<Exports> {
  const { dotnet } = await import(/* @vite-ignore */ `${base}dotnet.js`);
  const started = await dotnet.create();
  return (await started.getAssemblyExports('Tabletop.Geometry.Wasm')).Tabletop.Geometry.Wasm.Exports as Exports;
}

self.onmessage = async (e: MessageEvent<Request>) => {
  const { id, base, method, json } = e.data;
  try {
    runtime ??= load(base);
    const exports = await runtime;
    const f = exports[method];
    if (f === undefined) throw new Error(`Geometry has no method ${method}`);
    self.postMessage({ id, result: f(json) });
  } catch (error) {
    self.postMessage({ id, error: error instanceof Error ? error.message : String(error) });
  }
};
