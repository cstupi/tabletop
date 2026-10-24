// The WebAssembly Geometry for tests: the runtime ./scripts/geometry-wasm publishes into public/geometry/_framework/, read from disk.
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadGeometry } from '../src/lib/geometry';
import type { Geometry } from '../src/lib/geometry';

export function loadTestGeometry(): Promise<Geometry> {
  const dir = resolve('public/geometry/_framework');
  if (!existsSync(resolve(dir, 'dotnet.js'))) throw new Error('run ./scripts/geometry-wasm');
  return loadGeometry(pathToFileURL(dir).href + '/');
}
