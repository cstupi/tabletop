import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

describe('the viewer project', () => {
  it('is a plain Vite app with no SvelteKit and no dependency on web/', () => {
    const pkg = JSON.parse(readFileSync('package.json', 'utf8'));
    expect(pkg.name).toBe('tabletop-viewer');
    expect(Object.keys(pkg.devDependencies)).not.toContain('@sveltejs/kit');
    expect(pkg.dependencies).toBeUndefined();
  });

  it('proxies /v1 to management and names the game socket nowhere but the config', () => {
    const config = readFileSync('vite.config.ts', 'utf8');
    expect(config).toContain("target: 'http://localhost:5100'");
    expect(config).toContain("port: 5174");
  });
});
