/// <reference lib="webworker" />
// `lib` carries both dom and webworker, so a bare `self` would type as Window and its postMessage would want a targetOrigin.
declare const self: DedicatedWorkerGlobalScope;

import { convert, type ConversionInput } from './convert';
self.onmessage = async (e: MessageEvent<ConversionInput>) => {
  try {
    const result = await convert(e.data, (progress) => self.postMessage({ progress }));
    self.postMessage({ result }, [result.glb.buffer as ArrayBuffer]);
  } catch (err) { self.postMessage({ error: err instanceof Error ? err.message : String(err) }); }
};
