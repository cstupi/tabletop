import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { STORAGE_KEY, session } from '../src/lib/session/session.svelte';
import { ApiError } from '../src/lib/api/http';
import { api } from '../src/lib/api/client';

const store = new Map<string, string>();
beforeEach(() => {
  vi.stubGlobal('localStorage', { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => store.set(k, v), removeItem: (k: string) => store.delete(k) });
  store.clear();
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

describe('session', () => {
  it('accept stores the token and account; logout revokes and clears', async () => {
    const logout = vi.spyOn(api.auth, 'logout').mockResolvedValue(undefined);
    session.accept({ sessionJwt: 'jwt-a', account: { id: 'acc', name: 'Clay' } });
    expect(session.token).toBe('jwt-a');
    expect(JSON.parse(store.get(STORAGE_KEY)!)).toEqual({ sessionJwt: 'jwt-a', account: { id: 'acc', name: 'Clay' } });
    await session.logout();
    expect(logout).toHaveBeenCalled();
    expect(session.token).toBeNull();
    expect(store.has(STORAGE_KEY)).toBe(false);
  });

  it('restore refreshes the stored token and loads the account; a failed refresh signs out', async () => {
    store.set(STORAGE_KEY, JSON.stringify({ sessionJwt: 'old', account: { id: 'acc', name: 'Old' } }));
    vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'new', account: { id: 'acc', name: 'Clay' } });
    await session.restore();
    expect(session.token).toBe('new');
    expect(session.account?.name).toBe('Clay');
    expect(session.ready).toBe(true);
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new ApiError(401, 'Unauthorized'));
    expect(await session.refresh()).toBe(false);
    expect(session.token).toBeNull();
  });

  it('a network failure during refresh keeps the session', async () => {
    store.set(STORAGE_KEY, JSON.stringify({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } }));
    vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } });
    await session.restore();
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new TypeError('Failed to fetch'));
    expect(await session.refresh()).toBe(false);
    expect(session.token).toBe('live');
    expect(session.account?.name).toBe('Clay');
  });

  it('a non-401 API failure during refresh keeps the session', async () => {
    store.set(STORAGE_KEY, JSON.stringify({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } }));
    vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'live', account: { id: 'acc', name: 'Clay' } });
    await session.restore();
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new ApiError(502, 'Upstream failure'));
    expect(await session.refresh()).toBe(false);
    expect(session.token).toBe('live');
  });

  it('marks a session that ended under the reader as expired, but not a logout', async () => {
    vi.spyOn(api.auth, 'logout').mockResolvedValue(undefined);
    session.accept({ sessionJwt: 'a', account: { id: 'acc', name: 'Clay' } });
    await session.logout();
    expect(session.expired).toBe(false);
    session.accept({ sessionJwt: 'b', account: { id: 'acc', name: 'Clay' } });
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new ApiError(401, 'Unauthorized'));
    await session.refresh();
    expect(session.expired).toBe(true);
    session.accept({ sessionJwt: 'c', account: { id: 'acc', name: 'Clay' } });
    expect(session.expired).toBe(false);
  });

  it('overlapping refreshes make one request', async () => {
    session.accept({ sessionJwt: 'old', account: { id: 'acc', name: 'Clay' } });
    const refresh = vi.spyOn(api.auth, 'refresh').mockResolvedValue({ sessionJwt: 'new', account: { id: 'acc', name: 'Clay' } });
    const [a, b] = await Promise.all([session.refresh(), session.refresh()]);
    expect(a).toBe(true);
    expect(b).toBe(true);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(session.token).toBe('new');
  });
});
