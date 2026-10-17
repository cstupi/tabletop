import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../src/lib/api/client';
import { ApiError } from '../src/lib/api/http';
import { importErrorMessage, normaliseSource, reimport } from '../src/lib/armies/import';
afterEach(() => vi.restoreAllMocks());

describe('normaliseSource', () => {
  it('accepts share links, list URLs with an id, and bare ids', () => {
    expect(normaliseSource(' https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py&name=Wormhole ')).toBe('https://army-forge.onepagerules.com/share?id=c2-6YsWRH3py&name=Wormhole');
    expect(normaliseSource('https://army-forge.onepagerules.com/list?listId=x&id=c2-6YsWRH3py')).toBe('c2-6YsWRH3py');
    expect(normaliseSource('c2-6YsWRH3py')).toBe('c2-6YsWRH3py');
    expect(normaliseSource('https://example.com/nothing')).toBeNull();
    expect(normaliseSource('')).toBeNull();
  });
});

describe('reimport', () => {
  it('imports the same source as a new army and then deletes the old one', async () => {
    const calls: string[] = [];
    vi.spyOn(api.armies, 'import').mockImplementation(async (s, b) => { calls.push(`import ${s} ${b}`); return { id: 'new', name: 'N', gameSystem: 'grimdark-future', source: s, units: [] }; });
    vi.spyOn(api.armies, 'delete').mockImplementation(async (id) => { calls.push(`delete ${id}`); });
    const army = await reimport({ id: 'old', source: 'c2-6YsWRH3py' }, 'square');
    expect(army.id).toBe('new');
    expect(calls).toEqual(['import c2-6YsWRH3py square', 'delete old']);
  });
  it('leaves the old army alone when the import fails', async () => {
    vi.spyOn(api.armies, 'import').mockRejectedValue(new ApiError(502, 'Upstream failure', 'Army Forge answered 404'));
    const del = vi.spyOn(api.armies, 'delete');
    await expect(reimport({ id: 'old', source: 'x' }, 'round')).rejects.toBeInstanceOf(ApiError);
    expect(del).not.toHaveBeenCalled();
    expect(importErrorMessage(new ApiError(502, 'Upstream failure', 'Army Forge answered 404'))).toBe('Army Forge answered 404 Check the link is a public share link and try again.');
    expect(importErrorMessage(new ApiError(422, 'Unprocessable', "Only Grimdark Future and Age of Fantasy lists import; this list is 'aofs'."))).toMatch(/Age of Fantasy/);
  });
});
