import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../src/lib/api/client';
import type { ClientDownload } from '../src/lib/api/types';
import { detectPlatform, FIRST_RUN, orderDownloads } from '../src/lib/download/platforms';

const UA = {
  windows: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36',
  mac: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15',
  linux: 'Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0',
  android: 'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Mobile Safari/537.36',
  iphone: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1',
  chromebook: 'Mozilla/5.0 (X11; CrOS x86_64 16000.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36',
};

describe('detectPlatform', () => {
  it('reads the desktop OS from the user agent', () => {
    expect(detectPlatform(UA.windows)).toBe('windows');
    expect(detectPlatform(UA.mac)).toBe('macos');
    expect(detectPlatform(UA.linux)).toBe('linux');
  });

  it('offers nothing first to phones, tablets and Chromebooks', () => {
    expect(detectPlatform(UA.android)).toBeNull();
    expect(detectPlatform(UA.iphone)).toBeNull();
    expect(detectPlatform(UA.chromebook)).toBeNull();
    expect(detectPlatform('')).toBeNull();
  });

  it('prefers the client hint when the browser gives one', () => {
    expect(detectPlatform(UA.windows, 'macOS')).toBe('macos');
    expect(detectPlatform(UA.linux, 'Chrome OS')).toBeNull();
    expect(detectPlatform(UA.mac, '')).toBe('macos');
  });
});

describe('orderDownloads', () => {
  const d = (platform: ClientDownload['platform']): ClientDownload => ({ platform, fileName: `${platform}.zip`, size: 1, sha256: 'a', url: `https://b/${platform}` });
  const all = [d('windows'), d('macos'), d('linux')];

  it('puts the detected build first and keeps the rest in order', () => {
    expect(orderDownloads(all, 'linux').map((x) => x.platform)).toEqual(['linux', 'windows', 'macos']);
    expect(orderDownloads(all, null).map((x) => x.platform)).toEqual(['windows', 'macos', 'linux']);
  });

  it('every platform has first-run steps that name the unsigned build where the OS warns', () => {
    expect(FIRST_RUN.windows.join(' ')).toMatch(/SmartScreen.*Run anyway/);
    expect(FIRST_RUN.macos.join(' ')).toMatch(/Open Tabletop\.command.*Open Anyway/);
    expect(FIRST_RUN.linux.join(' ')).toMatch(/chmod \+x/);
  });
});

describe('api.client.latest', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('gets /v1/client/latest', async () => {
    const f = vi.fn(async () => new Response(JSON.stringify({ version: '1.0.0', downloads: [] }), { status: 200, headers: { 'content-type': 'application/json' } }));
    vi.stubGlobal('fetch', f);
    expect((await api.client.latest()).version).toBe('1.0.0');
    expect((f.mock.calls[0] as unknown as [string])[0]).toBe('/v1/client/latest');
  });
});
