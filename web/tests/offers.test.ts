import { afterEach, describe, expect, it, vi } from 'vitest';
import { alreadyHeld, appendPage, filterPlayers, offerSummary, playerContext, STATE_LABELS, stateChip } from '../src/lib/library/offers';
import { api } from '../src/lib/api/client';
import type { AssetDto, OfferAssetDto, OfferDto, PlayerDto } from '../src/lib/api/types';

const player = (id: string, name: string, games: string[], lastPlayed = '2026-10-20T18:00:00Z'): PlayerDto => ({ accountId: id, name, games, lastPlayed });
const item = (id: string, byteSize = 1_000_000, hash = id): OfferAssetDto => ({
  id, name: id, kind: 'miniature', hash, byteSize, contentType: 'model/gltf-binary', scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 },
});
const offer = (id: string, assets: OfferAssetDto[], folder?: string): OfferDto => ({
  id, state: 'open', sharerId: 's', sharerName: 'S', recipientId: 'r', recipientName: 'R', folder, createdAt: '2026-10-25T00:00:00Z', assets,
});
const mine = (hash: string): AssetDto => ({
  id: 'm', name: 'm', kind: 'miniature', hash, byteSize: 1, contentType: 'model/gltf-binary', uploaded: true, scale: 1, orientation: { pitch: 0, yaw: 0, roll: 0 },
});

afterEach(() => vi.unstubAllGlobals());

describe('offers', () => {
  it('tells same-named players apart by their games, then by id', () => {
    const a = player('0000-aaaa', 'Kim', ['Friday']); const b = player('0000-bbbb', 'Kim', ['Sunday']);
    expect(playerContext(a, [a, b])).toBe('Friday · 20 Oct 2026');
    const c = player('0000-cccc', 'Kim', ['Friday']);
    expect(playerContext(a, [a, c])).toBe('Friday · 20 Oct 2026 · #aaaa');
  });
  it('filters players by name', () => {
    expect(filterPlayers([player('1', 'Kim', []), player('2', 'Sam', [])], ' kI ').map((p) => p.name)).toEqual(['Kim']);
  });
  it('summarises an offer', () => {
    expect(offerSummary(offer('o', [item('Dread', 4 * 1024 * 1024)]))).toBe('Dread · 4.0 MB');
    expect(offerSummary(offer('o', [item('a'), item('b')], 'Orks'))).toMatch(/^Folder Orks · 2 models · /);
    expect(offerSummary(offer('o', [], 'Orks'))).toBe('Folder Orks · deleted');
  });
  it('finds offered assets already in the library', () => {
    expect(alreadyHeld(offer('o', [item('x', 1, 'h1'), item('y', 1, 'h2')]), [mine('h2')])).toEqual(new Set(['y']));
  });
  it('appends a page without repeating offers', () => {
    expect(appendPage([offer('x', [])], [offer('x', []), offer('y', [])]).map((o) => o.id)).toEqual(['x', 'y']);
  });
  it('calls the offer endpoints', async () => {
    const f = vi.fn(async () => new Response(JSON.stringify({ items: [], offers: [], notFound: [], copied: [] }), { status: 200, headers: { 'content-type': 'application/json' } }));
    vi.stubGlobal('fetch', f);
    await api.offers.create({ recipientIds: ['b'], assetIds: ['x'], folderId: null });
    await api.offers.received({ limit: 20 });
    await api.offers.accept('o', 'f');
    await api.offers.decline('o', true);
    await api.offers.withdraw('o');
    await api.offers.download('o', 'x');
    const calls = f.mock.calls as unknown as [string, RequestInit][];
    expect(calls.map(([u, i]) => `${i.method ?? 'GET'} ${u}`)).toEqual([
      'POST /v1/offers', 'GET /v1/offers/received?limit=20', 'POST /v1/offers/o/accept', 'POST /v1/offers/o/decline', 'POST /v1/offers/o/withdraw',
      'GET /v1/offers/o/assets/x/download',
    ]);
    expect(JSON.parse(calls[0]![1].body as string)).toEqual({ recipientIds: ['b'], assetIds: ['x'], folderId: null });
    expect(JSON.parse(calls[2]![1].body as string)).toEqual({ folderId: 'f' });
    expect(JSON.parse(calls[3]![1].body as string)).toEqual({ block: true });
  });
});

describe('stateChip', () => {
  it('is waiting for an open offer and the state otherwise', () => {
    expect(stateChip('open')).toBe('chip status-waiting');
    expect(stateChip('accepted')).toBe('chip status-accepted');
    expect(STATE_LABELS.open).toBe('waiting');
  });
});
